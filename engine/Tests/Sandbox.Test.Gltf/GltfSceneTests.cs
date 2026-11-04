using System;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Vector3 = System.Numerics.Vector3;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfSceneTests
{
	private static GltfDocument Read( string json ) => GltfReader.Read( Encoding.UTF8.GetBytes( json ) );

	private static void AssertVector( Vector3 expected, Vector3 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.0001f, "X" );
		Assert.AreEqual( expected.Y, actual.Y, 0.0001f, "Y" );
		Assert.AreEqual( expected.Z, actual.Z, 0.0001f, "Z" );
	}

	[TestMethod]
	public void AValidAssetMayHaveNoDefaultScene()
	{
		using var empty = Read( """{"asset":{"version":"2.0"}}""" );
		using var noDefault = Read( """{"asset":{"version":"2.0"},"scenes":[{"nodes":[0]}],"nodes":[{}]}""" );
		Assert.IsNull( empty.GetDefaultScene() );
		Assert.IsNull( noDefault.GetDefaultScene() );
	}

	[TestMethod]
	public void ExplicitDefaultSceneMayBeAnyValidScene()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"scene":1,"scenes":[{},{}]}""" );
		Assert.AreEqual( 1, document.GetDefaultScene() );
	}

	[TestMethod]
	public void InvalidDefaultSceneIsRejected()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """{"asset":{"version":"2.0"},"scene":1,"scenes":[{}]}""" );
			_ = document.GetDefaultScene();
		} );
	}

	[TestMethod]
	public void LocalTrsIsComposedBeforeParentWithNegativeScalePreserved()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"nodes":[
			  {"translation":[1,2,3],"children":[1]},
			  {"rotation":[0,0,0.70710678,0.70710678],"scale":[-2,3,4]}
			]}
			""" );

		var point = Vector3.Transform( Vector3.One, document.GetWorldTransform( 1 ) );
		AssertVector( new Vector3( -2, 0, 7 ), point );
	}

	[TestMethod]
	public void MatrixNodeUsesGltfColumnMajorAndComposesWithParent()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"nodes":[
			  {"translation":[10,0,0],"children":[1]},
			  {"matrix":[1,0,0,0, 0,0,1,0, 0,-1,0,0, 2,3,4,1]}
			]}
			""" );

		AssertVector( new Vector3( 12, 3, 5 ), Vector3.Transform( Vector3.UnitY, document.GetWorldTransform( 1 ) ) );
	}

	[TestMethod]
	public void NodeWithoutTransformUsesIdentity()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"nodes":[{}]}""" );
		AssertVector( new Vector3( 1, -2, 3 ), Vector3.Transform( new Vector3( 1, -2, 3 ), document.GetWorldTransform( 0 ) ) );
	}

	[TestMethod]
	public void CycleAndMultipleParentsAreRejected()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """{"asset":{"version":"2.0"},"nodes":[{"children":[1]},{"children":[0]}]}""" );
			_ = document.GetWorldTransform( 0 );
		} );
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """{"asset":{"version":"2.0"},"nodes":[{"children":[2]},{"children":[2]},{}]}""" );
			_ = document.GetWorldTransform( 2 );
		} );
	}

	[TestMethod]
	public void NodeCannotDefineBothMatrixAndTrs()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """{"asset":{"version":"2.0"},"nodes":[{"matrix":[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1],"translation":[1,0,0]}]}""" );
			_ = document.GetWorldTransform( 0 );
		} );
	}
}
