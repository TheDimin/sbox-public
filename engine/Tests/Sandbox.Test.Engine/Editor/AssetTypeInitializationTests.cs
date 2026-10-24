using Editor;

namespace EditorTests;

[TestClass]
public class AssetTypeInitializationTests
{
	[TestMethod]
	public void NativeScanPreservesCustomResourceBindingAndDoesNotLoadFallbackIcons()
	{
		var type = new AssetType
		{
			IsGameResource = true,
			FriendlyName = "Model",
			FileExtension = "custom",
			ResourceType = typeof( CustomResource ),
			Category = "Custom Resources"
		};

		// Scans can repeat before and after project types are loaded. A custom type
		// must not become a native model or load the native default PNGs in either pass.
		type.Init();
		type.Init();

		Assert.AreEqual( typeof( CustomResource ), type.ResourceType );
		Assert.AreEqual( "Model", type.FriendlyName );
		Assert.AreEqual( "custom", type.FileExtension );
		Assert.AreEqual( "Custom Resources", type.Category );
		Assert.IsNull( type.IconPathSmall );
		Assert.IsNull( type.IconPathLarge );
		Assert.IsNull( type.Icon16 );
		Assert.IsNull( type.Icon64 );
		Assert.IsNull( type.Icon128 );
		Assert.IsNull( type.Icon256 );
	}

	private sealed class CustomResource : Sandbox.GameResource
	{
	}
}
