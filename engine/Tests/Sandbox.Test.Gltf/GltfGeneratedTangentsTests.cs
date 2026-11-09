using System.Numerics;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfGeneratedTangentsTests
{
	private static readonly Vector3 RowU = new( 1, 0, 0 );
	private static readonly Vector3 RowV = new( 0, 1, 0 );

	[TestMethod]
	public void SharedVertexMirroredSeamKeepsDistinctCornerFrames()
	{
		Vector3[] positions = [new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ), new( -1, 0, 0 )];
		Vector3[] normals = [Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ, Vector3.UnitZ];
		Vector2[] uvs = [new( 0, 0 ), new( 1, 0 ), new( 0, 1 ), new( 1, 0 )];
		uint[] indices = [0, 1, 2, 0, 2, 3];
		var original = (uint[])indices.Clone();
		var result = GltfGeneratedTangents.Generate( positions, normals, uvs, indices, 4, RowU, RowV );
		CollectionAssert.AreEqual( original, indices ); CollectionAssert.AreEqual( original, result.SourceVertices );
		for ( var i = 0; i < 6; i++ ) Near( i < 3 ? new( 1, 0, 0, -1 ) : new( -1, 0, 0, 1 ), result.CornerTangents[i] );
	}

	[TestMethod]
	public void NormalUvRotationChangesTheTangentBasis()
	{
		Vector3[] positions = [new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 )];
		Vector2[] uvs = [new( 0, 0 ), new( 1, 0 ), new( 0, 1 )];
		var result = GltfGeneratedTangents.Generate( positions, null, uvs, [0, 1, 2], 4, new( 0, -1, 0 ), new( 1, 0, 0 ) );
		foreach ( var tangent in result.CornerTangents ) Near( new( 0, -1, 0, -1 ), tangent );
	}

	[TestMethod]
	[DataRow( 5 )]
	[DataRow( 6 )]
	public void StripAndFanExpandBeforePerCornerGeneration( int mode )
	{
		Vector3[] positions = [new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ), new( 1, 1, 0 )];
		Vector2[] uvs = [new( 0, 0 ), new( 1, 0 ), new( 0, 1 ), new( 1, 1 )];
		uint[] input = mode == 5 ? [0, 1, 2, 3] : [0, 1, 3, 2];
		uint[] expected = mode == 5 ? [0, 1, 2, 2, 1, 3] : [0, 1, 3, 0, 3, 2];
		var result = GltfGeneratedTangents.Generate( positions, null, uvs, input, mode, RowU, RowV );
		CollectionAssert.AreEqual( expected, result.SourceVertices );
		foreach ( var tangent in result.CornerTangents ) Near( new( 1, 0, 0, -1 ), tangent );
	}

	[TestMethod]
	public void LargeSharedFanUsesFiniteFramesWithoutRecursiveTraversal()
	{
		const int count = 3000;
		var positions = new Vector3[count + 1]; var normals = new Vector3[count + 1]; var uvs = new Vector2[count + 1];
		var indices = new uint[count * 3]; normals[0] = Vector3.UnitZ;
		for ( var i = 0; i < count; i++ )
		{
			var angle = (float)(2 * Math.PI * i / count); var x = MathF.Cos( angle ); var y = MathF.Sin( angle );
			positions[i + 1] = new( x, y, 0 ); uvs[i + 1] = new( x, y ); normals[i + 1] = Vector3.UnitZ;
			indices[i * 3] = 0; indices[i * 3 + 1] = (uint)(i + 1); indices[i * 3 + 2] = (uint)((i + 1) % count + 1);
		}
		var result = GltfGeneratedTangents.Generate( positions, normals, uvs, indices, 4, RowU, RowV );
		Assert.AreEqual( indices.Length, result.CornerTangents.Length );
		foreach ( var tangent in result.CornerTangents ) Near( new( 1, 0, 0, -1 ), tangent, .001f );
	}

	[TestMethod]
	public void ExplicitWorkspaceBudgetAndNonFiniteTransformFailBeforeGeneration()
	{
		Vector3[] positions = [Vector3.Zero, Vector3.UnitX, Vector3.UnitY];
		Vector2[] uvs = [Vector2.Zero, Vector2.UnitX, Vector2.UnitY];
		Assert.ThrowsException<GltfException>( () => GltfGeneratedTangents.Generate( positions, null, uvs, [0, 1, 2], 4, RowU, RowV, 1024 ) );
		Assert.ThrowsException<GltfException>( () => GltfGeneratedTangents.Generate( positions, null, uvs, [0, 1, 2], 4, new( float.NaN, 0, 0 ), RowV ) );
		uvs[1] = new( float.MaxValue, 0 );
		Assert.ThrowsException<GltfException>( () => GltfGeneratedTangents.Generate( positions, null, uvs, [0, 1, 2], 4, new( 2, 0, 0 ), RowV ) );
	}

	private static void Near( Vector4 expected, Vector4 actual, float tolerance = .00001f ) => Assert.IsTrue( Vector4.Distance( expected, actual ) < tolerance, $"Expected {expected}, got {actual}." );
}
