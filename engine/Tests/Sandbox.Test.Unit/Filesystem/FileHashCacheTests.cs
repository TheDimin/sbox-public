using System;
using System.IO;

namespace FilesystemTests;

[TestClass]
public class FileHashCacheTests
{
	private string _root;
	private LocalFileSystem _files;

	[TestInitialize]
	public void Initialize()
	{
		_root = Path.Combine( Path.GetTempPath(), "sbox-file-hash-cache-tests", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( _root );
		_files = new LocalFileSystem( _root );
	}

	[TestCleanup]
	public void Cleanup()
	{
		_files?.Dispose();
		if ( Directory.Exists( _root ) )
			Directory.Delete( _root, true );
	}

	[TestMethod]
	public void SourceHashMissPersistsAndBecomesHitAfterReload()
	{
		_files.WriteAllText( "source.json", "{\"name\":\"caf?\"}" );
		var expected = _files.ReadAllText( "source.json" ).FastHash();
		var cache = new FileHashCache( _root );

		var actual = cache.GetOrComputeSourceHash( _files, "source.json", out var firstHit );
		cache.Flush();
		var reloaded = new FileHashCache( _root );
		var persisted = reloaded.GetOrComputeSourceHash( _files, "source.json", out var secondHit );

		Assert.IsFalse( firstHit );
		Assert.AreEqual( expected, actual );
		Assert.IsTrue( secondHit );
		Assert.AreEqual( expected, persisted );
	}

	[TestMethod]
	public void HotSourceHashDoesNotReopenFileContents()
	{
		_files.WriteAllText( "source.json", "cached contents" );
		var cache = new FileHashCache( _root );
		var expected = cache.GetOrComputeSourceHash( _files, "source.json", out var coldHit );
		cache.Flush();
		var reloaded = new FileHashCache( _root );

		using var exclusiveLock = new FileStream( _files.GetFullPath( "source.json" ), FileMode.Open, FileAccess.ReadWrite, FileShare.None );
		var actual = reloaded.GetOrComputeSourceHash( _files, "source.json", out var hotHit );

		Assert.IsFalse( coldHit );
		Assert.IsTrue( hotHit );
		Assert.AreEqual( expected, actual );
	}

	[TestMethod]
	public void CrcMissPersistsAndBecomesHitAfterReload()
	{
		_files.WriteAllBytes( "large.bin", [1, 2, 3, 4, 5] );
		var expected = _files.GetCrc( "large.bin" );
		var cache = new FileHashCache( _root );

		var actual = cache.GetOrComputeCrc( _files, "large.bin", false, out var size, out var firstHit );
		cache.Flush();
		var reloaded = new FileHashCache( _root );
		var persisted = reloaded.GetOrComputeCrc( _files, "large.bin", false, out var persistedSize, out var secondHit );

		Assert.IsFalse( firstHit );
		Assert.AreEqual( expected, actual );
		Assert.AreEqual( 5L, size );
		Assert.IsTrue( secondHit );
		Assert.AreEqual( expected, persisted );
		Assert.AreEqual( size, persistedSize );
	}

	[TestMethod]
	public void SizeChangeInvalidatesEntry()
	{
		_files.WriteAllText( "source.json", "small" );
		var cache = new FileHashCache( _root );
		cache.GetOrComputeSourceHash( _files, "source.json", out _ );
		cache.Flush();

		_files.WriteAllText( "source.json", "a larger replacement" );
		var reloaded = new FileHashCache( _root );
		var actual = reloaded.GetOrComputeSourceHash( _files, "source.json", out var cacheHit );

		Assert.IsFalse( cacheHit );
		Assert.AreEqual( _files.ReadAllText( "source.json" ).FastHash(), actual );
	}

	[TestMethod]
	public void ModificationTimeChangeInvalidatesEntry()
	{
		_files.WriteAllText( "same-size.bin", "alpha" );
		var cache = new FileHashCache( _root );
		cache.GetOrComputeCrc( _files, "same-size.bin", false, out _, out _ );
		cache.Flush();

		_files.WriteAllText( "same-size.bin", "bravo" );
		File.SetLastWriteTimeUtc( _files.GetFullPath( "same-size.bin" ), DateTime.UtcNow.AddMinutes( 2 ) );
		var reloaded = new FileHashCache( _root );
		var actual = reloaded.GetOrComputeCrc( _files, "same-size.bin", false, out _, out var cacheHit );

		Assert.IsFalse( cacheHit );
		Assert.AreEqual( _files.GetCrc( "same-size.bin" ), actual );
	}

	[TestMethod]
	public void ForcedCrcRefreshBypassesCacheAndReplacesValue()
	{
		_files.WriteAllBytes( "large.bin", [1, 2, 3, 4, 5] );
		var cache = new FileHashCache( _root );
		cache.GetOrComputeCrc( _files, "large.bin", false, out _, out _ );
		cache.Flush();
		var reloaded = new FileHashCache( _root );

		var refreshed = reloaded.GetOrComputeCrc( _files, "large.bin", true, out _, out var forcedHit );
		var reused = reloaded.GetOrComputeCrc( _files, "large.bin", false, out _, out var normalHit );

		Assert.IsFalse( forcedHit );
		Assert.IsTrue( normalHit );
		Assert.AreEqual( refreshed, reused );
	}

	[TestMethod]
	public void SourceHashAndCrcCoexistInOneEntry()
	{
		_files.WriteAllText( "shared.txt", "shared contents" );
		var cache = new FileHashCache( _root );
		var source = cache.GetOrComputeSourceHash( _files, "shared.txt", out _ );
		var crc = cache.GetOrComputeCrc( _files, "shared.txt", false, out _, out _ );
		cache.Flush();
		var reloaded = new FileHashCache( _root );

		Assert.AreEqual( source, reloaded.GetOrComputeSourceHash( _files, "shared.txt", out var sourceHit ) );
		Assert.IsTrue( sourceHit );
		Assert.AreEqual( crc, reloaded.GetOrComputeCrc( _files, "shared.txt", false, out _, out var crcHit ) );
		Assert.IsTrue( crcHit );
	}

	[TestMethod]
	public void CorruptCacheIsDisposable()
	{
		AssertInvalidCacheIsSafe( [0x13, 0x37, 0x42] );
	}

	[TestMethod]
	public void TruncatedCacheIsDisposable()
	{
		using ( var stream = NewCacheFile() )
		using ( var writer = new BinaryWriter( stream ) )
		{
			writer.Write( FileHashCache.CacheMagic );
		}
		AssertInvalidCacheIsSafe();
	}

	[TestMethod]
	public void VersionMismatchedCacheIsDisposable()
	{
		using ( var stream = NewCacheFile() )
		using ( var writer = new BinaryWriter( stream ) )
		{
			writer.Write( FileHashCache.CacheMagic );
			writer.Write( FileHashCache.CacheVersion + 1 );
			writer.Write( 0 );
		}
		AssertInvalidCacheIsSafe();
	}

	[TestMethod]
	public void NonphysicalFilesCalculateWithoutPersistence()
	{
		var files = new MemoryFileSystem();
		files.WriteAllText( "virtual.txt", "virtual contents" );
		var cache = new FileHashCache( _root );

		var source = cache.GetOrComputeSourceHash( files, "virtual.txt", out var sourceHit );
		var crc = cache.GetOrComputeCrc( files, "virtual.txt", false, out var size, out var crcHit );
		cache.Flush();
		var reloaded = new FileHashCache( _root );

		Assert.IsFalse( sourceHit );
		Assert.AreEqual( files.ReadAllText( "virtual.txt" ).FastHash(), source );
		Assert.IsFalse( crcHit );
		Assert.AreEqual( files.GetCrc( "virtual.txt" ), crc );
		Assert.AreEqual( files.FileSize( "virtual.txt" ), size );
		reloaded.GetOrComputeSourceHash( files, "virtual.txt", out var reloadedHit );
		Assert.IsFalse( reloadedHit );
		files.Dispose();
	}

	[TestMethod]
	public void ClearRemovesMemoryAndPersistentData()
	{
		_files.WriteAllText( "source.json", "contents" );
		var cache = new FileHashCache( _root );
		cache.GetOrComputeSourceHash( _files, "source.json", out _ );
		cache.Flush();
		Assert.IsTrue( File.Exists( cache.CachePath ) );

		cache.Clear();

		Assert.IsFalse( File.Exists( cache.CachePath ) );
		cache.GetOrComputeSourceHash( _files, "source.json", out var cacheHit );
		Assert.IsFalse( cacheHit );
	}

	private FileStream NewCacheFile()
	{
		var cache = new FileHashCache( _root );
		Directory.CreateDirectory( Path.GetDirectoryName( cache.CachePath ) );
		return File.Create( cache.CachePath );
	}

	private void AssertInvalidCacheIsSafe( byte[] contents = null )
	{
		if ( contents is not null )
		{
			using var stream = NewCacheFile();
			stream.Write( contents );
		}

		_files.WriteAllText( "source.json", "valid source" );
		var cache = new FileHashCache( _root );
		var actual = cache.GetOrComputeSourceHash( _files, "source.json", out var cacheHit );

		Assert.IsFalse( cacheHit );
		Assert.AreEqual( _files.ReadAllText( "source.json" ).FastHash(), actual );
	}
}
