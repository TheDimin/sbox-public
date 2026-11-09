using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Gltf;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Tests the actually loaded producer against source_oracle.py expectations.
/// Compile into the coordinator's test helper; these tests allocate no renderer
/// resources. GPU readback and image acceptance remain separate requirements.
/// </summary>
public static class OrientationCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve, JsonElement Expected )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}

	public static Result[] RunAll( string checkoutRoot, string oraclePath )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		return RunForms( OpenForms( checkoutRoot, oracle.RootElement ) );
	}

	public static Result[] RunFixture( string sourcePath, string oraclePath )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var bytes = File.ReadAllBytes( sourcePath );
		Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == oracle.RootElement.GetProperty( "source_sha256" ).GetString(), "Diagnostic fixture hash differs from independent oracle." );
		return RunForms( new[]
		{
			new Form( "synthetic_interleaved/source", bytes, null, oracle.RootElement ),
			new Form( "synthetic_interleaved/compiled_glb", GltfCompiler.Compile( bytes ), null, oracle.RootElement )
		} );
	}

	private static Result[] RunForms( IEnumerable<Form> forms )
	{
		var results = new List<Result>();
		foreach ( var form in forms )
		{
			Run( "all_authored_nodes_and_material_slots_preserved", form, TestScene );
			Run( "actual_source_formats_offsets_strides_and_index_bytes", form, TestUploads );
			Run( "rest_world_positions_match_independent_column_vector_reference", form, TestPose );
			Run( "engine_positions_normals_winding_match_independent_reference", form, TestAssembled );
			Run( "owned_uploads_survive_source_document_disposal", form, TestUploadLifetime );
		}
		return results.ToArray();

		void Run( string name, Form form, Func<Form, int> test )
		{
			try { results.Add( new Result( name, form.Name, test( form ), true, null ) ); }
			catch ( Exception error ) { results.Add( new Result( name, form.Name, 0, false, error.ToString() ) ); }
		}
	}

	public static IEnumerable<Form> OpenForms( string checkoutRoot, JsonElement oracle )
	{
		foreach ( var variant in oracle.GetProperty( "variants" ).EnumerateObject() )
		{
			var relative = variant.Name == "glTF-Binary" ? "glTF-Binary/OrientationTest.glb" : variant.Name + "/OrientationTest.gltf";
			var path = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/OrientationTest", relative );
			var source = File.ReadAllBytes( path );
			Require( Convert.ToHexStringLower( SHA256.HashData( source ) ) == variant.Value.GetProperty( "source_sha256" ).GetString(), "Independent oracle source hash does not match " + path );
			byte[] Resolve( string uri )
			{
				var parent = Path.GetFullPath( Path.GetDirectoryName( path ) ) + Path.DirectorySeparatorChar;
				var target = Path.GetFullPath( Path.Combine( parent, Uri.UnescapeDataString( uri ) ) );
				Require( target.StartsWith( parent, StringComparison.Ordinal ), "Model dependency escapes its variant directory." );
				return File.ReadAllBytes( target );
			}
			yield return new Form( variant.Name + "/source", source, Resolve, variant.Value );
			yield return new Form( variant.Name + "/compiled_glb", GltfCompiler.Compile( source, Resolve ), Resolve, variant.Value );
		}
	}

	private static int TestScene( Form form )
	{
		using var document = form.Read();
		Require( document.GetDefaultScene() == 0, "Pinned sample selects scene zero." );
		Require( document.Root.GetProperty( "nodes" ).GetArrayLength() == 13, "All 13 nodes are required." );
		Require( document.Root.GetProperty( "meshes" ).GetArrayLength() == 13, "All 13 meshes are required." );
		Require( document.Root.GetProperty( "materials" ).GetArrayLength() == 7, "All 7 material slots are required." );
		Require( !document.Root.TryGetProperty( "animations", out _ ) && !document.Root.TryGetProperty( "skins", out _ ), "OrientationTest is static; do not misclassify it as animated." );
		var count = 5;
		foreach ( var row in form.Expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var node = row.GetProperty( "node" ).GetInt32();
			var source = document.Root.GetProperty( "nodes" )[node];
			Require( source.GetProperty( "name" ).GetString() == row.GetProperty( "node_name" ).GetString(), "Authored node identity changed." );
			Require( source.GetProperty( "mesh" ).GetInt32() == row.GetProperty( "mesh" ).GetInt32(), "Authored mesh binding changed." );
			var primitive = document.ReadPrimitive( row.GetProperty( "mesh" ).GetInt32(), row.GetProperty( "primitive" ).GetInt32() );
			Require( primitive.Material == row.GetProperty( "material" ).GetInt32(), "Material slot assignment changed." );
			Require( primitive.Mode == 4, "OrientationTest must remain triangles." );
			count += 4;
		}
		return count;
	}

	private static int TestUploads( Form form )
	{
		using var document = form.Read();
		var count = 0;
		var indexFormats = new HashSet<int>();
		foreach ( var row in form.Expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var mesh = row.GetProperty( "mesh" ).GetInt32();
			var primitive = row.GetProperty( "primitive" ).GetInt32();
			var upload = document.MaterializePrimitiveUpload( mesh, primitive );
			var declared = document.Root.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[primitive];
			Require( upload.Layout.SourceMode == 4 && !upload.Layout.RequiresTopologyExpansion, "Triangle topology changed." );
			Require( upload.Attributes.Count == 2 && upload.Attributes.ContainsKey( "POSITION" ) && upload.Attributes.ContainsKey( "NORMAL" ), "Pinned source attributes are POSITION and NORMAL only." );
			foreach ( var attribute in declared.GetProperty( "attributes" ).EnumerateObject() )
			{
				var stream = upload.Attributes[attribute.Name];
				var accessor = document.Root.GetProperty( "accessors" )[attribute.Value.GetInt32()];
				var view = document.Root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
				Require( stream.ComponentType == accessor.GetProperty( "componentType" ).GetInt32(), "Upload component type must come from the accessor." );
				Require( stream.Components == 3 && stream.Count == accessor.GetProperty( "count" ).GetInt32(), "Upload arity/count mismatch." );
				Require( stream.ByteOffset == IntegerOr( accessor, "byteOffset", 0 ), "Upload offset is view-relative, never buffer-relative." );
				Require( stream.ByteStride == IntegerOr( view, "byteStride", 12 ), "Upload stride must come from this source view." );
				Require( stream.Storage.Bytes.Length == view.GetProperty( "byteLength" ).GetInt32(), "Owned upload view length changed." );
				var expected = row.GetProperty( attribute.Name == "POSITION" ? "source_positions" : "source_normals" );
				for ( var vertex = 0; vertex < stream.Count; vertex++ )
					Vector( JsonVector( expected[vertex] ), Float3( stream, vertex ), 0, $"{form.Name}, {attribute.Name}, mesh {mesh}, vertex {vertex}" );
				count += 5 + stream.Count;
			}
			var indices = upload.Indices;
			Require( indices is not null && indices.Components == 1 && !indices.Normalized, "Expected scalar non-normalized index stream." );
			var positionView = upload.Attributes["POSITION"].Binding.SourceBufferView;
			var normalView = upload.Attributes["NORMAL"].Binding.SourceBufferView;
			if ( positionView == normalView )
			{
				Require( ReferenceEquals( upload.Attributes["POSITION"].Storage, upload.Attributes["NORMAL"].Storage ), "Attributes sharing one source bufferView must share its owned upload snapshot." );
				count++;
			}
			var distinctBytes = upload.Attributes.Values.Select( stream => stream.Storage ).Append( indices.Storage ).Distinct().Sum( storage => (long)storage.Bytes.Length );
			Require( upload.OwnedBytes == distinctBytes, "Owned-byte accounting disagrees with distinct upload storage." );
			indexFormats.Add( indices.ComponentType );
			var expectedIndices = row.GetProperty( "indices" );
			Require( indices.Count == expectedIndices.GetArrayLength(), "Index count changed." );
			for ( var index = 0; index < indices.Count; index++ )
				Require( Index( indices, index ) == expectedIndices[index].GetUInt32(), "Unsigned index bytes or offsets were interpreted incorrectly." );
			count += 3 + indices.Count;
		}
		Require( indexFormats.SetEquals( new[] { 5121, 5123 } ), "Sample must exercise both U8 and U16 source indices." );
		return count + 1;
	}

	private static int TestPose( Form form )
	{
		using var document = form.Read();
		var pose = document.EvaluatePose();
		var count = 0;
		foreach ( var row in form.Expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var node = row.GetProperty( "node" ).GetInt32();
			var world = document.GetWorldTransform( node );
			Require( pose.GetWorldTransform( node ) == world, "Pose and direct rest transform disagree." );
			var local = row.GetProperty( "source_positions" );
			var expected = row.GetProperty( "source_world_positions" );
			for ( var vertex = 0; vertex < local.GetArrayLength(); vertex++ )
				Vector( JsonVector( expected[vertex] ), NVector.Transform( JsonVector( local[vertex] ), world ), .000005f, $"{form.Name}, rest world node {node}, vertex {vertex}" );
			count += 1 + local.GetArrayLength();
		}
		return count;
	}

	private static int TestAssembled( Form form )
	{
		using var document = form.Read();
		var assembled = GltfStaticSceneAssembler.Assemble( document );
		Require( assembled.Primitives.Count == 13 && assembled.HasBounds, "Selected model lost primitives/bounds." );
		var count = 1;
		foreach ( var row in form.Expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var node = row.GetProperty( "node" ).GetInt32();
			var data = assembled.Primitives.Single( value => value.Node == node && value.Primitive == row.GetProperty( "primitive" ).GetInt32() ).Data;
			var positions = row.GetProperty( "engine_world_positions" );
			var normals = row.GetProperty( "engine_world_normals" );
			Require( data.Positions.Length == positions.GetArrayLength() && data.Normals.Length == normals.GetArrayLength(), "Assembled stream lengths changed." );
			for ( var vertex = 0; vertex < data.Positions.Length; vertex++ )
			{
				Vector( JsonVector( positions[vertex] ), data.Positions.Span[vertex], .0002f, $"{form.Name}, engine position node {node}, vertex {vertex}" );
				Vector( JsonVector( normals[vertex] ), data.Normals.Span[vertex], .000002f, $"{form.Name}, engine normal node {node}, vertex {vertex}" );
			}
			var indices = row.GetProperty( "indices" );
			Require( data.Indices.Length == indices.GetArrayLength(), "Assembled index count changed." );
			for ( var index = 0; index < data.Indices.Length; index++ )
				Require( data.Indices.Span[index] == indices[index].GetUInt32(), "Positive-determinant sample must retain authored front-face order." );
			count += 2 + data.Positions.Length * 2 + data.Indices.Length;
		}
		return count;
	}

	private static int TestUploadLifetime( Form form )
	{
		var owned = new List<(JsonElement Row, GltfPrimitiveUploadData Upload)>();
		using ( var document = form.Read() )
			foreach ( var row in form.Expected.GetProperty( "primitives" ).EnumerateArray() )
				owned.Add( (row, document.MaterializePrimitiveUpload( row.GetProperty( "mesh" ).GetInt32(), row.GetProperty( "primitive" ).GetInt32() )) );
		var count = 0;
		foreach ( var (row, upload) in owned )
		{
			var positions = row.GetProperty( "source_positions" );
			for ( var vertex = 0; vertex < positions.GetArrayLength(); vertex++ )
				Vector( JsonVector( positions[vertex] ), Float3( upload.Attributes["POSITION"], vertex ), 0, "Owned upload after document disposal" );
			count += positions.GetArrayLength();
		}
		return count;
	}

	internal static NVector JsonVector( JsonElement value ) => new( value[0].GetSingle(), value[1].GetSingle(), value[2].GetSingle() );
	private static int IntegerOr( JsonElement value, string property, int fallback ) => value.TryGetProperty( property, out var result ) ? result.GetInt32() : fallback;
	private static NVector Float3( GltfUploadStream stream, int vertex )
	{
		Require( stream.ComponentType == 5126 && stream.Components == 3 && !stream.Normalized, "Expected source FLOAT3 stream." );
		var bytes = stream.Storage.Bytes.Span;
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride);
		return new NVector( BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( offset, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( offset + 4, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( offset + 8, 4 ) ) );
	}

	private static uint Index( GltfUploadStream stream, int index )
	{
		var bytes = stream.Storage.Bytes.Span.Slice( checked(stream.ByteOffset + index * stream.ByteStride) );
		return stream.ComponentType switch { 5121 => bytes[0], 5123 => BinaryPrimitives.ReadUInt16LittleEndian( bytes ), 5125 => BinaryPrimitives.ReadUInt32LittleEndian( bytes ), _ => throw new InvalidOperationException( "Unsupported index component type." ) };
	}

	internal static void Vector( NVector expected, NVector actual, float tolerance, string label )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ), label + " is not finite." );
		Require( MathF.Abs( expected.X - actual.X ) <= tolerance && MathF.Abs( expected.Y - actual.Y ) <= tolerance && MathF.Abs( expected.Z - actual.Z ) <= tolerance, $"{label}: expected {expected}, got {actual}, tolerance {tolerance:R}." );
	}

	internal static void Require( bool condition, string message )
	{
		if ( !condition ) throw new InvalidOperationException( message );
	}
}
