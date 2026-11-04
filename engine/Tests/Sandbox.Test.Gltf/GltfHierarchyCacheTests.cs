using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfHierarchyCacheTests
{
	[TestMethod]
	public void LongHierarchyTransformsMatchRestPoseAcrossRepeatedConcurrentReads()
	{
		const int count = 256;
		var nodes = Enumerable.Range( 0, count ).Select( i => i + 1 < count
			? "{\"translation\":[1,0,0],\"children\":[" + (i + 1) + "]}"
			: "{\"translation\":[1,0,0]}" );
		using var document = Open( "{\"asset\":{\"version\":\"2.0\"},\"nodes\":[" + string.Join( ",", nodes ) + "]}" );
		var pose = document.EvaluatePose();
		Parallel.For( 0, count, i =>
		{
			var world = document.GetWorldTransform( i );
			Assert.AreEqual( (float)(i + 1), world.M41, 0.00001f );
			Assert.AreEqual( world, pose.GetWorldTransform( i ) );
		} );
		Assert.AreEqual( count, document.GetWorldTransform( count - 1 ).M41, 0.00001f );
	}

	[TestMethod]
	public void UnrelatedMalformedTransformRemainsLazyButPoseValidatesAllLocals()
	{
		using var document = Open( """{"asset":{"version":"2.0"},"nodes":[{"translation":[2,0,0]},{"translation":[1,2]}]}""" );
		Assert.AreEqual( 2f, document.GetWorldTransform( 0 ).M41, 0.00001f );
		Assert.AreEqual( 2f, document.GetWorldTransform( 0 ).M41, 0.00001f );
		Assert.ThrowsException<GltfException>( () => document.GetWorldTransform( 1 ) );
		Assert.ThrowsException<GltfException>( () => document.EvaluatePose() );
	}

	[TestMethod]
	[DataRow( "[{\"children\":[1]},{\"children\":[0]}]" )]
	[DataRow( "[{\"children\":[2]},{\"children\":[2]},{}]" )]
	[DataRow( "[{},null]" )]
	public void WholeGraphValidationIsRetainedOnEveryQuery( string nodes )
	{
		using var document = Open( "{\"asset\":{\"version\":\"2.0\"},\"nodes\":" + nodes + "}" );
		Assert.ThrowsException<GltfException>( () => document.GetWorldTransform( 0 ) );
		Assert.ThrowsException<GltfException>( () => document.GetWorldTransform( 0 ) );
		Assert.ThrowsException<GltfException>( () => document.EvaluatePose() );
	}

	[TestMethod]
	public void CachedReadsStillRejectUseAfterDisposal()
	{
		var document = Open( """{"asset":{"version":"2.0"},"nodes":[{}]}""" );
		_ = document.GetWorldTransform( 0 );
		_ = document.EvaluatePose();
		document.Dispose();
		Assert.ThrowsException<ObjectDisposedException>( () => document.GetWorldTransform( 0 ) );
		Assert.ThrowsException<ObjectDisposedException>( () => document.EvaluatePose() );
	}

	private static GltfDocument Open( string json ) => GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
}
