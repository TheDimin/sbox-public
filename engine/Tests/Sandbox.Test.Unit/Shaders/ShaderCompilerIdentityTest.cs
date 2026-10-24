using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Engine.Shaders;

namespace EngineTests;

[TestClass]
public class ShaderCompilerIdentityTest
{
	DirectoryInfo directory;
	string GameDirectory => directory.FullName;
	string BinaryPath( string name ) => Path.Combine( GameDirectory, "bin", "win64", name );
	string ManifestPath => Path.Combine( GameDirectory, "bin", ".sbox-artifacts.json" );

	[TestInitialize]
	public void Initialize()
	{
		directory = Directory.CreateTempSubdirectory( "shader-compiler-identity-" );
		Directory.CreateDirectory( Path.Combine( GameDirectory, "bin", "win64" ) );
		foreach ( var name in new[] { "vfx_vulkan.dll", "dxcompiler.dll", "resourcecompiler.dll", "slang.dll" } )
			File.WriteAllText( BinaryPath( name ), name );
		WriteManifest();
	}

	[TestCleanup]
	public void Cleanup() => directory?.Delete( true );

	void WriteManifest()
	{
		var files = Directory.GetFiles( Path.Combine( GameDirectory, "bin", "win64" ) )
			.Select( path => new
			{
				path = "game/bin/win64/" + Path.GetFileName( path ),
				sha256 = Convert.ToHexString( SHA256.HashData( File.ReadAllBytes( path ) ) ),
				size = new FileInfo( path ).Length
			} ).ToArray();
		File.WriteAllText( ManifestPath, JsonSerializer.Serialize( new { commit = "upstream-commit", files } ) );
	}

	string Read( Action<string> warning = null ) => ShaderCompilerIdentity.Read( GameDirectory,
		Path.Combine( "bin", "win64" ), name => name + ".dll", warning ?? (message => Assert.Fail( message )) );

	[TestMethod]
	public void StableAcrossReadsAndManifestRemoval()
	{
		var identity = Read();
		Assert.IsNotNull( identity );
		Assert.AreEqual( identity, Read() );
		File.Delete( ManifestPath );
		Assert.AreEqual( identity, Read() );
	}

	[TestMethod]
	public void IgnoresUnrelatedArtifactChangesAndMalformedManifest()
	{
		var identity = Read();
		File.WriteAllText( BinaryPath( "unrelated.dll" ), "different engine feature" );
		WriteManifest();
		Assert.AreEqual( identity, Read() );
		File.WriteAllText( ManifestPath, "{partial download" );
		Assert.AreEqual( identity, Read() );
	}

	[TestMethod]
	[DataRow( "vfx_vulkan.dll" )]
	[DataRow( "dxcompiler.dll" )]
	[DataRow( "resourcecompiler.dll" )]
	[DataRow( "slang.dll" )]
	public void DetectsCompilerChangeWithStaleManifestAndUnchangedFileMetadata( string name )
	{
		var identity = Read();
		var path = BinaryPath( name );
		var timestamp = File.GetLastWriteTimeUtc( path );
		var bytes = File.ReadAllBytes( path );
		bytes[0] ^= 1;
		File.WriteAllBytes( path, bytes );
		File.SetLastWriteTimeUtc( path, timestamp );
		var changed = Read();
		Assert.AreNotEqual( identity, changed );
		WriteManifest();
		Assert.AreEqual( changed, Read() );
	}

	[TestMethod]
	public void OptionalSlangBackendChangesInvalidateIdentity()
	{
		var identity = Read();
		var path = BinaryPath( "slang-compiler.dll" );
		File.WriteAllText( path, "compiler backend" );
		var installed = Read();
		Assert.AreNotEqual( identity, installed );
		File.WriteAllText( path, "updated backend" );
		Assert.AreNotEqual( installed, Read() );
		File.Delete( path );
		Assert.AreEqual( identity, Read() );
	}

	[TestMethod]
	public void MissingRequiredCompilerDisablesReuseWithOneWarning()
	{
		File.Delete( BinaryPath( "dxcompiler.dll" ) );
		var warnings = new List<string>();
		Assert.IsNull( Read( warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count );
		StringAssert.Contains( warnings[0], "dxcompiler.dll" );
	}

	[TestMethod]
	public void UnreadableRequiredCompilerDisablesReuseWithOneWarning()
	{
		using var locked = new FileStream( BinaryPath( "vfx_vulkan.dll" ), FileMode.Open, FileAccess.ReadWrite, FileShare.None );
		var warnings = new List<string>();
		Assert.IsNull( Read( warnings.Add ) );
		Assert.AreEqual( 1, warnings.Count );
		StringAssert.Contains( warnings[0], "vfx_vulkan.dll" );
	}

	[TestMethod]
	[DataRow( "linuxsteamrt64", ".so" )]
	[DataRow( "osxarm64", ".dylib" )]
	public void UsesNativePlatformDirectoryAndLibraryNames( string platform, string extension )
	{
		var nativeDirectory = Path.Combine( GameDirectory, "bin", platform );
		Directory.CreateDirectory( nativeDirectory );
		foreach ( var name in new[] { "vfx_vulkan", "dxcompiler" } )
			File.WriteAllText( Path.Combine( nativeDirectory, "lib" + name + extension ), name );

		// Resource compiler is not shipped in every runtime install. Its absence has a distinct identity.
		string ReadPlatform() => ShaderCompilerIdentity.Read( GameDirectory, nativeDirectory,
			name => "lib" + name + extension, message => Assert.Fail( message ) );
		var identity = ReadPlatform();
		Assert.IsNotNull( identity );
		Assert.AreEqual( identity, ReadPlatform() );
		File.WriteAllText( Path.Combine( nativeDirectory, "libdxcompiler" + extension ), "changed backend" );
		Assert.AreNotEqual( identity, ReadPlatform() );
	}
}
