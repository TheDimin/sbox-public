using System.Numerics;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private static void ValidateLocalNodeMatrix( Matrix4x4 matrix )
	{
		if ( matrix.M14 != 0 || matrix.M24 != 0 || matrix.M34 != 0 || matrix.M44 != 1 )
			throw new GltfException( "A local node matrix must be affine." );
		// Row-vector S*R*T has mutually orthogonal linear rows. Check their
		// relative angles in double precision, avoiding absolute tolerances that
		// depend on scene units/translation or overflow at large valid scales.
		// Zero rows are representable by singular scale; never decompose/rewrite
		// the authored matrix or apply this local test to inherited world shear.
		var x = new Vector3( matrix.M11, matrix.M12, matrix.M13 );
		var y = new Vector3( matrix.M21, matrix.M22, matrix.M23 );
		var z = new Vector3( matrix.M31, matrix.M32, matrix.M33 );
		if ( !Orthogonal( x, y ) || !Orthogonal( x, z ) || !Orthogonal( y, z ) )
			throw new GltfException( "A local node matrix must be decomposable to TRS without shear." );
	}

	private static bool Orthogonal( Vector3 a, Vector3 b )
	{
		static double Dot( Vector3 x, Vector3 y ) => (double)x.X * y.X + (double)x.Y * y.Y + (double)x.Z * y.Z;
		var lengths = Math.Sqrt( Dot( a, a ) ) * Math.Sqrt( Dot( b, b ) );
		if ( lengths == 0 ) return true;
		// Float32 matrices retain rounding from authored rotation/scale. Eight
		// float32 epsilons also accepts the decimal-rounded published camera
		// example, while a genuine shear stays invalid at every linear scale.
		const double float32Roundoff = 8d / (1 << 23);
		return Math.Abs( Dot( a, b ) ) <= float32Roundoff * lengths;
	}
}
