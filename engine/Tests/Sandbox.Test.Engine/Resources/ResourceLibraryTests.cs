using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
[DoNotParallelize]
public class ResourceLibraryTests
{
	[TestInitialize]
	public void MarkTestThreadAsMain()
	{
		ThreadSafe.MarkMainThread();
	}

	[TestMethod]
	public void TruncatedIdCollisionDoesNotCorruptAuthoritativeStrongIndex()
	{
		var paths = FindTruncatedHashCollision();
		var resources = new ResourceSystem();
		var first = new TestResource( paths.First );
		var second = new TestResource( paths.Second );

#pragma warning disable CS0618
		Assert.AreEqual( first.ResourceId, second.ResourceId );
#pragma warning restore CS0618
		Assert.AreNotEqual( first.ResourceIdLong, second.ResourceIdLong );

		resources.Register( first );
		resources.Register( second );

		var allCount = resources.GetAll<TestResource>().Count();
		var folderCount = resources.GetAll<TestResource>( "resource-index-collision" ).Count();
		var stats = resources.GetResourceStats();

#pragma warning disable CS0618
		Assert.IsNull( resources.Get<TestResource>( first.ResourceId ), "A colliding legacy id must report ambiguity." );
#pragma warning restore CS0618

		resources.Unregister( first );

#pragma warning disable CS0618
		Assert.AreSame( second, resources.Get<TestResource>( second.ResourceId ), "Removing one collision should restore the remaining compatibility entry." );
#pragma warning restore CS0618

		Assert.AreEqual( 1, resources.GetAll<TestResource>().Count() );
		resources.Register( first );

		resources.Clear();

		Assert.AreEqual(
			"all=2; folder=2; total=2; typed=2; firstDestroyed=1; secondDestroyed=1",
			$"all={allCount}; folder={folderCount}; total={stats.StrongTotal}; typed={stats.StrongIndex[nameof( TestResource )]}; firstDestroyed={first.DestroyCount}; secondDestroyed={second.DestroyCount}",
			$"Collision fixture: '{paths.First}' and '{paths.Second}'" );
	}

	[TestMethod]
	public void StaleWrapperCannotUnregisterReplacementWithSamePath()
	{
		var resources = new ResourceSystem();
		var stale = new TestResource( "resource-index-replacement/shared.resource" );
		var replacement = new TestResource( "resource-index-replacement/shared.resource" );

		resources.Register( stale );
		resources.Register( replacement );
		resources.Unregister( stale );

		Assert.AreSame( replacement, resources.Get<TestResource>( replacement.ResourcePath ) );
		Assert.AreEqual( 1, resources.GetAll<TestResource>().Count() );

		resources.Clear();

		Assert.AreEqual( 0, stale.DestroyCount );
		Assert.AreEqual( 1, replacement.DestroyCount );
	}

	private static (string First, string Second) FindTruncatedHashCollision()
	{
		var hashes = new Dictionary<int, (ulong Hash, string Path)>( 500_000 );

		for ( var i = 0; i < 500_000; ++i )
		{
			var path = $"resource-index-collision/{i:x8}.resource";
			var hash = path.FastHash64();
			var truncated = unchecked( (int)hash );

			if ( hashes.TryGetValue( truncated, out var previous ) && previous.Hash != hash )
				return (previous.Path, path);

			hashes[truncated] = (hash, path);
		}

		throw new AssertFailedException( "Failed to find a deterministic truncated 32-bit collision in the fixture range." );
	}

	private sealed class TestResource : Resource
	{
		public override bool IsValid => true;

		public int DestroyCount { get; private set; }

		public TestResource( string path )
		{
			ResourcePath = FixPath( path );
			ResourceName = System.IO.Path.GetFileNameWithoutExtension( ResourcePath );

#pragma warning disable CS0618
			ResourceId = ResourcePath.FastHash();
#pragma warning restore CS0618
			ResourceIdLong = ResourcePath.FastHash64();
		}

		internal override void Destroy()
		{
			DestroyCount++;
			GC.SuppressFinalize( this );
		}
	}
}
