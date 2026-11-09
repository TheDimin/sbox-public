using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NMatrix = System.Numerics.Matrix4x4;
using static BoxVertexColorsReference;

/// <summary>
/// Actual s&box images versus independent literal FLOAT4-colored triangles.
/// Material parameters are intentionally shared to isolate shader color
/// consumption; this does not independently certify PBR/sampler fidelity.
/// </summary>
internal static class BoxVertexColorsImageProbe
{
	public sealed record Frame( int Width, int Height, byte[] Rgba, string ImagePath, double MonotonicSeconds, IReadOnlyDictionary<string, string> Metadata );
	public sealed record Difference( int ReferenceForeground, int ActualForeground, double ForegroundIoU, int ChangedRgbPixels, double MeanAbsoluteRgbByteError, int MaximumRgbByteError );
	public sealed record Result( string Form, string Scope, Difference SourceVsIndependentLinearColor, Difference MissingColorControl, Difference IncorrectGammaControl,
		BoxVertexColorsGpuChecks.Measurement Gpu, Frame Reference, Frame Actual, string Status );
	public const int ExpectedComparisons = 20;

	public static async Task<Result[]> Run( string checkoutRoot, SceneWorld world, Material borrowedComputeMaterial, Shader independentLinearColorShader,
		Func<Task<Frame>> captureCompletedNativeFrame, double minimumIoU, double maximumMeanRgbByteError, int maximumRgbByteError )
	{
		ArgumentNullException.ThrowIfNull( independentLinearColorShader ); ArgumentNullException.ThrowIfNull( captureCompletedNativeFrame );
		Require( double.IsFinite( minimumIoU ) && minimumIoU > 0 && minimumIoU <= 1 && double.IsFinite( maximumMeanRgbByteError ) && maximumMeanRgbByteError >= 0 && maximumRgbByteError >= 0, "Explicit reviewed image thresholds are required." );
		var reference = new BoxVertexColorsReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document, borrowedComputeMaterial );
			var actualMaterial = asset.GetMaterials().Single();
			// A compiled reference may be anonymous. Material.Shader's setter
			// intentionally rejects anonymous shaders; create its own template and
			// acquire the same prepared source inputs through a retained lease.
			var referenceTemplate = Material.Create( "BoxVertexColors independent linear-color reference", independentLinearColorShader );
			try
			{
				Require( referenceTemplate is { IsValid: true, IsError: false }, "Independent reference template could not retain its shader." );
				var sourceIndex = asset.MaterialSlotSourceIndices.Single();
				var preparedInputs = global::Sandbox.GltfStockMaterialAdapter.Prepare( document, sourceIndex.HasValue ? document.ReadMaterial( sourceIndex.Value ) : null );
				using var referenceLease = global::Sandbox.GltfStockMaterialAdapter.Acquire( preparedInputs, referenceTemplate );
				var independentMaterial = referenceLease.Value;
				Require( ReferenceEquals( independentMaterial.Shader, independentLinearColorShader ), "Reference lease lost the compiled shader identity." );
				Require( independentMaterial.ShaderName != actualMaterial.ShaderName, "Expected image must use the separate audited reference color shader." );
				Empty( await captureCompletedNativeFrame() ); Frame expected; int referenceDraws;
				var expectedScene = new BoxVertexColorsReferenceScene( reference, form, world, independentMaterial );
				try
				{
					expected = await captureCompletedNativeFrame(); Positive( expected );
					Require( expectedScene.DrawCalls > 0 && expectedScene.CallbackErrors.Length == 0, "Independent reference failed to submit." );
					Require( Compare( expected, await captureCompletedNativeFrame() ).ChangedRgbPixels == 0, "Static reference frame is unstable." );
					referenceDraws = expectedScene.DrawCalls;
				}
				finally { expectedScene.Dispose(); }
				Empty( await captureCompletedNativeFrame() ); Require( expectedScene.DrawCalls == referenceDraws, "Disposed independent reference callbacks continued." );
				Difference whiteDifference;
				using ( var white = new BoxVertexColorsReferenceScene( reference, form, world, independentMaterial, BoxVertexColorsReferenceScene.ColorControl.MissingColorWhite ) )
				{
					whiteDifference = Compare( expected, await captureCompletedNativeFrame() ); Require( white.DrawCalls > 0 && white.CallbackErrors.Length == 0 && whiteDifference.ChangedRgbPixels >= 256 && whiteDifference.MeanAbsoluteRgbByteError > maximumMeanRgbByteError, "Capture cannot detect ignored vertex colors." );
				}
				Empty( await captureCompletedNativeFrame() ); Difference gammaDifference = null;
				if ( form.Derived )
				{
					using ( var gamma = new BoxVertexColorsReferenceScene( reference, form, world, independentMaterial, BoxVertexColorsReferenceScene.ColorControl.IncorrectSrgbDecode ) )
					{
						gammaDifference = Compare( expected, await captureCompletedNativeFrame() ); Require( gamma.DrawCalls > 0 && gamma.CallbackErrors.Length == 0 && gammaDifference.ChangedRgbPixels >= 256 && gammaDifference.MeanAbsoluteRgbByteError > maximumMeanRgbByteError, "Capture cannot detect sRGB decoding of fractional linear vertex color." );
					}
					Empty( await captureCompletedNativeFrame() );
				}
				Frame actual; BoxVertexColorsGpuChecks.Measurement gpu; var instance = asset.CreateInstance( world );
				try
				{
					actual = await captureCompletedNativeFrame(); Positive( actual );
					Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "Actual source material draw failed." );
					gpu = BoxVertexColorsGpuChecks.Measure( reference, form, instance.Readback(), NMatrix.Identity ); BoxVertexColorsGpuChecks.RequireWithin( gpu );
				}
				finally { instance.Dispose(); }
				var submitted = instance.DrawCalls; Empty( await captureCompletedNativeFrame() ); Require( instance.DrawCalls == submitted, "Disposed actual callbacks continued." );
				var measured = Compare( expected, actual );
				Require( measured.ForegroundIoU >= minimumIoU && measured.MeanAbsoluteRgbByteError <= maximumMeanRgbByteError && measured.MaximumRgbByteError <= maximumRgbByteError,
					$"Source material fails independent rendered linear color: {form.Name}; IoU {measured.ForegroundIoU:R}, mean {measured.MeanAbsoluteRgbByteError:R}, max {measured.MaximumRgbByteError}." );
				results.Add( new( form.Name, "default_source_PBR_material_color_consumption; independent_literal_color_and_reference_shader; shared_material_parameters_do_not_certify_PBR_or_sampler_fidelity", measured, whiteDifference, gammaDifference, gpu, expected, actual, "accepted_only_against_explicit_supplied_thresholds" ) );
			}
			finally { referenceTemplate?.Destroy(); }
		}
		Require( results.Count == ExpectedComparisons, "Frozen image inventory changed." ); return results.ToArray();
	}

	public static Difference Compare( Frame expected, Frame actual )
	{
		Validate( expected ); Validate( actual ); Require( expected.Width == actual.Width && expected.Height == actual.Height, "Camera size differs." );
		var aCount = 0; var bCount = 0; var intersection = 0; var union = 0; var changed = 0; long sum = 0; var maximum = 0;
		for ( var pixel = 0; pixel < expected.Width * expected.Height; pixel++ )
		{
			var offset = pixel * 4; var a = Foreground( expected.Rgba, offset ); var b = Foreground( actual.Rgba, offset );
			if ( a ) aCount++; if ( b ) bCount++; if ( a && b ) intersection++; if ( a || b ) union++; var difference = false;
			for ( var channel = 0; channel < 3; channel++ ) { var error = Math.Abs( expected.Rgba[offset + channel] - actual.Rgba[offset + channel] ); sum += error; maximum = Math.Max( maximum, error ); difference |= error != 0; }
			if ( difference ) changed++;
		}
		return new( aCount, bCount, union == 0 ? double.NaN : intersection / (double)union, changed, sum / (double)(expected.Width * expected.Height * 3), maximum );
	}
	private static void Validate( Frame value ) => Require( value is not null && value.Width > 0 && value.Height > 0 && value.Rgba.Length == checked(value.Width * value.Height * 4) && value.Metadata is not null && double.IsFinite( value.MonotonicSeconds ), "Missing/invalid native frame evidence." );
	private static void Positive( Frame value ) { Validate( value ); Require( Enumerable.Range( 0, value.Width * value.Height ).Count( pixel => Foreground( value.Rgba, pixel * 4 ) ) >= 1024, "Cube is absent/too small for color proof." ); }
	private static void Empty( Frame value ) { Validate( value ); Require( !Enumerable.Range( 0, value.Width * value.Height ).Any( pixel => Foreground( value.Rgba, pixel * 4 ) ), "Fresh empty/deleted control contains geometry." ); }
	private static bool Foreground( byte[] value, int offset ) => (value[offset] | value[offset + 1] | value[offset + 2]) != 0;
}
