using Sandbox;

namespace Sandbox.Gltf.DirectGpu.CamerasValidation;

[TestClass]
public sealed class CameraFrustumTests
{
	[DataTestMethod]
	[DataRow( false )] [DataRow( true )]
	public void LiteralPerspectiveFrustumPreservesFiniteOrInfiniteFar( bool infinite )
	{
		var position = new global::Vector3( 7, -5, 2 ); var tangent = MathF.Tan( .35f );
		var tl = new Ray( position, new global::Vector3( -1, -tangent, tangent ).Normal );
		var tr = new Ray( position, new global::Vector3( -1, tangent, tangent ).Normal );
		var br = new Ray( position, new global::Vector3( -1, tangent, -tangent ).Normal );
		var bl = new Ray( position, new global::Vector3( -1, -tangent, -tangent ).Normal );
		var frustum = Frustum.FromCorners( tl, tr, br, bl, .125f, infinite ? float.PositiveInfinity : 100 );
		Assert.IsTrue( frustum.IsInside( position + new global::Vector3( -2, 0, 0 ) ) );
		Assert.IsFalse( frustum.IsInside( position + new global::Vector3( -.0625f, 0, 0 ) ) );
		Assert.IsFalse( frustum.IsInside( position + new global::Vector3( 2, 0, 0 ) ) );
		Assert.AreEqual( infinite, frustum.IsInside( position + new global::Vector3( -101, 0, 0 ) ) );
		Assert.AreEqual( infinite, frustum.IsInside( position + new global::Vector3( -1e6f, 0, 0 ) ) );
		Assert.IsFalse( frustum.IsInside( position + new global::Vector3( -2, 5, 0 ) ) );
		Assert.AreEqual( infinite, frustum.IsInside( new BBox( position + new global::Vector3( -102, -1, -1 ), position + new global::Vector3( -101, 1, 1 ) ) ) );
		Assert.AreEqual( infinite, frustum.IsInside( position + new global::Vector3( -102, 0, 0 ), 1 ) );
		Assert.IsFalse( float.IsNaN( frustum.FarPlane.Distance ) );
	}
}
