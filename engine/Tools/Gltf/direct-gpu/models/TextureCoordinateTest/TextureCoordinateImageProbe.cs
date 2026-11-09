using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;

/// <summary>Actual image controls plus source-PNG label landmarks independent of the stock sampler.</summary>
public static class TextureCoordinateImageProbe
{
	public sealed record Frame( int Width, int Height, byte[] Rgba, string ImagePath, double MonotonicSeconds, IReadOnlyDictionary<string, string> Metadata );
	public sealed record Difference( int ReferenceForeground, int ActualForeground, double IoU, int ChangedRgbPixels, double MeanAbsoluteRgbByteError );
	public sealed record Landmark( int Mesh, int SourceX, int SourceY, NVector2 BlackPixel, NVector2 WhitePixel, double DarkLuminance, double WhiteLuminance, double DarkToWhiteRatio );
	public sealed record Row( string Form, string Placement, Difference ActualVsLiteralReference, Difference FlippedVVsLiteralReference,
		TextureCoordinateGpuChecks.Measurement Gpu, Landmark[] ActualLandmarks, Landmark[] FlippedVLandmarks, Frame Reference, Frame Actual,
		string Status = "measured-not-accepted: thresholds and independent source-PNG landmarks require explicit review; sampler filtering/color-space fidelity is separate" );

