using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

/// <summary>Actual GPU byte checks and default-stock-material resource proof, not final image/filter proof.</summary>
internal static class BoxTexturedGpuChecks
{
	public sealed record Measurement( int OutputVertices, int Indices, float PositionErrorInches, float NormalError,
		float UvError, bool Finite, bool ActualGpuIndices, bool TopologyMatches, long SubmittedRevision, int DrawCalls );
	public sealed record Residency( string Shader, string TexturePath, int DescriptorWidth, int DescriptorHeight,
		int Mips, string Format, int ImmediatePixelCount, int SettledPixelCount, int AwaitedFrames,
		double ObservationMilliseconds, int DifferentPixels, string RgbaSha256,
		int MetallicTextureFeature, float MetallicFactor, float RoughnessScale,
		string Scope = "native texture base-pixel residency/resource mapping; sampler and final rendered image unproven" );
	public sealed record Row( string Form, string Case, NMatrix Placement, Measurement Gpu, Residency Material );

	public static async Task<Row[]> MeasureAll( string checkoutRoot, global::Sandbox.SceneWorld world,
		global::Sandbox.Material borrowedComputeMaterial, Func<Task> awaitCompletedFrame )
	{
		ArgumentNullException.ThrowIfNull( awaitCompletedFrame ); var reference = BoxTexturedReference.Open( checkoutRoot ); var rows = new List<Row>();
		var placements = new[]
		{
			(Name: "authored_rest", Matrix: NMatrix.Identity),
			(Name: "full_affine_world_shear", Matrix: new NMatrix( 1, .2f, 0, 0, 0, 1.5f, .125f, 0, 0, 0, .75f, 0, 8, -5, 3, 1 )),
			(Name: "reflected_y_single_sided", Matrix: NMatrix.CreateScale( 1, -1, 1 ))
		};
		foreach ( var form in reference.Forms() )
		{
			using var document = form.Read();
			// A null/default resolver is deliberate: the actual model must use its
			// own generated stock material, never an untextured diagnostic override.
			using var asset = GltfGpuAsset.Create( document, borrowedComputeMaterial );
			using var instance = asset.CreateInstance( world );
			BoxTexturedCoreTests.Require( asset.Primitives.Count == 1, "Pinned model must have one shared primitive/material." );
			var material = asset.Primitives[0].Material;
			foreach ( var placement in placements )
			{
				instance.Placement = placement.Matrix; instance.SetPose( document.EvaluatePose() );
				await awaitCompletedFrame();
				var measurement = Measure( instance, form.Independent, placement.Matrix );
				var residency = await ObserveMaterial( material, reference.Rgba, awaitCompletedFrame );
				rows.Add( new( form.Name, placement.Name, placement.Matrix, measurement, residency ) );
			}
		}
		return rows.ToArray();
	}

