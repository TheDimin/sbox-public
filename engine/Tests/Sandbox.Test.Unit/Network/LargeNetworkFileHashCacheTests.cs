using System;
using System.IO;

namespace NetworkTests;

[TestClass]
public class LargeNetworkFileHashCacheTests
{
	private string _root;
	private LocalFileSystem _files;

	[TestInitialize]
	public void Initialize()
	{
		_root = Path.Combine( Path.GetTempPath(), "sbox-large-network-cache-tests", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( _root );
		_files = new LocalFileSystem( _root );
		_files.WriteAllBytes( "large.vtex_c", [1, 2, 3, 4, 5, 6] );
	}

	[TestCleanup]
	public void Cleanup()
	{
		_files?.Dispose();
		if ( Directory.Exists( _root ) )
			Directory.Delete( _root, true );
	}

	[TestMethod]
	public void AddFileReportsPersistentHitAndPreservesLargeFileInfo()
	{
		var cache = new FileHashCache( _root );
		var first = new LargeNetworkFiles( "large-first", cache );
		var firstHit = true;

		Assert.IsTrue( first.AddFile( _files, "large.vtex_c", false, (_, _, hit) => firstHit = hit ) );
		Assert.IsFalse( firstHit );
		Assert.IsTrue( first.TryGetFileInfo( "large.vtex_c", out var firstInfo ) );
		Assert.AreEqual( new LargeNetworkFiles.LargeFileInfo( 6, _files.GetCrc( "large.vtex_c" ) ), firstInfo );
		cache.Flush();

		var reloaded = new FileHashCache( _root );
		var second = new LargeNetworkFiles( "large-second", reloaded );
		var secondHit = false;

		Assert.IsTrue( second.AddFile( _files, "large.vtex_c", false, (_, _, hit) => secondHit = hit ) );
		Assert.IsTrue( secondHit );
		Assert.IsTrue( second.TryGetFileInfo( "large.vtex_c", out var secondInfo ) );
		Assert.AreEqual( firstInfo, secondInfo );
	}

	[TestMethod]
	public void AddFileForcedRefreshBypassesPersistentCrc()
	{
		var cache = new FileHashCache( _root );
		cache.GetOrComputeCrc( _files, "large.vtex_c", false, out _, out _ );
		cache.Flush();
		var reloaded = new FileHashCache( _root );
		var files = new LargeNetworkFiles( "large-forced", reloaded );
		var cacheHit = true;

		Assert.IsTrue( files.AddFile( _files, "large.vtex_c", true, (_, _, hit) => cacheHit = hit ) );
		Assert.IsFalse( cacheHit );
		Assert.IsTrue( files.TryGetFileInfo( "large.vtex_c", out var info ) );
		Assert.AreEqual( new LargeNetworkFiles.LargeFileInfo( 6, _files.GetCrc( "large.vtex_c" ) ), info );
	}
}
