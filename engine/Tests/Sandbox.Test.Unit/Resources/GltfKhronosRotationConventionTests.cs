using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using NVector2 = System.Numerics.Vector2;

namespace Sandbox.Tests;

/// <summary>
/// Literal signed landmarks from the current Khronos Sample Renderer matrix.
/// The extension README's GLSL example uses the opposite sign; see issue 1563.
/// </summary>
[TestClass]
public class GltfKhronosRotationConventionTests
{
	[DataTestMethod]
	[DataRow( 1.5707963267948966f, 1.75f, -1.0f )]
	[DataRow( -1.5707963267948966f, -1.25f, 0.0f )]
	[DataRow( 0f, .75f, 1.0f )]
	public void SignedRotationNonuniformScaleAndOffsetMatchViewerLandmarks( float angle, float expectedU, float expectedV )
	{
		var texture = new GltfTextureInfo { Rotation = angle, Tiling = new NVector2( 2, 3 ), Offset = new NVector2( .25f, -.5f ) };
		var input = new NVector2( .25f, .5f );
		var api = texture.TransformUv( input );
		var engine = new GltfMaterialUvTransform( texture ).Apply( input );
		if ( angle != 0 )
		{
			// The extension README's GLSL example uses the opposite sign.
			// This negative control must remain observably distinct.
			var cosine = MathF.Cos( angle ); var sine = MathF.Sin( angle );
			var opposite = new NVector2( .25f + cosine * .5f - sine * 1.5f, -.5f + sine * .5f + cosine * 1.5f );
			Assert.IsTrue( NVector2.Distance( opposite, new NVector2( expectedU, expectedV ) ) > 1f );
		}
		Assert.AreEqual( expectedU, api.X, 1e-6f ); Assert.AreEqual( expectedV, api.Y, 1e-6f );
		Assert.AreEqual( expectedU, engine.X, 1e-6f ); Assert.AreEqual( expectedV, engine.Y, 1e-6f );
	}
}
