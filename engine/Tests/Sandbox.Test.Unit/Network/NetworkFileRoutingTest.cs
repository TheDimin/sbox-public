using System;
using System.IO;

namespace NetworkTests;

// Issue #11195: small compiled shaders (.shader_c < 64KB) took the in-memory small-file path and
// failed to load on joining clients (ERROR_FILEOPEN). Native-loaded formats must always go large.
[TestClass]
public class NetworkFileRoutingTest
{
	[TestMethod]
	public void MissingLargeFileDoesNotEnterManifest()
	{
		var source = new MemoryFileSystem();
		try
		{
			var files = new LargeNetworkFiles( "missing-large", new FileHashCache( null ) );
			Assert.IsFalse( files.AddFile( source, "missing.vmdl_c" ) );
			Assert.AreEqual( 0, files.StringTable.Entries.Count );
		}
		finally
		{
			source.Dispose();
		}
	}

	[TestMethod]
	public void VirtualLargeFileUsesStreamSizeAndChecksum()
	{
		var source = new MemoryFileSystem();
		try
		{
			source.WriteAllBytes( "models/test.vmdl_c", [1, 3, 5, 7] );
			var files = new LargeNetworkFiles( "virtual-large", new FileHashCache( null ) );
			var tableWrites = 0;

			Assert.IsTrue( files.AddFile( source, "models/test.vmdl_c", onTableSet: _ => tableWrites++ ) );
			Assert.IsTrue( files.TryGetFileInfo( "models/test.vmdl_c", out var info ) );
			Assert.AreEqual( 4L, info.Size );
			Assert.AreEqual( source.GetCrc( "models/test.vmdl_c" ), info.CRC );
			Assert.IsTrue( files.AddFile( source, "models/test.vmdl_c", onTableSet: _ => tableWrites++ ) );
			Assert.AreEqual( 1, tableWrites );
		}
		finally
		{
			source.Dispose();
		}
	}

	[TestMethod]
	public void AlreadyReadSmallFileSkipsUnchangedTableWrites()
	{
		var files = new SmallNetworkFiles( "small-preloaded" );
		var tableWrites = 0;
		files.AddFile( "styles/menu.scss", [1, 2], _ => tableWrites++ );
		files.AddFile( "styles/menu.scss", [1, 2], _ => tableWrites++ );
		files.AddFile( "styles/menu.scss", [1, 3], _ => tableWrites++ );

		Assert.AreEqual( 2, tableWrites );
		CollectionAssert.AreEqual( new byte[] { 1, 3 }, files.StringTable.Entries["styles/menu.scss"].Data );
	}

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
	public void OptedInNetworkRulesExpandFoldersAndIgnoreCommentsAndExclusions()
	{
		var paths = GameInstanceDll.GetNetworkIncludePaths( "sounds/\n UI\\ \n# note\n!sounds/dev*\n// ignored\nfonts/*.ttf\n  ", true ).ToArray();
		CollectionAssert.AreEqual( new[] { "/resources/*", "/sounds/*", "/UI/*", "/fonts/*.ttf" }, paths, string.Join( ", ", paths ) );
		Assert.IsTrue( "/sounds/ambient/birds.wav".WildcardMatch( paths[1] ) );
	}

	[TestMethod]
	public void LegacyNetworkRulesKeepSpecialCharactersLiteral()
	{
		var paths = GameInstanceDll.GetNetworkIncludePaths( "sounds/\n#data.txt\n!data.txt\n// ignored", false ).ToArray();
		CollectionAssert.AreEqual( new[] { "/sounds/", "/#data.txt", "/!data.txt" }, paths );
	}

	[TestMethod]
	public void NetworkResourcesFolderRequiresOptIn()
	{
		Assert.AreEqual( 0, GameInstanceDll.GetNetworkIncludePaths( null, false ).Count() );
		CollectionAssert.AreEqual( new[] { "/resources/*" }, GameInstanceDll.GetNetworkIncludePaths( null, true ).ToArray() );
	}

