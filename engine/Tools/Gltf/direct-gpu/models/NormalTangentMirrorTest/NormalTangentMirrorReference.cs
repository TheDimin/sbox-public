using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;

/// <summary>Raw-source double-precision oracle independent of production deformation.</summary>
internal sealed class NormalTangentMirrorReference
{
	public const string Revision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly Matrix4x4 Affine = new( 1.5f,.2f,0,0, 0,.75f,.3f,0, .1f,0,1.25f,0, 7,-11,13,1 );
	public static readonly Matrix4x4 Mirrored = new( -1.5f,.2f,0,0, 0,.75f,.3f,0, .1f,0,1.25f,0, 7,-11,13,1 );
	public JsonElement Oracle { get; }
	public IReadOnlyList<Form> Forms { get; }
	internal sealed record Form( string Name, bool Compiled, byte[] Bytes, Func<string,byte[]> Resolve )
	{
		public GltfDocument Read() => GltfReader.Read( Bytes,Resolve );
	}
	public NormalTangentMirrorReference( string root )
	{
		var folder = Path.Combine( root,"research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest" );
		using var json = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( folder,"reference.json" ) ) ); Oracle = json.RootElement.Clone();
		Require( Oracle.GetProperty( "revision" ).GetString() == Revision,"Pinned corpus revision changed." );
		var files = new Dictionary<string,byte[]>( StringComparer.Ordinal );
		foreach ( var file in Oracle.GetProperty( "files" ).EnumerateObject() )
		{
			var bytes = File.ReadAllBytes( Path.Combine( root,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentMirrorTest",file.Name ) );
			Require( bytes.Length == file.Value.GetProperty( "length" ).GetInt32() && Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == file.Value.GetProperty( "sha256" ).GetString(),"Pinned input changed: " + file.Name ); files.Add( file.Name,bytes );
		}
		byte[] Resolve( string uri ) => files.TryGetValue( "glTF/" + uri,out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected dependency: " + uri );
		var forms = new List<Form>();
		foreach ( var variant in new[] { "glTF","glTF-Binary" } )
		{
			var source = files[variant + "/NormalTangentMirrorTest." + (variant == "glTF" ? "gltf" : "glb")];
			forms.Add( new( variant + "/source",false,source,Resolve ) );
			forms.Add( new( variant + "/compiled_glb_c",true,GltfCompiler.Compile( source,Resolve ),Resolve ) );
		}
		Forms = forms;
	}
	public double[] Values( int accessor, int vertex ) => Oracle.GetProperty( "accessors" )[accessor].GetProperty( "values" )[vertex].EnumerateArray().Select( v => v.GetDouble() ).ToArray();
	public static double Det( Matrix4x4 m ) => (double)m.M11 * (m.M22 * (double)m.M33 - m.M23 * (double)m.M32) - (double)m.M12 * (m.M21 * (double)m.M33 - m.M23 * (double)m.M31) + (double)m.M13 * (m.M21 * (double)m.M32 - m.M22 * (double)m.M31);
	public double[] Position( int vertex, Matrix4x4 m )
	{
		var s = Values( 1,vertex ); var p = Apply( [s[2] / .0254,s[0] / .0254,s[1] / .0254],m );
		p[0] += m.M41; p[1] += m.M42; p[2] += m.M43; return p;
	}
	public double[] Normal( int vertex, Matrix4x4 m )
	{
		var s = Values( 2,vertex ); var p = new[] { s[2],s[0],s[1] };
		var a = new double[,] { {m.M11,m.M12,m.M13},{m.M21,m.M22,m.M23},{m.M31,m.M32,m.M33} }; var cof = new double[3,3]; var d = Det( m ); Require( d != 0,"Singular reference is undefined." );
		for ( var r = 0; r < 3; r++ ) for ( var c = 0; c < 3; c++ )
		{
			var rr = Enumerable.Range( 0,3 ).Where( x => x != r ).ToArray(); var cc = Enumerable.Range( 0,3 ).Where( x => x != c ).ToArray();
			cof[r,c] = ((r+c)%2 == 0 ? 1 : -1) * (a[rr[0],cc[0]]*a[rr[1],cc[1]]-a[rr[0],cc[1]]*a[rr[1],cc[0]])/d;
		}
		var result = new double[3]; for ( var c = 0; c < 3; c++ ) for ( var r = 0; r < 3; r++ ) result[c] += p[r]*cof[r,c]; return Unit( result );
	}
	public double[] Tangent( int vertex, Matrix4x4 m )
	{
		var s = Values( 3,vertex ); var t = Apply( [s[2],s[0],s[1]],m ); var n = Normal( vertex,m ); var dot = t.Zip( n,(a,b) => a*b ).Sum();
		for ( var c = 0; c < 3; c++ ) t[c] -= n[c]*dot; Unit( t ); return [t[0],t[1],t[2],s[3]*(Det( m ) < 0 ? -1 : 1)];
	}
	private static double[] Apply( double[] p, Matrix4x4 m ) => [p[0]*m.M11+p[1]*m.M21+p[2]*m.M31,p[0]*m.M12+p[1]*m.M22+p[2]*m.M32,p[0]*m.M13+p[1]*m.M23+p[2]*m.M33];
	public static double[] Unit( double[] p ) { var length = Math.Sqrt( p.Sum( x => x*x ) ); Require( length > 0 && double.IsFinite( length ),"Undefined direction." ); for ( var c = 0; c < p.Length; c++ ) p[c] /= length; return p; }
	public static double[] Cross( double[] a, double[] b ) => [a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]];
	public static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	public static void Close( double expected, double actual, double tolerance, string message ) => Require( double.IsFinite( actual ) && Math.Abs( expected-actual ) <= tolerance,$"{message}: expected {expected:R}, got {actual:R}." );
}
