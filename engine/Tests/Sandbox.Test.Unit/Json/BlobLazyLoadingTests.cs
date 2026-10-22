using System;
using System.IO;
using System.Text.Json.Nodes;

namespace JsonTests;

[TestClass]
[DoNotParallelize]
public class BlobLazyLoadingTests
{
	private sealed class PlainData
	{
		public int Value { get; set; }
	}

	[TestMethod]
	public void BlobFreeJsonDoesNotOpenTheSidecar()
	{
		var path = Path.Combine( Path.GetTempPath(), $"blob-lazy-{Guid.NewGuid():N}" );
		try
		{
			// An eager sidecar read fails while this handle excludes readers.
			using var locked = new FileStream( path + "_d", FileMode.Create, FileAccess.ReadWrite, FileShare.None );
			using var context = BlobDataSerializer.LoadFrom( path );
			var value = Json.FromNode( new JsonObject { ["Value"] = 42 }, typeof( PlainData ) ) as PlainData;
			Assert.AreEqual( 42, value?.Value );
		}
		finally
		{
			File.Delete( path + "_d" );
		}
	}

	[TestMethod]
	public void EscapedBlobPropertyStillLoadsSidecarData()
	{
		var path = Path.Combine( Path.GetTempPath(), $"blob-lazy-{Guid.NewGuid():N}" );
		try
		{
			var original = new TestBlob { IntValue = 42, StringValue = "escaped" };
			string json;
			using ( var capture = BlobDataSerializer.Capture() )
			{
				json = Json.ToNode( original ).ToJsonString().Replace( "$blob", "\\u0024blob", StringComparison.Ordinal );
				Assert.IsTrue( capture.SaveTo( path ) );
			}
			using var context = BlobDataSerializer.LoadFrom( path );
			var loaded = Json.FromNode( JsonNode.Parse( json ), typeof( TestBlob ) ) as TestBlob;
			Assert.AreEqual( 42, loaded?.IntValue );
			Assert.AreEqual( "escaped", loaded?.StringValue );
		}
		finally
		{
			File.Delete( path + "_d" );
		}
	}
}
