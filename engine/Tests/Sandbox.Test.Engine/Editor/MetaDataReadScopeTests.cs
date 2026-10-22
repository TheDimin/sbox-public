using System;
using System.IO;
using Editor;

namespace EditorTests;

public partial class MetaDataTest
{
	[TestMethod]
	public void ScopedReadsObserveWritesThroughNestedFilesAndOtherInstances()
	{
		var firstPath = Path.Combine( Path.GetTempPath(), $"metadata-scope-{Guid.NewGuid():N}.json" );
		var secondPath = Path.Combine( Path.GetTempPath(), $"metadata-scope-{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText( firstPath, """{"value":"old"}""" );
			File.WriteAllText( secondPath, """{"value":"other"}""" );
			var first = new MetaData( firstPath );
			var second = new MetaData( secondPath );
			using ( MetaData.CacheReads( first ) )
			{
				Assert.AreEqual( "old", first.GetString( "value" ) );
				using ( MetaData.CacheReads( second ) )
				{
					new MetaData( firstPath ).Set( "value", "changed" );
				}
				Assert.AreEqual( "changed", first.GetString( "value" ) );
				first.Set( "value", "latest" );
				Assert.AreEqual( "latest", first.GetString( "value" ) );
			}
			File.WriteAllText( firstPath, """{"value":"external-change"}""" );
			File.SetLastWriteTimeUtc( firstPath, DateTime.UtcNow.AddSeconds( 2 ) );
			using ( MetaData.CacheReads( first ) )
			{
				Assert.AreEqual( "external-change", first.GetString( "value" ) );
			}
		}
		finally
		{
			File.Delete( firstPath );
			File.Delete( secondPath );
		}
	}

	[TestMethod]
	public void LegacyReadsBypassAnActiveReadScope()
	{
		var path = Path.Combine( Path.GetTempPath(), $"metadata-scope-{Guid.NewGuid():N}.json" );
		try
		{
			File.WriteAllText( path, """{"value":"first"}""" );
			var writeTime = File.GetLastWriteTimeUtc( path );
			var meta = new MetaData( path );
			using var scope = MetaData.CacheReads( meta );
			Assert.AreEqual( "first", meta.GetString( "value" ) );
			File.WriteAllText( path, """{"value":"other"}""" );
			File.SetLastWriteTimeUtc( path, writeTime );
			using ( AssetPipelineCompatibility.ForceLegacy() )
			{
				Assert.AreEqual( "other", meta.GetString( "value" ) );
			}
			Assert.AreEqual( "other", meta.GetString( "value" ) );
		}
		finally
		{
			File.Delete( path );
		}
	}
}
