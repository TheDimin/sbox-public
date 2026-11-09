using System;

namespace Sandbox.Tests;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public sealed class GltfMaterialCacheFailureTests
{
	private sealed class Fake { internal bool Alive = true; internal bool FailInspection; internal bool FailRelease; }
	private static GltfMaterialKey Key( object template ) => new( template, 0, [], [], [] );

	[TestMethod]
	public void InvalidCreatedResourceAndThrowingReleaseRetainBothFailuresAndPermitRetry()
	{
		var invalid = true; var creates = 0; var releases = 0; var template = new object(); var cleanup = new InvalidOperationException( "release failed" );
		using var cache = new GltfMaterialCacheCore<Fake>( _ => { creates++; return new Fake { Alive = !invalid, FailRelease = invalid }; },
			value => { releases++; value.Alive = false; if ( value.FailRelease ) throw cleanup; }, value => value.Alive );
		var error = Assert.ThrowsException<AggregateException>( () => cache.Acquire( Key( template ) ) );
		Assert.AreEqual( 2, error.InnerExceptions.Count ); Assert.IsInstanceOfType<InvalidOperationException>( error.InnerExceptions[0] );
		StringAssert.Contains( error.InnerExceptions[0].Message, "invalid resource" ); Assert.AreSame( cleanup, error.InnerExceptions[1] ); Assert.AreEqual( 1, releases );
		invalid = false; var retry = cache.Acquire( Key( template ) ); Assert.IsTrue( retry.Value.Alive ); Assert.AreEqual( 2, creates );
		retry.Dispose(); Assert.AreEqual( 2, releases );
	}

	[TestMethod]
	public void FailedInspectionAndCleanupDoNotReleaseOrReplaceAnExistingLivePeer()
	{
		var peerKey = new object(); var badKey = new object(); var inspection = new InvalidOperationException( "inspection failed" ); var cleanup = new InvalidOperationException( "release failed" );
		var badReleases = 0; var peerReleases = 0;
		using var cache = new GltfMaterialCacheCore<Fake>( key => new Fake { FailInspection = ReferenceEquals( key.Template, badKey ), FailRelease = ReferenceEquals( key.Template, badKey ) },
			value => { value.Alive = false; if ( value.FailRelease ) { badReleases++; throw cleanup; } peerReleases++; },
			value => value.FailInspection ? throw inspection : value.Alive );
		var peer = cache.Acquire( Key( peerKey ) ); var original = peer.Value;
		try
		{
			var error = Assert.ThrowsException<AggregateException>( () => cache.Acquire( Key( badKey ) ) );
			Assert.AreEqual( 2, error.InnerExceptions.Count ); Assert.AreSame( inspection, error.InnerExceptions[0] ); Assert.AreSame( cleanup, error.InnerExceptions[1] );
			Assert.AreEqual( 1, badReleases ); Assert.AreEqual( 0, peerReleases ); Assert.IsTrue( original.Alive );
			using var same = cache.Acquire( Key( peerKey ) ); Assert.AreSame( original, same.Value );
		}
		finally { peer.Dispose(); }
		Assert.IsFalse( original.Alive ); Assert.AreEqual( 1, peerReleases );
	}

	[TestMethod]
	public void ShutdownDuringCreationRetainsDisposedErrorAndCleanupWithoutDestroyingPeer()
	{
		var peerKey = new object(); var shutdownKey = new object(); var cleanup = new InvalidOperationException( "release failed" ); var peerReleases = 0;
		GltfMaterialCacheCore<Fake> cache = null;
		cache = new GltfMaterialCacheCore<Fake>( key =>
		{
			if ( ReferenceEquals( key.Template, shutdownKey ) ) { cache.Dispose(); return new Fake { FailRelease = true }; }
			return new Fake();
		}, value => { value.Alive = false; if ( value.FailRelease ) throw cleanup; peerReleases++; }, value => value.Alive );
		using ( cache )
		{
			var peer = cache.Acquire( Key( peerKey ) ); var original = peer.Value;
			try
			{
				var error = Assert.ThrowsException<AggregateException>( () => cache.Acquire( Key( shutdownKey ) ) );
				Assert.AreEqual( 2, error.InnerExceptions.Count ); Assert.IsInstanceOfType<ObjectDisposedException>( error.InnerExceptions[0] ); Assert.AreSame( cleanup, error.InnerExceptions[1] );
				Assert.IsTrue( original.Alive ); Assert.AreEqual( 0, peerReleases ); Assert.ThrowsException<ObjectDisposedException>( () => cache.Acquire( Key( peerKey ) ) );
			}
			finally { peer.Dispose(); }
			Assert.IsFalse( original.Alive ); Assert.AreEqual( 1, peerReleases );
		}
	}
}
