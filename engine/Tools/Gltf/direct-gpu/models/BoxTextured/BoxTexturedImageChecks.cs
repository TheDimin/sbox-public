using System;
using System.Collections.Concurrent;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// Model-local independent geometry/image controls for the coordinator's proven
/// native capture fixture. This class creates no camera, target, Model or Mesh.
/// A shared textured material tests geometry/UV bindings only; it cannot prove
/// the material's sampler/sRGB fetch is correct. Keep that acceptance separate.
/// </summary>
public static class BoxTexturedImageChecks
{
	public sealed record ImageMeasurement( int Width, int Height, int IntersectionPixels, int UnionPixels,
		int DifferentMaskPixels, double SilhouetteIoU, double MeanAbsoluteRgbError,
		double MaximumRgbError, int DifferentRgbPixels );

	public sealed class ReferenceDraw : IDisposable
	{
		private readonly global::Sandbox.SceneCustomObject sceneObject;
		private readonly ConcurrentQueue<string> errors = new();
		public int DrawCalls { get; private set; }
		public string[] CallbackErrors => errors.ToArray();
		private bool disposed;

		public ReferenceDraw( global::Sandbox.SceneWorld world, BoxTexturedReference.IndependentPrimitive independent,
			NMatrix placement, global::Sandbox.Material borrowedActualMaterial, bool deliberatelyFlipImageV = false )
		{
			var vertices = BoxTexturedGpuChecks.ReferenceTriangles( independent, placement, deliberatelyFlipImageV );
			var minimum = new global::Vector3( float.PositiveInfinity ); var maximum = new global::Vector3( float.NegativeInfinity );
			foreach ( var vertex in vertices ) { minimum = global::Vector3.Min( minimum, vertex.Position ); maximum = global::Vector3.Max( maximum, vertex.Position ); }
			sceneObject = new global::Sandbox.SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
			sceneObject.Bounds = new global::BBox( minimum - .001f, maximum + .001f );
			sceneObject.RenderOverride = _ =>
			{
				try { DrawCalls++; global::Sandbox.Graphics.Draw( vertices.AsSpan(), vertices.Length, borrowedActualMaterial ); }
				catch ( Exception error ) { if ( errors.Count < 8 ) errors.Enqueue( error.ToString() ); }
			};
		}
		public void Dispose()
		{
			if ( disposed ) return; disposed = true; sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete();
		}
	}

	public static int RequireEmpty( global::Color[] pixels, int width, int height, string label )
	{
		Validate( pixels, width, height ); var visible = pixels.Count( Foreground );
		BoxTexturedCoreTests.Require( visible == 0, "Native empty capture contains geometry: " + label ); return visible;
	}
	public static int RequirePositive( global::Color[] pixels, int width, int height, string label )
	{
		Validate( pixels, width, height ); var visible = pixels.Count( Foreground );
		BoxTexturedCoreTests.Require( visible > 64, "Native positive capture has no usable geometry: " + label ); return visible;
	}
	public static ImageMeasurement Compare( global::Color[] expected, global::Color[] actual, int width, int height )
	{
		Validate( expected, width, height ); Validate( actual, width, height );
		var intersection = 0; var union = 0; var masks = 0; var different = 0; var sum = 0d; var maximum = 0d;
		for ( var pixel = 0; pixel < expected.Length; pixel++ )
		{
			var a = expected[pixel]; var b = actual[pixel]; var aVisible = Foreground( a ); var bVisible = Foreground( b );
			if ( aVisible && bVisible ) intersection++; if ( aVisible || bVisible ) union++; if ( aVisible != bVisible ) masks++;
			var red = Math.Abs( (double)a.r - b.r ); var green = Math.Abs( (double)a.g - b.g ); var blue = Math.Abs( (double)a.b - b.b );
			sum += red + green + blue; maximum = Math.Max( maximum, Math.Max( red, Math.Max( green, blue ) ) ); if ( red != 0 || green != 0 || blue != 0 ) different++;
		}
		return new( width, height, intersection, union, masks, union == 0 ? 0 : (double)intersection / union, sum / (3 * expected.Length), maximum, different );
	}
	public static void RequireBindingComparison( ImageMeasurement actualVsReference, ImageMeasurement flippedVVsReference,
		double minimumIoU, double maximumMeanRgbError, double maximumRgbError )
	{
		BoxTexturedCoreTests.Require( double.IsFinite( minimumIoU ) && minimumIoU > 0 && minimumIoU <= 1 && double.IsFinite( maximumMeanRgbError ) && maximumMeanRgbError >= 0 && double.IsFinite( maximumRgbError ) && maximumRgbError >= 0, "Need explicit reviewed image thresholds." );
		BoxTexturedCoreTests.Require( actualVsReference.UnionPixels > 64 && actualVsReference.SilhouetteIoU >= minimumIoU && actualVsReference.MeanAbsoluteRgbError <= maximumMeanRgbError && actualVsReference.MaximumRgbError <= maximumRgbError, "Actual GPU geometry/UV image differs from independent literal source reference." );
		BoxTexturedCoreTests.Require( flippedVVsReference.DifferentRgbPixels > 64 && flippedVVsReference.MeanAbsoluteRgbError > actualVsReference.MeanAbsoluteRgbError, "Asymmetric logo V-flip negative control did not exceed actual error." );
		// No sampler/color-space/material-fidelity pass is implied. The reference
		// above intentionally borrows the same actual stock material.
	}
	private static void Validate( global::Color[] pixels, int width, int height ) => BoxTexturedCoreTests.Require( width > 0 && height > 0 && pixels is not null && pixels.Length == checked(width * height), "Image dimensions/pixel count differ." );
	private static bool Foreground( global::Color color )
	{
		BoxTexturedCoreTests.Require( float.IsFinite( color.r ) && float.IsFinite( color.g ) && float.IsFinite( color.b ), "Nonfinite capture RGB." ); return color.r > .03f || color.g > .03f || color.b > .03f;
	}
}
