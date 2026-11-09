using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using System.Text;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

/// <summary>Compares the actual managed producer with a separately compiled original C MikkTSpace oracle.</summary>
internal static class NativeMikkDifferential
{
	public static int Run( string oracleExecutable )
	{
		ArgumentException.ThrowIfNullOrWhiteSpace( oracleExecutable );
		// Irregular geometry has shared vertices, curved normals, different UV
		// scales and opposite chart orientation; planar positive controls alone
		// cannot detect traversal, weld or mirrored-frame discrepancies.
		NVector3[] positions = [new(0,0,0),new(1,0,0),new(0,1,0),new(-1,0,.2f),new(0,-1,.1f)];
		NVector3[] normals = [NVector3.UnitZ,NVector3.Normalize(new(.1f,0,1)),NVector3.Normalize(new(0,.1f,1)),NVector3.Normalize(new(-.1f,0,1)),NVector3.Normalize(new(0,-.1f,1))];
		NVector2[] uv = [new(0,0),new(1,0),new(0,1),new(1.2f,.1f),new(.1f,-.8f)];
		uint[] indices = [0,1,2,0,2,3,0,3,4,0,4,1];
		Compare( oracleExecutable, positions, normals, uv, indices );
		Compare( oracleExecutable, positions, normals, uv, indices.Reverse().ToArray() );
		// Degenerate UV chart is tested separately from nondegenerate frames.
		Compare( oracleExecutable, positions, normals, Enumerable.Repeat( NVector2.Zero, positions.Length ).ToArray(), indices );
		return 3;
	}

	private static void Compare( string executable, NVector3[] p, NVector3[] n, NVector2[] uv, uint[] indices )
	{
		var input = new StringBuilder(); input.AppendLine( $"{p.Length} {indices.Length}" );
		string F( float x ) => x.ToString( "R", CultureInfo.InvariantCulture );
		for ( var i=0; i<p.Length; i++ ) input.AppendLine( $"{F(p[i].X)} {F(p[i].Y)} {F(p[i].Z)} {F(n[i].X)} {F(n[i].Y)} {F(n[i].Z)} {F(uv[i].X)} {F(uv[i].Y)}" );
		input.AppendLine( string.Join( " ", indices ) );
		var start = new ProcessStartInfo( executable ) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
		using var process = Process.Start( start ) ?? throw new InvalidOperationException( "Failed to start native oracle." );
		var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
		process.StandardInput.Write( input ); process.StandardInput.Close();
		if ( !process.WaitForExit( 30000 ) )
		{
			process.Kill( entireProcessTree: true );
			_ = process.WaitForExit( 5000 );
			throw new TimeoutException( "Native Mikk oracle exceeded its 30-second execution limit." );
		}
		if ( !Task.WhenAll( stdout, stderr ).Wait( 5000 ) ) throw new TimeoutException( "Native Mikk oracle pipes did not close after process exit." );
		var output = stdout.Result; var errors = stderr.Result;
		if ( process.ExitCode != 0 ) throw new InvalidOperationException( "Native Mikk oracle failed: " + errors );
		var tokens = output.Split( (char[])null, StringSplitOptions.RemoveEmptyEntries );
		if ( tokens.Length != indices.Length * 4 ) throw new InvalidOperationException( "Native oracle did not return four values per corner." );
		var actual = GltfGeneratedTangents.Generate( p, n, uv, indices, 4, new(1,0,0), new(0,1,0) ).CornerTangents;
		for ( var i=0; i<indices.Length; i++ )
		{
			float Parse( int j ) => float.Parse( tokens[i*4+j], CultureInfo.InvariantCulture );
			var expected = new NVector4( Parse(0), Parse(1), Parse(2), -Parse(3) );
			if ( !float.IsFinite(expected.X) || !float.IsFinite(expected.Y) || !float.IsFinite(expected.Z) || !float.IsFinite(expected.W) || NVector4.Distance( expected, actual[i] ) > .0001f )
				throw new InvalidOperationException( $"Native/managed tangent differs at corner {i}: {expected} vs {actual[i]}." );
		}
	}
}
