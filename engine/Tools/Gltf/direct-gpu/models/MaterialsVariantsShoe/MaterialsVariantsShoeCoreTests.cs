using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MaterialsVariantsShoeValidation;

using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using static MaterialsVariantsShoeReference;

/// <summary>Independent model CPU/data checks; no native/GPU object is created.</summary>
public static class MaterialsVariantsShoeCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var inputs = new Inputs( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in inputs.Forms )
		{
			Run( "pinned_inventory_and_variant_names", form.Name, () => Inventory( form ) );
			Run( "standard_default_and_named_material_resolution", form.Name, () => Mappings( form ) );
			Run( "raw_descriptor_view_origins_and_all_owned_bytes", form.Name, () => Streams( form ) );
			Run( "independent_hierarchy_world_and_sample_values", form.Name, () => Samples( form ) );
			Run( "distinct_variant_image_payloads_and_shared_linear_roles", form.Name, () => Materials( form ) );
			Run( "unsupported_optional_extension_core_fallback", form.Name, () => OptionalFallback( form ) );
			Run( "owned_geometry_lifetime_and_exact_budget", form.Name, () => Lifetime( form ) );
		}
		Run( "shuffled_mapping_is_not_variant_equals_material", "derived fixture", () =>
		{
			using var document = inputs.ReadFixture( root => Primitive( root )["extensions"][Extension]["mappings"] = JsonNode.Parse( "[{\"material\":2,\"variants\":[0]},{\"material\":0,\"variants\":[1]},{\"material\":1,\"variants\":[2]}]" ) );
			for ( var i = 0; i < 3; i++ ) Require( document.GetMaterialForVariant( 0, 0, i ) == new[] { 2, 0, 1 }[i], "Variant index was treated as a material index." );
		} );
		Run( "grouped_mapping_and_unmapped_default_reset", "derived fixture", () =>
		{
			using var document = inputs.ReadFixture( root => Primitive( root )["extensions"][Extension]["mappings"] = JsonNode.Parse( "[{\"material\":1,\"variants\":[0,2]}]" ) );
			foreach ( var (variant, material) in new[] { ((int?)0, 1), ((int?)2, 1), ((int?)1, 0), ((int?)null, 0), ((int?)0, 1) } )
				Require( document.GetMaterialForVariant( 0, 0, variant ) == material, "Grouped mapping or unmapped/null fallback is sticky." );
		} );
		Run( "unmapped_variant_without_primitive_material_is_default_gltf", "derived fixture", () =>
		{
			using var document = inputs.ReadFixture( root => { Primitive( root ).AsObject().Remove( "material" ); Primitive( root )["extensions"][Extension]["mappings"] = JsonNode.Parse( "[{\"material\":1,\"variants\":[0]}]" ); } );
			Require( document.GetMaterialForVariant( 0, 0, 0 ) == 1 && document.GetMaterialForVariant( 0, 0, 1 ) is null && document.GetMaterialForVariant( 0, 0, null ) is null, "No-material core fallback must stay null." );
		} );
		Run( "unmapped_primitive_uses_its_own_core_material", "derived fixture", () =>
		{
			using var document = inputs.ReadFixture( root =>
			{
				var duplicate = Primitive( root ).DeepClone(); duplicate["material"] = 2; duplicate.AsObject().Remove( "extensions" ); root["meshes"][0]["primitives"].AsArray().Add( duplicate );
			} );
			Require( document.GetMaterialForVariant( 0, 0, 1 ) == 1 && document.GetMaterialForVariant( 0, 1, 1 ) == 2, "Variant leaked from one primitive to another." );
		} );
		Run( "unsupported_required_extension_rejected", "derived fixture", () => Throws( () => { using var document = inputs.ReadFixture( root => root["extensionsRequired"] = new JsonArray( Extension ), false ); } ) );
		Run( "supported_required_extension_is_usable", "derived fixture", () => { using var document = inputs.ReadFixture( root => root["extensionsRequired"] = new JsonArray( Extension ) ); Require( document.GetMaterialForVariant( 0, 0, 2 ) == 2, "Supported required extension was dropped." ); } );
		Run( "duplicate_variant_mapping_rejected", "derived fixture", () => Throws( () => { using var document = inputs.ReadFixture( root => Primitive( root )["extensions"][Extension]["mappings"][1]["variants"][0] = 0 ); document.GetMaterialForVariant( 0, 0, 0 ); } ) );
		Run( "out_of_range_mapped_variant_rejected", "derived fixture", () => Throws( () => { using var document = inputs.ReadFixture( root => Primitive( root )["extensions"][Extension]["mappings"][2]["variants"][0] = 3 ); document.GetMaterialForVariant( 0, 0, 0 ); } ) );
		Run( "out_of_range_mapped_material_rejected", "derived fixture", () => Throws( () => { using var document = inputs.ReadFixture( root => Primitive( root )["extensions"][Extension]["mappings"][2]["material"] = 3 ); document.GetMaterialForVariant( 0, 0, 0 ); } ) );
		Run( "negative_requested_variant_rejected", "derived fixture", () => Throws( () => { using var document = inputs.Forms[0].Read(); document.GetMaterialForVariant( 0, 0, -1 ); } ) );
		Run( "out_of_range_requested_variant_rejected", "derived fixture", () => Throws( () => { using var document = inputs.Forms[0].Read(); document.GetMaterialForVariant( 0, 0, 3 ); } ) );
		Run( "distinct_source_materials_defeat_sticky_selection", "oracle control", () =>
		{
			var images = inputs.Forms[0].Oracle.GetProperty( "images" ); var hashes = BaseColorTextures.Select( x => images[x].GetProperty( "sha256" ).GetString() ).ToArray();
			Require( hashes.Distinct().Count() == 3 && VariantNames.Distinct().Count() == 3, "Oracle cannot distinguish named variant images." );
		} );
		return rows.ToArray();
		void Run( string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, false, error.ToString() ) ); } }
	}
	private static JsonNode Primitive( JsonNode root ) => root["meshes"][0]["primitives"][0];
	private static void Throws( Action body ) { try { body(); } catch ( GltfException ) { return; } throw new InvalidOperationException( "Expected GltfException." ); }
	private static void Inventory( Form form )
	{
		using var document = form.Read(); var root = document.Root;
		Require( document.GetDefaultScene() == 0 && root.GetProperty( "nodes" ).GetArrayLength() == 3 && root.GetProperty( "meshes" ).GetArrayLength() == 1 && root.GetProperty( "materials" ).GetArrayLength() == 3, "Pinned scene shape changed." );
		Require( !root.TryGetProperty( "animations", out _ ) && !root.TryGetProperty( "skins", out _ ), "Static source acquired animation/skin." );
		var names = root.GetProperty( "extensions" ).GetProperty( Extension ).GetProperty( "variants" ).EnumerateArray().Select( x => x.GetProperty( "name" ).GetString() ).ToArray();
		Require( names.SequenceEqual( VariantNames ) && document.UnsupportedOptionalExtensions.Count == 0, "Variant names/order or selected capability changed." );
	}
	private static void Mappings( Form form )
	{
		using var document = form.Read();
		foreach ( var (variant, material) in new[] { ((int?)null, 0), ((int?)2, 2), ((int?)0, 0), ((int?)1, 1), ((int?)null, 0) } )
			Require( document.GetMaterialForVariant( 0, 0, variant ) == material, "Authored material mapping/default reset changed." );
		Require( document.ReadPrimitive( 0, 0 ).Material == 0, "Selection mutated authored primitive default." );
	}
	private static void Streams( Form form )
	{
		using var document = form.Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		Require( upload.Layout.VertexCount == VertexCount && upload.Layout.SourceMode == 4 && upload.Attributes.Keys.Order().SequenceEqual( new[] { "NORMAL", "POSITION", "TEXCOORD_0" } ), "Primitive inventory changed." );
		foreach ( var pair in form.Oracle.GetProperty( "streams" ).EnumerateObject() )
		{
			var expected = pair.Value; var stream = pair.Name == "INDICES" ? upload.Indices : upload.Attributes[pair.Name];
			Require( stream.Count == expected.GetProperty( "count" ).GetInt32() && stream.ComponentType == expected.GetProperty( "component_type" ).GetInt32() && stream.Components == expected.GetProperty( "components" ).GetInt32() && !stream.Normalized && !stream.IsMaterialized, "Source format changed: " + pair.Name );
			Require( stream.ByteOffset == 0 && stream.ByteStride == expected.GetProperty( "stride" ).GetInt32() && stream.Binding.ViewByteOffset == expected.GetProperty( "view_offset" ).GetInt32(), "View origin/relative offset or stride changed: " + pair.Name );
			Require( stream.Storage.Bytes.Length == expected.GetProperty( "length" ).GetInt32() && Hash( stream.Storage.Bytes.Span ) == expected.GetProperty( "sha256" ).GetString(), "Entire raw view differs: " + pair.Name );
		}
	}
	private static void Samples( Form form )
	{
		using var document = form.Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 ); var pose = document.EvaluatePose();
		foreach ( var sample in form.Oracle.GetProperty( "samples" ).EnumerateArray() )
		{
			var vertex = sample.GetProperty( "vertex" ).GetInt32();
			foreach ( var (semantic, property) in new[] { ("POSITION", "position"), ("NORMAL", "normal"), ("TEXCOORD_0", "uv") } )
			{
				var stream = upload.Attributes[semantic]; var expected = Numbers( sample.GetProperty( property ) );
				for ( var c = 0; c < expected.Length; c++ ) Require( BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span[(stream.ByteOffset + vertex * stream.ByteStride + c * 4)..] ) == (float)expected[c], "Independent raw sample differs." );
			}
			Vector( document.EvaluateVertexPosition( 0, 0, vertex, pose ), Numbers( sample.GetProperty( "world" ) ), 6e-8, "Independent authored hierarchy sample differs." );
		}
	}
	private static void Materials( Form form )
	{
		using var document = form.Read();
		for ( var i = 0; i < 3; i++ )
		{
			var material = document.ReadMaterial( i );
			Require( material.BaseColorFactor == NVector4.One && material.MetallicFactor == 1 && material.RoughnessFactor == 1 && material.EmissiveFactor == NVector3.Zero && material.AlphaMode == "OPAQUE" && !material.DoubleSided && !material.Unlit, "Authored PBR factors or flags changed." );
			Require( material.BaseColorTexture.Index == BaseColorTextures[i] && material.BaseColorTexture.IsSrgb && material.BaseColorTexture.TexCoord == 0, "Distinct variant base-color texture was lost." );
			Require( material.NormalTexture.Index == 2 && !material.NormalTexture.IsSrgb && material.NormalTexture.Scale == 1 && material.OcclusionTexture.Index == 0 && !material.OcclusionTexture.IsSrgb && material.OcclusionTexture.Strength == 1 && material.MetallicRoughnessTexture.Index == 0 && !material.MetallicRoughnessTexture.IsSrgb, "Shared linear normal/ORM bindings changed." );
		}
		for ( var i = 0; i < 5; i++ )
		{
			var texture = document.ReadTexture( i ); Require( texture.Image == i && texture.Sampler == new[] { 0, 1, 2, 0, 0 }[i], "Image/sampler link changed." );
			var image = document.ReadImage( i ); var expected = form.Oracle.GetProperty( "images" )[i];
			Require( image.MimeType == "image/jpeg" && image.Bytes.Length == expected.GetProperty( "length" ).GetInt32() && Hash( image.Bytes ) == expected.GetProperty( "sha256" ).GetString(), "Image source payload differs." );
		}
		for ( var i = 0; i < 3; i++ ) { var sampler = document.ReadSampler( i ); Require( sampler.MagFilter == 9729 && sampler.MinFilter == 9985 && sampler.WrapS == 10497 && sampler.WrapT == 10497, "LINEAR_MIPMAP_NEAREST repeat sampler changed." ); }
	}
	private static void OptionalFallback( Form form )
	{
		using var document = form.Read( false ); Require( document.UnsupportedOptionalExtensions.SequenceEqual( new[] { Extension } ), "Unsupported optional extension was hidden." );
		foreach ( var variant in new int?[] { null, 0, 1, 2 } ) Require( document.GetMaterialForVariant( 0, 0, variant ) == 0, "Unsupported optional extension did not use primitive core fallback." );
	}
	private static void Lifetime( Form form )
	{
		GltfPrimitiveUploadData upload;
		using ( var document = form.Read() )
		{
			Throws( () => document.MaterializePrimitiveUpload( 0, 0, 705679 ) );
			upload = document.MaterializePrimitiveUpload( 0, 0, 705680 ); Require( upload.OwnedBytes == 705680, "Owned geometry budget is not exact." );
		}
		Require( Hash( upload.Attributes["POSITION"].Storage.Bytes.Span ) == form.Oracle.GetProperty( "streams" ).GetProperty( "POSITION" ).GetProperty( "sha256" ).GetString(), "Owned upload depends on disposed document storage." );
	}
	public static void Vector( NVector3 actual, double[] expected, double tolerance, string message )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) && Math.Abs( actual.X - expected[0] ) <= tolerance && Math.Abs( actual.Y - expected[1] ) <= tolerance && Math.Abs( actual.Z - expected[2] ) <= tolerance, message + $" Observed {actual}; expected {string.Join( ",", expected )}." );
	}
}
