using System;
using System.IO;
using System.Text;

namespace FilesystemTests;

[TestClass]
public class FileIndexTests
{
	[TestMethod]
	public void SnapshotSupportsVirtualFallbackAndSubfolders()
	{
		using var fallback = new Zio.FileSystems.MemoryFileSystem();
		using var high = new Zio.FileSystems.MemoryFileSystem();
		fallback.CreateDirectory( "/sub" );
		high.CreateDirectory( "/sub" );
		Write( fallback, "/sub/shared.txt", "fallback" );
		Write( fallback, "/sub/only.txt", "only" );
		Write( high, "/sub/shared.txt", "high" );
		using var aggregate = new Zio.FileSystems.AggregateFileSystem( fallback, false );
		aggregate.AddFileSystem( high );
		var fs = new BaseFileSystem( aggregate ).CreateSubSystem( "/sub" );
		var index = fs.IndexFiles( "/" );

		foreach ( var path in new[] { "shared.txt", "only.txt" } )
		{
			Assert.IsTrue( index.TryReadAllBytes( path, out var bytes ) );
			CollectionAssert.AreEqual( fs.ReadAllBytes( path ).ToArray(), bytes );
		}
		Assert.AreEqual( "high", fs.ReadAllText( "shared.txt" ) );
		Assert.AreEqual( "only", fs.ReadAllText( "only.txt" ) );
	}

	static void Write( Zio.IFileSystem fs, string path, string value )
	{
		using var stream = fs.OpenFile( path, FileMode.Create, FileAccess.Write, FileShare.Read );
		using var writer = new StreamWriter( stream );
		writer.Write( value );
	}

	[TestMethod]
	public void SnapshotPreservesMountPriorityPathsAndSourceHashCache()
	{
		var root = Path.Combine( Path.GetTempPath(), $"sbox-file-index-{Guid.NewGuid():N}" );
		var low = Path.Combine( root, "low" );
		var high = Path.Combine( root, "high" );
		Directory.CreateDirectory( low );
		Directory.CreateDirectory( Path.Combine( high, "sub" ) );
		File.WriteAllText( Path.Combine( low, "shared.json" ), "low" );
		File.WriteAllText( Path.Combine( high, "shared.json" ), "high", new UTF8Encoding( true ) );
		File.WriteAllText( Path.Combine( high, "sub", "nested.json" ), "nested" );
		var fs = new AggregateFileSystem();
		try
		{
			var nested = new AggregateFileSystem();
			nested.Mount( new LocalFileSystem( high ) );
			fs.Mount( new LocalFileSystem( low ) );
			fs.Mount( nested );
			var index = fs.IndexFiles( "/" );
			CollectionAssert.AreEqual( fs.FindFile( "/", "*", true ).ToArray(), index.Files.ToArray() );
			foreach ( var path in new[] { "shared.json", "SHARED.JSON", "sub/nested.json" } )
			{
				Assert.IsTrue( index.TryReadAllBytes( path, out var data ) );
				CollectionAssert.AreEqual( fs.ReadAllBytes( path ).ToArray(), data );
				Assert.IsTrue( index.TryGetFileSystem( path, out var owner, out var ownerPath ) );
				Assert.AreEqual( fs.GetFullPath( path ), owner.GetFullPath( ownerPath ) );
			}

			Assert.IsTrue( index.TryGetFileSystem( "shared.json", out var source, out var sourcePath ) );
			var hashes = new FileHashCache( root );
			var expected = hashes.GetOrComputeSourceHash( fs, "shared.json", out _ );
			Assert.AreEqual( expected, hashes.GetOrComputeSourceHash( source, sourcePath, out var hit ) );
			Assert.IsTrue( hit, "The snapshot must retain the physical cache identity and BOM decoding." );
		}
		finally
		{
			fs.Dispose();
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void SnapshotSkipsDeletedFilesAndDoesNotIncludeLaterFiles()
	{
		var root = Path.Combine( Path.GetTempPath(), $"sbox-file-index-{Guid.NewGuid():N}" );
		Directory.CreateDirectory( root );
		var fs = new LocalFileSystem( root );
		try
		{
			File.WriteAllText( Path.Combine( root, "before.txt" ), "before" );
			var index = fs.IndexFiles( "/" );
			File.Delete( Path.Combine( root, "before.txt" ) );
			File.WriteAllText( Path.Combine( root, "after.txt" ), "after" );
			Assert.IsFalse( index.TryReadAllBytes( "before.txt", out _ ) );
			Assert.IsFalse( index.Contains( "after.txt" ) );
			Assert.IsFalse( index.TryReadAllBytes( "after.txt", out _ ) );
			Assert.IsTrue( fs.IndexFiles( "/" ).TryReadAllBytes( "after.txt", out _ ) );
		}
		finally
		{
			fs.Dispose();
			Directory.Delete( root, true );
		}
	}
}