	public static Measurement Measure( GltfGpuInstance instance, BoxTexturedReference.IndependentPrimitive expected, NMatrix placement )
	{
		BoxTexturedCoreTests.Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && !instance.CallbackErrors.Any(), "Current own-buffer pose was not successfully submitted." );
		var rows = instance.Readback(); BoxTexturedCoreTests.Require( rows.Count == 1, "Pinned model has one node/primitive draw." ); var row = rows[0];
		BoxTexturedCoreTests.Require( row.Node == 1 && row.Mesh == 0 && row.Primitive == 0 && row.VertexCount == 24 && row.VertexStride > 0 && row.Bytes.Length == checked(row.VertexCount * row.VertexStride), "Unexpected primitive identity/output extent." );
		BoxTexturedCoreTests.Require( row.SourceVertices.Length == 24 && row.SourceVertices.All( vertex => vertex < 24 ) && row.Indices.Length == 36 && row.Indices.All( index => index < 24 ), "Missing or out-of-range topology/source mapping." );
		var position = Field( "POSITION", 3 ); var normal = Field( "NORMAL", 3 ); var uv = Field( "TEXCOORD", 2 );
		BoxTexturedCoreTests.Require( NMatrix.Invert( placement, out var inverse ), "Normal comparison requires invertible test placement." );
		var normalTransform = NMatrix.Transpose( inverse ); var positionError = 0f; var normalError = 0f; var uvError = 0f; var finite = true;
		for ( var output = 0; output < row.VertexCount; output++ )
		{
			var source = checked((int)row.SourceVertices[output]);
			var actual = Read3( row.Bytes, output * row.VertexStride + position.Offset ); var actualNormal = Read3( row.Bytes, output * row.VertexStride + normal.Offset );
			var desired = NVector3.Transform( BoxTexturedReference.WorldPosition( expected.Positions[source] ), placement );
			var desiredNormal = NVector3.Normalize( NVector3.TransformNormal( BoxTexturedReference.WorldNormal( expected.Normals[source] ), normalTransform ) );
			var u = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( output * row.VertexStride + uv.Offset ) ); var v = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( output * row.VertexStride + uv.Offset + 4 ) );
			finite &= IsFinite( actual ) && IsFinite( actualNormal ) && float.IsFinite( u ) && float.IsFinite( v );
			positionError = MathF.Max( positionError, Maximum( NVector3.Abs( actual - desired ) ) ); normalError = MathF.Max( normalError, NVector3.Distance( actualNormal, desiredNormal ) );
			uvError = MathF.Max( uvError, MathF.Max( MathF.Abs( u - expected.Uvs[source].X ), MathF.Abs( v - expected.Uvs[source].Y ) ) );
		}
		var actualIndices = row.Indices.Select( index => row.SourceVertices[index] ).ToArray(); var desiredIndices = (uint[])expected.Indices.Clone();
		if ( placement.GetDeterminant() < 0 ) for ( var corner = 0; corner < desiredIndices.Length; corner += 3 ) (desiredIndices[corner], desiredIndices[corner + 1]) = (desiredIndices[corner + 1], desiredIndices[corner]);
		return new( row.VertexCount, row.Indices.Length, positionError, normalError, uvError, finite, row.IndexDataOrigin == "gpu-readback", Canonical( actualIndices ).SequenceEqual( Canonical( desiredIndices ) ), instance.SubmittedRevision, instance.DrawCalls );

		GltfGpuField Field( string semantic, int components )
		{
			var field = row.Fields.Single( field => field.Semantic == semantic && field.SemanticIndex == 0 );
			BoxTexturedCoreTests.Require( field.ComponentType == 5126 && field.Components == components && field.Offset >= 0 && field.Offset + components * 4 <= row.VertexStride, "Output readback does not match declared dynamic field: " + semantic ); return field;
		}
	}

	public static async Task<Residency> ObserveMaterial( global::Sandbox.Material material, byte[] expectedRgba, Func<Task> awaitCompletedFrame )
	{
		BoxTexturedCoreTests.Require( material is { IsValid: true } && !material.IsError && material.ShaderName.Contains( "complex", StringComparison.OrdinalIgnoreCase ), "Actual default model material is not the stock complex shader." );
		var texture = material.GetTexture( "g_tColor" ); BoxTexturedCoreTests.Require( texture is { IsValid: true } && !texture.IsError, "Default material has no native source color texture." );
		var timer = Stopwatch.StartNew(); var pixels = texture.GetPixels(); var immediate = pixels?.Length ?? 0; var frames = 0;
		while ( pixels?.Length != 256 * 256 && frames < 12 && timer.Elapsed < TimeSpan.FromSeconds( 2 ) )
		{
			texture.MarkUsed( 256 ); await awaitCompletedFrame(); frames++; pixels = texture.GetPixels();
		}
		BoxTexturedCoreTests.Require( pixels?.Length == 256 * 256, "Native color texture did not become full-resolution during bounded real-frame residency observation." );
		var differences = 0; var rgba = new byte[pixels.Length * 4];
		for ( var pixel = 0; pixel < pixels.Length; pixel++ )
		{
			rgba[pixel * 4] = pixels[pixel].r; rgba[pixel * 4 + 1] = pixels[pixel].g; rgba[pixel * 4 + 2] = pixels[pixel].b; rgba[pixel * 4 + 3] = pixels[pixel].a;
			if ( !rgba.AsSpan( pixel * 4, 4 ).SequenceEqual( expectedRgba.AsSpan( pixel * 4, 4 ) ) ) differences++;
		}
		return new( material.ShaderName, texture.ResourcePath, texture.Width, texture.Height, texture.Mips, texture.ImageFormat.ToString(), immediate, pixels.Length, frames, timer.Elapsed.TotalMilliseconds,
			differences, Convert.ToHexStringLower( SHA256.HashData( rgba ) ), material.GetFeature( "F_METALNESS_TEXTURE" ), material.GetVector4( "g_flMetalness" ).x, material.GetVector4( "g_flRoughnessScaleFactor" ).x );
	}

	/// <summary>Literal independently decoded expected geometry, no native Model or Mesh construction.</summary>
	public static global::Sandbox.Vertex[] ReferenceTriangles( BoxTexturedReference.IndependentPrimitive expected, NMatrix placement, bool flipImageV = false )
	{
		BoxTexturedCoreTests.Require( NMatrix.Invert( placement, out var inverse ), "Reference needs an invertible placement." ); var normalTransform = NMatrix.Transpose( inverse );
		var indices = (uint[])expected.Indices.Clone(); if ( placement.GetDeterminant() < 0 ) for ( var corner = 0; corner < indices.Length; corner += 3 ) (indices[corner], indices[corner + 1]) = (indices[corner + 1], indices[corner]);
		var vertices = new global::Sandbox.Vertex[indices.Length];
		for ( var corner = 0; corner < indices.Length; corner++ )
		{
			var source = checked((int)indices[corner]); var position = NVector3.Transform( BoxTexturedReference.WorldPosition( expected.Positions[source] ), placement );
			var normal = NVector3.Normalize( NVector3.TransformNormal( BoxTexturedReference.WorldNormal( expected.Normals[source] ), normalTransform ) );
			var tangent = NVector3.Normalize( NVector3.Cross( normal, MathF.Abs( normal.X ) < .7f ? NVector3.UnitX : NVector3.UnitY ) ); var uv = expected.Uvs[source];
			vertices[corner] = new global::Sandbox.Vertex( new global::Vector3( position.X, position.Y, position.Z ) )
			{
				Normal = new global::Vector3( normal.X, normal.Y, normal.Z ), Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, 1 ),
				TexCoord0 = new global::Vector4( uv.X, flipImageV ? 1 - uv.Y : uv.Y, 0, 0 ), Color = global::Color32.White
			};
		}
		return vertices;
	}

	public static void RequireWithin( Row row, float positionToleranceInches, float normalTolerance )
	{
		BoxTexturedCoreTests.Require( float.IsFinite( positionToleranceInches ) && positionToleranceInches >= 0 && float.IsFinite( normalTolerance ) && normalTolerance >= 0, "Require explicit reviewed numerical tolerances." );
		BoxTexturedCoreTests.Require( row.Gpu.Finite && row.Gpu.ActualGpuIndices && row.Gpu.TopologyMatches && row.Gpu.UvError == 0 && row.Gpu.PositionErrorInches <= positionToleranceInches && row.Gpu.NormalError <= normalTolerance, "GPU bytes differ from independently decoded source geometry/UVs." );
		BoxTexturedCoreTests.Require( row.Material.DescriptorWidth == 256 && row.Material.DescriptorHeight == 256 && row.Material.Mips == 9 && row.Material.DifferentPixels == 0 && row.Material.MetallicTextureFeature == 0 && row.Material.MetallicFactor == 0 && row.Material.RoughnessScale == 1, "Default stock material/texture resource mapping differs." );
		// Passing this function does NOT accept sampler behavior, sRGB fetch,
		// final lighting appearance, load-time or runtime-memory parity.
	}
	private static string[] Canonical( uint[] indices )
	{
		var triangles = new string[indices.Length / 3]; for ( var corner = 0; corner < indices.Length; corner += 3 ) { var a = indices[corner]; var b = indices[corner + 1]; var c = indices[corner + 2]; if ( b < a && b < c ) (a,b,c) = (b,c,a); else if ( c < a && c < b ) (a,b,c) = (c,a,b); triangles[corner / 3] = $"{a}/{b}/{c}"; } Array.Sort( triangles, StringComparer.Ordinal ); return triangles;
	}
	private static NVector3 Read3( byte[] bytes, int offset ) => new( BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + 8 ) ) );
	private static float Maximum( NVector3 value ) => MathF.Max( value.X, MathF.Max( value.Y, value.Z ) );
	private static bool IsFinite( NVector3 value ) => float.IsFinite( value.X ) && float.IsFinite( value.Y ) && float.IsFinite( value.Z );
}
