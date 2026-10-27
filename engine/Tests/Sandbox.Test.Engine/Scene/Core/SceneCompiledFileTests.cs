using Editor;
using Sandbox.Resources;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace SceneTests;

[TestClass]
[DoNotParallelize]
public class SceneCompiledFileTests
{
	private string directory;
	private string source;
	private readonly Guid guid = Guid.NewGuid();

	[TestInitialize]
	public void Initialize()
	{
		directory = Directory.CreateTempSubdirectory( "sbox-scene-compiled-" ).FullName;
		source = Path.Combine( directory, "minimal.scene" );
		File.WriteAllText( source, "{}" );
	}

	[TestCleanup]
	public void Cleanup() => Directory.Delete( directory, true );

	private void WriteCompiled( string path, string title )
	{
		var scene = new SceneFile { Id = guid, GameObjects = [], SceneProperties = new JsonObject { ["Title"] = title } };
		var writer = new ResourceWriter();
		writer.SetDataBlock( Encoding.UTF8.GetBytes( scene.Serialize().ToJsonString() ) );
		File.WriteAllBytes( path, writer.ToArray() );
	}

	[TestMethod]
	public void OutputCreatedAfterMissingCatalogAnswerLoadsWithoutRestart()
	{
		Assert.ThrowsException<FileNotFoundException>( () => SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, source, true ) );
		WriteCompiled( source + "_c", "new output" );
		var loaded = SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, source, true );
		Assert.AreEqual( guid, loaded.Id );
		Assert.AreEqual( "scenes/minimal.scene", loaded.ResourcePath );
		Assert.AreEqual( "new output", loaded.SceneProperties["Title"].GetValue<string>() );
		Assert.AreEqual( 0, loaded.GameObjects.Length );
	}

	[TestMethod]
	public void ExistingNativeCatalogPathTakesPrecedence()
	{
		var catalog = Path.Combine( directory, "catalog.scene_c" );
		WriteCompiled( catalog, "native output" );
		WriteCompiled( source + "_c", "adjacent output" );
		var loaded = SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, catalog, source, true );
		Assert.AreEqual( "native output", loaded.SceneProperties["Title"].GetValue<string>() );
	}

	[TestMethod]
	public void MissingCatalogFileCanUseAdjacentOutput()
	{
		WriteCompiled( source + "_c", "adjacent output" );
		Assert.IsNotNull( SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, Path.Combine( directory, "missing.scene_c" ), source, true ) );
	}

	[TestMethod]
	public void DisabledFallbackDoesNotBypassTheCatalog()
	{
		WriteCompiled( source + "_c", "adjacent output" );
		Assert.ThrowsException<FileNotFoundException>( () => SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, source, false ) );
	}

	[TestMethod]
	public void InvalidCompiledBytesAreRejected()
	{
		File.WriteAllText( source + "_c", "this is not a compiled scene" );
		Assert.ThrowsException<InvalidDataException>( () => SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, source, true ) );
	}

	[TestMethod]
	public void MissingSourceDoesNotResurrectAnOrphanedOutput()
	{
		WriteCompiled( source + "_c", "orphaned output" );
		File.Delete( source );
		Assert.ThrowsException<FileNotFoundException>( () => SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, source, true ) );
	}

	[TestMethod]
	public void OtherResourceTypesDoNotUseSceneFallback()
	{
		var material = Path.Combine( directory, "minimal.vmat" );
		File.WriteAllText( material, "{}" );
		WriteCompiled( material + "_c", "not a scene output" );
		Assert.ThrowsException<FileNotFoundException>( () => SceneSource.LoadCompiledScene( "scenes/minimal.scene", guid, null, material, true ) );
	}
}