	[TestMethod]
	public void SmallShaderUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "shaders/toon_postprocess.shader_c", 1024 ) );
	}

	[DataTestMethod]
	[DataRow( 0L )]
	[DataRow( 1695L )]
	[DataRow( 65535L )]
	public void SmallPhysicsUsesLargeDownload( long size )
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "scenes/world_scene_data/compiled/generation/collision_0.vphys_c", size ) );
	}

	[TestMethod]
	public void SmallCompiledPrefabUsesLargeDownload()
	{
		Assert.IsTrue( GameInstanceDll.ShouldUseLargeDownload( "survival/prefabs/player.prefab_c", 1024 ) );
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

	[TestMethod]
	public void MatchingDeveloperFileInstallsPhysicalRedirect()
	{
		var root = Path.Combine( Path.GetTempPath(), "sbox-network-file-routing", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var source = new LocalFileSystem( root );

		try
		{
			source.WriteAllBytes( "models/player.vmdl_c", [1, 2, 3, 4] );
			var info = new LargeNetworkFiles.LargeFileInfo(
				source.FileSize( "models/player.vmdl_c" ),
				source.GetCrc( "models/player.vmdl_c" ) );
			string redirectPath = null;
			string redirectTarget = null;

			Assert.IsTrue( LargeNetworkFiles.TryReuseMatchingMountedFile(
				source,
				"models/player.vmdl_c",
				info,
				( path, target ) =>
				{
					redirectPath = path;
					redirectTarget = target;
				} ) );
			Assert.AreEqual( "/models/player.vmdl_c", redirectPath );
			Assert.AreEqual( source.GetFullPath( "models/player.vmdl_c" ), redirectTarget );
		}
		finally
		{
			source.Dispose();
			Directory.Delete( root, true );
		}
	}

	[DataTestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void NestedMountedDeveloperFileInstallsPhysicalRedirect( bool virtualMountFirst )
	{
		var root = Path.Combine( Path.GetTempPath(), "sbox-nested-network-file", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var source = new LocalFileSystem( root );
		var virtualFiles = new MemoryFileSystem();
		var inner = new AggregateFileSystem();
		var outer = new AggregateFileSystem();
		try
		{
			source.WriteAllBytes( "models/player.vmdl_c", [1, 2, 3, 4] );
			virtualFiles.WriteAllBytes( "models/player.vmdl_c", [9] );
			if ( virtualMountFirst ) inner.Mount( virtualFiles );
			inner.Mount( source );
			if ( !virtualMountFirst ) inner.Mount( virtualFiles );
			outer.Mount( inner );
			string target = null;
			var info = new LargeNetworkFiles.LargeFileInfo( 4, source.GetCrc( "models/player.vmdl_c" ) );

			Assert.IsTrue( LargeNetworkFiles.TryReuseMatchingMountedFile(
				outer, "models/player.vmdl_c", info, ( _, path ) => target = path ) );
			Assert.AreEqual( source.GetFullPath( "models/player.vmdl_c" ), target );
		}
		finally
		{
			outer.UnMountAll();
			inner.UnMountAll();
			outer.Dispose();
			inner.Dispose();
			virtualFiles.Dispose();
			source.Dispose();
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void VirtualMountedFileFallsBackToNetworkDownload()
	{
		var source = new MemoryFileSystem();
		try
		{
			source.WriteAllBytes( "models/player.vmdl_c", [1, 2, 3, 4] );
			var files = new LargeNetworkFiles( "virtual-mounted" );
			var info = new LargeNetworkFiles.LargeFileInfo( 4, source.GetCrc( "models/player.vmdl_c" ) );
			Assert.IsFalse( LargeNetworkFiles.TryReuseMatchingMountedFile(
				source, "models/player.vmdl_c", info, ( _, _ ) => Assert.Fail( "Virtual file has no physical redirect" ) ) );
			files.QueueFileIfNeeded( "models/player.vmdl_c", info, ( _, _ ) => false );
			Assert.AreEqual( 1, files.PendingDownloadCount );
		}
		finally
		{
			source.Dispose();
		}
	}

	[TestMethod]
	public void MismatchedDeveloperFileQueuesDownload()
	{
		var files = new LargeNetworkFiles( "large-queue" );
		files.QueueFileIfNeeded(
			"models/player.vmdl_c",
			new LargeNetworkFiles.LargeFileInfo( 4, 123 ),
			( _, _ ) => false );

		Assert.AreEqual( 1, files.PendingDownloadCount );
	}

	[TestMethod]
	public void CachedFileInstallsPhysicalRedirect()
	{
		var root = Path.Combine( Path.GetTempPath(), "sbox-cached-network-file", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var cacheFiles = new LocalFileSystem( root );

		try
		{
			cacheFiles.WriteAllBytes( "models/player.vmdl_c", [1, 2, 3] );
			string redirectPath = null;
			string redirectTarget = null;

			Assert.IsTrue( AssetDownloadCache.TryAddPhysicalRedirect(
				cacheFiles,
				"models/player.vmdl_c",
				( path, target ) =>
				{
					redirectPath = path;
					redirectTarget = target;
				} ) );
			Assert.AreEqual( "/models/player.vmdl_c", redirectPath );
			Assert.AreEqual( cacheFiles.GetFullPath( "models/player.vmdl_c" ), redirectTarget );
		}
		finally
		{
			cacheFiles.Dispose();
			Directory.Delete( root, true );
		}
	}
}
