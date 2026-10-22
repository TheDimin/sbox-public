using Editor;
using System;
using System.IO;

namespace EditorTests;

[TestClass]
[DoNotParallelize]
public class NativeAssetProcessorTests
{
	[TestMethod]
	public void IdenticalReferencesDoNotRewriteMetadata()
	{
		WithMetadataFile( ( path, metadata ) =>
		{
			var publishing = CreateSettings( ["facepunch.props", "facepunch.citizen"] );
			metadata.Set( "publish", publishing );
			var contents = File.ReadAllBytes( path );
			File.SetLastWriteTimeUtc( path, new DateTime( 2001, 1, 1, 0, 0, 0, DateTimeKind.Utc ) );
			var writeTime = File.GetLastWriteTimeUtc( path );

			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props", "facepunch.citizen"] );

			Assert.AreEqual( writeTime, File.GetLastWriteTimeUtc( path ) );
			CollectionAssert.AreEqual( contents, File.ReadAllBytes( path ) );
		} );
	}

	[TestMethod]
	public void ChangedReorderedAndRemovedReferencesAreSaved()
	{
		WithMetadataFile( ( path, metadata ) =>
		{
			var publishing = CreateSettings( ["facepunch.old"] );
			metadata.Set( "publish", publishing );
			metadata.Set( "unrelated", "preserved" );

			string[][] changes =
			[
				["facepunch.props", "facepunch.citizen"],
				["facepunch.citizen", "facepunch.props"],
				[]
			];
			foreach ( var references in changes )
			{
				NativeAssetProcessor.SaveEditorReferences( publishing, metadata, references );
				// Read from disk rather than the metadata cache to prove each change was saved.
				using var document = System.Text.Json.JsonDocument.Parse( File.ReadAllText( path ) );
				var saved = System.Text.Json.JsonSerializer.Deserialize<Asset.PublishSettings>( document.RootElement.GetProperty( "publish" ).GetRawText() );
				CollectionAssert.AreEqual( references, saved.ProjectConfig.EditorReferences.ToArray() );
				Assert.AreEqual( "preserved", document.RootElement.GetProperty( "unrelated" ).GetString() );
			}
		} );
	}

	[TestMethod]
	public void MissingReferencesRemainAbsentUntilAReferenceIsAdded()
	{
		WithMetadataFile( ( path, metadata ) =>
		{
			var publishing = CreateSettings( null );
			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, Array.Empty<string>() );
			Assert.IsFalse( File.Exists( path ) );
			Assert.IsNull( publishing.ProjectConfig.EditorReferences );

			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			Assert.IsTrue( File.Exists( path ) );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, metadata.Get<Asset.PublishSettings>( "publish" ).ProjectConfig.EditorReferences.ToArray() );
		} );
	}

	[TestMethod]
	public void FailedSaveIsRetriedWhenTheDirectoryBecomesAvailable()
	{
		var directory = Path.Combine( Environment.CurrentDirectory, ".source2", $"native_asset_retry_{Guid.NewGuid():N}" );
		var path = Path.Combine( directory, "references.meta" );
		try
		{
			var metadata = new MetaData( path );
			var publishing = CreateSettings( null );
			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			Assert.IsFalse( File.Exists( path ) );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, publishing.ProjectConfig.EditorReferences.ToArray() );

			Directory.CreateDirectory( directory );
			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, ReadSavedReferences( path ) );
		}
		finally
		{
			if ( Directory.Exists( directory ) )
			{
				File.Delete( path );
				Directory.Delete( directory );
			}
		}
	}

	[TestMethod]
	public void ExternallyDeletedMetadataIsRestoredDespiteCachedPublishingSettings()
	{
		WithMetadataFile( ( path, metadata ) =>
		{
			var publishing = CreateSettings( ["facepunch.props"] );
			metadata.Set( "publish", publishing );
			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			File.Delete( path );

			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, ReadSavedReferences( path ) );
		} );
	}

	[TestMethod]
	public void ExternallyChangedReferencesAreReplacedDespiteCachedPublishingSettings()
	{
		WithMetadataFile( ( path, metadata ) =>
		{
			var publishing = CreateSettings( ["facepunch.props"] );
			metadata.Set( "publish", publishing );
			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			File.WriteAllText( path, """{"publish":{"ProjectConfig":{"EditorReferences":["external.changed"]}},"unrelated":"external value"}""" );
			File.SetLastWriteTimeUtc( path, DateTime.UtcNow.AddSeconds( 1 ) );

			NativeAssetProcessor.SaveEditorReferences( publishing, metadata, ["facepunch.props"] );
			CollectionAssert.AreEqual( new[] { "facepunch.props" }, ReadSavedReferences( path ) );
			Assert.AreEqual( "external value", metadata.GetString( "unrelated" ) );
		} );
	}

	private static string[] ReadSavedReferences( string path )
	{
		using var document = System.Text.Json.JsonDocument.Parse( File.ReadAllText( path ) );
		var saved = System.Text.Json.JsonSerializer.Deserialize<Asset.PublishSettings>( document.RootElement.GetProperty( "publish" ).GetRawText() );
		return saved.ProjectConfig.EditorReferences.ToArray();
	}

	private static Asset.PublishSettings CreateSettings( System.Collections.Generic.List<string> references ) => new()
	{
		ProjectConfig = new Sandbox.DataModel.ProjectConfig { EditorReferences = references }
	};

	private static void WithMetadataFile( Action<string, MetaData> test )
	{
		var path = Path.Combine( Environment.CurrentDirectory, ".source2", $"native_asset_references_{Guid.NewGuid():N}.json" );
		try
		{
			test( path, new MetaData( path ) );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
