using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

/// <summary>Expected values come from the real upstream fallback, independently decoded in Python.</summary>
public sealed class MeshoptCubeReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const string Compression = "KHR_meshopt_compression";
	public const string Quantization = "KHR_mesh_quantization";
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Compression, Quantization };
	public static readonly IReadOnlySet<string> QuantizationOnly = new HashSet<string>( StringComparer.Ordinal ) { Quantization };
	public const int NodeCount = 35;
	public const int AccessorCount = 109;
	public const int PrimitiveCount = 35;
	public const int TotalVertices = 640;
	public const int TotalIndices = 960;
	public static readonly float[] PoseTimes = { 0, .375f, 1, 1.625f, 2, 2.25f };
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	public sealed record Form( string Name, string Variant, bool Compiled, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read( bool supportsCompression = true ) => GltfReader.Read( Bytes, Resolve, supportsCompression ? Selected : QuantizationOnly );
	}

	public MeshoptCubeReference( string checkoutRoot )
	{
		var root = Path.GetFullPath( checkoutRoot );
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( root, "research/gltf-validation/direct-gpu/models/MeshoptCubeTest/reference.json" ) ) );
		Oracle = oracle.RootElement.Clone();
		Require( Oracle.GetProperty( "revision" ).GetString() == CorpusRevision, "Independent oracle corpus pin changed." );
		var forms = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Meshopt" } )
		{
			var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
			var expected = Oracle.GetProperty( "forms" ).GetProperty( variant );
			foreach ( var file in expected.GetProperty( "files" ).EnumerateObject() )
			{
				var path = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/MeshoptCubeTest", variant, file.Name );
				var info = new FileInfo( path );
				Require( info.Exists && info.Length == file.Value.GetProperty( "length" ).GetInt32() && info.Length < 1_000_000, "Pinned source dependency absent or size changed: " + path );
				var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned dependency hash changed: " + path ); files.Add( file.Name, bytes );
			}
			byte[] Resolve( string uri ) => files.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected relative dependency: " + uri );
			var source = files[expected.GetProperty( "source" ).GetString()];
			forms.Add( new( variant + "/source", variant, false, source, Resolve ) );
			forms.Add( new( variant + "/compiled_glb_c", variant, true, GltfCompiler.Compile( source, Resolve, Selected ), Resolve ) );
		}
		Forms = forms.AsReadOnly();
	}

	public JsonElement Accessor( int index ) => Oracle.GetProperty( "accessors" )[index];
	public JsonElement Primitive( int mesh ) => Oracle.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[0];
	public int AccessorFor( int mesh, string semantic ) => semantic == "INDICES" ? Primitive( mesh ).GetProperty( "indices" ).GetInt32() : Primitive( mesh ).GetProperty( "attributes" ).GetProperty( semantic ).GetInt32();
	public double[] Value( int accessor, int vertex ) => Numbers( Accessor( accessor ).GetProperty( "values" )[vertex] );
	public uint[] Indices( int mesh ) => Accessor( AccessorFor( mesh, "INDICES" ) ).GetProperty( "values" ).EnumerateArray().Select( x => checked((uint)x[0].GetInt32()) ).ToArray();

	/// <summary>Literal independent source hierarchy: all 35 nodes are roots; five rotate about Y.</summary>
	public double[] World( int node, int vertex, float? time, bool normal = false )
	{
		var n = Oracle.GetProperty( "nodes" )[node]; var mesh = n.GetProperty( "mesh" ).GetInt32();
		var value = Value( AccessorFor( mesh, normal ? "NORMAL" : "POSITION" ), vertex );
		var scale = n.TryGetProperty( "scale", out var s ) ? Numbers( s ) : new double[] { 1, 1, 1 };
		for ( var c = 0; c < 3; c++ ) value[c] *= normal ? 1 / scale[c] : scale[c];
		// Normalized [0,23170,0,23170] is exactly a 90-degree Y rotation.
		// LINEAR rotation interpolation is shortest-path slerp, so between these
		// three literal keys its angle is linear, then clamped after time 2.
		var t = time.HasValue ? Math.Clamp( time.Value, 0, 2 ) : 0;
		var angle = node >= 14 && (node - 14) % 5 == 0 ? Math.PI / 2 * Math.Min( t, 2 - t ) : 0;
		var x = value[0]; var z = value[2]; value[0] = x * Math.Cos( angle ) + z * Math.Sin( angle ); value[2] = -x * Math.Sin( angle ) + z * Math.Cos( angle );
		if ( normal )
		{
			var length = Math.Sqrt( value.Sum( v => v * v ) ); for ( var c = 0; c < 3; c++ ) value[c] /= length;
		}
		else
		{
			var translation = Numbers( n.GetProperty( "translation" ) ); for ( var c = 0; c < 3; c++ ) value[c] += translation[c];
		}
		return value;
	}

	public double[] Engine( int node, int vertex, float? time, bool normal = false )
	{
		var world = World( node, vertex, time, normal ); var factor = normal ? 1 : 1 / .0254;
		return new[] { world[2] * factor, world[0] * factor, world[1] * factor };
	}
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string label ) => Require( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance, $"{label}: expected {expected:R}, observed {actual:R}, tolerance {tolerance:R}." );
}
