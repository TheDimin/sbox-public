using System;
using System.Collections.Generic;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfStockSourceMaterialTests
{
	// Independent literal RGB(0,136,255), with deliberately non-sRGB gAMA=0.2.
	private const string GammaPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";

	[TestMethod]
	public void SourceNormalMapKeepsLinearTextureAndAuthoredScale()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"textures":[{"source":0}],"materials":[{"normalTexture":{"index":0,"scale":0.25}}]}""" ), _ => Convert.FromBase64String( GammaPng ) );
		var prepared = GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) );
		Assert.AreEqual( .25f, prepared.NormalScale );
		var normal = prepared.Resources[GltfMaterialTextureRole.Normal];
		Assert.IsFalse( normal.Encoding.IsSrgb );
		Assert.AreEqual( GltfMaterialTextureChannels.Rgb, normal.Encoding.Channels );
		Assert.IsTrue( normal.Resource.Length > 16 );
	}

	[TestMethod]
	public void SourceNormalAndColorRolesDoNotShareDifferentTransferFunctions()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}},"normalTexture":{"index":0,"scale":-0.5}}]}""" ), _ => Convert.FromBase64String( GammaPng ) );
		var prepared = GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) );
		Assert.AreEqual( -.5f, prepared.NormalScale );
		Assert.IsTrue( prepared.Resources[GltfMaterialTextureRole.BaseColor].Encoding.IsSrgb );
		Assert.IsFalse( prepared.Resources[GltfMaterialTextureRole.Normal].Encoding.IsSrgb );
		Assert.AreNotSame( prepared.Resources[GltfMaterialTextureRole.BaseColor].Resource, prepared.Resources[GltfMaterialTextureRole.Normal].Resource );
	}

	[TestMethod]
	public void SourceNormalMapRequiresAnImageBeforeNativeAcquisition()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"textures":[{}],"materials":[{"normalTexture":{"index":0}}]}""" ) );
		var error = Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) ) );
		StringAssert.Contains( error.Message, "Normal" );
	}

	[TestMethod]
	public void SourcePreparationUsesRetainedImageReaderOnceForSharedImage()
	{
		var resolverReads = 0;
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}},"normalTexture":{"index":0}}]}""" ), _ => { resolverReads++; return Convert.FromBase64String( GammaPng ); } );
		var snapshot = doc.ReadImage( 0 );
		var retainedReads = 0;
		var prepared = GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ), index => { Assert.AreEqual( 0, index ); retainedReads++; return snapshot; } );
		Assert.AreEqual( 1, resolverReads ); Assert.AreEqual( 1, retainedReads );
		Assert.AreEqual( 2, prepared.Resources.Count );
	}

	[TestMethod]
	public void CachedImageStillValidatesEveryTextureSourceContract()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"images":[{"uri":"literal.png"}],"textures":[{"source":0},{"source":0,"extensions":{"KHR_texture_basisu":{"source":0}}}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}},"emissiveTexture":{"index":1}}]}""" ), _ => Convert.FromBase64String( GammaPng ), supportedExtensions: new HashSet<string> { "KHR_texture_basisu" } );
		var error = Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) ) );
		StringAssert.Contains( error.Message, "format" );
	}

	[TestMethod]
	public void DirectSourcePreparationSharesMatchingRolesWithoutBakingAuthoredFactors()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"baseColorTexture":{"index":0},"metallicRoughnessTexture":{"index":0},"metallicFactor":0.001,"roughnessFactor":0.7},"occlusionTexture":{"index":0,"strength":0.6},"emissiveTexture":{"index":0},"emissiveFactor":[0.25,0.5,0.75]}]}""" ), _ => Convert.FromBase64String( GammaPng ) );
		var prepared = GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) );
		Assert.AreEqual( new System.Numerics.Vector4( .2f, .3f, .4f, .5f ), prepared.LinearBaseColorFactor );
		Assert.AreEqual( new System.Numerics.Vector3( .25f, .5f, .75f ), prepared.EmissiveFactor );
		Assert.AreEqual( .001f, prepared.MetallicFactor ); Assert.AreEqual( .7f, prepared.RoughnessFactor ); Assert.AreEqual( .6f, prepared.OcclusionStrength );
		Assert.AreSame( prepared.Resources[GltfMaterialTextureRole.BaseColor].Resource, prepared.Resources[GltfMaterialTextureRole.Emissive].Resource );
		Assert.AreSame( prepared.Resources[GltfMaterialTextureRole.MetallicRoughness].Resource, prepared.Resources[GltfMaterialTextureRole.Occlusion].Resource );
		Assert.AreNotSame( prepared.Resources[GltfMaterialTextureRole.BaseColor].Resource, prepared.Resources[GltfMaterialTextureRole.MetallicRoughness].Resource );
	}

	[TestMethod]
	public void MissingSupportedSourceFailsBeforeAnyNativeAcquisition()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"textures":[{}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}]}""" ) );
		var error = Assert.ThrowsException<GltfException>( () => GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) ) );
		StringAssert.Contains( error.Message, "BaseColor" ); StringAssert.Contains( error.Message, "image" );
	}

	[TestMethod]
	public void SourcePreparationHasNoTextureWorkForFactorOnlyMaterial()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"unused.png"}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"metallicFactor":0.1,"roughnessFactor":0.9}}]}""" ), _ => throw new InvalidOperationException( "Unused source image must not be opened." ) );
		var prepared = GltfStockMaterialAdapter.PrepareSource( doc, doc.ReadMaterial( 0 ) );
		Assert.AreEqual( 0, prepared.Resources.Count ); Assert.AreEqual( .1f, prepared.MetallicFactor ); Assert.AreEqual( .9f, prepared.RoughnessFactor );
	}
}
