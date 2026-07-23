namespace NetworkTests;

// Issue #11195: small compiled shaders (.shader_c < 64KB) took the in-memory small-file path and
// failed to load on joining clients (ERROR_FILEOPEN). Native-loaded formats must always go large.
[TestClass]
public class NetworkFileRoutingTest
{
	[TestMethod]
	public void ManifestOracleIsStableAcrossInsertionOrder()
	{
		var smallA = new Sandbox.Network.StringTable( "small-a", true );
		var largeA = new Sandbox.Network.StringTable( "large-a", true );
		smallA.Set( "z.scss", new byte[] { 1, 2, 3 } );
		smallA.Set( "a.scss", new byte[] { 4, 5, 6 } );
		largeA.Set( "z.vtex_c", new LargeNetworkFiles.LargeFileInfo( 123, 456 ) );

		var smallB = new Sandbox.Network.StringTable( "small-b", true );
		var largeB = new Sandbox.Network.StringTable( "large-b", true );
		largeB.Set( "z.vtex_c", new LargeNetworkFiles.LargeFileInfo( 123, 456 ) );
		smallB.Set( "a.scss", new byte[] { 4, 5, 6 } );
		smallB.Set( "z.scss", new byte[] { 1, 2, 3 } );

		Assert.AreEqual(
			NetworkFileManifestOracle.ComputeHash( smallA, largeA ),
			NetworkFileManifestOracle.ComputeHash( smallB, largeB ) );
	}

	[TestMethod]
	public void ManifestOracleChangesWithPayload()
	{
		var small = new Sandbox.Network.StringTable( "small", true );
		var large = new Sandbox.Network.StringTable( "large", true );
		small.Set( "styles/menu.scss", new byte[] { 1, 2, 3 } );
		var before = NetworkFileManifestOracle.ComputeHash( small, large );

		small.Set( "styles/menu.scss", new byte[] { 1, 2, 4 } );

		Assert.AreNotEqual( before, NetworkFileManifestOracle.ComputeHash( small, large ) );
	}

	[TestMethod]
	public void LiveLargeFileUpdateRunsDownloadCallback()
	{
		var files = new LargeNetworkFiles( "large" );
		var calls = 0;
		files.EnableLiveDownloads( () =>
		{
			calls++;
			return Task.CompletedTask;
		} );

		files.StringTable.PostNetworkUpdate();

		Assert.AreEqual( 1, calls );
	}

	[TestMethod]
	public void ResourcePatternsAreNormalizedAndDeduplicated()
	{
		var patterns = GameInstanceDll.ParseNetworkIncludePaths(
			"\n// comment\n Materials/*.vmat \nmaterials/*.vmat\nfonts/*.ttf\n" );

		CollectionAssert.AreEqual(
			new[] { "materials/*.vmat", "fonts/*.ttf" },
			patterns );
	}

	[TestMethod]
	public void RemovingResourcePatternRemovesItFromParsedRules()
	{
		var before = GameInstanceDll.ParseNetworkIncludePaths( "materials/*.vmat\nfonts/*.ttf" );
		var after = GameInstanceDll.ParseNetworkIncludePaths( "fonts/*.ttf" );

		CollectionAssert.Contains( before, "materials/*.vmat" );
		CollectionAssert.DoesNotContain( after, "materials/*.vmat" );
	}

	[TestMethod]
	public void SmallShaderUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "shaders/toon_postprocess.shader_c", 1024 ) );
	}

	[TestMethod]
	public void SmallNonEngineFileUsesSmallDownload()
	{
		Assert.IsFalse( GameInstanceDll.ShouldUseLargeDownload( "styles/menu.scss", 1024 ) );
	}

	[TestMethod]
	public void LargeFileUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "styles/menu.scss", 1024 * 64 ) );
	}
}
