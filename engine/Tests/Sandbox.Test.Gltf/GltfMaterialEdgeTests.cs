using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Vector2 = System.Numerics.Vector2;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfMaterialEdgeTests
{
	private static GltfDocument Read( string json, params string[] extensions ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( extensions, StringComparer.Ordinal ) );

	[TestMethod]
	public void UnsupportedOptionalUnlitUsesCoreMaterial()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],
			 "materials":[{"pbrMetallicRoughness":{"metallicFactor":0.25},"extensions":{"KHR_materials_unlit":{}}}]}
			""" );
		var material = document.ReadMaterial( 0 );
		Assert.IsFalse( material.Unlit );
		Assert.AreEqual( 0.25f, material.MetallicFactor );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), "KHR_materials_unlit" );
	}

	[TestMethod]
	public void UnsupportedOptionalVariantsUsePrimitiveDefault()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
			 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"}]}},"materials":[{},{}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{
			 "mappings":[{"material":1,"variants":[0]}]}}}]}]}
			""" );
		Assert.AreEqual( 0, document.GetMaterialForVariant( 0, 0, 0 ) );
		Assert.AreEqual( 0, document.GetMaterialForVariant( 0, 0, null ) );
		CollectionAssert.Contains( document.UnsupportedOptionalExtensions.ToArray(), "KHR_materials_variants" );
	}

	[TestMethod]
	public void LaterDuplicateVariantMappingIsNotHiddenByEarlierMatch()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """
				{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
				 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"},{"name":"Blue"}]}},"materials":[{},{}],
				 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
				 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"extensions":{"KHR_materials_variants":{"mappings":[
				 {"material":0,"variants":[0]},{"material":1,"variants":[1]},{"material":1,"variants":[1]}]}}}]}]}
				""", "KHR_materials_variants" );
			_ = document.GetMaterialForVariant( 0, 0, 0 );
		} );
	}

	[TestMethod]
	[DataRow( -1 )]
	[DataRow( 1 )]
	public void SelectedVariantIndexMustExist( int variant )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """
				{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
				 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"}]}},
				 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
				 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
				""", "KHR_materials_variants" );
			_ = document.GetMaterialForVariant( 0, 0, variant );
		} );
	}

	[TestMethod]
	[DataRow( "{\"index\":1}" )]
	[DataRow( "{\"index\":-1}" )]
	[DataRow( "{}" )]
	[DataRow( "1" )]
	public void InvalidTextureReferenceThrowsGltfException( string textureInfo )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"textures\":[{}],\"materials\":[{\"normalTexture\":" + textureInfo + "}]}" );
			_ = document.ReadMaterial( 0 );
		} );
	}

	[TestMethod]
	[DataRow( "{\"index\":0,\"strength\":-0.1}" )]
	[DataRow( "{\"index\":0,\"strength\":1.1}" )]
	[DataRow( "{\"index\":0,\"strength\":\"bad\"}" )]
	public void InvalidOcclusionStrengthThrowsGltfException( string textureInfo )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"textures\":[{}],\"materials\":[{\"occlusionTexture\":" + textureInfo + "}]}" );
			_ = document.ReadMaterial( 0 );
		} );
	}

	[TestMethod]
	public void NegativeNormalScaleIsPreserved()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"textures":[{}],"materials":[{"normalTexture":{"index":0,"scale":-2}}]}""" );
		Assert.AreEqual( -2f, document.ReadMaterial( 0 ).NormalTexture.Scale );
	}

	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "[]" )]
	[DataRow( "{\"pbrMetallicRoughness\":null}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1]}}" )]
	[DataRow( "{\"doubleSided\":0}" )]
	[DataRow( "{\"alphaMode\":\"UNKNOWN\"}" )]
	public void MalformedMaterialObjectsThrowGltfException( string material )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"materials\":[" + material + "]}" );
			_ = document.ReadMaterial( 0 );
		} );
	}

	[TestMethod]
	public void InvalidMaterialReferenceThrowsGltfException()
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( """{"asset":{"version":"2.0"},"materials":[{}],"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":1}]}]}""" );
			_ = document.GetMaterialForVariant( 0, 0, null );
		} );
	}

	[TestMethod]
	[DataRow( "{\"material\":1,\"variants\":[0]}" )]
	[DataRow( "{\"material\":0,\"variants\":[1]}" )]
	[DataRow( "{\"material\":0,\"variants\":null}" )]
	[DataRow( "true" )]
	public void InvalidVariantMappingThrowsGltfException( string mapping )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_materials_variants\"]," +
				"\"extensions\":{\"KHR_materials_variants\":{\"variants\":[{\"name\":\"Red\"}]}},\"materials\":[{}]," +
				"\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}]," +
				"\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"extensions\":{\"KHR_materials_variants\":{\"mappings\":[" + mapping + "]}}}]}]}",
				"KHR_materials_variants" );
			_ = document.GetMaterialForVariant( 0, 0, 0 );
		} );
	}

	[TestMethod]
	[DataRow( "null" )]
	[DataRow( "\"bad\"" )]
	public void InvalidNormalScaleThrowsGltfException( string scale )
	{
		Assert.ThrowsException<GltfException>( () =>
		{
			using var document = Read( "{\"asset\":{\"version\":\"2.0\"},\"textures\":[{}],\"materials\":[{\"normalTexture\":{\"index\":0,\"scale\":" + scale + "}}]}" );
			_ = document.ReadMaterial( 0 );
		} );
	}

	[TestMethod]
	public void OptionalTextureTransformDoesNotAlterCoreTexCoordWhenUnsupported()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],"textures":[{}],
			 "materials":[{"normalTexture":{"index":0,"texCoord":1,"extensions":{
			 "KHR_texture_transform":{"offset":[0.2,0.3],"scale":[2,2],"texCoord":3}}}}]}
			""" );
		var texture = document.ReadMaterial( 0 ).NormalTexture;
		Assert.AreEqual( 1, texture.TexCoord );
		Assert.AreEqual( new Vector2( 0.25f, 0.5f ), texture.TransformUv( new Vector2( 0.25f, 0.5f ) ) );
	}

	[TestMethod]
	public void InvalidJsonThrowsGltfException()
	{
		Assert.ThrowsException<GltfException>( () => Read( "{\"asset\":" ) );
	}
}
