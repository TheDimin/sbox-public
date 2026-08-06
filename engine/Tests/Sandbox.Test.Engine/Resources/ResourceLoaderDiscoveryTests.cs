using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class ResourceLoaderDiscoveryTests
{
	[TestMethod]
	public void DiscoveryRetainsOnlyGameResourceCandidates()
	{
		var gameResourceType = new AssetTypeAttribute
		{
			Extension = "test",
			Name = "Test Resource"
		};

		var types = new Dictionary<string, AssetTypeAttribute>( StringComparer.OrdinalIgnoreCase )
		{
			[".test_c"] = gameResourceType
		};

		var registeredExtensions = new HashSet<string>( types.Keys, StringComparer.OrdinalIgnoreCase )
		{
			".vmdl_c"
		};

		var discovery = new ResourceLoader.DiscoveryPlan();

		Assert.IsFalse( discovery.Observe( "content/readme.txt", types, registeredExtensions ) );
		Assert.IsTrue( discovery.Observe( "models/prop.vmdl_c", types, registeredExtensions ) );
		Assert.IsTrue( discovery.Observe( "data/first.test_c", types, registeredExtensions ) );
		Assert.IsTrue( discovery.Observe( "data/SECOND.TEST_C", types, registeredExtensions ) );

		Assert.AreEqual( 4, discovery.ScannedFileCount );
		Assert.AreEqual( 3, discovery.RegisteredPathCount );
		Assert.AreEqual( 2, discovery.GameResources.Count );
		Assert.AreEqual( "data/first.test_c", discovery.GameResources[0].Path );
		Assert.AreSame( gameResourceType, discovery.GameResources[0].Type );
		Assert.AreEqual( "data/SECOND.TEST_C", discovery.GameResources[1].Path );
	}
}
