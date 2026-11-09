using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.PrimitiveModeNormalsValidation;

using static PrimitiveModeNormalsReference;

/// <summary>CPU reader and staging assertions against independently decoded pinned bytes.</summary>
public static class PrimitiveModeNormalsCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 104;
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new PrimitiveModeNormalsReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "all_25_applicable_primitives_and_normal_presence", () =>
			{
				using var document = form.Read(); var root = document.Root;
				Require( document.GetDefaultScene() == 0 && root.GetProperty( "meshes" ).GetArrayLength() == 25 && root.GetProperty( "nodes" ).GetArrayLength() == 28, "Pinned scene inventory changed." );
				Require( !root.TryGetProperty( "extensionsUsed", out _ ) && !root.TryGetProperty( "animations", out _ ) && !root.TryGetProperty( "skins", out _ ), "Expected static core-only sample." );
				for ( var mesh = 0; mesh < 25; mesh++ )
				{
					var primitive = document.ReadPrimitive( mesh, 0 );
					Require( root.GetProperty( "nodes" )[Node( mesh )].GetProperty( "mesh" ).GetInt32() == mesh, "Pinned mesh node identity changed." );
					Require( primitive.Mode == SourceMode( mesh ) && primitive.VertexCount == Count( mesh ) && primitive.Material == Material( mesh ), "Primitive topology/count/material changed at " + mesh );
					Require( primitive.Attributes.ContainsKey( "NORMAL" ) == HasNormal( mesh ) && primitive.Attributes.ContainsKey( "COLOR_0" ) == HasColor( mesh ), "Authored attribute presence changed." );
					Require( primitive.Indices.SequenceEqual( reference.RawIndices( mesh ) ), "Indexed or implicit topology differs from literal raw source." );
				}
			} );
			for ( var mesh = 0; mesh < 25; mesh++ )
			{
				var selected = mesh;
				Run( "mesh_" + mesh + "_source_descriptors_and_entire_views", () => Streams( form, reference, selected ) );
				Run( "mesh_" + mesh + "_runtime_plan_topology_and_layout", () => Plan( form, reference, selected ) );
			}
			Run( "label_texture_and_material_policy_inputs_survive_packaging", () =>
			{
				using var document = form.Read();
				Require( document.ReadImage( 0 ).Bytes.SequenceEqual( reference.Blobs["Labels.png"] ), "Label texture changed or was omitted as an external package dependency." );
				var metal = document.ReadMaterial( 0 ); var label = document.ReadMaterial( 1 );
				Require( metal.MetallicFactor == 1 && metal.RoughnessFactor == 0 && metal.EmissiveFactor.Y == .1f && metal.EmissiveFactor.Z == .1f, "Metallic/emissive sample material changed." );
				Require( label.AlphaMode == "MASK" && label.DoubleSided && label.BaseColorTexture is not null && label.EmissiveTexture is not null, "Label material lost alpha/texture policy." );
			} );
			void Run( string name, Action test ) { try { test(); results.Add( new( name, form.Name, true, null ) ); } catch ( Exception error ) { results.Add( new( name, form.Name, false, error.ToString() ) ); } }
		}
		Require( results.Count == ExpectedCases, "Frozen CPU case inventory changed." ); return results.ToArray();
	}
	private static void Streams( PrimitiveModeNormalsReference.Form form, PrimitiveModeNormalsReference reference, int mesh )
	{
		using var document = form.Read(); var upload = document.MaterializePrimitiveUpload( mesh, 0 );
		foreach ( var vertex in new[] { 0, Count( mesh ) - 1 } )
		{
			var expected = reference.SourceWorldPosition( mesh, vertex ); var actual = document.EvaluateVertexPosition( Node( mesh ), 0, vertex );
			Require( System.Numerics.Vector3.Distance( actual, expected ) <= .000002f, "Literal parent translation/label scale differs from evaluated source world position." );
		}
		Check( "POSITION", PositionAccessor( mesh ), reference.PositionBytes( mesh ).AsSpan( 0, Count( mesh ) * 12 ), 5126, 3, 12, false );
		if ( HasNormal( mesh ) )
		{
			Check( "NORMAL", NormalAccessor( mesh ), reference.PositionBytes( mesh ).AsSpan( 0, Count( mesh ) * 12 ), 5126, 3, 12, false );
			Require( ReferenceEquals( upload.Attributes["POSITION"].Storage, upload.Attributes["NORMAL"].Storage ), "Aliased source POSITION/NORMAL must retain shared view storage." );
		}
		if ( HasColor( mesh ) ) Check( "COLOR_0", ColorAccessor( mesh ), reference.Blobs["Colors.bin"], 5121, 4, 4, true );
		if ( mesh == 24 ) Check( "TEXCOORD_0", 8, reference.Blobs["Plane.bin"].AsSpan( 48, 32 ), 5126, 2, 8, false );
		if ( mesh == 24 || mesh % 3 == 2 )
		{
			var indices = upload.Indices; var expected = reference.RawIndices( mesh );
			Require( indices.ComponentType == 5123 && indices.Components == 1 && indices.ByteStride == 2 && !indices.Normalized && indices.Count == expected.Length, "Index format was inferred incorrectly." );
			for ( var i = 0; i < expected.Length; i++ ) Require( BinaryPrimitives.ReadUInt16LittleEndian( indices.Storage.Bytes.Span[(indices.ByteOffset + i * indices.ByteStride)..] ) == expected[i], "Index source view origin differs." );
		}
		else Require( upload.Indices is null && upload.Layout.RequiresGeneratedIndices, "Nonindexed points/lines were silently reinterpreted." );
		void Check( string semantic, int accessor, ReadOnlySpan<byte> expected, int componentType, int components, int stride, bool normalized )
		{
			var stream = upload.Attributes[semantic];
			Require( stream.Count == Count( mesh ) && stream.ComponentType == componentType && stream.Components == components && stream.ByteStride == stride && stream.ByteOffset == 0 && stream.Normalized == normalized && !stream.IsMaterialized, "Raw descriptor differs: " + semantic );
			Require( stream.Binding.AccessorIndex == accessor && stream.Storage.Bytes.Span.SequenceEqual( expected ), "Full source view differs: " + semantic );
			var sourceView = document.Root.GetProperty( "bufferViews" )[stream.Binding.SourceBufferView.Value];
			var viewOffset = sourceView.TryGetProperty( "byteOffset", out var value ) ? value.GetInt32() : 0;
			Require( stream.Binding.ViewByteOffset == viewOffset && stream.Binding.ViewByteLength == expected.Length, "Authored or compiled buffer-view provenance was lost." );
		}
	}
	private static void Plan( PrimitiveModeNormalsReference.Form form, PrimitiveModeNormalsReference reference, int mesh )
	{
		using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, mesh, 0 ); var expanded = reference.ExpandedIndices( mesh );
		Require( plan.Mode == DrawMode( mesh ) && plan.GenerateFlatNormals == Flat( mesh ) && plan.Material == Material( mesh ), "Mode or normal generation policy differs." );
		Require( plan.SourceVertices.SequenceEqual( Flat( mesh ) ? expanded : Enumerable.Range( 0, Count( mesh ) ).Select( x => (uint)x ) ), "Output source mapping lost valid vertices or flat corners." );
		Require( plan.Indices.Length == expanded.Length && plan.Indices.Select( i => plan.SourceVertices[i] ).SequenceEqual( expanded ), "Expanded topology/winding differs from independently decoded source." );
		Require( plan.VertexStride > 0 && plan.VertexStride % 4 == 0, "Runtime stride invalid." );
		var fields = plan.Fields.OrderBy( f => f.Offset ).ToArray();
		foreach ( var f in fields ) Require( f.ComponentType == 5126 && f.Components is >= 1 and <= 4 && f.Offset >= 0 && f.Offset % 4 == 0 && f.Offset + f.Components * 4 <= plan.VertexStride, "Runtime field representation/range invalid." );
		for ( var i = 1; i < fields.Length; i++ ) Require( fields[i - 1].Offset + fields[i - 1].Components * 4 <= fields[i].Offset, "Runtime fields overlap." );
		foreach ( var semantic in new[] { "POSITION", "NORMAL", "COLOR", "TEXCOORD" } ) Require( fields.Count( f => f.Semantic == semantic && f.SemanticIndex == 0 ) == 1, "Runtime field missing/duplicated: " + semantic );
		foreach ( var binding in plan.Sources.Values )
		{
			var start = binding.ArenaByteOffset - binding.Stream.ByteOffset;
			Require( start >= 0 && start % 4 == 0 && start + binding.Stream.Storage.Bytes.Length <= plan.InputBytes.Length, "Descriptor arena range invalid." );
			Require( plan.InputBytes.AsSpan( start, binding.Stream.Storage.Bytes.Length ).SequenceEqual( binding.Stream.Storage.Bytes.Span ), "Arena changed raw source bytes." );
		}
		if ( HasNormal( mesh ) ) Require( plan.Sources["POSITION"].ArenaByteOffset == plan.Sources["NORMAL"].ArenaByteOffset, "Distinct NORMAL/POSITION descriptors must reference aliased raw bytes correctly." );
	}
}
