using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// Model-specific image orchestration. The coordinator supplies the validated
/// native camera/light/capture route and owns every GPU execution. No synthetic
/// frame interpolation, native model or mesh construction is used here.
/// </summary>
internal static class TextureTransformImageProbe
{
	public sealed record Frame( int Width, int Height, byte[] Rgba, string ImagePath, double MonotonicSeconds, IReadOnlyDictionary<string, string> Metadata );
	public sealed record Difference( int ReferenceForeground, int ActualForeground, double RgbForegroundIoU, int ChangedRgbPixels, double MeanAbsoluteRgbByteError );
	public sealed record Row( string Form, string Placement, Difference ActualVsIndependentUvReference, Difference ReferenceVsMissingTransformControl,
		TextureTransformGpuChecks.Measurement Gpu, Frame Reference, Frame Actual,
		string Status = "measured-not-accepted: UV/geometry image agreement only; independent sampling and material fidelity still require their own gates" );

	public static async Task<Row[]> MeasureAll( string checkoutRoot, SceneWorld world, Material borrowedComputeMaterial,
		Func<Task<Frame>> captureCompletedNativeFrame, TextureTransformUvRepresentation outputUvRepresentation )
	{
		ArgumentNullException.ThrowIfNull( captureCompletedNativeFrame );
		var result = new List<Row>();
		foreach ( var compiled in new[] { false, true } )
		{
			using var input = new TextureTransformReference.Inputs( checkoutRoot, compiled );
			using var asset = GltfGpuAsset.Create( input.Document, borrowedComputeMaterial ); // Actual default materials; no resolver override.
			using var referenceDocument = TextureTransformIdentityMaterialReference.CreateDocument( input, compiled );
			using var referenceAsset = GltfGpuAsset.Create( referenceDocument, borrowedComputeMaterial );
			var referenceMaterials = referenceAsset.GetMaterials();
			Require( !referenceAsset.HasGpuResources, "Reference material realization must not allocate a redundant geometry buffer instance." );
			Material BorrowIdentityMaterial( int? material )
			{
				var slot = referenceAsset.MaterialSlotSourceIndices.ToList().IndexOf( material );
				Require( slot >= 0, "The independent identity reference lost a source material slot." );
				return referenceMaterials[slot];
			}
			foreach ( var (name, placement) in new[]
			{
				("identity", NMatrix.Identity),
				("full_affine_shear", new NMatrix( 1, .1f, 0, 0, 0, 1.25f, .1f, 0, 0, 0, .75f, 0, 5, -3, 2, 1 )),
				("reflected_y", NMatrix.CreateScale( 1, -1, 1 ))
			} )
			{
				Empty( await captureCompletedNativeFrame() );
				Frame expected; int referenceCallbacks;
				var reference = new TextureTransformReferenceScene( world, placement, BorrowIdentityMaterial );
				try
				{
					expected = await captureCompletedNativeFrame();
					Require( reference.DrawCalls > 0 && reference.CallbackErrors.Length == 0, "Independent reference did not submit cleanly." );
					var repeated = await captureCompletedNativeFrame();
					Require( Compare( expected, repeated ).ChangedRgbPixels == 0, "Repeated static reference captures are unstable; quantitative UV comparisons would be unreliable." );
					referenceCallbacks = reference.DrawCalls;
				}
				finally { reference.Dispose(); }
				Empty( await captureCompletedNativeFrame() );
				Require( reference.DrawCalls == referenceCallbacks, "Reference callbacks continued after disposal." );
				Difference negativeDifference = null;
				if ( name == "identity" )
				{
					using ( var negative = new TextureTransformReferenceScene( world, placement, BorrowIdentityMaterial, omitTransforms: true ) )
					{
						var missingTransform = await captureCompletedNativeFrame();
						Require( negative.DrawCalls > 0 && negative.CallbackErrors.Length == 0, "Missing-transform control did not submit cleanly." );
						negativeDifference = Compare( expected, missingTransform );
						Require( negativeDifference.ChangedRgbPixels > 0, "This capture cannot distinguish missing texture transforms." );
					}
					Empty( await captureCompletedNativeFrame() );
				}
				Frame actual; TextureTransformGpuChecks.Measurement gpu;
				var instance = asset.CreateInstance( world );
				try
				{
					instance.Placement = placement;
					actual = await captureCompletedNativeFrame();
					gpu = TextureTransformGpuChecks.MeasureCurrent( instance, placement, outputUvRepresentation );
				}
				finally { instance.Dispose(); }
				var actualCallbacks = instance.DrawCalls;
				Empty( await captureCompletedNativeFrame() );
				Require( instance.DrawCalls == actualCallbacks, "Actual callbacks continued after disposal." );
				var comparison = Compare( expected, actual );
				Require( referenceCallbacks > 0 && comparison.ReferenceForeground > 0 && comparison.ActualForeground > 0, "Both image paths must contain positive geometry." );
				result.Add( new Row( compiled ? "glTF/compiled_glb" : "glTF/source", name, comparison, negativeDifference, gpu, expected, actual ) );
			}
		}
		return result.ToArray();
	}

	public static Difference Compare( Frame reference, Frame actual )
	{
		Validate( reference ); Validate( actual ); Require( reference.Width == actual.Width && reference.Height == actual.Height, "Camera image dimensions differ." );
		var referenceForeground = 0; var actualForeground = 0; var intersection = 0; var union = 0; var changed = 0; long absolute = 0;
		for ( var pixel = 0; pixel < reference.Width * reference.Height; pixel++ )
		{
			var offset = pixel * 4; var a = Foreground( reference.Rgba, offset ); var b = Foreground( actual.Rgba, offset );
			if ( a ) referenceForeground++; if ( b ) actualForeground++; if ( a && b ) intersection++; if ( a || b ) union++;
			var pixelChanged = false;
			for ( var channel = 0; channel < 3; channel++ ) { var delta = Math.Abs( reference.Rgba[offset + channel] - actual.Rgba[offset + channel] ); absolute += delta; pixelChanged |= delta != 0; }
			if ( pixelChanged ) changed++;
		}
		return new Difference( referenceForeground, actualForeground, union == 0 ? double.NaN : (double)intersection / union, changed,
			(double)absolute / (reference.Width * reference.Height * 3) );
	}
	private static void Empty( Frame value ) { Validate( value ); Require( !Enumerable.Range( 0, value.Width * value.Height ).Any( pixel => Foreground( value.Rgba, pixel * 4 ) ), "Fresh empty/deleted capture contains stale geometry." ); }
	private static bool Foreground( byte[] rgba, int offset ) => (rgba[offset] | rgba[offset + 1] | rgba[offset + 2]) != 0;
	private static void Validate( Frame frame ) => Require( frame is not null && frame.Width > 0 && frame.Height > 0 && frame.Rgba.Length == checked(frame.Width * frame.Height * 4) && double.IsFinite( frame.MonotonicSeconds ) && frame.Metadata is not null, "Invalid actual native capture evidence." );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
