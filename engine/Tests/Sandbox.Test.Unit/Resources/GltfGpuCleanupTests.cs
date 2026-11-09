using Sandbox.Gltf.DirectGpu;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public class GltfGpuCleanupTests
{
	[TestMethod]
	public void RollbackAttemptsAllReleasesAndPreservesOriginalFailure()
	{
		var original = new InvalidOperationException( "acquire" );
		var first = new InvalidOperationException( "release-first" );
		var last = new InvalidOperationException( "release-last" );
		var calls = new List<int>();
		var error = Assert.ThrowsException<AggregateException>( () => GltfGpuCleanup.Rollback( original,
			[new Action( () => { calls.Add( 1 ); throw first; } ), () => calls.Add( 2 ), () => { calls.Add( 3 ); throw last; }] ) );
		CollectionAssert.AreEqual( new[] { 1, 2, 3 }, calls.ToArray() );
		CollectionAssert.AreEqual( new Exception[] { original, first, last }, error.InnerExceptions.ToArray() );
	}

	[TestMethod]
	public void SuccessfulRollbackRethrowsTheOriginalExceptionInstance()
	{
		var original = new InvalidOperationException( "acquire" ); var released = false;
		var error = Assert.ThrowsException<InvalidOperationException>( () => GltfGpuCleanup.Rollback( original, [() => released = true] ) );
		Assert.AreSame( original, error ); Assert.IsTrue( released );
	}

	[TestMethod]
	public void FinalReleaseDoesNotAbandonLaterResourcesAfterFailure()
	{
		var released = false; var error = new InvalidOperationException( "first" );
		var aggregate = Assert.ThrowsException<AggregateException>( () => GltfGpuCleanup.ReleaseAll( [() => throw error, () => released = true] ) );
		Assert.IsTrue( released ); Assert.AreSame( error, aggregate.InnerExceptions.Single() );
	}
}
