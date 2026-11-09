using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Model-specific image gates for actual native camera captures.</summary>
public static class OrientationImageChecks
{
	public sealed record Region( string Name, int ActualPixels, int ReferencePixels, double IntersectionOverUnion, bool Passed );
	public sealed record Result( int ActualCoverage, int ReferenceCoverage, int MagentaPixelsOutsideReference,
		double ForegroundIntersectionOverUnion, float MaximumRgbError, double MeanRgbError,
		int ReversedWindingChangedPixels, int VisibleColorRegions, Region[] Regions, bool Passed );
	private static readonly string[] Names = ["red_positive_x", "cyan_negative_x", "green_positive_y", "magenta_negative_y", "blue_positive_z", "yellow_negative_z"];

	public static bool IsEmpty( Color[] pixels ) => pixels is { Length: > 0 } && !pixels.Any( Nonblack );

	public static Result Compare( Color[] actual, Color[] reference, Color[] reversedReference )
	{
		OrientationCoreTests.Require( actual is { Length: > 0 } && actual.Length == reference.Length && actual.Length == reversedReference.Length, "Image sizes differ or capture is empty." );
		var actualCoverage = 0;
		var referenceCoverage = 0;
		var intersection = 0;
		var union = 0;
		var magenta = 0;
		var reversedChanged = 0;
		var maximum = 0f;
		double errorSum = 0;
		for ( var pixel = 0; pixel < actual.Length; pixel++ )
		{
			var a = actual[pixel];
			var b = reference[pixel];
			var c = reversedReference[pixel];
			var aOn = Nonblack( a );
			var bOn = Nonblack( b );
			if ( aOn ) actualCoverage++;
			if ( bOn ) referenceCoverage++;
			if ( aOn && bOn ) intersection++;
			if ( aOn || bOn ) union++;
			// Negative-Y geometry is intentionally magenta. A magenta pixel inside
			// valid reference coverage is not evidence of an uncleared target.
			if ( !bOn && a.r > .8f && a.g < .2f && a.b > .8f ) magenta++;
			var error = MathF.Max( MathF.Abs( a.r - b.r ), MathF.Max( MathF.Abs( a.g - b.g ), MathF.Abs( a.b - b.b ) ) );
			maximum = MathF.Max( maximum, error );
			errorSum += (MathF.Abs( a.r - b.r ) + MathF.Abs( a.g - b.g ) + MathF.Abs( a.b - b.b )) / 3;
			if ( MathF.Max( MathF.Abs( b.r - c.r ), MathF.Max( MathF.Abs( b.g - c.g ), MathF.Abs( b.b - c.b ) ) ) > .05f ) reversedChanged++;
		}
		var regions = new List<Region>();
		var visible = 0;
		for ( var region = 0; region < Names.Length; region++ )
		{
			var aCount = 0;
			var bCount = 0;
			var same = 0;
			var either = 0;
			for ( var pixel = 0; pixel < actual.Length; pixel++ )
			{
				var a = Classify( actual[pixel] ) == region;
				var b = Classify( reference[pixel] ) == region;
				if ( a ) aCount++;
				if ( b ) bCount++;
				if ( a && b ) same++;
				if ( a || b ) either++;
			}
			if ( bCount > 5 ) visible++;
			var iou = either == 0 ? 1 : (double)same / either;
			regions.Add( new Region( Names[region], aCount, bCount, iou, iou > .97 ) );
		}
		var foreground = union == 0 ? 0 : (double)intersection / union;
		var passed = actualCoverage > 64 && referenceCoverage > 64 && magenta == 0 && foreground > .97 && reversedChanged > 30 && visible >= 2 && regions.All( region => region.Passed );
		return new Result( actualCoverage, referenceCoverage, magenta, foreground, maximum, errorSum / actual.Length, reversedChanged, visible, regions.ToArray(), passed );
	}

	private static bool Nonblack( Color pixel ) => pixel.r > .03f || pixel.g > .03f || pixel.b > .03f;

	private static int Classify( Color pixel )
	{
		if ( !Nonblack( pixel ) ) return -1;
		// Ratios retain colored regions under ordinary white-light shading. RGB
		// errors are reported separately; this is not a glTF PBR material oracle.
		if ( pixel.r > pixel.g * 2 && pixel.r > pixel.b * 2 ) return 0;
		if ( pixel.g > pixel.r * 2 && pixel.b > pixel.r * 2 ) return 1;
		if ( pixel.g > pixel.r * 2 && pixel.g > pixel.b * 2 ) return 2;
		if ( pixel.r > pixel.g * 2 && pixel.b > pixel.g * 2 ) return 3;
		if ( pixel.b > pixel.r * 2 && pixel.b > pixel.g * 2 ) return 4;
		if ( pixel.r > pixel.b * 2 && pixel.g > pixel.b * 2 ) return 5;
		return -1;
	}
}