	/// <summary>
	/// The coordinator supplies the established native capture fixture and its
	/// matching camera projection into top-left pixel coordinates. Use >=1024 square
	/// capture with every textured quad >=220 pixels wide for label-stroke probes.
	/// Frame.Metadata must identify "camera" and "presentation" consistently.
	/// No Model/Mesh/camera/editor is created here.
	/// </summary>
	public static async Task<Row[]> MeasureAll( string checkoutRoot, SceneWorld world, Material borrowedComputeMaterial,
		Func<Task<Frame>> captureCompletedNativeFrame, Func<NVector3, NVector2> projectEnginePointToTopLeftPixel )
	{
		ArgumentNullException.ThrowIfNull( captureCompletedNativeFrame ); ArgumentNullException.ThrowIfNull( projectEnginePointToTopLeftPixel );
		var reference = TextureCoordinateReference.Open( checkoutRoot ); var result = new List<Row>();
		foreach ( var form in reference.Forms() )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, borrowedComputeMaterial );
			Material Borrow( int? index ) => asset.Primitives.Single( value => value.Plan.Material == index ).Material;
			foreach ( var placement in new[] { (Name: "identity", Matrix: NMatrix.Identity), (Name: "reflected_y", Matrix: NMatrix.CreateScale( 1, -1, 1 )) } )
			{
				RequireEmpty( await captureCompletedNativeFrame() );
				Frame expected; int callbacks;
				var literal = new TextureCoordinateReferenceScene( world, form, placement.Matrix, Borrow );
				try
				{
					expected = await captureCompletedNativeFrame(); var repeated = await captureCompletedNativeFrame();
					TextureCoordinateCoreTests.Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0 && Compare( expected, repeated ).ChangedRgbPixels == 0, "Independent native reference must be positive and static." );
					callbacks = literal.DrawCalls;
				}
				finally { literal.Dispose(); }
				RequireEmpty( await captureCompletedNativeFrame() ); TextureCoordinateCoreTests.Require( literal.DrawCalls == callbacks, "Reference callbacks continued after deletion." );
				Frame flipped;
				using ( var control = new TextureCoordinateReferenceScene( world, form, placement.Matrix, Borrow, flipV: true ) )
				{
					flipped = await captureCompletedNativeFrame(); TextureCoordinateCoreTests.Require( control.DrawCalls > 0 && control.CallbackErrors.Length == 0, "Whole-image V-flip control did not submit." );
				}
				RequireEmpty( await captureCompletedNativeFrame() );
				Frame actual; TextureCoordinateGpuChecks.Measurement gpu; var instance = asset.CreateInstance( world );
				try { instance.Placement = placement.Matrix; actual = await captureCompletedNativeFrame(); gpu = TextureCoordinateGpuChecks.MeasureCurrent( form, instance, placement.Matrix ); }
				finally { instance.Dispose(); }
				callbacks = instance.DrawCalls; RequireEmpty( await captureCompletedNativeFrame() ); TextureCoordinateCoreTests.Require( callbacks == instance.DrawCalls, "Actual callbacks continued after deletion." );
				var comparison = Compare( expected, actual ); var negative = Compare( expected, flipped );
				TextureCoordinateCoreTests.Require( comparison.ReferenceForeground > 0 && comparison.ActualForeground > 0 && negative.ChangedRgbPixels > 0, "Native capture lacks positive geometry or V-flip sensitivity." );
				var landmarks = MeasureLandmarks( reference, form, actual, placement.Matrix, projectEnginePointToTopLeftPixel );
				var flippedLandmarks = MeasureLandmarks( reference, form, flipped, placement.Matrix, projectEnginePointToTopLeftPixel );
				result.Add( new( form.Name, placement.Name, comparison, negative, gpu, landmarks, flippedLandmarks, expected, actual ) );
			}
		}
		return result.ToArray();
	}

	public static Landmark[] MeasureLandmarks( TextureCoordinateReference reference, TextureCoordinateReference.Form form, Frame frame, NMatrix placement, Func<NVector3, NVector2> project )
	{
		Validate( frame ); var result = new List<Landmark>();
		foreach ( var primitive in form.Expected.Where( value => value.Mesh != 3 ) )
		{
			var corners = primitive.Streams["POSITION"].Values.Select( value => project( NVector3.Transform( TextureCoordinateReference.EnginePosition( value ), placement ) ) ).ToArray();
			// A source label stroke spans 1-2 texels. Subpixel captures cannot prove its origin.
			var minimumEdge = float.PositiveInfinity;
			for ( var i = 0; i < 4; i++ ) for ( var j = i + 1; j < 4; j++ ) minimumEdge = MathF.Min( minimumEdge, NVector2.Distance( corners[i], corners[j] ) );
			TextureCoordinateCoreTests.Require( minimumEdge >= 220, "Capture resolution cannot resolve independent PNG label landmarks; increase target/camera scale." );
			var sourceMesh = form.Derived ? primitive.Mesh switch { 0 => 1, 1 => 0, 2 => 4, 4 => 2, _ => throw new InvalidOperationException() } : primitive.Mesh;
			foreach ( var point in reference.Landmarks.Where( value => value.Mesh == sourceMesh ) )
			{
				TextureCoordinateCoreTests.Require( point.Rgba.Take( 3 ).All( value => value == 0 ) && point.WrongVrgba.Take( 3 ).All( value => value == 255 ), "Source landmark must distinguish top-left image origin from V-flip." );
				var black = project( NVector3.Transform( PointAtUv( primitive, form.Derived ? 1 : 0, point.U, point.V ), placement ) );
				var white = project( NVector3.Transform( PointAtUv( primitive, form.Derived ? 1 : 0, point.WhiteU, point.WhiteV ), placement ) );
				var dark = Luminance( frame, black ); var bright = Luminance( frame, white );
				result.Add( new( primitive.Mesh, point.X, point.Y, black, white, dark, bright, bright > 0 ? dark / bright : double.PositiveInfinity ) );
			}
		}
		TextureCoordinateCoreTests.Require( result.Count == 12, "Independent per-quadrant landmarks were omitted." ); return result.ToArray();
	}
	public static void RequireAccepted( Row row, double minimumIoU, double maximumMeanRgbError, int maximumChangedRgbPixels,
		double minimumWhiteByteLuminance, double maximumDarkToWhiteRatio, int minimumRejectedVFlipLandmarks )
	{
		TextureCoordinateCoreTests.Require( double.IsFinite( minimumIoU ) && minimumIoU > 0 && minimumIoU <= 1 && double.IsFinite( maximumMeanRgbError ) && maximumMeanRgbError >= 0 && maximumChangedRgbPixels >= 0 &&
			double.IsFinite( minimumWhiteByteLuminance ) && minimumWhiteByteLuminance > 0 && minimumWhiteByteLuminance <= 255 && double.IsFinite( maximumDarkToWhiteRatio ) && maximumDarkToWhiteRatio >= 0 && maximumDarkToWhiteRatio < 1 && minimumRejectedVFlipLandmarks is > 0 and <= 12, "Explicit reviewed image/landmark thresholds are required." );
		var comparison = row.ActualVsLiteralReference;
		TextureCoordinateCoreTests.Require( comparison.IoU >= minimumIoU && comparison.MeanAbsoluteRgbByteError <= maximumMeanRgbError && comparison.ChangedRgbPixels <= maximumChangedRgbPixels, "Actual image differs from independent literal UV/geometry reference." );
		bool Pass( Landmark value ) => double.IsFinite( value.DarkToWhiteRatio ) && value.WhiteLuminance >= minimumWhiteByteLuminance && value.DarkToWhiteRatio <= maximumDarkToWhiteRatio;
		TextureCoordinateCoreTests.Require( row.ActualLandmarks.Length == 12 && row.ActualLandmarks.All( Pass ), "Actual source-PNG labels fail independent projected black/white landmarks." );
		TextureCoordinateCoreTests.Require( row.FlippedVLandmarks.Length == 12 && row.FlippedVLandmarks.Count( value => !Pass( value ) ) >= minimumRejectedVFlipLandmarks && row.FlippedVVsLiteralReference.ChangedRgbPixels > comparison.ChangedRgbPixels, "V-flip control was not independently rejected." );
	}
	/// <summary>Barycentric interpolation in authored UV triangles, independent of producer vertex evaluation.</summary>
	public static NVector3 PointAtUv( TextureCoordinateReference.Primitive primitive, int uvSet, double u, double v )
	{
		var uv = primitive.Streams["TEXCOORD_" + uvSet].Values; var indices = primitive.Indices.Values;
		for ( var triangle = 0; triangle < indices.Length; triangle += 3 )
		{
			var a = (int)indices[triangle][0]; var b = (int)indices[triangle + 1][0]; var c = (int)indices[triangle + 2][0];
			var determinant = (uv[b][1] - uv[c][1]) * (uv[a][0] - uv[c][0]) + (uv[c][0] - uv[b][0]) * (uv[a][1] - uv[c][1]);
			var wa = ((uv[b][1] - uv[c][1]) * (u - uv[c][0]) + (uv[c][0] - uv[b][0]) * (v - uv[c][1])) / determinant;
			var wb = ((uv[c][1] - uv[a][1]) * (u - uv[c][0]) + (uv[a][0] - uv[c][0]) * (v - uv[c][1])) / determinant; var wc = 1 - wa - wb;
			if ( wa < -1e-7 || wb < -1e-7 || wc < -1e-7 ) continue;
			var p = primitive.Streams["POSITION"].Values; var raw = Enumerable.Range( 0, 3 ).Select( component => p[a][component] * wa + p[b][component] * wb + p[c][component] * wc ).ToArray(); return TextureCoordinateReference.EnginePosition( raw );
		}
		throw new InvalidOperationException( "Landmark is outside the selected source UV triangles." );
	}
	public static Difference Compare( Frame reference, Frame actual )
	{
		Validate( reference ); Validate( actual ); TextureCoordinateCoreTests.Require( reference.Width == actual.Width && reference.Height == actual.Height && reference.Metadata["camera"] == actual.Metadata["camera"] && reference.Metadata["presentation"] == actual.Metadata["presentation"], "Camera/presentation changed between native captures." );
		var aCount = 0; var bCount = 0; var intersection = 0; var union = 0; var changed = 0; long sum = 0;
		for ( var offset = 0; offset < reference.Rgba.Length; offset += 4 )
		{
			var a = Foreground( reference.Rgba, offset ); var b = Foreground( actual.Rgba, offset ); if ( a ) aCount++; if ( b ) bCount++; if ( a && b ) intersection++; if ( a || b ) union++;
			var different = false; for ( var channel = 0; channel < 3; channel++ ) { var delta = Math.Abs( reference.Rgba[offset + channel] - actual.Rgba[offset + channel] ); sum += delta; different |= delta != 0; } if ( different ) changed++;
		}
		return new( aCount, bCount, union == 0 ? double.NaN : (double)intersection / union, changed, (double)sum / (reference.Width * reference.Height * 3) );
	}
	private static double Luminance( Frame frame, NVector2 point )
	{
		TextureCoordinateCoreTests.Require( float.IsFinite( point.X ) && float.IsFinite( point.Y ), "Invalid camera projection." );
		var x = (int)MathF.Floor( point.X ); var y = (int)MathF.Floor( point.Y ); TextureCoordinateCoreTests.Require( x >= 0 && y >= 0 && x < frame.Width && y < frame.Height, "Projected PNG landmark lies outside the native capture." );
		var offset = (y * frame.Width + x) * 4; return .2126 * frame.Rgba[offset] + .7152 * frame.Rgba[offset + 1] + .0722 * frame.Rgba[offset + 2];
	}
	private static void RequireEmpty( Frame frame ) { Validate( frame ); TextureCoordinateCoreTests.Require( !Enumerable.Range( 0, frame.Width * frame.Height ).Any( value => Foreground( frame.Rgba, value * 4 ) ), "Native empty/deleted control contains stale geometry." ); }
	private static bool Foreground( byte[] rgba, int offset ) => (rgba[offset] | rgba[offset + 1] | rgba[offset + 2]) != 0;
	private static void Validate( Frame frame ) => TextureCoordinateCoreTests.Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Rgba?.Length == checked(frame.Width * frame.Height * 4) && double.IsFinite( frame.MonotonicSeconds ) && frame.Metadata is not null && frame.Metadata.TryGetValue( "camera", out var camera ) && !string.IsNullOrWhiteSpace( camera ) && frame.Metadata.TryGetValue( "presentation", out var presentation ) && !string.IsNullOrWhiteSpace( presentation ), "Incomplete native camera/presentation capture evidence." );
}
