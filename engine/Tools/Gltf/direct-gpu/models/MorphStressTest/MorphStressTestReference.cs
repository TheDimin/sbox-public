using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

/// <summary>Independent raw JSON/BIN oracle; no producer vertex, normal or animation evaluator.</summary>
public sealed class MorphStressTestReference
{
	public const string Revision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly Matrix4x4 AffinePlacement = new( 1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 );
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	private readonly Dictionary<string, byte[]> files = new( StringComparer.Ordinal );
	private readonly Dictionary<string, double[][]> values;
	public sealed record Form( string Name, string Variant, bool Compiled, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}

	public MorphStressTestReference( string checkoutRoot )
	{
		using var json = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkoutRoot, "research/gltf-validation/direct-gpu/models/MorphStressTest/reference.json" ) ) );
		Oracle = json.RootElement.Clone(); Require( Oracle.GetProperty( "revision" ).GetString() == Revision, "Oracle corpus pin changed." );
		foreach ( var file in Oracle.GetProperty( "files" ).EnumerateObject() )
		{
			var bytes = File.ReadAllBytes( Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/MorphStressTest", file.Name ) );
			Require( bytes.Length == file.Value.GetProperty( "length" ).GetInt32() && Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned source/dependency hash changed: " + file.Name );
			files.Add( file.Name, bytes );
		}
		values = Oracle.GetProperty( "values" ).EnumerateObject().ToDictionary( p => p.Name,
			p => p.Value.EnumerateArray().Select( Numbers ).ToArray(), StringComparer.Ordinal );
		var forms = new List<Form>();
		foreach ( var form in Oracle.GetProperty( "forms" ).EnumerateArray() )
		{
			var variant = form.GetProperty( "name" ).GetString(); var source = files[form.GetProperty( "path" ).GetString()];
			forms.Add( new( variant + "/source", variant, false, source, Resolve ) );
			forms.Add( new( variant + "/compiled_glb_c", variant, true, GltfCompiler.Compile( source, Resolve ), Resolve ) );
		}
		Forms = forms.AsReadOnly();
	}

	public byte[] Resolve( string uri ) => files.TryGetValue( "glTF/" + Uri.UnescapeDataString( uri ), out var bytes )
		? bytes : throw new InvalidOperationException( "Unexpected dependency: " + uri );
	public JsonElement Primitive( int primitive ) => Oracle.GetProperty( "primitives" )[primitive];
	public int VertexCount( int primitive ) => Primitive( primitive ).GetProperty( "vertexCount" ).GetInt32();
	public double[][] AccessorValues( int accessor ) => values[accessor.ToString( System.Globalization.CultureInfo.InvariantCulture )];
	public JsonElement Descriptor( string variant, int accessor ) => Oracle.GetProperty( "forms" ).EnumerateArray()
		.Single( x => x.GetProperty( "name" ).GetString() == variant ).GetProperty( "accessors" )[accessor];
	public uint[] Indices( int primitive ) => AccessorValues( Primitive( primitive ).GetProperty( "indices" ).GetInt32() ).Select( x => checked((uint)x[0]) ).ToArray();
	public double[] SignedWeights => Numbers( Oracle.GetProperty( "signedWeights" ) );

	public Form Fixture( bool ninth, bool nodeOverride, bool compiled )
	{
		var root = JsonNode.Parse( files["glTF/MorphStressTest.gltf"] ).AsObject(); root.Remove( "animations" );
		var mesh = root["meshes"][0]; var count = ninth ? 9 : 8;
		if ( ninth ) foreach ( var primitive in mesh["primitives"].AsArray() ) primitive["targets"].AsArray().Add( primitive["targets"][7].DeepClone() );
		var defaults = new double[count]; defaults[^1] = 1.75;
		mesh["weights"] = JsonSerializer.SerializeToNode( defaults );
		if ( nodeOverride ) root["nodes"][0]["weights"] = JsonSerializer.SerializeToNode( ninth ? SignedWeights.Append( -.375 ).ToArray() : SignedWeights );
		var bytes = JsonSerializer.SerializeToUtf8Bytes( root );
		if ( compiled ) bytes = GltfCompiler.Compile( bytes, Resolve );
		return new( $"synthetic_{count}_targets_{(nodeOverride ? "node_override" : "mesh_defaults")}/{(compiled ? "compiled_glb_c" : "source")}", "glTF", compiled, bytes, Resolve );
	}

	public double[] Morph( int primitive, int vertex, double[] weights, string semantic )
	{
		Require( weights.Length is 8 or 9, "Oracle weights need actual eight targets or the explicit ninth-target fixture." );
		var p = Primitive( primitive ); var result = (double[])AccessorValues( p.GetProperty( "attributes" ).GetProperty( semantic ).GetInt32() )[vertex].Clone();
		for ( var target = 0; target < weights.Length; target++ )
		{
			var t = p.GetProperty( "targets" )[target < 8 ? target : 7]; // Only explicit fixture ninth duplicates target 7.
			if ( !t.TryGetProperty( semantic, out var accessor ) ) continue;
			var delta = AccessorValues( accessor.GetInt32() )[vertex]; for ( var c = 0; c < result.Length; c++ ) result[c] += weights[target] * delta[c];
		}
		return result;
	}
	public double[] Weights( int clip, double time )
	{
		var c = Oracle.GetProperty( "clips" )[clip]; var times = Numbers( c.GetProperty( "times" ) ); var keys = c.GetProperty( "weights" );
		if ( time <= times[0] ) return Numbers( keys[0] ); if ( time >= times[^1] ) return Numbers( keys[times.Length - 1] );
		var right = Array.FindIndex( times, t => t > time ); var alpha = (time - times[right - 1]) / (times[right] - times[right - 1]);
		var leftWeights = Numbers( keys[right - 1] ); var rightWeights = Numbers( keys[right] );
		return leftWeights.Select( (x, i) => x + (rightWeights[i] - x) * alpha ).ToArray();
	}
	public double[] Engine( int primitive, int vertex, double[] weights, Matrix4x4 placement, bool normal = false )
	{
		var source = Morph( primitive, vertex, weights, normal ? "NORMAL" : "POSITION" );
		var factor = normal ? 1 : 1 / .0254; var p = new[] { source[2] * factor, source[0] * factor, source[1] * factor };
		var a = new double[,] { { placement.M11, placement.M12, placement.M13 }, { placement.M21, placement.M22, placement.M23 }, { placement.M31, placement.M32, placement.M33 } };
		if ( normal )
		{
			if ( source.Sum( x => x * x ) <= 1e-24 ) return null; // Definedness is measured, never an invented direction.
			var cofactor = new double[3, 3];
			for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ )
			{
				var rr = Enumerable.Range( 0, 3 ).Where( x => x != r ).ToArray(); var cc = Enumerable.Range( 0, 3 ).Where( x => x != c ).ToArray();
				cofactor[r, c] = ((r + c) % 2 == 0 ? 1 : -1) * (a[rr[0], cc[0]] * a[rr[1], cc[1]] - a[rr[0], cc[1]] * a[rr[1], cc[0]]);
			}
			var determinant = a[0, 0] * cofactor[0, 0] + a[0, 1] * cofactor[0, 1] + a[0, 2] * cofactor[0, 2]; Require( Math.Abs( determinant ) > 1e-12, "Reference placement must be invertible." );
			for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ ) a[r, c] = cofactor[r, c] / determinant;
		}
		var output = new double[3]; for ( var c = 0; c < 3; c++ ) for ( var r = 0; r < 3; r++ ) output[c] += p[r] * a[r, c];
		if ( normal ) { var length = Math.Sqrt( output.Sum( x => x * x ) ); for ( var c = 0; c < 3; c++ ) output[c] /= length; }
		else { output[0] += placement.M41; output[1] += placement.M42; output[2] += placement.M43; }
		return output;
	}
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string label ) => Require( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance,
		$"{label}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
