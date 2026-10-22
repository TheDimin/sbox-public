using Editor;
using System;
using System.IO;

namespace EditorTests;

public partial class MetaDataTest
{
	public sealed class PublishFlag
	{
		public bool Enabled { get; set; }
	}

	[TestMethod]
	public void MissingPropertyCacheReadsAnExternallyAddedPublishConfig()
	{
		WithMetadataFile( """{"guid":"existing"}""", ( path, metadata ) =>
		{
			MetaData.MissingProperty missing = null;
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			Assert.IsNotNull( missing );
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );

			File.WriteAllText( path, """{"publish":{"Enabled":true}}""" );
			File.SetLastWriteTimeUtc( path, DateTime.UtcNow.AddSeconds( 1 ) );
			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );
			Assert.IsNull( missing );
		} );
	}

	[TestMethod]
	public void MissingPropertyCacheInvalidatesAfterAnInProcessSave()
	{
		WithMetadataFile( "{}", ( path, metadata ) =>
		{
			metadata.Set( "missing", new PublishFlag { Enabled = true } );
			var writeTime = File.GetLastWriteTimeUtc( path );
			var length = new FileInfo( path ).Length;
			MetaData.MissingProperty missing = null;
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			metadata.Set<PublishFlag>( "missing", null );
			metadata.Set( "publish", new PublishFlag { Enabled = true } );
			File.SetLastWriteTimeUtc( path, writeTime );
			Assert.AreEqual( length, new FileInfo( path ).Length );
			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );
			Assert.IsNull( missing );
		} );
	}

	[TestMethod]
	public void MissingPropertyCacheDoesNotHideChangesFromLegacyReads()
	{
		WithMetadataFile( """{"missing":{"Enabled":true}}""", ( path, metadata ) =>
		{
			MetaData.MissingProperty missing = null;
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			var writeTime = File.GetLastWriteTimeUtc( path );
			File.WriteAllText( path, """{"publish":{"Enabled":true}}""" );
			File.SetLastWriteTimeUtc( path, writeTime );

			// Keep the file identity unchanged. The forced legacy path must still read it.
			using ( AssetPipelineCompatibility.ForceLegacy() )
			{
				Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );
				Assert.IsNull( missing );
			}
		} );
	}

	[TestMethod]
	public void MissingPropertyCacheRetriesMalformedJsonAndInvalidValues()
	{
		WithMetadataFile( """{"publish":{"Enabled":true}x""", ( path, metadata ) =>
		{
			MetaData.MissingProperty missing = null;
			var writeTime = File.GetLastWriteTimeUtc( path );
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			Assert.IsNull( missing );
			File.WriteAllText( path, """{"publish":{"Enabled":true}}""" );
			File.SetLastWriteTimeUtc( path, writeTime );
			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );

			File.WriteAllText( path, """{"publish":false}""" );
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			Assert.IsNull( missing );
		} );
	}

	[TestMethod]
	public void MissingPropertyCacheKeepsTheIdentityOfItsScopedDocument()
	{
		WithMetadataFile( """{"other":true}""", ( path, metadata ) =>
		{
			MetaData.MissingProperty missing = null;
			using ( MetaData.CacheReads( metadata ) )
			{
				Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
				File.WriteAllText( path, """{"publish":{"Enabled":true}}""" );
				File.SetLastWriteTimeUtc( path, DateTime.UtcNow.AddSeconds( 1 ) );
				Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			}

			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );
		} );
	}

	[TestMethod]
	public void MissingPropertyCacheDoesNotSuppressANewFileOrAnotherKey()
	{
		WithMetadataFile( """{"other":{"Enabled":true}}""", ( path, metadata ) =>
		{
			MetaData.MissingProperty missing = null;
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "other", ref missing ).Enabled );
			File.Delete( path );
			Assert.IsNull( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ) );
			Assert.IsNull( missing );
			File.WriteAllText( path, """{"publish":{"Enabled":true}}""" );
			Assert.IsTrue( metadata.GetWithMissingPropertyCache<PublishFlag>( "publish", ref missing ).Enabled );
		} );
	}

	private static void WithMetadataFile( string json, Action<string, MetaData> test )
	{
		var path = Path.Combine( Environment.CurrentDirectory, ".source2", $"metadata_missing_{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText( path, json );
			test( path, new MetaData( path ) );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
