using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfWebpSourceTests
{
	private const string Extension = "EXT_texture_webp";
	private const string Webp = "UklGRiQAAABXRUJQVlA4TBcAAAAvAQAAEA9wEaMz9B7n+4H/gYcKRPQ/AAA=";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OptionalWebpSelectsExtensionOrCoreFallback( bool supports )
	{
		using var document = Open( fallback: true, required: false, supports );
		Assert.AreEqual( supports ? 1 : 0, document.ReadTexture( 0 ).Image );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void WebpWithoutCoreFallbackMustBeRequired( bool supports )
	{
		using var document = Open( fallback: false, required: false, supports );
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => document.ReadTexture( 0 ) ).Message, Extension );
	}

	[TestMethod]
	public void RequiredWebpNeedsCapabilityAndSelectsExtensionImage()
	{
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => { using var _ = Open( false, true, false ); } ).Message, Extension );
		using var document = Open( false, true, true );
		Assert.AreEqual( 1, document.ReadTexture( 0 ).Image );
	}

	[TestMethod]
	public void WebpMimeIsDetectedWithoutAnExplicitTypeAndMismatchIsRejected()
	{
		var bytes = Convert.FromBase64String( Webp );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"a.webp"},{"uri":"a.webp","mimeType":"image/png"}]}""" ), _ => bytes );
		Assert.AreEqual( "image/webp", document.ReadImage( 0 ).MimeType );
		Assert.ThrowsException<GltfException>( () => document.ReadImage( 1 ) );
	}

	[TestMethod]
	public void BufferViewWebpRetainsBytesAndDeclaredMime()
	{
		var bytes = Convert.FromBase64String( Webp );
		var json = $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Webp}}}","byteLength":{{{bytes.Length}}} }],
			 "bufferViews":[{"buffer":0,"byteLength":{{{bytes.Length}}} }],"images":[{"bufferView":0,"mimeType":"image/webp"}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		var image = document.ReadImage( 0 );
		Assert.AreEqual( "image/webp", image.MimeType );
		CollectionAssert.AreEqual( bytes, image.Bytes );
	}

	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "{\"source\":\"1\"}" )]
	[DataRow( "{\"source\":-1}" )]
	[DataRow( "{\"source\":2}" )]
	public void MalformedSelectedWebpSourcesFailClearly( string extension )
	{
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_texture_webp"],
			 "images":[{"uri":"fallback.png"},{"uri":"texture.webp"}],
			 "textures":[{"source":0,"extensions":{"EXT_texture_webp":{{{extension}}} }}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { Extension } );
		Assert.ThrowsException<GltfException>( () => document.ReadTexture( 0 ) );
	}

	[TestMethod]
	public void TextureExtensionMustBeDeclaredUsed()
	{
		const string json = """{"asset":{"version":"2.0"},"images":[{"uri":"a.png"},{"uri":"b.webp"}],"textures":[{"source":0,"extensions":{"EXT_texture_webp":{"source":1}}}]}""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { Extension } );
		StringAssert.Contains( Assert.ThrowsException<GltfException>( () => document.ReadTexture( 0 ) ).Message, "extensionsUsed" );
	}

	[TestMethod]
	public void SelectedImageValidationUsesEncodedFormatAndFallbackCapability()
	{
		using var selected = Open( fallback: true, required: false, supports: true );
		var webp = new GltfImage { MimeType = "image/webp", Bytes = Convert.FromBase64String( Webp ) };
		selected.ValidateTextureImage( 0, webp );
		Assert.ThrowsException<GltfException>( () => selected.ValidateTextureImage( 0, new GltfImage { MimeType = "image/png", Bytes = Convert.FromBase64String( Webp ) } ) );
		Assert.ThrowsException<GltfException>( () => selected.ValidateTextureImage( 0, new GltfImage { MimeType = "image/webp", Bytes = [1, 2, 3] } ) );
		using var fallback = Open( fallback: true, required: false, supports: false );
		Assert.ThrowsException<GltfException>( () => fallback.ValidateTextureImage( 0, webp ) );
		var png = new GltfImage { MimeType = "image/png", Bytes = Convert.FromBase64String( "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==" ) };
		fallback.ValidateTextureImage( 0, png );
		Assert.ThrowsException<GltfException>( () => selected.ValidateTextureImage( 0, png ) );
	}

	[TestMethod]
	public void SelectedBasisImageCannotBeReplacedWithWebp()
	{
		const string json = """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"images":[{"uri":"a.png"},{"uri":"b.ktx2"}],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}]}""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_texture_basisu" } );
		Assert.ThrowsException<GltfException>( () => document.ValidateTextureImage( 0, new GltfImage { MimeType = "image/webp", Bytes = Convert.FromBase64String( Webp ) } ) );
	}

	[TestMethod]
	[DataRow( false, false, 0 )]
	[DataRow( false, true, 1 )]
	[DataRow( true, false, 2 )]
	[DataRow( true, true, 2 )]
	public void AlternativeFormatsUseSelectedCapabilityWithDeterministicPriority( bool basis, bool webp, int expected )
	{
		const string json = """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu","EXT_texture_webp"],"images":[{"uri":"a.png"},{"uri":"b.webp"},{"uri":"c.ktx2"}],"textures":[{"source":0,"extensions":{"EXT_texture_webp":{"source":1},"KHR_texture_basisu":{"source":2}}}]}""";
		var supported = new HashSet<string>( StringComparer.Ordinal );
		if ( basis ) supported.Add( "KHR_texture_basisu" );
		if ( webp ) supported.Add( Extension );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supported );
		Assert.AreEqual( expected, document.ReadTexture( 0 ).Image );
	}

	private static GltfDocument Open( bool fallback, bool required, bool supports )
	{
		var requiredField = required ? ",\"extensionsRequired\":[\"EXT_texture_webp\"]" : "";
		var sourceField = fallback ? "\"source\":0," : "";
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_texture_webp"]{{{requiredField}}},
			 "images":[{"uri":"fallback.png"},{"uri":"texture.webp"}],
			 "textures":[{ {{{sourceField}}}"extensions":{"EXT_texture_webp":{"source":1}} }]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { Extension } : null );
	}
}
