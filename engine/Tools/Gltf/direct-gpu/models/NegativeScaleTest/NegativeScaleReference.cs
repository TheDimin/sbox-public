using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;

/// <summary>Independent raw Python data and double cofactor placement oracle.</summary>
public sealed class NegativeScaleReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly Matrix4x4 AffinePlacement = new( 1.5f,.2f,0,0, 0,.75f,.3f,0, .1f,0,1.25f,0, 7,-11,13,1 );
	public static readonly Matrix4x4 MirroredPlacement = new( -1.5f,.2f,0,0, 0,.75f,.3f,0, .1f,0,1.25f,0, 7,-11,13,1 );
	public static readonly Matrix4x4 TinyUniformMirroredPlacement = Matrix4x4.CreateScale( -1e-20f,1e-20f,1e-20f );
	public static readonly Matrix4x4 TinyAnisotropicPlacement = Matrix4x4.CreateScale( 1e-30f,1e-30f,1 );
	public static readonly Matrix4x4 TinyAnisotropicMirroredPlacement = Matrix4x4.CreateScale( -1e-30f,1e-30f,1 );
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	public sealed record Form( string Name, bool Compiled, bool Fixture, byte[] Bytes, Func<string, byte[]> Resolve, JsonElement Oracle )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
		public JsonElement Expected( int key = 0 ) => Fixture ? Oracle.GetProperty( "fixture" ).GetProperty( key.ToString( System.Globalization.CultureInfo.InvariantCulture ) ) : Oracle;
	}
	public NegativeScaleReference( string checkoutRoot )
	{
		var root = Path.GetFullPath( checkoutRoot ); var folder = Path.Combine( root, "research/gltf-validation/direct-gpu/models/NegativeScaleTest" );
		using var json = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( folder, "reference.json" ) ) ); Oracle = json.RootElement.Clone();
		Require( Oracle.GetProperty( "revision" ).GetString() == CorpusRevision, "Reference corpus revision changed." );
		var files = new Dictionary<string, byte[]>( StringComparer.Ordinal );
		foreach ( var file in Oracle.GetProperty( "files" ).EnumerateObject() )
		{
			var path = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/NegativeScaleTest", file.Name );
			Require( new FileInfo( path ).Length == file.Value.GetProperty( "length" ).GetInt64(), "Pinned source length changed: " + path );
			var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == file.Value.GetProperty( "sha256" ).GetString(), "Pinned source hash changed: " + path ); files.Add( file.Name, bytes );
		}
		byte[] Resolve( string uri ) => files.TryGetValue( "glTF/" + uri, out var value ) ? value : throw new InvalidOperationException( "Unexpected source dependency: " + uri );
		var forms = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Binary" } )
		{
			var source = files[variant + (variant == "glTF" ? "/NegativeScaleTest.gltf" : "/NegativeScaleTest.glb")];
			forms.Add( new( variant + "/source", false, false, source, Resolve, Oracle ) );
			forms.Add( new( variant + "/compiled_glb_c", true, false, GltfCompiler.Compile( source, Resolve ), Resolve, Oracle ) );
		}
		var fixture = File.ReadAllBytes( Path.Combine( folder, "affine-tangent-fixture.gltf" ) );
		forms.Add( new( "independent_affine_tangent_fixture/source", false, true, fixture, null, Oracle ) );
		forms.Add( new( "independent_affine_tangent_fixture/compiled_glb_c", true, true, GltfCompiler.Compile( fixture ), null, Oracle ) );
		Forms = forms.AsReadOnly();
	}
	public static double[] Numbers( JsonElement value ) => value.EnumerateArray().Select( x => x.GetDouble() ).ToArray();
	public static double[] Position( JsonElement row, int vertex, Matrix4x4 placement ) => Apply( Numbers( row.GetProperty( "positions" )[vertex] ), placement, false );
	public static double[] Normal( JsonElement row, int vertex, Matrix4x4 placement ) => Apply( Numbers( row.GetProperty( "normals" )[vertex] ), placement, true );
	public static bool Defined( JsonElement row, Matrix4x4 placement ) => row.GetProperty( "normals" ).ValueKind != JsonValueKind.Null && Determinant( placement ) != 0;
	public static double Determinant( Matrix4x4 m ) => (double)m.M11 * (m.M22 * (double)m.M33 - m.M23 * (double)m.M32) - (double)m.M12 * (m.M21 * (double)m.M33 - m.M23 * (double)m.M31) + (double)m.M13 * (m.M21 * (double)m.M32 - m.M22 * (double)m.M31);
	public static double[] Apply( double[] p, Matrix4x4 m, bool normal )
	{
		var a = new double[,] { { m.M11,m.M12,m.M13 }, { m.M21,m.M22,m.M23 }, { m.M31,m.M32,m.M33 } };
		if ( normal )
		{
			var d = Determinant( m ); Require( d != 0, "Singular transform has no defined inverse-transpose normal." ); var cof = new double[3,3];
			for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ )
			{
				var rr = Enumerable.Range( 0,3 ).Where( i => i != r ).ToArray(); var cc = Enumerable.Range( 0,3 ).Where( i => i != c ).ToArray();
				cof[r,c] = ((r + c) % 2 == 0 ? 1 : -1) * (a[rr[0],cc[0]] * a[rr[1],cc[1]] - a[rr[0],cc[1]] * a[rr[1],cc[0]]) / d;
			}
			a = cof;
		}
		var result = new double[3]; for ( var c = 0; c < 3; c++ ) for ( var r = 0; r < 3; r++ ) result[c] += p[r] * a[r,c];
		if ( normal ) Unit( result ); else { result[0] += m.M41; result[1] += m.M42; result[2] += m.M43; }
		return result;
	}
	public static double[] Tangent( JsonElement row, int vertex, Matrix4x4 placement )
	{
		var source = Numbers( row.GetProperty( "tangents" )[vertex] ); var t = Apply( source, placement, false );
		// Tangents are directions: remove the placement translation included by Apply(position).
		t[0] -= placement.M41; t[1] -= placement.M42; t[2] -= placement.M43;
		var n = Normal( row, vertex, placement ); var dot = t.Zip( n, (x,y) => x * y ).Sum(); for ( var c = 0; c < 3; c++ ) t[c] -= n[c] * dot; Unit( t );
		return [t[0],t[1],t[2],source[3] * (Determinant( placement ) < 0 ? -1 : 1)];
	}
	public static void Unit( double[] p ) { var length = Math.Sqrt( p.Sum( x => x * x ) ); Require( length > 0 && double.IsFinite( length ), "Undefined reference direction." ); for ( var c = 0; c < p.Length; c++ ) p[c] /= length; }
	public static string Hash( ReadOnlySpan<byte> bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	public static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string message ) => Require( double.IsFinite( actual ) && Math.Abs( expected - actual ) <= tolerance, $"{message}: expected {expected:R}, actual {actual:R}, tolerance {tolerance:R}." );
}
