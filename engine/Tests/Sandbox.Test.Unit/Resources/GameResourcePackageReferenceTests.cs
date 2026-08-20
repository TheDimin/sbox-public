namespace Sandbox;

[TestClass]
public sealed class GameResourcePackageReferenceTests
{
	private sealed class TestResource : GameResource
	{
	}

	[TestMethod]
	public void GetReferencedPackages_RejectsLocalAssetPaths()
	{
		var resource = new TestResource
		{
			referencedPackages =
			[
				"facepunch.tv#279757",
				"survival/prefabs/player.prefab",
				"thirdparty/synty/fantasykingdom/models/sm_prop_throne_01.vmdl"
			]
		};

		CollectionAssert.AreEqual(
			new[] { "facepunch.tv#279757" },
			resource.GetReferencedPackages().ToArray() );
	}
}
