using System;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

public static class NegativeScaleImageChecks
{
	public sealed record Measurement( int ActualCoverage, int ReferenceCoverage, double ForegroundIoU, double MeanRgbError, float MaximumRgbError, int RgbChangedPixels, int ReversedSingleSidedChangedPixels, bool Passed );
	public static bool IsEmpty( Color[] image ) => image is { Length: > 0 } && !image.Any( On );
	public static Measurement Compare( Color[] actual, Color[] reference, Color[] reversedSingleSided )
	{
		Require( actual is { Length: > 0 } && actual.Length == reference.Length && actual.Length == reversedSingleSided.Length,"Missing or mismatched image dimensions." );
		var actualCoverage = 0; var referenceCoverage = 0; var intersection = 0; var union = 0; var changed = 0; var negativeChanged = 0; var maximum = 0f; var error = 0d;
		for ( var i = 0; i < actual.Length; i++ )
		{
			var a = actual[i]; var b = reference[i]; var c = reversedSingleSided[i]; var ao = On( a ); var bo = On( b ); if ( ao ) actualCoverage++; if ( bo ) referenceCoverage++; if ( ao && bo ) intersection++; if ( ao || bo ) union++;
			var delta = MathF.Max( MathF.Abs( a.r - b.r ),MathF.Max( MathF.Abs( a.g - b.g ),MathF.Abs( a.b - b.b ) ) ); maximum = MathF.Max( maximum,delta ); error += (Math.Abs( a.r - b.r ) + Math.Abs( a.g - b.g ) + Math.Abs( a.b - b.b )) / 3;
			if ( delta > .03f ) changed++; if ( MathF.Max( MathF.Abs( c.r - b.r ),MathF.Max( MathF.Abs( c.g - b.g ),MathF.Abs( c.b - b.b ) ) ) > .03f ) negativeChanged++;
		}
		var iou = union == 0 ? 0 : (double)intersection / union; var mean = error / actual.Length;
		var passed = actualCoverage > 128 && referenceCoverage > 128 && iou >= .99 && mean <= .003 && changed <= Math.Max( 12,.015 * referenceCoverage ) && negativeChanged >= 30;
		return new( actualCoverage,referenceCoverage,iou,mean,maximum,changed,negativeChanged,passed );
	}
	private static bool On( Color value ) => value.r > .03f || value.g > .03f || value.b > .03f;
}
