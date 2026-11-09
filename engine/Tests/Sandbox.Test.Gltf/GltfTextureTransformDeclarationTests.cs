using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>Selected texture transforms must be declared without changing optional fallback.</summary>
[TestClass]
public class GltfTextureTransformDeclarationTests
{
	private const string Extension = "KHR_texture_transform";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SelectedTransformMustAppearInExtensionsUsed( bool anotherExtensionIsDeclared )
	{
		using var document = Read( declared: false, supports: true, anotherExtensionIsDeclared );
		var error = Assert.ThrowsException<GltfException>( () => document.ReadMaterial( 0 ) );
		StringAssert.Contains( error.Message, Extension );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DeclaredSelectedTransformUsesOverrideAndSpecifiedOrder( bool required )
	{
		using var document = Read( declared: true, supports: true, anotherExtensionIsDeclared: false, required );
		var texture = document.ReadMaterial( 0 ).BaseColorTexture;
		Assert.AreEqual( 1, texture.TexCoord );
		// scale (0.25,0.5) by (2,3), rotate pi/2 CCW, then translate (0.1,0.2).
		var transformed = texture.TransformUv( new Vector2( .25f, .5f ) );
		Assert.AreEqual( -1.4f, transformed.X, 1e-5f );
		Assert.AreEqual( .7f, transformed.Y, 1e-5f );
	}

	[TestMethod]
	public void UnsupportedOptionalTransformKeepsCoreTextureCoordinates()
	{
		using var document = Read( declared: true, supports: false, anotherExtensionIsDeclared: false );
		var texture = document.ReadMaterial( 0 ).BaseColorTexture;
		Assert.AreEqual( 0, texture.TexCoord );
		Assert.AreEqual( new Vector2( .25f, .5f ), texture.TransformUv( new Vector2( .25f, .5f ) ) );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), Extension );
	}

	[TestMethod]
	public void RequiredUnsupportedTransformIsRejected()
	{
		var error = Assert.ThrowsException<GltfException>( () => { using var ignored = Read( declared: true, supports: false, anotherExtensionIsDeclared: false, required: true ); } );
		StringAssert.Contains( error.Message, Extension );
	}

	private static GltfDocument Read( bool declared, bool supports, bool anotherExtensionIsDeclared, bool required = false )
	{
		var used = declared ? "\"extensionsUsed\":[\"KHR_texture_transform\"]," :
			anotherExtensionIsDeclared ? "\"extensionsUsed\":[\"EXT_unrelated\"]," : "";
		var requiredField = required ? "\"extensionsRequired\":[\"KHR_texture_transform\"]," : "";
		var json = $$$$$$"""
			{"asset":{"version":"2.0"},{{{{{{used}}}}}} {{{{{{requiredField}}}}}} "textures":[{}],
			 "materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":0,
			 "extensions":{"KHR_texture_transform":{"offset":[0.1,0.2],"scale":[2,3],"rotation":1.5707963267948966,"texCoord":1}}}}}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supports ? new HashSet<string>( StringComparer.Ordinal ) { Extension } : null );
	}
}
