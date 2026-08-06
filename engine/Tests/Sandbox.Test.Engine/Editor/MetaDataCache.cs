using System.IO;

namespace EditorTests;

public partial class MetaDataTest
{
	[TestMethod]
	public void CacheInvalidatesWhenFileChanges()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", $"metadata_external_{System.Guid.NewGuid():N}.json" );

		try
		{
			File.WriteAllText( fn, """{"value":"first"}""" );
			var md = new Editor.MetaData( fn );
			Assert.AreEqual( "first", md.GetString( "value" ) );

			File.WriteAllText( fn, """{"value":"second-value"}""" );
			File.SetLastWriteTimeUtc( fn, System.DateTime.UtcNow.AddSeconds( 1 ) );

			Assert.AreEqual( "second-value", md.GetString( "value" ) );
		}
		finally
		{
			File.Delete( fn );
		}
	}

	[TestMethod]
	public void CacheInvalidatesWhenFileIsDeleted()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", $"metadata_deleted_{System.Guid.NewGuid():N}.json" );

		try
		{
			File.WriteAllText( fn, """{"value":"present"}""" );
			var md = new Editor.MetaData( fn );
			Assert.AreEqual( "present", md.GetString( "value" ) );

			File.Delete( fn );

			Assert.IsNull( md.GetString( "value" ) );
		}
		finally
		{
			File.Delete( fn );
		}
	}

	[TestMethod]
	public void SetInvalidatesCachedDocument()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", $"metadata_set_{System.Guid.NewGuid():N}.json" );

		try
		{
			File.WriteAllText( fn, """{"value":"old"}""" );
			var md = new Editor.MetaData( fn );
			Assert.AreEqual( "old", md.GetString( "value" ) );

			md.Set( "value", "written" );

			Assert.AreEqual( "written", md.GetString( "value" ) );
			Assert.AreEqual( "written", md.GetString( "value" ) );
		}
		finally
		{
			File.Delete( fn );
		}
	}

	[TestMethod]
	public void LegacyScopeBypassesCachedMetadata()
	{
		var fn = Path.Combine( System.Environment.CurrentDirectory, ".source2", $"metadata_legacy_{System.Guid.NewGuid():N}.json" );

		try
		{
			File.WriteAllText( fn, """{"value":"first"}""" );
			var originalWriteTime = File.GetLastWriteTimeUtc( fn );
			var md = new Editor.MetaData( fn );
			Assert.AreEqual( "first", md.GetString( "value" ) );

			// Keep the cache identity unchanged to prove that ForceLegacy does not consult it.
			File.WriteAllText( fn, """{"value":"other"}""" );
			File.SetLastWriteTimeUtc( fn, originalWriteTime );
			Assert.AreEqual( "first", md.GetString( "value" ) );

			using ( Editor.AssetPipelineCompatibility.ForceLegacy() )
			{
				Assert.AreEqual( "other", md.GetString( "value" ) );
			}
		}
		finally
		{
			File.Delete( fn );
		}
	}

	[TestMethod]
	public void ParsedMetadataCacheHasFixedLimits()
	{
		var directory = Path.Combine(
			System.Environment.CurrentDirectory,
			".source2",
			$"metadata_cache_limit_{System.Guid.NewGuid():N}" );

		Directory.CreateDirectory( directory );

		try
		{
			for ( var i = 0; i < Editor.MetaDataDocumentCache.MaximumEntries + 64; i++ )
			{
				var fn = Path.Combine( directory, $"{i}.json" );
				File.WriteAllText( fn, $$"""{"value":{{i}}}""" );
				var md = new Editor.MetaData( fn );
				Assert.AreEqual( i, md.GetInt( "value" ) );
			}

			Assert.IsTrue(
				Editor.MetaDataDocumentCache.Count <= Editor.MetaDataDocumentCache.MaximumEntries,
				$"Cache retained {Editor.MetaDataDocumentCache.Count} entries." );
			Assert.IsTrue(
				Editor.MetaDataDocumentCache.RetainedSourceBytes <= Editor.MetaDataDocumentCache.MaximumSourceBytes,
				$"Cache retained {Editor.MetaDataDocumentCache.RetainedSourceBytes} source bytes." );
		}
		finally
		{
			Directory.Delete( directory, recursive: true );
		}
	}
}
