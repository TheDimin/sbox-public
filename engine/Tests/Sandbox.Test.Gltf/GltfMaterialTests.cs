using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;
using Vector4 = System.Numerics.Vector4;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfMaterialTests
{
	private static GltfDocument Read( string json, params string[] extensions ) =>
		GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string>( extensions, StringComparer.Ordinal ) );

	private static void AssertVector( Vector2 expected, Vector2 actual )
	{
		Assert.AreEqual( expected.X, actual.X, 0.00001f, "X" );
		Assert.AreEqual( expected.Y, actual.Y, 0.00001f, "Y" );
	}

	[TestMethod]
	public void DefaultMaterialAndSamplerUseGltfDefaults()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"materials":[{}],"samplers":[{}]}""" );
		var material = document.ReadMaterial( 0 );
		Assert.AreEqual( Vector4.One, material.BaseColorFactor );
		Assert.AreEqual( 1.0f, material.MetallicFactor );
		Assert.AreEqual( 1.0f, material.RoughnessFactor );
		Assert.AreEqual( Vector3.Zero, material.EmissiveFactor );
		Assert.AreEqual( "OPAQUE", material.AlphaMode );
		Assert.AreEqual( 0.5f, material.AlphaCutoff );
		Assert.IsFalse( material.DoubleSided );
		Assert.IsFalse( material.Unlit );
		Assert.IsNull( material.BaseColorTexture );
		Assert.IsNull( material.MetallicRoughnessTexture );
		Assert.IsNull( material.NormalTexture );
		Assert.IsNull( material.OcclusionTexture );
		Assert.IsNull( material.EmissiveTexture );

		var sampler = document.ReadSampler( 0 );
		Assert.AreEqual( 10497, sampler.WrapS );
		Assert.AreEqual( 10497, sampler.WrapT );
		Assert.IsNull( sampler.MinFilter );
		Assert.IsNull( sampler.MagFilter );
	}

	[TestMethod]
	public void MaterialFactorsAndRenderStateAreRetained()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"materials":[{
			  "pbrMetallicRoughness":{"baseColorFactor":[0.1,0.2,0.3,0.4],"metallicFactor":0.7,"roughnessFactor":0.2},
			  "emissiveFactor":[0.3,0.4,0.5],"alphaMode":"MASK","alphaCutoff":0.25,"doubleSided":true
			}]}
			""" );
		var material = document.ReadMaterial( 0 );
		Assert.AreEqual( new Vector4( 0.1f, 0.2f, 0.3f, 0.4f ), material.BaseColorFactor );
		Assert.AreEqual( 0.7f, material.MetallicFactor, 0.00001f );
		Assert.AreEqual( 0.2f, material.RoughnessFactor, 0.00001f );
		Assert.AreEqual( new Vector3( 0.3f, 0.4f, 0.5f ), material.EmissiveFactor );
		Assert.AreEqual( "MASK", material.AlphaMode );
		Assert.AreEqual( 0.25f, material.AlphaCutoff, 0.00001f );
		Assert.IsTrue( material.DoubleSided );
	}

	[TestMethod]
	public void TextureRolesRetainColorSpaceTextureSetAndChannelStrengths()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"images":[{"uri":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg=="}],"textures":[{"source":0},{"source":0},{"source":0},{"source":0},{"source":0}],"materials":[{
			  "pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":1},"metallicRoughnessTexture":{"index":1}},
			  "normalTexture":{"index":2,"scale":0.6},"occlusionTexture":{"index":3,"strength":0.25},"emissiveTexture":{"index":4}
			}]}
			""" );
		var material = document.ReadMaterial( 0 );
		var color = material.BaseColorTexture ?? throw new AssertFailedException( "Missing base color texture" );
		var metallic = material.MetallicRoughnessTexture ?? throw new AssertFailedException( "Missing metallic-roughness texture" );
		var normal = material.NormalTexture ?? throw new AssertFailedException( "Missing normal texture" );
		var occlusion = material.OcclusionTexture ?? throw new AssertFailedException( "Missing occlusion texture" );
		var emissive = material.EmissiveTexture ?? throw new AssertFailedException( "Missing emissive texture" );
		Assert.AreEqual( 0, color.Index );
		Assert.AreEqual( 1, color.TexCoord );
		Assert.IsTrue( color.IsSrgb );
		Assert.AreEqual( 1, metallic.Index );
		Assert.IsFalse( metallic.IsSrgb );
		Assert.AreEqual( 2, normal.Index );
		Assert.IsFalse( normal.IsSrgb );
		Assert.AreEqual( 0.6f, normal.Scale, 0.00001f );
		Assert.AreEqual( 3, occlusion.Index );
		Assert.IsFalse( occlusion.IsSrgb );
		Assert.AreEqual( 0.25f, occlusion.Strength, 0.00001f );
		Assert.AreEqual( 4, emissive.Index );
		Assert.IsTrue( emissive.IsSrgb );
	}

	[TestMethod]
	public void TextureTransformAppliesScaleThenRotationThenOffsetAndOverridesTexCoord()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],"images":[{"uri":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg=="}],"textures":[{"source":0}],"materials":[{
			  "pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":1,"extensions":{
			    "KHR_texture_transform":{"offset":[0.1,0.2],"scale":[2,3],"rotation":1.5707963267948966,"texCoord":3}
			  }}}
			}]}
			""", "KHR_texture_transform" );
		var texture = document.ReadMaterial( 0 ).BaseColorTexture ?? throw new AssertFailedException( "Missing texture" );
		Assert.AreEqual( 3, texture.TexCoord );
		AssertVector( new Vector2( 0.1f, 0.2f ), texture.Offset );
		AssertVector( new Vector2( 2, 3 ), texture.Tiling );
		Assert.AreEqual( MathF.PI / 2, texture.Rotation, 0.00001f );
		AssertVector( new Vector2( -1.4f, 0.7f ), texture.TransformUv( new Vector2( 0.25f, 0.5f ) ) );
	}

	[TestMethod]
	public void TextureTransformDefaultsToIdentity()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"images":[{"uri":"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg=="}],"textures":[{"source":0}],"materials":[{"normalTexture":{"index":0},"occlusionTexture":{"index":0}}]}""" );
		var material = document.ReadMaterial( 0 );
		var normal = material.NormalTexture ?? throw new AssertFailedException( "Missing normal texture" );
		var occlusion = material.OcclusionTexture ?? throw new AssertFailedException( "Missing occlusion texture" );
		Assert.AreEqual( 0, normal.TexCoord );
		Assert.AreEqual( 1.0f, normal.Scale );
		Assert.AreEqual( 1.0f, occlusion.Strength );
		AssertVector( Vector2.Zero, normal.Offset );
		AssertVector( Vector2.One, normal.Tiling );
		Assert.AreEqual( 0.0f, normal.Rotation );
		AssertVector( new Vector2( 0.25f, 0.75f ), normal.TransformUv( new Vector2( 0.25f, 0.75f ) ) );
	}

	[TestMethod]
	public void UnlitExtensionIsRecognizedWhenSupported()
	{
		using var document = Read( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],"materials":[{"extensions":{"KHR_materials_unlit":{}}}]}""", "KHR_materials_unlit" );
		Assert.IsTrue( document.ReadMaterial( 0 ).Unlit );
	}

	[TestMethod]
	public void VariantsResolvePerPrimitiveAndFallBackToItsDefaultMaterial()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
			 "extensions":{"KHR_materials_variants":{"variants":[{"name":"Red"},{"name":"Blue"}]}},
			 "materials":[{},{},{}],"meshes":[{"primitives":[
			   {"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[1]}]}}},
			   {"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":2,"variants":[0]}]}}}
			 ]}],"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}]}
			""", "KHR_materials_variants" );
		Assert.AreEqual( 0, document.GetMaterialForVariant( 0, 0, null ) );
		Assert.AreEqual( 1, document.GetMaterialForVariant( 0, 0, 0 ) );
		Assert.AreEqual( 2, document.GetMaterialForVariant( 0, 0, 1 ) );
		Assert.AreEqual( 2, document.GetMaterialForVariant( 0, 1, 0 ) );
		Assert.AreEqual( 0, document.GetMaterialForVariant( 0, 1, 1 ) );
	}
}
