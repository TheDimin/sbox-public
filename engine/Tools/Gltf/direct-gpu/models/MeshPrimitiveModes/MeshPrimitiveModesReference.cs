using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using NVector3 = global::System.Numerics.Vector3;
using NMatrix = global::System.Numerics.Matrix4x4;

/// <summary>Literal, model-specific oracle. No producer decoder, topology helper or pose evaluator supplies expected values.</summary>
public static class MeshPrimitiveModesReference
{
	public const string PinnedCommit = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly string[] ModeNames = ["POINTS", "LINES", "LINE_LOOP", "LINE_STRIP", "TRIANGLES", "TRIANGLE_STRIP", "TRIANGLE_FAN"];
	public static readonly NVector3[] Hexagon = [new( 0, 0, 0 ), new( .866f, -.5f, 0 ), new( .866f, .5f, 0 ), new( 0, 1, 0 ), new( -.866f, .5f, 0 ), new( -.866f, -.5f, 0 ), new( 0, -1, 0 )];
	public static readonly NVector3[] Translations = [new( 0, 3, 0 ), new( -2, 0, 0 ), new( 0, 0, 0 ), new( 2, 0, 0 ), new( -2, -3, 0 ), new( 0, -3, 0 ), new( 2, -3, 0 )];
	public static readonly uint[][] AuthoredIndices =
	[
		[0, 1, 2, 3, 4, 5, 6],
		[0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6],
		[0, 1, 2, 3, 4, 5, 6],
		[0, 1, 2, 3, 4, 5, 6],
		[0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 1],
		[2, 3, 1, 4, 6, 5],
		[0, 1, 2, 3, 4, 5, 6, 1]
	];
	// Explicit oriented lists, including loop closure and alternating strip winding.
	public static readonly uint[][] DrawHexagonIndices =
	[
		[0, 1, 2, 3, 4, 5, 6],
		[0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6],
		[0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 0],
		[0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6],
		[0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 1],
		[2, 3, 1, 1, 3, 4, 1, 4, 6, 6, 4, 5],
		[0, 1, 2, 0, 2, 3, 0, 3, 4, 0, 4, 5, 0, 5, 6, 0, 6, 1]
	];
	public static readonly int[] SourceIndexOffsets = [0, 14, 38, 52, 66, 102, 114];
	public static int DrawMode( int mode ) => mode == 0 ? 0 : mode < 4 ? 1 : 4;
	public static void Require( bool value, string message ) { if ( !value ) throw new InvalidDataException( message ); }
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static NVector3 ToEngine( NVector3 p, NMatrix placement ) => NVector3.Transform( new NVector3( p.Z, p.X, p.Y ) * (1f / .0254f), placement );
	public static NVector3 ExpectedPosition( int mode, uint hexagonVertex, NMatrix placement ) => ToEngine( Hexagon[hexagonVertex] + Translations[mode], placement );
	public static NVector3 ExpectedTriangleNormal( NMatrix placement )
	{
		Require( NMatrix.Invert( placement, out var inverse ), "Singular placement is outside this probe." );
		return NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitX, NMatrix.Transpose( inverse ) ) );
	}
	public static void Near( NVector3 expected, NVector3 actual, float tolerance, string context ) => Require(
		float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) &&
		MathF.Abs( expected.X - actual.X ) <= tolerance && MathF.Abs( expected.Y - actual.Y ) <= tolerance && MathF.Abs( expected.Z - actual.Z ) <= tolerance,
		$"{context}: expected {expected}, actual {actual}, per-component bound {tolerance:R}" );
	public static readonly NMatrix AffinePlacement = new( 1.25f, .2f, 0, 0, 0, .75f, .1f, 0, .1f, 0, -1, 0, 3, -4, 5, 1 );

	public sealed class Inputs
	{
		public Form[] Forms { get; }
		public Inputs( string checkoutRoot )
		{
			var root = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes" );
			var external = File.ReadAllBytes( Path.Combine( root, "glTF/MeshPrimitiveModes.gltf" ) );
			var binary = File.ReadAllBytes( Path.Combine( root, "glTF/buffer.bin" ) );
			var embedded = File.ReadAllBytes( Path.Combine( root, "glTF-Embedded/MeshPrimitiveModes.gltf" ) );
			Require( Hash( external ) == "2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977", "Pinned external JSON changed." );
			Require( Hash( binary ) == "4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7", "Pinned buffer changed." );
			Require( Hash( embedded ) == "9c6dd4c66dd5f4f15c052224b5c1446947ada7f935c521137520dbebbc7f2312", "Pinned embedded JSON changed." );
			var forms = new List<Form>();
			foreach ( var compiled in new[] { false, true } )
			{
				forms.Add( new Form( "glTF", external, binary, compiled, 5123, false, false ) );
				forms.Add( new Form( "glTF-Embedded", embedded, binary, compiled, 5123, false, false ) );
				foreach ( var indexType in new[] { 5121, 5123, 5125, 0 } )
					forms.Add( new Form( indexType == 0 ? "derived/nonindexed" : "derived/index-" + indexType,
						LayoutFixture( external, binary, indexType ), null, compiled, indexType, true, indexType == 0 ) );
			}
			Forms = forms.ToArray();
		}
		public Form Get( string name ) => Forms.Single( x => x.Name == name );
	}

	public sealed class Form( string variant, byte[] source, byte[] binary, bool compiled, int indexType, bool repacked, bool nonindexed )
	{
		public string Name => variant + (compiled ? "/compiled_glb" : "/source");
		public bool Compiled => compiled;
		public bool Repacked => repacked;
		public bool Nonindexed => nonindexed;
		public int IndexType => indexType;
		public int LocalVertexCount( int mode ) => nonindexed ? AuthoredIndices[mode].Length : 7;
		public (int Position, int Index) ViewOrigins( int mode )
		{
			if ( !repacked ) return (132, 0);
			var cursor = 0; var width = indexType == 5121 ? 1 : indexType == 5123 ? 2 : 4;
			for ( var i = 0; i <= mode; i++ )
			{
				var position = (cursor + 3) / 4 * 4 + 32;
				cursor = position + 8 + (LocalVertexCount( i ) - 1) * 28 + 12;
				var index = nonindexed ? -1 : (cursor + 3) / 4 * 4 + 12;
				if ( i == mode ) return (position, index);
				if ( !nonindexed ) cursor = index + 4 + AuthoredIndices[i].Length * width;
			}
			throw new InvalidDataException( "Mode out of range." );
		}
		public uint HexagonVertex( int mode, uint local ) => nonindexed ? AuthoredIndices[mode][local] : local;
		public uint[] LocalIndices( int mode ) => nonindexed ? Enumerable.Range( 0, LocalVertexCount( mode ) ).Select( x => (uint)x ).ToArray() : (uint[])AuthoredIndices[mode].Clone();
		public GltfDocument Read( int? onlyMode = null )
		{
			var bytes = source;
			if ( onlyMode.HasValue )
			{
				Require( onlyMode.Value is >= 0 and < 7, "Mode out of range." );
				var selected = JsonNode.Parse( bytes ); selected["scenes"][0]["nodes"] = new JsonArray( onlyMode.Value );
				bytes = JsonSerializer.SerializeToUtf8Bytes( selected );
			}
			byte[] Resolve( string uri ) { Require( uri == "buffer.bin" && binary is not null, "Unexpected source dependency." ); return (byte[])binary.Clone(); }
			if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve );
			return GltfReader.Read( bytes, Resolve );
		}
	}

	// Explicit supplemental fixtures: same seven real topologies, same oriented
	// geometry and source floats. Positions have poisoned padding, view origin 32,
	// accessor offset 8 and stride 28. Each index view has an independent origin,
	// nonzero accessor offset and tightly packed legal UINT8/UINT16/UINT32 data.
	public static byte[] LayoutFixture( byte[] originalJson, byte[] originalBinary, int indexType )
	{
		Require( indexType is 0 or 5121 or 5123 or 5125, "Unknown fixture index representation." );
		var root = JsonNode.Parse( originalJson );
		var bytes = new List<byte>(); var views = new JsonArray(); var accessors = new JsonArray();
		void PadTo( int count ) { while ( bytes.Count < count ) bytes.Add( 0xcd ); }
		for ( var mode = 0; mode < 7; mode++ )
		{
			while ( bytes.Count % 4 != 0 ) bytes.Add( 0xcd );
			PadTo( bytes.Count + 32 ); var origin = bytes.Count;
			var count = indexType == 0 ? AuthoredIndices[mode].Length : 7;
			const int stride = 28; const int offset = 8;
			PadTo( origin + offset + (count - 1) * stride + 12 );
			for ( var vertex = 0; vertex < count; vertex++ )
			{
				var sourceVertex = indexType == 0 ? (int)AuthoredIndices[mode][vertex] : vertex;
				for ( var b = 0; b < 12; b++ ) bytes[origin + offset + vertex * stride + b] = originalBinary[132 + sourceVertex * 12 + b];
			}
			var viewIndex = views.Count; views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = origin, ["byteLength"] = bytes.Count - origin, ["byteStride"] = stride, ["target"] = 34962 } );
			var positionAccessor = accessors.Count;
			accessors.Add( new JsonObject { ["bufferView"] = viewIndex, ["byteOffset"] = offset, ["componentType"] = 5126, ["type"] = "VEC3", ["count"] = count,
				["min"] = new JsonArray( -.866, -1, 0 ), ["max"] = new JsonArray( .866, 1, 0 ) } );
			var primitive = root["meshes"][mode]["primitives"][0]; primitive["attributes"]["POSITION"] = positionAccessor;
			if ( indexType == 0 ) { primitive.AsObject().Remove( "indices" ); continue; }
			while ( bytes.Count % 4 != 0 ) bytes.Add( 0xcd );
			PadTo( bytes.Count + 12 ); var indexOrigin = bytes.Count; const int indexOffset = 4;
			var width = indexType == 5121 ? 1 : indexType == 5123 ? 2 : 4;
			PadTo( indexOrigin + indexOffset );
			foreach ( var index in AuthoredIndices[mode] ) for ( var b = 0; b < width; b++ ) bytes.Add( (byte)(index >> (8 * b)) );
			var indicesView = views.Count; views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = indexOrigin, ["byteLength"] = bytes.Count - indexOrigin, ["target"] = 34963 } );
			primitive["indices"] = accessors.Count;
			accessors.Add( new JsonObject { ["bufferView"] = indicesView, ["byteOffset"] = indexOffset, ["componentType"] = indexType, ["type"] = "SCALAR", ["count"] = AuthoredIndices[mode].Length } );
		}
		root["bufferViews"] = views; root["accessors"] = accessors;
		root["buffers"] = new JsonArray( new JsonObject { ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes.ToArray() ), ["byteLength"] = bytes.Count } );
		return JsonSerializer.SerializeToUtf8Bytes( root );
	}
}
