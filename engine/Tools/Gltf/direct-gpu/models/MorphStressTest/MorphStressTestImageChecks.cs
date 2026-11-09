using System;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using static MorphStressTestReference;

public static class MorphStressTestImageChecks
{
	public sealed record Measurement( int ActualCoverage, int ReferenceCoverage, double ForegroundIoU, double MeanRgbError, double MaximumRgbError, int ChangedPixels, int UnmorphedNegativeChangedPixels );
	public static bool IsEmpty( Color[] image ) => image is { Length: > 0 } && !image.Any( Foreground );
	public static Measurement Compare( Color[] actual, Color[] reference, Color[] unmorphedNegative )
	{
		Require( actual is { Length: > 0 } && reference?.Length == actual.Length && unmorphedNegative?.Length == actual.Length, "Missing/mismatched matched-image input." );
		var ac = 0; var rc = 0; var intersection = 0; var union = 0; var changed = 0; var negative = 0; double error = 0, maximum = 0;
		for ( var i = 0; i < actual.Length; i++ )
		{
			var a = actual[i]; var r = reference[i]; var n = unmorphedNegative[i]; Require( Finite( a ) && Finite( r ) && Finite( n ), "Nonfinite image sample." );
			var af = Foreground( a ); var rf = Foreground( r ); if ( af ) ac++; if ( rf ) rc++; if ( af && rf ) intersection++; if ( af || rf ) union++;
			var delta = Delta( a, r ); maximum = Math.Max( maximum, delta ); error += (Math.Abs( a.r - r.r ) + Math.Abs( a.g - r.g ) + Math.Abs( a.b - r.b )) / 3;
			if ( delta > .03 ) changed++; if ( Delta( n, r ) > .03 ) negative++;
		}
		return new( ac, rc, union > 0 ? (double)intersection / union : 0, error / actual.Length, maximum, changed, negative );
	}
	public static void RequireWithin( Measurement m, double minimumIoU, double maximumMeanRgbError, double maximumChangedFraction, int minimumNegativeChangedPixels )
	{
		Require( minimumIoU is >= 0 and <= 1 && double.IsFinite( maximumMeanRgbError ) && maximumMeanRgbError >= 0 && maximumChangedFraction is >= 0 and <= 1 && minimumNegativeChangedPixels > 0, "Explicit reviewed image limits required." );
		Require( m.ActualCoverage > 128 && m.ReferenceCoverage > 128 && m.ForegroundIoU >= minimumIoU && m.MeanRgbError <= maximumMeanRgbError && m.ChangedPixels <= Math.Max( 12, maximumChangedFraction * m.ReferenceCoverage ) && m.UnmorphedNegativeChangedPixels >= minimumNegativeChangedPixels, "Independent morph image/negative control exceeded selected bounds: " + m );
	}
	private static bool Foreground( Color c ) => c.r > .03 || c.g > .03 || c.b > .03;
	private static bool Finite( Color c ) => float.IsFinite( c.r ) && float.IsFinite( c.g ) && float.IsFinite( c.b );
	private static double Delta( Color a, Color b ) => Math.Max( Math.Abs( a.r - b.r ), Math.Max( Math.Abs( a.g - b.g ), Math.Abs( a.b - b.b ) ) );
}
