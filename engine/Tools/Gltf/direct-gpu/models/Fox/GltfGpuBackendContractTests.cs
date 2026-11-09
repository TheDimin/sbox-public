using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu;

using NMatrix = System.Numerics.Matrix4x4;
using NV3 = System.Numerics.Vector3;

/// <summary>Pure descriptor/ownership checks. These do not claim a GPU draw.</summary>
public static class GltfGpuBackendContractTests
{
	public static IReadOnlyList<string> Run()
	{
		var passed = new List<string>();
		using var document = InterleavedTriangle();
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var position = plan.Sources["POSITION"];
		var normal = plan.Sources["NORMAL"];
		var color = plan.Sources["COLOR_0"];
		Require( position.Stream.ByteOffset == 4 && position.Stream.ByteStride == 32, "Source position offset/stride were inferred instead of read." );
		Require( normal.Stream.ByteOffset == 16 && color.Stream.ByteOffset == 28, "Shared view accessor offsets were lost." );
		Require( color.Stream.ComponentType == 5121 && color.Stream.Normalized && color.Stream.Components == 4, "Normalized U8 color descriptor was canonicalized before upload." );
		Require( ReferenceEquals( position.Stream.Storage, normal.Stream.Storage ) && ReferenceEquals( position.Stream.Storage, color.Stream.Storage ), "Shared source view was copied per semantic." );
		Require( position.ArenaByteOffset == 4 && normal.ArenaByteOffset == 16 && color.ArenaByteOffset == 28, "Arena offsets included the source buffer origin twice." );
		Require( BinaryPrimitives.ReadSingleLittleEndian( plan.InputBytes.AsSpan( position.ArenaByteOffset + 32, 4 ) ) == 1f, "Raw source position bytes changed." );
		Require( plan.InputBytes[color.ArenaByteOffset + 1] == 128, "Raw normalized color bytes changed." );
		passed.Add( "interleaved offsets, raw formats, view dedup and arena origins" );

		Require( plan.SourceVertices.SequenceEqual( new uint[] { 0, 1, 2 } ), "An authored-normal triangle unexpectedly expanded its vertices." );
		Require( plan.Indices.SequenceEqual( new uint[] { 0, 1, 2 } ), "U8 source index decoding did not preserve topology." );
		var outputs = plan.Fields.ToDictionary( x => (x.Semantic, x.SemanticIndex) );
		Require( outputs[("POSITION", 0)].Components == 3 && outputs[("NORMAL", 0)].Components == 3, "Stock draw position/normal outputs have the wrong dimensions." );
		Require( outputs[("COLOR", 0)].Components == 4 && outputs[("TANGENT", 0)].Components == 4 && outputs[("TEXCOORD", 0)].Components == 2, "Declared source/generated stock draw dimensions are wrong." );
		Require( plan.Fields.All( x => x.ComponentType == 5126 && x.Offset >= 0 && x.Offset + x.Components * 4 <= plan.VertexStride ), "An output field exceeds the declared dynamic layout." );
		Require( plan.Fields.Select( x => x.Offset ).Distinct().Count() == plan.Fields.Count, "Output fields overlap." );
		passed.Add( "source U8 topology and declared generated stock-shader fields" );

		using var flatDocument = FlatIndexedQuad();
		var flat = GltfGpuPrimitivePlan.Create( flatDocument, 0, 0 );
		Require( flat.SourceVertices.SequenceEqual( new uint[] { 0, 1, 2, 0, 2, 3 } ), "Missing normals require face corners mapped to original source vertices." );
		Require( flat.Indices.SequenceEqual( new uint[] { 0, 1, 2, 3, 4, 5 } ), "Flat corner output topology is incorrect." );
		Require( flat.GenerateFlatNormals && !plan.GenerateFlatNormals, "Authored versus generated normals were not distinguished." );
		passed.Add( "indexed flat shading retains source-vertex provenance" );

		using var skinDocument = NonUnitSkin();
		var skinPlan = GltfGpuPrimitivePlan.Create( skinDocument, 0, 0 );
		var rig = new GltfGpuRig( skinDocument, 0, 0, 0, skinPlan );
		var pose = skinDocument.EvaluatePose(); var palette = rig.Evaluate( pose );
		var placement = NMatrix.CreateTranslation( 7, 11, 13 );
		var bounds = GltfGpuRig.Bounds( new[] { (rig, palette, Array.Empty<float>()) }, placement );
		// Two quarter-weight joints: source (2,3,5)*.5 + (4,0,0)*.25
		// + (0,6,0)*.25 = (2,3,2.5), then basis and placement ONCE.
		var expected = new NV3( 2.5f / .0254f + 7, 2f / .0254f + 11, 3f / .0254f + 13 );
		var center = bounds.Center;
		Require( MathF.Abs( center.x - expected.X ) < .0001f && MathF.Abs( center.y - expected.Y ) < .0001f && MathF.Abs( center.z - expected.Z ) < .0001f,
			"Nonunit FLOAT weights scaled the placement translation or were silently normalized." );
		Require( !rig.ReverseWinding( pose, placement ) && rig.ReverseWinding( pose, NMatrix.CreateScale( 1, -1, 1 ) * placement ), "Placement reflection was not exposed to the draw's front-face rule." );
		passed.Add( "unmodified nonunit FLOAT weights apply placement translation once" );

		using var noScene = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" ) );
		Require( noScene.GetDefaultScene() is null, "Core asset without a default scene was incorrectly rejected or assigned one." );
		passed.Add( "valid no-default-scene input remains valid independently of scene import" );
		return passed;
	}

