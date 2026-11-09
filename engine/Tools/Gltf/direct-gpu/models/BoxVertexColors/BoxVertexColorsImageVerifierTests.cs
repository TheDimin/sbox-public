using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

/// <summary>Pure checker controls only; synthetic pixels are never native image evidence.</summary>
public static class BoxVertexColorsImageVerifierTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public const int ExpectedCases = 8;
	public static Result[] RunAll( string checkoutRoot )
	{
		_ = checkoutRoot; var results = new List<Result>();
		Run( "identical_nonempty_rgba_has_exact_iou_and_zero_error", () =>
		{
			var comparison = BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ), Literal( 32, 32, 128 ) );
			Require( comparison.ForegroundIoU == 1 && comparison.ChangedRgbPixels == 0 && comparison.MeanAbsoluteRgbByteError == 0 && comparison.MaximumRgbByteError == 0, "Identical reference comparison is wrong." );
		} );
		Run( "white_control_detects_ignored_colors", () => { var comparison = BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ), Literal( 32, 32, 255 ) ); Require( comparison.ChangedRgbPixels == 1024 && comparison.MeanAbsoluteRgbByteError == 127 && comparison.MaximumRgbByteError == 127, "White control lost sensitivity." ); } );
		Run( "gamma_control_detects_fractional_linear_color_decode", () => { var comparison = BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ), Literal( 32, 32, 55 ) ); Require( comparison.ChangedRgbPixels == 1024 && comparison.MeanAbsoluteRgbByteError == 73, "Gamma control lost sensitivity." ); } );
		Run( "empty_expected_is_not_a_positive_parity_reference", () => { var comparison = BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 0 ), Literal( 32, 32, 0 ) ); Require( double.IsNaN( comparison.ForegroundIoU ) && comparison.ReferenceForeground == 0 && comparison.ActualForeground == 0, "Empty agreement must not become IoU one." ); } );
		Run( "missing_expected_image_rejects", () => Rejected( () => BoxVertexColorsImageProbe.Compare( null, Literal( 32, 32, 128 ) ) ) );
		Run( "mismatched_dimensions_reject", () => Rejected( () => BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ), Literal( 64, 16, 128 ) ) ) );
		Run( "truncated_rgba_rejects", () => Rejected( () => BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ) with { Rgba = new byte[3] }, Literal( 32, 32, 128 ) ) ) );
		Run( "missing_capture_metadata_rejects", () => Rejected( () => BoxVertexColorsImageProbe.Compare( Literal( 32, 32, 128 ) with { Metadata = null }, Literal( 32, 32, 128 ) ) ) );
		Require( results.Count == ExpectedCases, "Frozen image checker inventory changed." ); return results.ToArray();
		void Run( string name, Action test ) { try { test(); results.Add( new( name, "synthetic_image_checker_control", true, null ) ); } catch ( Exception error ) { results.Add( new( name, "synthetic_image_checker_control", false, error.ToString() ) ); } }
	}
	private static BoxVertexColorsImageProbe.Frame Literal( int width, int height, byte rgb ) => new( width, height,
		Enumerable.Range( 0, width * height ).SelectMany( _ => new[] { rgb, rgb, rgb, (byte)255 } ).ToArray(), "synthetic_checker_pixels", 1, new Dictionary<string, string> { ["scope"] = "synthetic_checker_control" } );
	private static void Rejected( Action test ) { try { test(); } catch ( InvalidOperationException ) { return; } throw new InvalidOperationException( "Malformed expected/actual image was accepted." ); }
	private static void Require( bool condition, string message ) => BoxVertexColorsReference.Require( condition, message );
}
