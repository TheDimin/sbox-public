using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

/// <summary>
/// Image acceptance using independent constant-color reference geometry and
/// independent eroded object masks supplied by the coordinator. All frames must
/// use identical camera/size/locked exposure/postprocessing. No upstream JPEG is
/// treated as a pixel oracle. This helper never creates a material or a scene.
/// </summary>
public static class UnlitTestImageChecks
{
	public sealed record ObjectMeasurement( int Material, int InteriorPixels, double MeanReferenceErrorBytes,
		int MaximumReferenceErrorBytes, double MeanLightingChangeBytes, int MaximumLightingChangeBytes, double MeanLitControlChangeBytes );

	public static ObjectMeasurement[] Measure( byte[] referenceRgba, byte[] darkSourceRgba, byte[] brightSourceRgba,
		byte[] darkLitControlRgba, byte[] brightLitControlRgba, IReadOnlyList<int>[] independentInteriorPixelMasks )
	{
		Require( referenceRgba is { Length: > 0 } && referenceRgba.Length % 4 == 0, "Independent expected RGBA image is absent/invalid." );
		var frames = new[] { darkSourceRgba, brightSourceRgba, darkLitControlRgba, brightLitControlRgba };
		Require( frames.All( frame => frame is not null && frame.Length == referenceRgba.Length ), "Matched RGBA frame dimensions/format differ." );
		Require( independentInteriorPixelMasks is { Length: 2 }, "Both independently identified Orange and Blue interiors are required." );
		var used = new HashSet<int>(); var result = new List<ObjectMeasurement>();
		for ( var material = 0; material < 2; material++ )
		{
			var mask = independentInteriorPixelMasks[material];
			Require( mask is not null && mask.Count >= 512, "Each independent object interior needs at least 512 non-edge pixels." );
			long errorSum = 0, lightSum = 0, litSum = 0; var errorMax = 0; var lightMax = 0;
			foreach ( var pixel in mask )
			{
				Require( pixel >= 0 && pixel < referenceRgba.Length / 4 && used.Add( pixel ), "Independent masks overlap, repeat pixels, or exceed the frame." );
				for ( var channel = 0; channel < 3; channel++ )
				{
					var offset = pixel * 4 + channel;
					foreach ( var source in new[] { darkSourceRgba, brightSourceRgba } )
					{
						var error = Math.Abs( source[offset] - referenceRgba[offset] ); errorSum += error; errorMax = Math.Max( errorMax, error );
					}
					var change = Math.Abs( darkSourceRgba[offset] - brightSourceRgba[offset] ); lightSum += change; lightMax = Math.Max( lightMax, change );
					litSum += Math.Abs( darkLitControlRgba[offset] - brightLitControlRgba[offset] );
				}
			}
			result.Add( new( material, mask.Count, errorSum / (double)(mask.Count * 6), errorMax, lightSum / (double)(mask.Count * 3), lightMax, litSum / (double)(mask.Count * 3) ) );
		}
		return result.ToArray();
	}

	public static void RequireWithin( IReadOnlyList<ObjectMeasurement> objects, double meanReferenceToleranceBytes,
		int maximumReferenceToleranceBytes, double meanLightingToleranceBytes, int maximumLightingToleranceBytes, double minimumLitControlChangeBytes )
	{
		Require( objects is { Count: 2 } && objects.Select( row => row.Material ).Order().SequenceEqual( new[] { 0, 1 } ), "Both material image results are required." );
		Require( double.IsFinite( meanReferenceToleranceBytes ) && meanReferenceToleranceBytes >= 0 && maximumReferenceToleranceBytes >= 0
			&& double.IsFinite( meanLightingToleranceBytes ) && meanLightingToleranceBytes >= 0 && maximumLightingToleranceBytes >= 0
			&& double.IsFinite( minimumLitControlChangeBytes ) && minimumLitControlChangeBytes > meanLightingToleranceBytes, "Invalid explicit image/control acceptance thresholds." );
		foreach ( var item in objects )
		{
			Require( item.InteriorPixels >= 512 && item.MeanReferenceErrorBytes <= meanReferenceToleranceBytes && item.MaximumReferenceErrorBytes <= maximumReferenceToleranceBytes, "Source unlit image differs from independent constant-color reference for material " + item.Material );
			Require( item.MeanLightingChangeBytes <= meanLightingToleranceBytes && item.MaximumLightingChangeBytes <= maximumLightingToleranceBytes, "Scene lighting changed the source unlit surface for material " + item.Material );
			Require( item.MeanLitControlChangeBytes >= minimumLitControlChangeBytes, "Lighting setup did not affect the explicit lit negative control for material " + item.Material );
		}
	}

	private static void Require( bool condition, string message ) => UnlitTestCoreTests.Require( condition, message );
}
