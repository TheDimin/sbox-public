using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Written after the four measured fee5 tiny-TRS failures, before any proposed
/// arithmetic change. Every acceptance calls the installed shared converter.
/// Direct Numerics diagnostics distinguish its basis recovery from our stricter
/// reconstruction guard; neither diagnostic supplies the expected matrix.
/// </summary>
public static class SimpleSkinTinyTransformRegressionTests
{
	public sealed record Result( string Name, bool Passed, bool ExpectedRepresentable, bool? Accepted,
		bool? NumericsAccepted, double[] SourceRowLengths, string[] NumericsScale, string[] NumericsQuaternion,
		string[] NumericsRelativeRowErrors, double? ActualRelativeError, string Runtime, string Error );
	private sealed record Case( string Name, NMatrix Matrix, bool Representable = true );

	/// <summary>Nineteen regressions and accuracy controls; no graphics or scene state.</summary>
	public static Result[] Run()
	{
		var runtime = $"runtime={Environment.Version}; numerics={typeof( NMatrix ).Assembly.GetName().Version}; informational={typeof( NMatrix ).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion}";
		var results = new List<Result>();
		foreach ( var test in Cases() )
		{
			bool? accepted = null, numericsAccepted = null;
			string[] scales = null, quaternion = null, numericsErrors = null;
			double? relative = null;
			string error = null;
			var matrix = test.Matrix;
			var lengths = new[] { Length( matrix.M11, matrix.M12, matrix.M13 ), Length( matrix.M21, matrix.M22, matrix.M23 ), Length( matrix.M31, matrix.M32, matrix.M33 ) };
			try
			{
				numericsAccepted = NMatrix.Decompose( matrix, out var scale, out var rotation, out var position );
				scales = new[] { Text( scale.X ), Text( scale.Y ), Text( scale.Z ) };
				quaternion = new[] { Text( rotation.X ), Text( rotation.Y ), Text( rotation.Z ), Text( rotation.W ) };
				var raw = NMatrix.CreateScale( scale ) * NMatrix.CreateFromQuaternion( rotation ) * NMatrix.CreateTranslation( position );
				numericsErrors = Array.ConvertAll( RowErrors( matrix, raw ), value => value.ToString( "R", CultureInfo.InvariantCulture ) );
				accepted = GltfModelRig.TryNativeTransform( matrix, out var actual );
				if ( accepted != test.Representable ) throw new InvalidOperationException( test.Representable
					? "Literal finite TRS was rejected. See actual Numerics output and relative reconstruction diagnostics."
					: "A small-axis shear was hidden by conversion or an absolute error floor." );
				if ( accepted == true )
				{
					var reconstructed = (NMatrix)Matrix.FromTransform( actual );
					var rows = RowErrors( matrix, reconstructed );
					relative = Math.Max( rows[0], Math.Max( rows[1], rows[2] ) );
					if ( !double.IsFinite( relative.Value ) || relative.Value > 2e-6 ) throw new InvalidOperationException( "Public Transform reconstruction lost a small row: " + relative );
					if ( reconstructed.M41 != 3 || reconstructed.M42 != 5 || reconstructed.M43 != -7 || reconstructed.M44 != 1 ) throw new InvalidOperationException( "Translation/affine component changed." );
				}
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			results.Add( new( test.Name, error is null, test.Representable, accepted, numericsAccepted, lengths, scales, quaternion, numericsErrors, relative, runtime, error ) );
		}
		return results.ToArray();
	}

	private static IEnumerable<Case> Cases()
	{
		yield return new( "actual_tiny_uniform_1e6", Trs( 1e-6f, 1e-6f, 1e-6f ) );
		yield return new( "actual_tiny_uniform_1e12", Trs( 1e-12f, 1e-12f, 1e-12f ) );
		yield return new( "actual_tiny_x_only", Trs( 1e-12f, 2, 3 ) );
		yield return new( "actual_tiny_nonuniform", Trs( 1e-12f, 2e-12f, 3e-12f ) );
		yield return new( "below_numerics_fixed_basis_threshold", Trs( 9.9e-5f, 9.9e-5f, 9.9e-5f ) );
		yield return new( "at_numerics_fixed_basis_threshold", Trs( 1e-4f, 1e-4f, 1e-4f ) );
		yield return new( "above_numerics_fixed_basis_threshold", Trs( 1.01e-4f, 1.01e-4f, 1.01e-4f ) );
		yield return new( "tiny_reflected_x", Trs( -1e-12f, 2, 3 ) );
		yield return new( "tiny_reflected_all", Trs( -1e-12f, -1e-12f, -1e-12f ) );
		yield return new( "tiny_reflected_nonuniform", Trs( -1e-12f, 2e-12f, -3e-12f ) );
		yield return new( "zero_x_with_tiny_survivors", Trs( 0, 1e-12f, 2e-12f ) );
		yield return new( "zero_y_with_tiny_survivors", Trs( 1e-12f, 0, 3e-12f ) );
		yield return new( "zero_z_with_tiny_survivors", Trs( 1e-12f, 2e-12f, 0 ) );
		yield return new( "row_square_subnormal", Trs( 1e-20f, 1e-20f, 1e-20f ) );
		yield return new( "row_square_underflows_float", Trs( 1e-30f, 1e-30f, 1e-30f ) );
		const float c = .7071067811865476f;
		yield return new( "tiny_inherited_shear_rejected", new NMatrix( 2e-12f * c, 1e-12f * c, 0, 0, -2e-12f * c, 1e-12f * c, 0, 0, 0, 0, 1e-12f, 0, 3, 5, -7, 1 ), false );
		var tinyX = Trs( 1e-12f, 2, 3 ); tinyX.M12 += 1e-14f;
		yield return new( "small_x_shear_not_hidden_by_large_other_axes", tinyX, false );
		var tinyY = Trs( 1e10f, 2e-12f, 3 ); tinyY.M21 += 2e-14f;
		yield return new( "small_y_shear_not_hidden_by_huge_x", tinyY, false );
		yield return new( "scale100_measured_guard_control", Trs( 100, 100, 100 ) );
	}
	private static NMatrix Trs( float x, float y, float z )
	{
		const float c = .7071067811865476f;
		return new( x * c, x * c, 0, 0, -y * c, y * c, 0, 0, 0, 0, z, 0, 3, 5, -7, 1 );
	}
	private static string Text( float value ) => value.ToString( "R", CultureInfo.InvariantCulture );
	private static double Length( float x, float y, float z ) => Math.Sqrt( (double)x * x + (double)y * y + (double)z * z );
	private static double[] RowErrors( NMatrix expected, NMatrix actual ) => new[]
	{
		Error( expected.M11, expected.M12, expected.M13, actual.M11, actual.M12, actual.M13 ),
		Error( expected.M21, expected.M22, expected.M23, actual.M21, actual.M22, actual.M23 ),
		Error( expected.M31, expected.M32, expected.M33, actual.M31, actual.M32, actual.M33 )
	};
	private static double Error( float a, float b, float c, float x, float y, float z )
	{
		var magnitude = Math.Max( Math.Abs( (double)a ), Math.Max( Math.Abs( (double)b ), Math.Abs( (double)c ) ) );
		var error = Math.Max( Math.Abs( (double)a - x ), Math.Max( Math.Abs( (double)b - y ), Math.Abs( (double)c - z ) ) );
		return magnitude == 0 ? error == 0 ? 0 : double.PositiveInfinity : error / magnitude;
	}
}
