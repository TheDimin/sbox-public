using Facepunch.Steps;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Tests;

[TestClass]
public class ArtifactLinksTests
{
	sealed class Fixture : IDisposable
	{
		internal string Root { get; } = Path.Combine( Path.GetTempPath(), "sbox-artifact-links", Guid.NewGuid().ToString( "N" ) );
		internal Fixture() => Directory.CreateDirectory( Root );
		internal string PathFor( string path ) => Path.Combine( Root, path );
		public void Dispose() => Directory.Delete( Root, true );
	}

	static void RequireSymlinks()
	{
		if ( OperatingSystem.IsWindows() )
			Assert.Inconclusive( "File symlink creation requires Windows developer mode or elevation." );
	}

	static ArtifactFileInfo FileEntry( string path, string target = null ) => new()
	{
		Path = path,
		LinkTarget = target,
		Sha256 = "payload-hash",
		Size = 7
	};

	[TestMethod]
	public void PublishedAliasesRoundTripAndRepairFlattenedCopies()
	{
		RequireSymlinks();
		using var source = new Fixture();
		using var restored = new Fixture();
		File.WriteAllText( source.PathFor( "libSDL3.so.0.4.14" ), "payload" );
		File.CreateSymbolicLink( source.PathFor( "libSDL3.so.0" ), "libSDL3.so.0.4.14" );
		File.CreateSymbolicLink( source.PathFor( "libSDL3.so" ), "libSDL3.so.0" );
		var candidates = new[] { "libSDL3.so", "libSDL3.so.0", "libSDL3.so.0.4.14" }
			.Select( path => (RepoPath: path, AbsolutePath: source.PathFor( path )) ).ToArray();
		var published = ArtifactLinks.Capture( source.Root, candidates ).Select( entry => entry with
		{
			Sha256 = Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( source.PathFor( entry.Path ) ) ) ),
			Size = entry.LinkTarget is null ? new FileInfo( source.PathFor( entry.Path ) ).Length : new FileInfo( File.ResolveLinkTarget( source.PathFor( entry.Path ), true ).FullName ).Length
		} ).ToList();
		var manifest = JsonSerializer.Deserialize<ArtifactManifest>( JsonSerializer.Serialize( new ArtifactManifest { Files = published } ) );
		Assert.AreEqual( "libSDL3.so.0", manifest.Files.Single( x => x.Path == "libSDL3.so" ).LinkTarget );
		Assert.AreEqual( 1, manifest.Files.Count( x => x.LinkTarget is null ) );
		foreach ( var entry in manifest.Files )
			File.Copy( source.PathFor( entry.Path ), restored.PathFor( entry.Path ) ); // old flattened layout

		var plan = new ArtifactLinks( restored.Root, manifest.Files );
		plan.Restore();
		plan.Restore(); // idempotent, without treating equal bytes as equal file types
		Assert.AreEqual( "libSDL3.so.0", new FileInfo( restored.PathFor( "libSDL3.so" ) ).LinkTarget );
		Assert.AreEqual( "libSDL3.so.0.4.14", new FileInfo( restored.PathFor( "libSDL3.so.0" ) ).LinkTarget );
		Assert.AreEqual( restored.PathFor( "libSDL3.so.0.4.14" ), File.ResolveLinkTarget( restored.PathFor( "libSDL3.so" ), true ).FullName );
		File.WriteAllText( restored.PathFor( "libSDL3.so.0.4.14" ), "updated" );
		Assert.AreEqual( "updated", File.ReadAllText( restored.PathFor( "libSDL3.so" ) ) );
		Assert.AreEqual( 3, Directory.GetFileSystemEntries( restored.Root ).Length );
	}

	[TestMethod]
	public void RepairsWrongAndDanglingAliases()
	{
		RequireSymlinks();
		using var fixture = new Fixture();
		File.WriteAllText( fixture.PathFor( "payload" ), "payload" );
		File.CreateSymbolicLink( fixture.PathFor( "alias" ), "missing" );
		new ArtifactLinks( fixture.Root, new[] { FileEntry( "alias", "payload" ), FileEntry( "payload" ) } ).Restore();
		Assert.AreEqual( "payload", new FileInfo( fixture.PathFor( "alias" ) ).LinkTarget );
	}

	[TestMethod]
	public void SupportsRelativeTargetsAcrossDirectories()
	{
		RequireSymlinks();
		using var fixture = new Fixture();
		File.WriteAllText( fixture.PathFor( "payload" ), "payload" );
		new ArtifactLinks( fixture.Root, new[] { FileEntry( "sub/alias", "../payload" ), FileEntry( "payload" ) } ).Restore();
		Assert.AreEqual( "payload", File.ReadAllText( fixture.PathFor( "sub/alias" ) ) );
	}

	[DataTestMethod]
	[DataRow( "/outside" )]
	[DataRow( "../outside" )]
	[DataRow( "C:\\outside" )]
	[DataRow( "" )]
	public void RejectsUnsafeLinkTargets( string target )
	{
		using var fixture = new Fixture();
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "alias", target ) } ) );
		Assert.AreEqual( 0, Directory.GetFileSystemEntries( fixture.Root ).Length );
	}

	[DataTestMethod]
	[DataRow( "../outside" )]
	[DataRow( "/absolute" )]
	[DataRow( "a/../../outside" )]
	[DataRow( "C:\\outside" )]
	public void RejectsUnsafeArtifactPaths( string path )
	{
		using var fixture = new Fixture();
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( path ) } ) );
	}

	[TestMethod]
	public void RejectsCyclesMissingAndFilteredTargets()
	{
		using var fixture = new Fixture();
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "a", "b" ), FileEntry( "b", "a" ) } ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "a", "a" ) } ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "a", "omitted" ) } ) );
	}

	[TestMethod]
	public void RejectsPayloadMetadataMismatchAndPathCollisions()
	{
		using var fixture = new Fixture();
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "alias", "payload" ), FileEntry( "payload" ) with { Sha256 = "different" } } ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "alias", "payload" ), FileEntry( "payload" ) with { Size = 99 } } ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "payload" ), FileEntry( "payload" ) } ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "payload" ), FileEntry( "payload/child" ) } ) );
	}

	[TestMethod]
	public void RejectsExistingSymlinkParentsIncludingDanglingOnes()
	{
		RequireSymlinks();
		using var fixture = new Fixture();
		using var outside = new Fixture();
		Directory.CreateSymbolicLink( fixture.PathFor( "escape" ), outside.Root );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "escape/file" ) } ) );
		Directory.CreateSymbolicLink( fixture.PathFor( "dangling" ), fixture.PathFor( "missing" ) );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root, new[] { FileEntry( "dangling/file" ) } ) );
		Assert.AreEqual( 0, Directory.GetFileSystemEntries( outside.Root ).Length );
	}

	[TestMethod]
	public void MissingPayloadDoesNotReplaceAnExistingCopy()
	{
		RequireSymlinks();
		using var fixture = new Fixture();
		File.WriteAllText( fixture.PathFor( "alias" ), "old" );
		var plan = new ArtifactLinks( fixture.Root, new[] { FileEntry( "alias", "payload" ), FileEntry( "payload" ) } );
		Assert.ThrowsException<IOException>( plan.Restore );
		Assert.IsNull( new FileInfo( fixture.PathFor( "alias" ) ).LinkTarget );
		Assert.AreEqual( "old", File.ReadAllText( fixture.PathFor( "alias" ) ) );
	}

	[TestMethod]
	public void LegacyManifestsKeepRegularFileSemantics()
	{
		using var fixture = new Fixture();
		var entry = JsonSerializer.Deserialize<ArtifactFileInfo>( """{"path":"payload","sha256":"hash","size":7}""" );
		Assert.IsNull( entry.LinkTarget );
		Assert.IsFalse( JsonSerializer.Serialize( entry ).Contains( "linkTarget" ) );
		new ArtifactLinks( fixture.Root, new[] { entry } ).Restore();
		Assert.AreEqual( 0, Directory.GetFileSystemEntries( fixture.Root ).Length );
	}
	[TestMethod]
	public void RejectsDirectoryLinkTraversalBeforeDotDotNormalization()
	{
		RequireSymlinks();
		using var fixture = new Fixture();
		using var outside = new Fixture();
		Directory.CreateSymbolicLink( fixture.PathFor( "redirect" ), outside.Root );
		Assert.ThrowsException<InvalidOperationException>( () => new ArtifactLinks( fixture.Root,
			new[] { FileEntry( "alias", "redirect/../payload" ), FileEntry( "payload" ) } ) );
	}

}
