using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

/// <summary>Raw little-endian Python oracle, independent of producer transform/vertex evaluators.</summary>
public sealed class SimpleInstancingReference
{
	public const string Extension = "EXT_mesh_gpu_instancing";
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const int InstanceCount = 125;
	public const int VertexCount = 24;
	public const int IndexCount = 36;
	public static readonly IReadOnlySet<string> Selected = new HashSet<string>( StringComparer.Ordinal ) { Extension };
	public static readonly Matrix4x4 AffinePlacement = new( 1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 );
	public static readonly Matrix4x4 MirroredPlacement = new( -1.5f, .2f, 0, 0, 0, .75f, .3f, 0, .1f, 0, 1.25f, 0, 7, -11, 13, 1 );
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	public sealed record Form( string Name, string Variant, bool Compiled, byte[] Bytes, Func<string, byte[]> Resolve )
	{
		public GltfDocument Read( bool selected = true ) => GltfReader.Read( Bytes, Resolve, selected ? Selected : null );
	}

	public SimpleInstancingReference( string checkoutRoot )
	{
		var root = Path.GetFullPath( checkoutRoot );
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( root, "research/gltf-validation/direct-gpu/models/SimpleInstancing/reference.json" ) ) );
		Oracle = oracle.RootElement.Clone(); Require( Oracle.GetProperty( "revision" ).GetString() == CorpusRevision, "Independent reference corpus pin changed." );
		var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var file in Oracle.GetProperty( "files" ).EnumerateObject() )
		{
			var path = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleInstancing", file.Name );
			var info = new FileInfo( path ); Require( info.Exists && info.Length == file.Value.GetProperty( "length" ).GetInt32() && info.Length < 16384, "Pinned input size changed: " + path );
			var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned input hash changed: " + path ); files.Add( file.Name, bytes );
		}
		byte[] Resolve( string uri ) => uri == "SimpleInstancing.bin" ? files["glTF/SimpleInstancing.bin"] : throw new InvalidOperationException( "Unexpected relative dependency: " + uri );
		var forms = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Binary" } )
		{
			var source = files[variant + (variant == "glTF" ? "/SimpleInstancing.gltf" : "/SimpleInstancing.glb")];
			forms.Add( new( variant + "/source", variant, false, source, Resolve ) );
			forms.Add( new( variant + "/compiled_glb_c", variant, true, GltfCompiler.Compile( source, Resolve, Selected ), Resolve ) );
		}
		Forms = forms.AsReadOnly();
	}

	public JsonElement Accessor( int index ) => Oracle.GetProperty( "accessors" )[index];
	public double[] Value( int accessor, int element ) => Numbers( Accessor( accessor ).GetProperty( "values" )[element] );
	public uint[] Indices => Accessor( 0 ).GetProperty( "values" ).EnumerateArray().Select( x => checked((uint)x[0].GetInt32()) ).ToArray();

	/// <summary>Explicit double quaternion polynomial and inverse scale, independent of System.Numerics matrix constructors.</summary>
	public double[] World( int instance, int vertex, bool normal = false, string omittedSemantic = null )
	{
		Require( instance is >= 0 and < InstanceCount && vertex is >= 0 and < VertexCount, "Reference element out of range." );
		var p = Value( normal ? 2 : 1, vertex );
		var translation = omittedSemantic is "TRANSLATION" or "ALL" ? new double[3] : Value( 3, instance );
		var q = omittedSemantic is "ROTATION" or "ALL" ? new double[] { 0, 0, 0, 1 } : Value( 4, instance );
		var scale = omittedSemantic is "SCALE" or "ALL" ? new double[] { 1, 1, 1 } : Value( 5, instance );
		var length = Math.Sqrt( q.Sum( component => component * component ) ); for ( var c = 0; c < 4; c++ ) q[c] /= length;
		var x = q[0]; var y = q[1]; var z = q[2]; var w = q[3];
		var rotation = new[,] { { 1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w) },
			{ 2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w) },
			{ 2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y) } };
		for ( var c = 0; c < 3; c++ ) p[c] *= normal ? 1 / scale[c] : scale[c];
		var result = new double[3]; for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ ) result[r] += rotation[r, c] * p[c];
		if ( normal ) Normalize( result ); else for ( var c = 0; c < 3; c++ ) result[c] += translation[c];
		return result;
	}
	public double[] Engine( int instance, int vertex, Matrix4x4 placement, bool normal = false )
	{
		var p = World( instance, vertex, normal ); var factor = normal ? 1 : 1 / .0254;
		p = new[] { p[2] * factor, p[0] * factor, p[1] * factor };
		return ApplyPlacement( p, placement, normal );
	}
	public static double[] ApplyPlacement( double[] p, Matrix4x4 m, bool normal )
	{
		// Row-vector positions: p * linear + translation. Normals: p * inverse(linear)^T.
		var a = new double[,] { { m.M11, m.M12, m.M13 }, { m.M21, m.M22, m.M23 }, { m.M31, m.M32, m.M33 } };
		if ( normal )
		{
			var cofactor = new double[3, 3];
			for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ )
			{
				var rows = Enumerable.Range( 0, 3 ).Where( i => i != r ).ToArray(); var cols = Enumerable.Range( 0, 3 ).Where( i => i != c ).ToArray();
				cofactor[r, c] = ((r + c) % 2 == 0 ? 1 : -1) * (a[rows[0], cols[0]] * a[rows[1], cols[1]] - a[rows[0], cols[1]] * a[rows[1], cols[0]]);
			}
			var determinant = a[0, 0] * cofactor[0, 0] + a[0, 1] * cofactor[0, 1] + a[0, 2] * cofactor[0, 2];
			Require( Math.Abs( determinant ) > 1e-12, "Reference placement must be invertible." );
			for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ ) a[r, c] = cofactor[r, c] / determinant;
		}
		var result = new double[3]; for ( var c = 0; c < 3; c++ ) for ( var r = 0; r < 3; r++ ) result[c] += p[r] * a[r, c];
		if ( normal ) Normalize( result ); else { result[0] += m.M41; result[1] += m.M42; result[2] += m.M43; }
		return result;
	}
	public static void Normalize( double[] p ) { var length = Math.Sqrt( p.Sum( x => x * x ) ); for ( var c = 0; c < p.Length; c++ ) p[c] /= length; }
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string label ) => Require( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance, $"{label}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
