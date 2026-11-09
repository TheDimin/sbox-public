using System;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using static MeshPrimitiveModesReference;

public static class MeshPrimitiveModesImageChecks
{
	public sealed record Measurement( string Status, int Mode, int ActualCoverage, int ReferenceCoverage, int Intersection, int Union,
		double ForegroundIoU, int NegativeChangedPixels, int UnexpectedMagentaPixels, double MaximumRgbError, double MeanRgbError, bool ExactGeometryCoverage );
	public static bool Foreground( global::Color p ) => p.r > .03f || p.g > .03f || p.b > .03f;
	public static void RequireEmpty( global::Color[] pixels ) => Require( pixels is { Length: > 0 } && !pixels.Any( Foreground ), "Fresh/deleted scene is not empty." );
	public static Measurement Measure( int mode, global::Color[] actual, global::Color[] reference, global::Color[] negative )
	{
		Require( actual is { Length: > 0 } && reference is not null && negative is not null && actual.Length == reference.Length && reference.Length == negative.Length,
			"Missing, empty or differently sized actual/reference/negative image." );
		var aCount = 0; var rCount = 0; var intersection = 0; var union = 0; var changed = 0; var magenta = 0; double maximum = 0, sum = 0;
		for ( var i = 0; i < actual.Length; i++ )
		{
			var a = Foreground( actual[i] ); var r = Foreground( reference[i] ); var n = Foreground( negative[i] );
			if ( a ) aCount++; if ( r ) rCount++; if ( a && r ) intersection++; if ( a || r ) union++; if ( r != n ) changed++;
			if ( actual[i].r > .8f && actual[i].g < .2f && actual[i].b > .8f && !r ) magenta++;
			foreach ( var e in new[] { Math.Abs( (double)actual[i].r - reference[i].r ), Math.Abs( (double)actual[i].g - reference[i].g ), Math.Abs( (double)actual[i].b - reference[i].b ) } )
			{ maximum = Math.Max( maximum, e ); sum += e; }
		}
		Require( aCount > 0 && rCount > 0, "Actual/reference primitive must have positive visible geometry; an empty match is never parity." );
		Require( changed > 0, "Independent negative topology did not change pixels; the camera/material cannot discriminate this mode." );
		return new( "measured-not-accepted", mode, aCount, rCount, intersection, union, union == 0 ? 0 : intersection / (double)union,
			changed, magenta, maximum, sum / (actual.Length * 3), intersection == union && magenta == 0 );
	}
	public static void AcceptExactGeometry( Measurement result ) => Require( result.ExactGeometryCoverage, "Actual/reference foreground masks differ; preserve measurement and investigate before adjusting acceptance." );
	/// <summary>Four manufactured-image verifier controls; never actual render evidence.</summary>
	public static MeshPrimitiveModesCoreTests.Result[] RunVerifierControls()
	{
		var rows = new System.Collections.Generic.List<MeshPrimitiveModesCoreTests.Result>();
		var reference = Enumerable.Repeat( global::Color.White, 7 ).Append( global::Color.Black ).ToArray();
		var negative = new[] { global::Color.White }.Concat( Enumerable.Repeat( global::Color.Black, 7 ) ).ToArray();
		Run( "accept_exact_nonempty_point_mask", () => AcceptExactGeometry( Measure( 0, reference, reference, negative ) ) );
		Run( "reject_empty_image_agreement", () => MustReject( () => Measure( 0, negative.Select( _ => global::Color.Black ).ToArray(), negative.Select( _ => global::Color.Black ).ToArray(), negative ) ) );
		Run( "reject_nondiscriminating_negative", () => MustReject( () => Measure( 0, reference, reference, reference ) ) );
		Run( "reject_missing_visible_point", () => MustReject( () => AcceptExactGeometry( Measure( 0, negative, reference, negative ) ) ) );
		return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "verifier-only synthetic image", true, null ) ); } catch ( Exception error ) { rows.Add( new( name, "verifier-only synthetic image", false, error.ToString() ) ); } }
	}
	private static void MustReject( Action body ) { try { body(); } catch ( System.IO.InvalidDataException ) { return; } throw new InvalidOperationException( "The image verifier negative control was accepted." ); }
}
