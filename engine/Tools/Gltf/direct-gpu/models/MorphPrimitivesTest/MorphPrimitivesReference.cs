using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

/// <summary>Frozen raw-source oracle, independent of production morph/transform evaluators.</summary>
public sealed class MorphPrimitivesReference
{
	public const string Pin = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { "KHR_draco_mesh_compression", "KHR_mesh_quantization" };
	public static readonly float[] Weights = { -.75f, 0, .5f, 1.5f };
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	public sealed record Form( string Name, string Variant, bool Compiled, byte[] Bytes, byte[] Source, Func<string, byte[]> Resolve, JsonElement Expected )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve, Selected );
		/// <summary>Explicit derived control, never reported as an upstream variant.</summary>
		public GltfDocument Control( float? nodeWeight = null, bool zeroFallback = false, bool affine = false, bool animated = false )
		{
			var root = SourceJson( Source, Resolve );
			if ( nodeWeight.HasValue ) root["nodes"][1]["weights"] = new JsonArray( nodeWeight.Value );
			if ( zeroFallback ) root["meshes"][0].AsObject().Remove( "weights" );
			if ( affine )
			{
				// Local shear is invalid glTF. Valid nonuniform parent scale plus
				// child rotation yields inherited world shear without violating TRS.
				root["nodes"][0]["scale"] = new JsonArray( 2, 3, 1.5f ); root["nodes"][0]["translation"] = new JsonArray( .3f, -.2f, .7f );
				root["nodes"][1]["rotation"] = new JsonArray( 0, 0, .6f, .8f );
			}
			if ( animated )
			{
				var animationBytes = new byte[24]; var values = new[] { 0f, 1f, 2f, -.75f, .5f, 1.5f };
				for ( var i = 0; i < values.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( animationBytes.AsSpan( i * 4 ), values[i] );
				var buffers = root["buffers"].AsArray(); var views = root["bufferViews"].AsArray(); var accessors = root["accessors"].AsArray();
				var buffer = buffers.Count; buffers.Add( new JsonObject { ["byteLength"] = animationBytes.Length, ["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( animationBytes ) } );
				var firstView = views.Count; views.Add( new JsonObject { ["buffer"] = buffer, ["byteLength"] = 12 } ); views.Add( new JsonObject { ["buffer"] = buffer, ["byteOffset"] = 12, ["byteLength"] = 12 } );
				var firstAccessor = accessors.Count;
				accessors.Add( new JsonObject { ["bufferView"] = firstView, ["componentType"] = 5126, ["type"] = "SCALAR", ["count"] = 3, ["min"] = new JsonArray( 0 ), ["max"] = new JsonArray( 2 ) } );
				accessors.Add( new JsonObject { ["bufferView"] = firstView + 1, ["componentType"] = 5126, ["type"] = "SCALAR", ["count"] = 3 } );
				root["animations"] = new JsonArray( new JsonObject { ["samplers"] = new JsonArray( new JsonObject { ["input"] = firstAccessor, ["output"] = firstAccessor + 1, ["interpolation"] = "LINEAR" } ),
					["channels"] = new JsonArray( new JsonObject { ["sampler"] = 0, ["target"] = new JsonObject { ["node"] = 1, ["path"] = "weights" } } ) } );
			}
			var bytes = JsonSerializer.SerializeToUtf8Bytes( root );
			if ( Compiled ) bytes = GltfCompiler.Compile( bytes, Resolve, Selected );
			return GltfReader.Read( bytes, Resolve, Selected );
		}
	}

	public MorphPrimitivesReference( string checkoutRoot, IEnumerable<string> variants = null )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkoutRoot, "research/gltf-validation/direct-gpu/models/MorphPrimitivesTest/reference.json" ) ) );
		Oracle = oracle.RootElement.Clone(); Require( Oracle.GetProperty( "pin" ).GetString() == Pin, "Oracle corpus pin changed." );
		var forms = new List<Form>();
		foreach ( var variant in variants ?? new[] { "glTF", "glTF-Binary", "glTF-Draco" } )
		{
			var expected = Oracle.GetProperty( "forms" ).GetProperty( variant ); var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
			foreach ( var file in expected.GetProperty( "files" ).EnumerateObject() )
			{
				var path = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/MorphPrimitivesTest", variant, file.Name );
				var info = new FileInfo( path ); Require( info.Exists && info.Length == file.Value.GetProperty( "length" ).GetInt32() && info.Length < 1_000_000, "Pinned dependency size changed: " + path );
				var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned dependency changed: " + path ); files.Add( file.Name, bytes );
			}
			byte[] Resolve( string uri ) => files.TryGetValue( Uri.UnescapeDataString( uri ), out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected dependency " + uri );
			var source = files[expected.GetProperty( "source" ).GetString()];
			forms.Add( new( variant + "/source", variant, false, source, source, Resolve, expected ) );
			forms.Add( new( variant + "/compiled_glb_c", variant, true, GltfCompiler.Compile( source, Resolve, Selected ), source, Resolve, expected ) );
		}
		Forms = forms.AsReadOnly();
	}
	public static JsonElement Primitive( Form form, int primitive ) => form.Expected.GetProperty( "primitives" )[primitive];
	public static int Count( int primitive ) => primitive == 0 ? 21 : 9;
	public static uint[] Indices( Form form, int primitive ) => Primitive( form, primitive ).GetProperty( "indices" ).EnumerateArray().Select( x => x.GetUInt32() ).ToArray();
	public static NVector Position( Form form, int primitive, int vertex, float weight, bool affine = false )
	{
		var p = Primitive( form, primitive ); var b = Vector( p.GetProperty( "attributes" ).GetProperty( "POSITION" ).GetProperty( "values" )[vertex] ); var d = Vector( p.GetProperty( "morph" ).GetProperty( "values" )[vertex] );
		var v = b + d * weight;
		// Literal, independent glTF column-major affine operation. Applying the
		// weighted delta after this transform would wrongly include translation.
		return affine ? new NVector( .56f * v.X - 1.92f * v.Y + .3f, 2.88f * v.X + .84f * v.Y - .2f, 1.5f * v.Z + .7f ) : v;
	}
	public static NVector EnginePosition( Form form, int primitive, int vertex, float weight, NMatrix placement, bool affine = false )
	{
		var p = Position( form, primitive, vertex, weight, affine ); return NVector.Transform( new NVector( p.Z, p.X, p.Y ) / .0254f, placement );
	}
	public static NVector EngineNormal( Form form, int primitive, int vertex, NMatrix placement )
	{
		var n = Vector( Primitive( form, primitive ).GetProperty( "attributes" ).GetProperty( "NORMAL" ).GetProperty( "values" )[vertex] );
		Require( NMatrix.Invert( placement, out var inverse ), "Normal control requires invertible placement." );
		return NVector.Normalize( NVector.TransformNormal( new NVector( n.Z, n.X, n.Y ), NMatrix.Transpose( inverse ) ) );
	}
	public static float Uv( Form form, int primitive, int vertex, int component ) => Primitive( form, primitive ).GetProperty( "attributes" ).GetProperty( "TEXCOORD_0" ).GetProperty( "values" )[vertex][component].GetSingle();
	public static NVector Vector( JsonElement v ) => new( v[0].GetSingle(), v[1].GetSingle(), v[2].GetSingle() );
	internal static JsonObject SourceJson( byte[] source, Func<string, byte[]> resolve )
	{
		JsonObject root; byte[] binary = null;
		if ( BinaryPrimitives.ReadUInt32LittleEndian( source ) == 0x46546c67 )
		{
			var length = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 12 ) )); root = JsonNode.Parse( source.AsSpan( 20, length ) ).AsObject();
			var offset = 20 + length; var binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset ) )); binary = source.AsSpan( offset + 8, binaryLength ).ToArray();
		}
		else root = JsonNode.Parse( source ).AsObject();
		if ( root["buffers"][0]["uri"] is null ) root["buffers"][0]["uri"] = "data:application/octet-stream;base64," + Convert.ToBase64String( binary );
		return root;
	}
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	public static void Close( float expected, float actual, float tolerance, string label ) => Require( float.IsFinite( actual ) && MathF.Abs( expected - actual ) <= tolerance, $"{label}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
