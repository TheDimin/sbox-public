using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>KHR_texture_basisu fallback selection is independent of native image decoding.</summary>
[TestClass]
public class GltfBasisFallbackTests
{
	private const string Extension = "KHR_texture_basisu";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void OptionalBasisUsesSelectedImageOrCoreFallback( bool supports )
	{
		using var document = Open( fallback: true, required: false, supports );
		Assert.AreEqual( supports ? 1 : 0, document.ReadTexture( 0 ).Image );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void BasisWithoutCoreFallbackMustBeRequiredEvenWhenSupported( bool supports )
	{
		using var document = Open( fallback: false, required: false, supports );
		var error = Assert.ThrowsException<GltfException>( () => document.ReadTexture( 0 ) );
		StringAssert.Contains( error.Message, Extension );
	}

	[TestMethod]
	public void RequiredBasisWithoutFallbackUsesBasisImageWhenSupported()
	{
		using var document = Open( fallback: false, required: true, supports: true );
		Assert.AreEqual( 1, document.ReadTexture( 0 ).Image );
	}

	[TestMethod]
	public void RequiredBasisWithoutCapabilityIsRejected()
	{
		var error = Assert.ThrowsException<GltfException>( () => { using var ignored = Open( fallback: false, required: true, supports: false ); } );
		StringAssert.Contains( error.Message, Extension );
	}

	private static GltfDocument Open( bool fallback, bool required, bool supports )
	{
		var requiredField = required ? ",\"extensionsRequired\":[\"KHR_texture_basisu\"]" : "";
		var sourceField = fallback ? "\"source\":0," : "";
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"]{{{requiredField}}},
			 "images":[{"uri":"fallback.png"},{"uri":"compressed.ktx2"}],
			 "textures":[{ {{{sourceField}}}"extensions":{"KHR_texture_basisu":{"source":1}} }]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { Extension } : null );
	}
}
