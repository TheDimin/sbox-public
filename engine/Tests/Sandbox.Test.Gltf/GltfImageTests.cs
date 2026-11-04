using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfImageTests
{
	// A generated opaque white 1x1 RGBA PNG, including correct chunk CRCs.
	private const string WhitePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";

	[TestMethod]
	public void DataUriImagePreservesEncodedPayloadAndMimeType()
	{
		using var document = Read( $"[{{\"uri\":\"data:image/png;base64,{WhitePng}\"}}]" );
		var image = document.ReadImage( 0 );
		Assert.AreEqual( "image/png", image.MimeType );
		CollectionAssert.AreEqual( Convert.FromBase64String( WhitePng ), image.Bytes );
	}

	[TestMethod]
	public void ExternalImageResolvesIndependentlyOfGeometry()
	{
		var requested = new List<string>();
		using var document = Read( """[{"uri":"textures/paint.png"}]""", resolve: uri =>
		{
			requested.Add( uri );
			return Convert.FromBase64String( WhitePng );
		} );
		Assert.AreEqual( 0, requested.Count, "Image reads must be deferred until requested." );
		var image = document.ReadImage( 0 );
		CollectionAssert.AreEqual( new[] { "textures/paint.png" }, requested );
		Assert.AreEqual( "image/png", image.MimeType );
		CollectionAssert.AreEqual( Convert.FromBase64String( WhitePng ), image.Bytes );
	}

	[TestMethod]
	public void BufferViewImageReadsOnlyItsDeclaredSlice()
	{
		var png = Convert.FromBase64String( WhitePng );
		var bytes = new byte[png.Length + 8];
		png.CopyTo( bytes, 4 );
		using var document = Read( """[{"bufferView":0,"mimeType":"image/png"}]""",
			$",\"buffers\":[{{\"uri\":\"data:application/octet-stream;base64,{Convert.ToBase64String( bytes )}\",\"byteLength\":{bytes.Length}}}],\"bufferViews\":[{{\"buffer\":0,\"byteOffset\":4,\"byteLength\":{png.Length}}}]" );
		var image = document.ReadImage( 0 );
		Assert.AreEqual( "image/png", image.MimeType );
		CollectionAssert.AreEqual( png, image.Bytes );
	}

	[TestMethod]
	[DataRow( "[{\"uri\":\"missing.png\"}]" )]
	[DataRow( "[{\"uri\":\"data:image/png;base64,%%%\"}]" )]
	[DataRow( "[{\"bufferView\":0}]" )]
	[DataRow( "[{\"bufferView\":9,\"mimeType\":\"image/png\"}]" )]
	public void MissingOrMalformedImageResourcesFailClearly( string images )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( images );
			_ = document.ReadImage( 0 );
		} );
	}

	[TestMethod]
	public void TextureRetainsIndependentImageAndSamplerReferences()
	{
		using var document = Read( """[{"uri":"a.png"},{"uri":"b.png"}]""",
			""","samplers":[{}],"textures":[{"source":1,"sampler":0},{"source":0},{}]""" );
		Assert.AreEqual( 1, document.ReadTexture( 0 ).Image );
		Assert.AreEqual( 0, document.ReadTexture( 0 ).Sampler );
		Assert.AreEqual( 0, document.ReadTexture( 1 ).Image );
		Assert.IsNull( document.ReadTexture( 1 ).Sampler );
		Assert.IsNull( document.ReadTexture( 2 ).Image, "Core permits application-supplied images." );
	}

	[TestMethod]
	public void OptionalBasisuWithoutSupportUsesItsCoreImageFallback()
	{
		using var document = Read( """[{"uri":"fallback.png"},{"uri":"texture.ktx2","mimeType":"image/ktx2"}]""",
			""","extensionsUsed":["KHR_texture_basisu"],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}]""" );
		Assert.AreEqual( 0, document.ReadTexture( 0 ).Image );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), "KHR_texture_basisu" );
	}

	[TestMethod]
	public void SelectedBasisuUsesTheExtensionImage()
	{
		using var document = Read( """[{"uri":"fallback.png"},{"uri":"texture.ktx2","mimeType":"image/ktx2"}]""",
			""","extensionsUsed":["KHR_texture_basisu"],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}]""",
			supported: new HashSet<string> { "KHR_texture_basisu" } );
		Assert.AreEqual( 1, document.ReadTexture( 0 ).Image );
		Assert.AreEqual( 0, document.UnsupportedOptionalExtensions.Count );
		// This verifies source selection only. KTX2 decode/transcode needs separate codec and GPU tests.
	}

	private static GltfDocument Read( string images, string extraRoot = "", Func<string, byte[]> resolve = null, IReadOnlySet<string> supported = null )
		=> GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"images\":" + images + extraRoot + "}" ), resolve, supported );
}
