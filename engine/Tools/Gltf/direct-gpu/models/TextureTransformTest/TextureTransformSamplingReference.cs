using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Independent source-sample landmarks for the final sampler image gate. The
/// expected colors are linear RGB/straight linear alpha before native lighting,
/// exposure and tone mapping. They must never be compared directly with final
/// display RGB bytes without a measured shader/lighting transfer control.
/// </summary>
public static class TextureTransformSamplingReference
{
	public sealed record Landmark( int Material, double FullQuadU, double FullQuadV, double SourceU, double SourceV,
		double TransformedU, double TransformedV, NVector4 ExpectedClampLinear, NVector4 WrongWrapLinear,
		bool OutsideUnitSquare, bool SensitiveToWrongWrap );

	public static Landmark[] Build( string checkoutRoot )
	{
		using var input = new TextureTransformReference.Inputs( checkoutRoot, false );
		var images = new[] { Png.Decode( input.Dependencies["UV.png"] ), Png.Decode( input.Dependencies["Arrow.png"] ) };
		var landmarks = new List<Landmark>();
		for ( var material = 0; material < 6; material++ )
			foreach ( var fullV in new[] { .125, .375, .625, .875 } ) foreach ( var fullU in new[] { .125, .375, .625, .875 } )
			{
				var sourceU = material < 3 ? fullU * .5 : fullU; var sourceV = material < 3 ? fullV * .5 : fullV;
				var uv = TextureTransformReference.SampleUv( material, sourceU, sourceV ); var image = images[material < 3 ? 0 : 1];
				var correct = Png.BilinearLinearRgba( image.Rgba, image.Width, image.Height, uv[0], uv[1], true );
				var wrong = Png.BilinearLinearRgba( image.Rgba, image.Width, image.Height, uv[0], uv[1], false );
				landmarks.Add( new Landmark( material, fullU, fullV, sourceU, sourceV, uv[0], uv[1], correct, wrong,
					uv[0] < 0 || uv[0] > 1 || uv[1] < 0 || uv[1] > 1, NVector4.Distance( correct, wrong ) > .01f ) );
			}
		return landmarks.ToArray();
	}
}
