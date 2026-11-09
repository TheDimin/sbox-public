using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NVector2 = System.Numerics.Vector2;

/// <summary>Tests the shared production plan against independently audited source layouts; no UV plan implementation is copied.</summary>
public static class TextureCoordinateUvPlanTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		foreach ( var form in TextureCoordinateReference.Open( checkoutRoot ).Forms() )
		{
			Run( "source_selection_retains_exact_descriptor_identity", form, () => CheckSelection( form ) );
			Run( "missing_selected_set_is_rejected_without_mutating_prior_plan", form, () => CheckMissing( form ) );
			Run( "per_role_scale_rotation_offset_order_preserves_original_data", form, () => CheckRoleTransform( form ) );
		}
		return rows.ToArray();
		void Run( string name, TextureCoordinateReference.Form form, Func<int> body )
		{
			try { rows.Add( new( name, form.Name, body(), true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form.Name, 0, false, error.ToString() ) ); }
		}
	}
	private static int CheckSelection( TextureCoordinateReference.Form form )
	{
		using var document = form.Read(); var assertions = 0;
		foreach ( var primitive in form.Expected )
		{
			var upload = document.MaterializePrimitiveUpload( primitive.Mesh, 0 ); var before = Snapshot( upload ); var material = document.ReadMaterial( primitive.Material );
			var plan = global::Sandbox.GltfMaterialUvPlan.Create( material, upload, primitive.Material );
			var variantDefault = global::Sandbox.GltfMaterialUvPlan.CreateForVariant( document, primitive.Mesh, 0, upload, null );
			TextureCoordinateCoreTests.Require( plan.MaterialIndex == primitive.Material && variantDefault.MaterialIndex == primitive.Material, "Default variant resolution changed material identity." ); assertions++;
			if ( primitive.Mesh == 3 )
			{
				TextureCoordinateCoreTests.Require( plan.Bindings.Count == 0 && variantDefault.Bindings.Count == 0, "UV-free untextured back plane acquired a source UV requirement." ); assertions++;
			}
			else
			{
				var role = global::Sandbox.GltfMaterialTextureRole.BaseColor; var binding = plan.Bindings[role]; var expected = upload.Attributes["TEXCOORD_" + (form.Derived ? 1 : 0)];
				TextureCoordinateCoreTests.Require( plan.Bindings.Count == 1 && ReferenceEquals( binding.Source, expected ) && ReferenceEquals( binding.TextureInfo, material.BaseColorTexture ), "Plan must retain the exact selected stream and authored texture binding." ); assertions++;
				TextureCoordinateCoreTests.Require( ReferenceEquals( variantDefault.Bindings[role].Source, expected ) && binding.TexCoord == (form.Derived ? 1 : 0) && binding.TextureIndex == 0 && binding.IsSrgb, "Effective coordinate/index/color-role metadata changed." ); assertions++;
				TextureCoordinateCoreTests.Require( binding.Transform.Offset == NVector2.Zero && binding.Transform.Tiling == NVector2.One && binding.Transform.Rotation == 0 && binding.Transform.Apply( new NVector2( .25f, .75f ) ) == new NVector2( .25f, .75f ), "Source identity transform changed." ); assertions++;
			}
			RequireUnchanged( upload, before ); assertions++;
		}
		return assertions;
	}
	private static int CheckMissing( TextureCoordinateReference.Form form )
	{
		using var document = form.Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 ); var before = Snapshot( upload );
		var original = global::Sandbox.GltfMaterialUvPlan.Create( document.ReadMaterial( 4 ), upload, 4 );
		var invalid = new GltfMaterial { BaseColorTexture = new GltfTextureInfo { Index = 0, TexCoord = 7, Tiling = NVector2.One, IsSrgb = true } };
		try { _ = global::Sandbox.GltfMaterialUvPlan.Create( invalid, upload, 99 ); }
		catch ( GltfException error ) when ( error.Message.Contains( "99", StringComparison.Ordinal ) && error.Message.Contains( "BaseColor", StringComparison.Ordinal ) && error.Message.Contains( "TEXCOORD_7", StringComparison.Ordinal ) )
		{
			RequireUnchanged( upload, before );
			TextureCoordinateCoreTests.Require( original.Bindings.Count == 1 && original.Bindings[global::Sandbox.GltfMaterialTextureRole.BaseColor].TexCoord == (form.Derived ? 1 : 0), "Failed preparation mutated a previously completed plan." ); return 3;
		}
		throw new InvalidOperationException( "Missing selected source coordinate did not identify material, role and source semantic." );
	}
	private static int CheckRoleTransform( TextureCoordinateReference.Form form )
	{
		using var document = form.Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 ); var before = Snapshot( upload );
		var baseInfo = new GltfTextureInfo { Index = 0, TexCoord = form.Derived ? 1 : 0, IsSrgb = true, Offset = new NVector2( .3f, -.2f ), Tiling = new NVector2( 2, 3 ), Rotation = MathF.PI / 2 };
		var normalInfo = new GltfTextureInfo { Index = 0, TexCoord = 0, Scale = .25f, Tiling = NVector2.One };
		var material = new GltfMaterial { BaseColorTexture = baseInfo, NormalTexture = normalInfo };
		var plan = global::Sandbox.GltfMaterialUvPlan.Create( material, upload, 4 );
		var color = plan.Bindings[global::Sandbox.GltfMaterialTextureRole.BaseColor]; var normal = plan.Bindings[global::Sandbox.GltfMaterialTextureRole.Normal];
		TextureCoordinateCoreTests.Require( plan.Bindings.Count == 2 && ReferenceEquals( color.Source, upload.Attributes["TEXCOORD_" + baseInfo.TexCoord] ) && ReferenceEquals( normal.Source, upload.Attributes["TEXCOORD_0"] ), "Each role must select its own source descriptor." );
		var transformed = color.Transform.Apply( new NVector2( .25f, .75f ) );
		// Independent column-vector arithmetic: (u,v)*scale = (.5,2.25),
		// counterclockwise pi/2 rotation = (-2.25,.5), then offset = (-1.95,.3).
		TextureCoordinateCoreTests.Require( NVector2.Distance( transformed, new NVector2( -1.95f, .3f ) ) < .000001f, "Required scale-then-rotation-then-offset order changed." );
		TextureCoordinateCoreTests.Require( normal.Transform.Apply( new NVector2( .25f, .75f ) ) == new NVector2( .25f, .75f ) && normal.TextureInfo.Scale == .25f, "One role's map or normal scale leaked into another role." );
		RequireUnchanged( upload, before ); return 4;
	}
	private static Dictionary<string, byte[]> Snapshot( GltfPrimitiveUploadData upload ) => upload.Attributes.ToDictionary( value => value.Key, value => value.Value.Storage.Bytes.ToArray(), StringComparer.Ordinal );
	private static void RequireUnchanged( GltfPrimitiveUploadData upload, Dictionary<string, byte[]> before )
	{
		foreach ( var (semantic, bytes) in before ) TextureCoordinateCoreTests.Require( upload.Attributes[semantic].Storage.Bytes.Span.SequenceEqual( bytes ), "UV selection or transform preparation mutated original source bytes: " + semantic );
	}
}
