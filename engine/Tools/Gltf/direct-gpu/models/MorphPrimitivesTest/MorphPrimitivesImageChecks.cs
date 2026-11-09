using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using static MorphPrimitivesReference;

/// <summary>Independent pixels plus native fresh empty/positive/deleted controls, preserving both primitive regions.</summary>
public static class MorphPrimitivesImageChecks
{
	public sealed record Frame( int Width, int Height, byte[] Rgba, string PresentationIdentity );
	public sealed record Region( int Primitive, int Left, int Top, int Right, int Bottom );
	/// <summary>Actual independent flat versus half-weight render must visibly expose an omitted default morph.</summary>
	public static object AssertDefaultMorphSensitivity( Frame empty, Frame flatReference, Frame halfWeightReference )
	{
		foreach ( var f in new[] { flatReference, halfWeightReference } ) Require( f.Width == empty.Width && f.Height == empty.Height && f.Rgba.Length == checked(empty.Width * empty.Height * 4) && f.PresentationIdentity == empty.PresentationIdentity, "Morph negative control presentation changed." );
		var changed = 0; var maximum = 0; var flatCoverage = 0; var halfCoverage = 0;
		for ( var pixel = 0; pixel < empty.Width * empty.Height; pixel++ )
		{
			var offset = pixel * 4; var error = 0; var flatDifference = 0; var halfDifference = 0;
			for ( var c = 0; c < 3; c++ ) { error = Math.Max( error, Math.Abs( flatReference.Rgba[offset + c] - halfWeightReference.Rgba[offset + c] ) ); flatDifference = Math.Max( flatDifference, Math.Abs( flatReference.Rgba[offset + c] - empty.Rgba[offset + c] ) ); halfDifference = Math.Max( halfDifference, Math.Abs( halfWeightReference.Rgba[offset + c] - empty.Rgba[offset + c] ) ); }
			if ( flatDifference > 2 ) flatCoverage++; if ( halfDifference > 2 ) halfCoverage++; if ( error > 8 ) changed++; maximum = Math.Max( maximum, error );
		}
		Require( flatCoverage >= 32 && halfCoverage >= 32 && changed >= 16 && maximum > 8, "Camera/material cannot expose an omitted default morph; select a useful independently framed view." );
		return new { flatCoverage, halfCoverage, pixelsChangedBeyondEightBytes = changed, maximumRgbByteError = maximum };
	}
	public static object AssertMatched( Frame emptyBefore, Frame expected, Frame emptyAfterReference, Frame actual, Frame deletedAfter,
		IReadOnlyList<Region> primitiveRegions, string expectedProvenance, string materialScope )
	{
		Require( !string.IsNullOrWhiteSpace( expectedProvenance ) && !string.IsNullOrWhiteSpace( materialScope ), "Independent reference provenance and source/diagnostic material scope must be explicit." );
		Require( emptyBefore.Width > 0 && emptyBefore.Height > 0 && !string.IsNullOrWhiteSpace( emptyBefore.PresentationIdentity ), "Invalid presentation identity." );
		foreach ( var f in new[] { emptyBefore, expected, emptyAfterReference, actual, deletedAfter } ) Require( f.Width == emptyBefore.Width && f.Height == emptyBefore.Height && f.Rgba.Length == checked(f.Width * f.Height * 4) && f.PresentationIdentity == emptyBefore.PresentationIdentity, "Camera/dimensions/material/presentation drift." );
		Require( emptyBefore.Rgba.Zip( emptyAfterReference.Rgba, (a, b) => Math.Abs( a - b ) ).Max() <= 1 && emptyBefore.Rgba.Zip( deletedAfter.Rgba, (a, b) => Math.Abs( a - b ) ).Max() <= 1, "Fresh deletion controls did not restore empty scene." );
		Require( primitiveRegions.Count == 2 && primitiveRegions.Select( x => x.Primitive ).Order().SequenceEqual( new[] { 0, 1 } ), "Both red and green primitive regions are required." );
		foreach ( var region in primitiveRegions ) Require( region.Left >= 0 && region.Top >= 0 && region.Right <= emptyBefore.Width && region.Bottom <= emptyBefore.Height && region.Left < region.Right && region.Top < region.Bottom, "Invalid independently selected primitive region." );
		var first = primitiveRegions[0]; var second = primitiveRegions[1];
		Require( first.Right <= second.Left || second.Right <= first.Left || first.Bottom <= second.Top || second.Bottom <= first.Top, "Primitive witness regions must be disjoint to detect an omitted primitive." );
		var regionRows = primitiveRegions.Select( r => Measure( r.Left, r.Top, r.Right, r.Bottom, r.Primitive ) ).ToArray();
		var whole = Measure( 0, 0, emptyBefore.Width, emptyBefore.Height, -1 );
		return new { expectedProvenance, materialScope, whole, primitiveRegions = regionRows, minimumIou = .995, maximumMeanRgbByteError = 1, maximumRgbByteError = 8 };
		object Measure( int left, int top, int right, int bottom, int primitive )
		{
			var intersection = 0; var union = 0; var expectedCoverage = 0; var actualCoverage = 0; var channels = 0; long totalError = 0; var maximum = 0;
			for ( var y = top; y < bottom; y++ ) for ( var x = left; x < right; x++ )
			{
				var offset = (y * emptyBefore.Width + x) * 4;
				bool Covered( Frame f ) => Enumerable.Range( 0, 3 ).Max( c => Math.Abs( f.Rgba[offset + c] - emptyBefore.Rgba[offset + c] ) ) > 2;
				var e = Covered( expected ); var a = Covered( actual ); if ( e ) expectedCoverage++; if ( a ) actualCoverage++; if ( e && a ) intersection++; if ( e || a ) union++;
				if ( !e && !a ) continue;
				for ( var c = 0; c < 3; c++ ) { var error = Math.Abs( expected.Rgba[offset + c] - actual.Rgba[offset + c] ); totalError += error; maximum = Math.Max( maximum, error ); channels++; }
			}
			Require( expectedCoverage >= 16 && actualCoverage >= 16 && union > 0 && channels > 0, "Missing/empty primitive or reference cannot establish pixels: " + primitive );
			var iou = intersection / (double)union; var mean = totalError / (double)channels;
			Require( iou >= .995 && mean <= 1 && maximum <= 8, $"Primitive {primitive} image differs: IoU {iou:R}, mean RGB error {mean:R}, max {maximum}." );
			return new { primitive, expectedCoverage, actualCoverage, iou, meanRgbByteError = mean, maximumRgbByteError = maximum };
		}
	}
	public static MorphPrimitivesCoreTests.Result[] RunVerifierControls()
	{
		const int width = 20; const int height = 10; var empty = new byte[width * height * 4]; var expected = new byte[empty.Length];
		var regions = new[] { new Region( 0, 0, 0, 10, 10 ), new Region( 1, 10, 0, 20, 10 ) };
		foreach ( var r in regions ) for ( var y = 2; y < 8; y++ ) for ( var x = r.Left + 2; x < r.Right - 2; x++ ) { var offset = (y * width + x) * 4; expected[offset + r.Primitive] = 180; expected[offset + 3] = 255; }
		Frame F( byte[] bytes, string id = "synthetic-fixed-camera-and-material" ) => new( width, height, bytes, id );
		object Check( byte[] wanted, byte[] observed, byte[] deleted = null, string identity = "synthetic-fixed-camera-and-material" ) => AssertMatched( F( empty ), F( wanted ), F( empty ), F( observed, identity ), F( deleted ?? empty ), regions, "synthetic controls, not model/GPU image evidence", "diagnostic synthetic RGB" );
		var rows = new List<MorphPrimitivesCoreTests.Result>();
		Run( "exact_two_region_positive", () => Check( expected, expected ), false );
		Run( "empty_expected_rejected", () => Check( empty, expected ), true );
		Run( "empty_actual_rejected", () => Check( expected, empty ), true );
		Run( "failed_deletion_rejected", () => Check( expected, expected, expected ), true );
		Run( "presentation_drift_rejected", () => Check( expected, expected, identity: "different-camera" ), true );
		Run( "missing_green_primitive_rejected", () => { var bad = (byte[])expected.Clone(); for ( var y = 0; y < height; y++ ) Array.Clear( bad, (y * width + 10) * 4, 40 ); return Check( expected, bad ); }, true );
		Run( "wrong_red_green_material_binding_rejected", () => { var bad = (byte[])expected.Clone(); for ( var i = 0; i < bad.Length; i += 4 ) (bad[i], bad[i + 1]) = (bad[i + 1], bad[i]); return Check( expected, bad ); }, true );
		return rows.ToArray();
		void Run( string name, Func<object> body, bool reject ) => MorphPrimitivesCoreTests.RunCase( rows, name, "synthetic_image_checker_control", () => { var rejected = false; try { _ = body(); } catch ( InvalidOperationException ) { rejected = true; } Require( reject == rejected, "Image verifier accepted/rejected the wrong control." ); } );
	}
}
