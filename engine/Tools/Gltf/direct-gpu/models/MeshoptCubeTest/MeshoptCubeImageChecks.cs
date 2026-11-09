using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;

/// <summary>
/// Material image gate requiring independently authored expected pixels and all
/// 35 model regions. This checker never manufactures the expected render. The
/// coordinator must retain camera/material/provenance receipts for its inputs.
/// </summary>
public static class MeshoptCubeImageChecks
{
	public sealed record Frame( int Width, int Height, byte[] Rgba, string PresentationIdentity );
	public sealed record Region( int Node, int Left, int Top, int Right, int Bottom );
	public static object AssertMatched( Frame emptyBefore, Frame independentExpected, Frame emptyAfterReference, Frame actual, Frame deletedAfter,
		IReadOnlyList<Region> regions, string independentExpectedProvenance )
	{
		Require( !string.IsNullOrWhiteSpace( independentExpectedProvenance ), "Independent expected source/projection/material provenance is required." );
		var frames = new[] { emptyBefore, independentExpected, emptyAfterReference, actual, deletedAfter };
		Require( emptyBefore.Width > 0 && emptyBefore.Height > 0 && !string.IsNullOrWhiteSpace( emptyBefore.PresentationIdentity ), "Invalid matched presentation contract." );
		foreach ( var frame in frames ) Require( frame.Width == emptyBefore.Width && frame.Height == emptyBefore.Height && frame.Rgba.Length == checked(frame.Width * frame.Height * 4) && frame.PresentationIdentity == emptyBefore.PresentationIdentity, "Capture camera/dimensions/presentation changed." );
		Require( regions.Count == NodeCount && regions.Select( x => x.Node ).Order().SequenceEqual( Enumerable.Range( 0, NodeCount ) ), "All ten label and 25 cube regions must be covered independently." );
		Require( emptyBefore.Rgba.Zip( emptyAfterReference.Rgba, (a, b) => Math.Abs( a - b ) ).Max() <= 1 && emptyBefore.Rgba.Zip( deletedAfter.Rgba, (a, b) => Math.Abs( a - b ) ).Max() <= 1, "Fresh deletion controls contain geometry or presentation drift." );
		var results = new List<object>();
		foreach ( var region in regions )
		{
			Require( region.Left >= 0 && region.Top >= 0 && region.Right <= emptyBefore.Width && region.Bottom <= emptyBefore.Height && region.Left < region.Right && region.Top < region.Bottom, "Invalid model region." );
			var intersection = 0; var union = 0; var expectedCoverage = 0; var actualCoverage = 0; long absoluteError = 0; var channels = 0; var maxError = 0;
			for ( var y = region.Top; y < region.Bottom; y++ ) for ( var x = region.Left; x < region.Right; x++ )
			{
				var offset = (y * emptyBefore.Width + x) * 4;
				bool Covered( Frame frame ) => Enumerable.Range( 0, 3 ).Max( c => Math.Abs( frame.Rgba[offset + c] - emptyBefore.Rgba[offset + c] ) ) > 2;
				var expected = Covered( independentExpected ); var observed = Covered( actual );
				if ( expected ) expectedCoverage++; if ( observed ) actualCoverage++; if ( expected && observed ) intersection++; if ( expected || observed ) union++;
				if ( !expected && !observed ) continue;
				for ( var c = 0; c < 3; c++ ) { var error = Math.Abs( independentExpected.Rgba[offset + c] - actual.Rgba[offset + c] ); absoluteError += error; maxError = Math.Max( maxError, error ); channels++; }
			}
			Require( expectedCoverage >= 4 && actualCoverage >= 4 && union > 0 && channels > 0, "Empty/missing model region cannot establish appearance: " + region.Node );
			var iou = intersection / (double)union; var mean = absoluteError / (double)channels;
			Require( iou >= .98 && mean <= 1 && maxError <= 8, $"Region {region.Node} material pixels differ: IoU {iou:R}, mean RGB byte error {mean:R}, max {maxError}." );
			results.Add( new { region.Node, role = region.Node < 10 ? "source textured MASK label" : "source/default cube with vertex colors where authored", expectedCoverage, actualCoverage, iou, meanRgbByteError = mean, maxRgbByteError = maxError } );
		}
		return new { independentExpectedProvenance, presentation = emptyBefore.PresentationIdentity, regions = results, minimumIou = .98, maximumMeanRgbByteError = 1, maximumRgbByteError = 8 };
	}

	public static MeshoptCubeCoreTests.Result[] RunVerifierControls()
	{
		const int width = 28; const int height = 20; var empty = new byte[width * height * 4]; var expected = new byte[empty.Length];
		var regions = Enumerable.Range( 0, NodeCount ).Select( node => new Region( node, node % 7 * 4, node / 7 * 4, node % 7 * 4 + 4, node / 7 * 4 + 4 ) ).ToArray();
		foreach ( var r in regions ) for ( var y = r.Top + 1; y < r.Bottom; y++ ) for ( var x = r.Left + 1; x < r.Right; x++ )
		{
			var offset = (y * width + x) * 4; expected[offset] = (byte)(32 + r.Node * 3); expected[offset + 1] = (byte)(220 - r.Node * 2); expected[offset + 2] = 64; expected[offset + 3] = 255;
		}
		Frame F( byte[] rgba, string identity = "synthetic-fixed-camera" ) => new( width, height, rgba, identity );
		object Check( byte[] wanted, byte[] observed, byte[] deleted = null, string identity = "synthetic-fixed-camera" ) => AssertMatched( F( empty ), F( wanted ), F( empty ), F( observed, identity ), F( deleted ?? empty ), regions, "synthetic checker controls only; not GPU image evidence" );
		var rows = new List<MeshoptCubeCoreTests.Result>();
		Run( "synthetic_exact_match_all_35_regions", () => Check( expected, expected ) );
		Run( "rejects_empty_expected_image", () => Reject( () => Check( empty, expected ) ) );
		Run( "rejects_empty_actual_image", () => Reject( () => Check( expected, empty ) ) );
		Run( "rejects_failed_deletion_control", () => Reject( () => Check( expected, expected, expected ) ) );
		Run( "rejects_different_camera_identity", () => Reject( () => Check( expected, expected, identity: "different-camera" ) ) );
		Run( "rejects_wrong_label_color_binding", () =>
		{
			var bad = (byte[])expected.Clone(); var r = regions[0]; for ( var y = r.Top + 1; y < r.Bottom; y++ ) for ( var x = r.Left + 1; x < r.Right; x++ ) bad[(y * width + x) * 4] = 255;
			Reject( () => Check( expected, bad ) );
		} );
		Run( "rejects_missing_last_v1_codec_cube", () =>
		{
			var bad = (byte[])expected.Clone(); var r = regions[^1]; for ( var y = r.Top; y < r.Bottom; y++ ) for ( var x = r.Left; x < r.Right; x++ ) Array.Clear( bad, (y * width + x) * 4, 4 );
			Reject( () => Check( expected, bad ) );
		} );
		return rows.ToArray();
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, "synthetic image verifier control only", true, null ) ); } catch ( Exception e ) { rows.Add( new( name, "synthetic image verifier control only", false, e.ToString() ) ); } }
		void Reject( Action body ) { try { body(); } catch ( InvalidOperationException ) { return; } throw new Exception( "Faulty capture was accepted." ); }
	}
}
