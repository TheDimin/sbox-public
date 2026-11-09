using System;
using System.Collections.Generic;
using System.Reflection;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Literal affine inputs distinguish representability from the current numeric
/// decomposition algorithm. The legacy guard's measured arithmetic is unchanged.
/// These are pure CPU helper cases, not renderer/shear deformation acceptance.
/// </summary>
public static class SimpleSkinTransformConversionTests
{
	public sealed record Result( string Name, bool Passed, bool ExpectedRepresentable, bool? Accepted, float? MaximumRelativeRowError, string Error );
	private sealed record Case( string Name, NMatrix Matrix, bool Representable = true );
	private delegate bool Converter( NMatrix matrix, out Transform transform );

	/// <summary>Eighteen real legacy-converter cases, callable on preserved fee5.</summary>
	public static Result[] RunLegacy() => Run( "legacy", Legacy );

	/// <summary>The same eighteen requirements against the new public-Transform adapter.</summary>
	public static Result[] RunPublicAdapter() => Run( "public_adapter", PublicAdapter );

	private static bool Legacy( NMatrix matrix, out Transform transform ) => GltfModelRig.TryNativeTransform( matrix, out transform );
	private static bool PublicAdapter( NMatrix matrix, out Transform transform )
	{
		var method = typeof( Model ).Assembly.GetType( "Sandbox.GltfManagedModelMetadata" )?.GetMethod( "ToPublicTransform", BindingFlags.NonPublic | BindingFlags.Static );
		if ( method is null ) throw new MissingMethodException( "The installed producer has no managed public bone-Transform adapter." );
		try { transform = (Transform)method.Invoke( null, new object[] { matrix } ); return true; }
		catch ( TargetInvocationException error ) when ( error.InnerException is NotSupportedException ) { transform = default; return false; }
	}

	private static Result[] Run( string prefix, Converter convert )
	{
		var results = new List<Result>();
		foreach ( var test in Cases() )
		{
			bool? accepted = null; float? relative = null; string error = null;
			try
			{
				accepted = convert( test.Matrix, out var transform );
				if ( accepted != test.Representable ) throw new InvalidOperationException( test.Representable
					? "This literal matrix is a valid TRS; rejection is a decomposition/conversion limitation, not proof that Transform cannot represent zero or tiny scales."
					: "Shear was silently projected onto a TRS." );
				if ( accepted == true ) relative = CheckReconstruction( test.Matrix, (NMatrix)Matrix.FromTransform( transform ) );
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			results.Add( new( prefix + "_" + test.Name, error is null, test.Representable, accepted, relative, error ) );
		}
		return results.ToArray();
	}

	private static IEnumerable<Case> Cases()
	{
		yield return new( "ordinary_rotated_TRS", Trs( 1, 2, 3 ) );
		yield return new( "zero_x", Trs( 0, 2, 3 ) );
		yield return new( "zero_y", Trs( 1, 0, 3 ) );
		yield return new( "zero_z", Trs( 1, 2, 0 ) );
		yield return new( "zero_xy", Trs( 0, 0, 3 ) );
		yield return new( "zero_xz", Trs( 0, 2, 0 ) );
		yield return new( "zero_yz", Trs( 1, 0, 0 ) );
		yield return new( "all_zero", Trs( 0, 0, 0 ) );
		yield return new( "reflected_x", Trs( -1, 2, 3 ) );
		yield return new( "reflected_y", Trs( 1, -2, 3 ) );
		yield return new( "reflected_z", Trs( 1, 2, -3 ) );
		yield return new( "reflected_all_axes", Trs( -1, -2, -3 ) );
		yield return new( "tiny_uniform_1e6", Trs( 1e-6f, 1e-6f, 1e-6f ) );
		yield return new( "tiny_uniform_1e12", Trs( 1e-12f, 1e-12f, 1e-12f ) );
		yield return new( "tiny_x_only", Trs( 1e-12f, 2, 3 ) );
		yield return new( "tiny_nonuniform", Trs( 1e-12f, 2e-12f, 3e-12f ) );
		const float c = .7071067811865476f;
		yield return new( "inherited_shear", new NMatrix( 2 * c, c, 0, 0, -2 * c, c, 0, 0, 0, 0, 1, 0, 3, 5, -7, 1 ), false );
		yield return new( "explicit_shear", new NMatrix( 1, .25f, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 3, 5, -7, 1 ), false );
	}

	// Literal Rz(45 degrees), with independently scaled rows and translation.
	// No producer decomposition/evaluator or reconstructed output supplies the oracle.
	private static NMatrix Trs( float x, float y, float z )
	{
		const float c = .7071067811865476f;
		return new( x * c, x * c, 0, 0, -y * c, y * c, 0, 0, 0, 0, z, 0, 3, 5, -7, 1 );
	}

	private static float CheckReconstruction( NMatrix expected, NMatrix actual )
	{
		var maximum = 0f;
		Row( expected.M11, expected.M12, expected.M13, actual.M11, actual.M12, actual.M13 );
		Row( expected.M21, expected.M22, expected.M23, actual.M21, actual.M22, actual.M23 );
		Row( expected.M31, expected.M32, expected.M33, actual.M31, actual.M32, actual.M33 );
		if ( !float.IsFinite( actual.M41 ) || !float.IsFinite( actual.M42 ) || !float.IsFinite( actual.M43 ) ||
			MathF.Abs( actual.M41 - 3 ) > 2e-6f || MathF.Abs( actual.M42 - 5 ) > 2e-6f || MathF.Abs( actual.M43 + 7 ) > 2e-6f || actual.M44 != 1 )
			throw new InvalidOperationException( "Translation or affine homogeneous component changed." );
		return maximum;

		void Row( float a, float b, float c, float x, float y, float z )
		{
			var magnitude = MathF.Max( MathF.Abs( a ), MathF.Max( MathF.Abs( b ), MathF.Abs( c ) ) );
			var difference = MathF.Max( MathF.Abs( a - x ), MathF.Max( MathF.Abs( b - y ), MathF.Abs( c - z ) ) );
			if ( !float.IsFinite( x ) || !float.IsFinite( y ) || !float.IsFinite( z ) || difference > magnitude * 2e-6f )
				throw new InvalidOperationException( $"Row reconstruction error {difference:R} exceeds scale-relative bound {magnitude * 2e-6f:R}; zero/tiny rows cannot use a fixed epsilon floor." );
			if ( magnitude > 0 ) maximum = MathF.Max( maximum, difference / magnitude );
		}
	}
}
