using Sandbox.Engine;
using System;

namespace Sandbox.Tests;

[TestClass]
[DoNotParallelize]
public class PublicResourceGuidTests
{
	private ResourceSystem previous;
	private ResourceSystem resources;

	[TestInitialize]
	public void Initialize()
	{
		ThreadSafe.MarkMainThread();
		previous = GlobalContext.Current.ResourceSystem;
		resources = new ResourceSystem();
		GlobalContext.Current.ResourceSystem = resources;
	}

	[TestCleanup]
	public void Cleanup()
	{
		resources.Clear();
		GlobalContext.Current.ResourceSystem = previous;
	}

	[TestMethod]
	public void FindsIndexedResourcesAndRejectsMissingAndWrongTypes()
	{
		var id = Guid.NewGuid();
		var resource = new TestResource { Guid = id };
		resources.Register( resource );

		Assert.AreEqual( id, resource.Guid );
		Assert.AreSame( resource, ResourceLibrary.Get<TestResource>( id ) );
		Assert.IsTrue( ResourceLibrary.TryGet<TestResource>( id, out var found ) );
		Assert.AreSame( resource, found );
		Assert.IsNull( ResourceLibrary.Get<OtherResource>( id ) );
		Assert.IsFalse( ResourceLibrary.TryGet<TestResource>( Guid.NewGuid(), out var unknown ) );
		Assert.IsNull( unknown );
		Assert.IsNull( ResourceLibrary.Get<TestResource>( Guid.Empty ) );
		Assert.AreEqual( Guid.Empty, new TestResource().Guid );

		resources.Unregister( resource );
		Assert.IsNull( ResourceLibrary.Get<TestResource>( id ), "Unindexed resources are not loaded by GUID lookup." );
	}

	[TestMethod]
	public void MovingAWeaklyIndexedResourceKeepsItsGuid()
	{
		var id = Guid.NewGuid();
		var resource = new TestResource();
		resource.RegisterWeakResourceId( "before/item.test", id );
		try
		{
			Assert.AreSame( resource, ResourceLibrary.Get<TestResource>( id ) );
			resources.MoveResource( resource, "after/item.test" );
			Assert.AreEqual( id, resource.Guid );
			Assert.AreEqual( "after/item.test", resource.ResourcePath );
			Assert.IsNull( ResourceLibrary.Get<TestResource>( "before/item.test" ) );
			Assert.AreSame( resource, ResourceLibrary.Get<TestResource>( id ) );
		}
		finally
		{
			resources.Unregister( resource );
		}
	}

	[TestMethod]
	public void LookupCanReturnAnUnresolvedGameResourcePromise()
	{
		var id = Guid.NewGuid();
		var promise = GameResource.GetPromise( typeof( SceneFile ), new ResourceId { Guid = id } );

		Assert.IsNotNull( promise );
		Assert.IsTrue( promise.IsPromise );
		Assert.AreEqual( id, promise.Guid );
		Assert.AreSame( promise, ResourceLibrary.Get<SceneFile>( id ) );
	}

	private sealed class TestResource : Resource
	{
		public override bool IsValid => true;
		internal override void Destroy() => GC.SuppressFinalize( this );
	}

	private sealed class OtherResource : Resource
	{
		public override bool IsValid => true;
		internal override void Destroy() => GC.SuppressFinalize( this );
	}
}
