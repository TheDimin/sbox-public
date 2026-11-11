using Sandbox.Gltf;
using System;

namespace Sandbox.Tests;

[TestClass]
public class GltfStockVertexFrameTests
{
	[TestMethod]
	public void MissingDirectionsProduceALitStockShaderFrameForEachTriangleCorner()
	{
		var prepared = GltfModelPrimitiveBuilder.Prepare( Triangle() );
		foreach ( var vertex in prepared.Vertices )
		{
			Assert.IsTrue( vertex.Normal.AlmostEqual( Vector3.Forward, .00001f ) );
			AssertFrame( vertex.Normal, vertex.Tangent );
		}
	}

	[TestMethod]
	public void SuppliedNormalsWithoutTangentsKeepTheirDirectionAndGetAFrame()
	{
		var data = Triangle();
		data = new GltfDrawData
		{
			Mode = data.Mode,
			Positions = data.Positions,
			Indices = data.Indices,
			Normals = new System.Numerics.Vector3[] { new( 0, 0, 1 ), new( 0, 0, 1 ), new( 0, 0, 1 ) }
		};
		var prepared = GltfModelPrimitiveBuilder.Prepare( data );
		foreach ( var vertex in prepared.Vertices )
		{
			Assert.AreEqual( Vector3.Up, vertex.Normal );
			AssertFrame( vertex.Normal, vertex.Tangent );
		}
	}

	[TestMethod]
	public void ValidAuthoredTangentsAndTheirSignSurvive()
	{
		var data = Triangle();
		data = new GltfDrawData
		{
			Mode = data.Mode,
			Positions = data.Positions,
			Indices = data.Indices,
			Normals = new System.Numerics.Vector3[] { new( 1, 0, 0 ), new( 1, 0, 0 ), new( 1, 0, 0 ) },
			Tangents = new System.Numerics.Vector4[] { new( 0, 1, 0, -1 ), new( 0, 1, 0, -1 ), new( 0, 1, 0, -1 ) }
		};
		var prepared = GltfModelPrimitiveBuilder.Prepare( data );
		foreach ( var vertex in prepared.Vertices ) Assert.AreEqual( new Vector4( 0, 1, 0, -1 ), vertex.Tangent );
	}

	[TestMethod]
	public void TangentsWithoutNormalsDoNotReplaceTheGeneratedFaceFrame()
	{
		var data = Triangle();
		data = new GltfDrawData
		{
			Mode = data.Mode,
			Positions = data.Positions,
			Indices = data.Indices,
			Tangents = new System.Numerics.Vector4[] { new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ) }
		};
		foreach ( var vertex in GltfModelPrimitiveBuilder.Prepare( data ).Vertices )
		{
			Assert.IsTrue( vertex.Normal.AlmostEqual( Vector3.Forward, .00001f ) );
			AssertFrame( vertex.Normal, vertex.Tangent );
		}
	}

	private static void AssertFrame( Vector3 normal, Vector4 tangent )
	{
		var direction = new Vector3( tangent.x, tangent.y, tangent.z );
		Assert.IsTrue( float.IsFinite( direction.x ) && float.IsFinite( direction.y ) && float.IsFinite( direction.z ) );
		Assert.AreEqual( 1f, direction.Length, .00001f, "Stock shading requires a unit tangent even without a normal map." );
		Assert.AreEqual( 0f, Vector3.Dot( normal, direction ), .00001f );
		Assert.AreEqual( 1f, MathF.Abs( tangent.w ), .00001f );
	}

	private static GltfDrawData Triangle() => new()
	{
		Mode = 4,
		Indices = new uint[] { 0, 1, 2 },
		Positions = new System.Numerics.Vector3[] { new( 0, 0, 0 ), new( 0, 1, 0 ), new( 0, 0, 1 ) }
	};
}