	public static GltfDocument InterleavedTriangle()
	{
		// Buffer origin 8, attribute offsets 4/16/28, stride32, trailing U8
		// indices. Garbage bytes ensure decoding the first position at zero fails.
		var bytes = Enumerable.Repeat( (byte)0xA7, 107 ).ToArray();
		var positions = new[] { new[] { 0f, 0f, 0f }, new[] { 1f, 0f, 0f }, new[] { 0f, 1f, 0f } };
		for ( var i = 0; i < 3; i++ )
		{
			for ( var c = 0; c < 3; c++ ) Float( bytes, 8 + i * 32 + 4 + c * 4, positions[i][c] );
			Float( bytes, 8 + i * 32 + 16, 0 ); Float( bytes, 8 + i * 32 + 20, 0 ); Float( bytes, 8 + i * 32 + 24, 1 );
			bytes[8 + i * 32 + 28] = 64; bytes[8 + i * 32 + 29] = 128; bytes[8 + i * 32 + 30] = 192; bytes[8 + i * 32 + 31] = 255;
		}
		bytes[104] = 0; bytes[105] = 1; bytes[106] = 2;
		const string json = """
		{"asset":{"version":"2.0"},"buffers":[{"uri":"mesh.bin","byteLength":107}],
		 "bufferViews":[{"buffer":0,"byteOffset":8,"byteLength":96,"byteStride":32},{"buffer":0,"byteOffset":104,"byteLength":3}],
		 "accessors":[{"bufferView":0,"byteOffset":4,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
		 {"bufferView":0,"byteOffset":16,"componentType":5126,"count":3,"type":"VEC3"},
		 {"bufferView":0,"byteOffset":28,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"},
		 {"bufferView":1,"componentType":5121,"count":3,"type":"SCALAR"}],
		 "meshes":[{"primitives":[{"attributes":{"COLOR_0":2,"NORMAL":1,"POSITION":0},"indices":3}]}],
		 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => bytes );
	}

	public static GltfDocument NonUnitSkin()
	{
		var bytes = new byte[36]; Float( bytes, 0, 2 ); Float( bytes, 4, 3 ); Float( bytes, 8, 5 );
		BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 14 ), 1 );
		Float( bytes, 20, .25f ); Float( bytes, 24, .25f );
		const string json = """
		{"asset":{"version":"2.0"},"buffers":[{"uri":"mesh.bin","byteLength":36}],
		 "bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":8},{"buffer":0,"byteOffset":20,"byteLength":16}],
		 "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[2,3,5],"max":[2,3,5]},
		 {"bufferView":1,"componentType":5123,"count":1,"type":"VEC4"},{"bufferView":2,"componentType":5126,"count":1,"type":"VEC4"}],
		 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0,"JOINTS_0":1,"WEIGHTS_0":2}}]}],
		 "skins":[{"joints":[1,2]}],"nodes":[{"mesh":0,"skin":0,"translation":[999,999,999]},{"translation":[4,0,0]},{"translation":[0,6,0]}],
		 "scenes":[{"nodes":[0,1,2]}],"scene":0}
		""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => bytes );
	}

	private static GltfDocument FlatIndexedQuad()
	{
		var bytes = new byte[54];
		var p = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 1f, 1f, 0f, 0f, 1f, 0f };
		for ( var i = 0; i < p.Length; i++ ) Float( bytes, i * 4, p[i] );
		new byte[] { 0, 1, 2, 0, 2, 3 }.CopyTo( bytes, 48 );
		const string json = """
		{"asset":{"version":"2.0"},"buffers":[{"uri":"mesh.bin","byteLength":54}],
		 "bufferViews":[{"buffer":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":6}],
		 "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[0,0,0],"max":[1,1,0]},
		 {"bufferView":1,"componentType":5121,"count":6,"type":"SCALAR"}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1}]}]}
		""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => bytes );
	}

	private static void Float( byte[] bytes, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset, 4 ), value );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
