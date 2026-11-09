using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>
/// Independent pixel oracle for real source-panel captures. Inputs are actual
/// linear RGB render-target samples (or a documented invertible display transfer),
/// never gamma-encoded PNG byte fractions. Camera/lighting/exposure stay fixed.
/// The opaque reference uses independently constant decoded RGB (223,229,71),
/// alpha=1, and literal panel geometry, not candidate base-color/alpha evaluation.
/// </summary>
public static class AlphaBlendModeImageChecks
{
	public sealed record Sample( int Panel, int RampRow, double SourceBlackR, double SourceBlackG, double SourceBlackB,
		double SourceWhiteR, double SourceWhiteG, double SourceWhiteB,
		double ReferenceR, double ReferenceG, double ReferenceB,
		double BackgroundBlackR, double BackgroundBlackG, double BackgroundBlackB,
		double BackgroundWhiteR, double BackgroundWhiteG, double BackgroundWhiteB );
	public sealed record Measurement( int Samples, double MaximumLinearRgbError, double MaximumCoverageError, double MinimumBackgroundContrast );

	public static Measurement Measure( IReadOnlyList<Sample> samples )
	{
		Require( samples is { Count: 25 }, "Exactly five independent ramp texels in each of five source panels are required." );
		var seen = new HashSet<(int, int)>(); double maxRgb = 0, maxCoverage = 0, contrast = double.PositiveInfinity;
		foreach ( var s in samples )
		{
			Require( s.Panel is >= 0 and < 5 && s.RampRow is >= 0 and < 5 && seen.Add( (s.Panel, s.RampRow) ), "Missing/repeated/invalid panel ramp sample." );
			var a = AlphaBlendModeReference.Coverage( AlphaBlendModeReference.PanelMaterials[s.Panel], AlphaBlendModeReference.RampAlpha[s.RampRow] / 255.0 );
			var channels = new[] { (s.SourceBlackR,s.SourceWhiteR,s.ReferenceR,s.BackgroundBlackR,s.BackgroundWhiteR),
				(s.SourceBlackG,s.SourceWhiteG,s.ReferenceG,s.BackgroundBlackG,s.BackgroundWhiteG), (s.SourceBlackB,s.SourceWhiteB,s.ReferenceB,s.BackgroundBlackB,s.BackgroundWhiteB) };
			foreach ( var (black, white, reference, bgBlack, bgWhite) in channels )
			{
				Require( new[] { black, white, reference, bgBlack, bgWhite }.All( double.IsFinite ), "Nonfinite actual/expected capture sample." );
				var delta = bgWhite - bgBlack; Require( delta >= .25, "Background control contrast is insufficient or capture is non-linear/unmatched." );
				contrast = Math.Min( contrast, delta );
				maxRgb = Math.Max( maxRgb, Math.Max( Math.Abs( black - (reference * a + bgBlack * (1 - a)) ), Math.Abs( white - (reference * a + bgWhite * (1 - a)) ) ) );
				maxCoverage = Math.Max( maxCoverage, Math.Abs( 1 - (white - black) / delta - a ) );
			}
		}
		return new( samples.Count, maxRgb, maxCoverage, contrast );
	}
	public static void RequireWithin( Measurement result, double maximumLinearRgbError, double maximumCoverageError )
	{
		Require( double.IsFinite( maximumLinearRgbError ) && maximumLinearRgbError >= 0 && maximumLinearRgbError < .05 && double.IsFinite( maximumCoverageError ) && maximumCoverageError >= 0 && maximumCoverageError < .05, "Reviewed alpha tolerances must be finite and below the wrong-mode separation." );
		Require( result.Samples == 25 && result.MinimumBackgroundContrast >= .25 && result.MaximumLinearRgbError <= maximumLinearRgbError && result.MaximumCoverageError <= maximumCoverageError, "Actual source panels violate straight alpha/cutoff/OPAQUE-ignore pixel acceptance." );
	}

	/// <summary>Independent nonintersecting two-layer over oracle; tests final pixels, not a mandated renderer sorting algorithm.</summary>
	public static double[] TwoLayers( double[] nearRgb, double nearAlpha, double[] farRgb, double farAlpha, double[] background )
	{
		Require( new[] { nearRgb, farRgb, background }.All( x => x is { Length: 3 } && x.All( double.IsFinite ) ) && Unit( nearAlpha ) && Unit( farAlpha ), "Invalid literal layer input." );
		return Enumerable.Range( 0, 3 ).Select( c => nearRgb[c] * nearAlpha + (farRgb[c] * farAlpha + background[c] * (1 - farAlpha)) * (1 - nearAlpha) ).ToArray();
	}
	public static void RequireLayers( double[] actualNearFirst, double[] actualFarFirst, double tolerance )
	{
		Require( double.IsFinite( tolerance ) && tolerance >= 0 && tolerance < .03, "Invalid reviewed layer tolerance." );
		// Red near (.5), green far (.25), blue background. All inputs linear.
		double[] expected = [.5, .125, .375];
		foreach ( var actual in new[] { actualNearFirst, actualFarFirst } )
			Require( actual is { Length: 3 } && actual.Select( (v,c) => double.IsFinite( v ) && Math.Abs( v - expected[c] ) <= tolerance ).All( x => x ), "Controlled separated layers depend on authored submission order or violate over composition." );
	}
	public static void RequireMaskDepthAndEquality( double[][] actual, double tolerance )
	{
		Require( actual is { Length: 4 } && double.IsFinite( tolerance ) && tolerance >= 0 && tolerance < .03, "Need four actual depth/cutoff captures and a reviewed tolerance." );
		// Draw near red MASK first, then farther blue opaque. For below/equal/
		// above-cutoff and alpha0/cutoff0: only discarded near geometry exposes blue.
		double[][] expected = [[0,0,1], [1,0,0], [1,0,0], [1,0,0]];
		for ( var i = 0; i < 4; i++ ) Require( actual[i] is { Length: 3 } && actual[i].Select( (v,c) => double.IsFinite( v ) && Math.Abs( v - expected[i][c] ) <= tolerance ).All( x => x ), "MASK discard/depth write or cutoff equality failed in actual pixels at case " + i );
	}
	public static void RequireProduct( double actualCoverage, bool rgbVertex, double tolerance )
	{
		// Source texture A=.5, factor A=.375, RGBA COLOR_0 A=.25; RGB COLOR_0 supplies A=1.
		var expected = rgbVertex ? .1875 : .046875;
		Require( double.IsFinite( actualCoverage ) && double.IsFinite( tolerance ) && tolerance >= 0 && tolerance < .01 && Math.Abs( actualCoverage - expected ) <= tolerance, "Actual linear texture*factor*COLOR_0 alpha product differs (or RGB vertex alpha was not one)." );
	}
	private static bool Unit( double x ) => double.IsFinite( x ) && x is >= 0 and <= 1;
	private static void Require( bool condition, string message ) => AlphaBlendModeCoreTests.Require( condition, message );
}
