using System;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfMaterialTextureEncodingPlanTests
{
	private static GltfDocument Document( string textures = "[{\"source\":0}]", string samplers = "[]" ) => GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"textures\":" + textures + ",\"samplers\":" + samplers + ",\"images\":[{\"uri\":\"must-not-be-read.png\"}]}" ), _ => throw new InvalidOperationException( "Encoding metadata planning must not resolve image bytes." ) );
	private static GltfTextureInfo Info( bool srgb, int texture = 0 ) => new() { Index = texture, IsSrgb = srgb };

	[TestMethod]
	public void EachCoreRoleHasItsOwnTransferChannelAndAlphaPolicyForOneSharedImage()
	{
		using var doc = Document();
		var plan = GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ), MetallicRoughnessTexture = Info( false ), NormalTexture = Info( false ), OcclusionTexture = Info( false ), EmissiveTexture = Info( true ) } );
		Assert.AreEqual( 5, plan.Bindings.Count );
		foreach ( var binding in plan.Bindings.Values )
		{
			Assert.AreEqual( 0, binding.TextureIndex ); Assert.AreEqual( 0, binding.ImageIndex ); Assert.IsNull( binding.SamplerIndex ); Assert.IsNull( binding.Sampler );
			Assert.IsTrue( binding.IgnoreImageColorMetadata && binding.TopLeftOrigin && binding.StraightAlpha );
			Assert.AreEqual( binding.Role is GltfMaterialTextureRole.BaseColor or GltfMaterialTextureRole.Emissive, binding.IsSrgb );
			Assert.AreEqual( binding.Role == GltfMaterialTextureRole.BaseColor, binding.HasLinearAlpha );
		}
		Assert.AreEqual( GltfMaterialTextureChannels.Rgba, plan.Bindings[GltfMaterialTextureRole.BaseColor].Channels );
		Assert.AreEqual( GltfMaterialTextureChannels.Green | GltfMaterialTextureChannels.Blue, plan.Bindings[GltfMaterialTextureRole.MetallicRoughness].Channels );
		Assert.AreEqual( GltfMaterialTextureChannels.Rgb, plan.Bindings[GltfMaterialTextureRole.Normal].Channels );
		Assert.AreEqual( GltfMaterialTextureChannels.Red, plan.Bindings[GltfMaterialTextureRole.Occlusion].Channels );
		Assert.AreEqual( GltfMaterialTextureChannels.Rgb, plan.Bindings[GltfMaterialTextureRole.Emissive].Channels );
	}

	[TestMethod]
	public void BaseColorOnlySelectionDoesNotInspectIgnoredUnlitPbrInputs()
	{
		using var doc = Document(); var color = Info( true );
		var plan = GltfMaterialTextureEncodingPlan.CreateForRoles( doc, new GltfMaterial { BaseColorTexture = color, MetallicRoughnessTexture = Info( true, 999 ), NormalTexture = Info( true, -1 ), OcclusionTexture = Info( true, 999 ), EmissiveTexture = Info( false, 999 ) }, [GltfMaterialTextureRole.BaseColor] );
		Assert.AreEqual( 1, plan.Bindings.Count ); Assert.AreSame( color, plan.Bindings[GltfMaterialTextureRole.BaseColor].TextureInfo );
	}

	[TestMethod]
	public void RequestedSamplerAndOmittedFilterValuesRemainUnapproximated()
	{
		using var doc = Document( "[{\"source\":0,\"sampler\":0}]", "[{\"wrapS\":33071,\"wrapT\":33648}]" );
		var binding = GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ) } ).Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.AreEqual( 0, binding.SamplerIndex ); Assert.AreEqual( 33071, binding.Sampler.WrapS ); Assert.AreEqual( 33648, binding.Sampler.WrapT ); Assert.IsNull( binding.Sampler.MinFilter ); Assert.IsNull( binding.Sampler.MagFilter );
	}

	[TestMethod]
	public void ExplicitSamplerFiltersAndDefaultWrapArePreserved()
	{
		using var doc = Document( "[{\"source\":0,\"sampler\":0}]", "[{\"minFilter\":9984,\"magFilter\":9729}]" );
		var binding = GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ) } ).Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.AreEqual( 10497, binding.Sampler.WrapS ); Assert.AreEqual( 10497, binding.Sampler.WrapT ); Assert.AreEqual( 9984, binding.Sampler.MinFilter ); Assert.AreEqual( 9729, binding.Sampler.MagFilter );
	}

	[TestMethod]
	public void MissingImageRemainsCapabilityMetadataWithoutEagerDecodeOrAllocation()
	{
		using var doc = Document( "[{}]" );
		var binding = GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ) } ).Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.IsNull( binding.ImageIndex );
	}

	[TestMethod]
	[DataRow( "OPAQUE", false )]
	[DataRow( "MASK", true )]
	[DataRow( "BLEND", true )]
	public void LinearAlphaEncodingIsDistinctFromWhetherMaterialCoverageConsumesIt( string alphaMode, bool consumesAlpha )
	{
		using var doc = Document();
		var binding = GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ), AlphaMode = alphaMode } ).Bindings[GltfMaterialTextureRole.BaseColor];
		Assert.IsTrue( binding.HasLinearAlpha ); Assert.AreEqual( consumesAlpha, binding.UsesAlphaCoverage );
	}

	[TestMethod]
	public void OptionalUnsupportedBasisUsesItsCoreFallbackWithoutOpeningEitherImage()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}],"images":[{"uri":"fallback.png"},{"uri":"encoded.ktx2"}]}""" ), _ => throw new InvalidOperationException( "No image I/O allowed." ), supportedExtensions: new System.Collections.Generic.HashSet<string>() );
		Assert.AreEqual( 0, GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ) } ).Bindings[GltfMaterialTextureRole.BaseColor].ImageIndex );
	}

	[TestMethod]
	public void SupportedBasisUsesTheExtensionImageWithoutOpeningItsPayload()
	{
		using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_basisu"],"textures":[{"source":0,"extensions":{"KHR_texture_basisu":{"source":1}}}],"images":[{"uri":"fallback.png"},{"uri":"encoded.ktx2"}]}""" ), _ => throw new InvalidOperationException( "No image I/O allowed." ), supportedExtensions: new System.Collections.Generic.HashSet<string> { "KHR_texture_basisu" } );
		Assert.AreEqual( 1, GltfMaterialTextureEncodingPlan.Create( doc, new GltfMaterial { BaseColorTexture = Info( true ) } ).Bindings[GltfMaterialTextureRole.BaseColor].ImageIndex );
	}

	[TestMethod]
	[DataRow( (int)GltfMaterialTextureRole.BaseColor, false )]
	[DataRow( (int)GltfMaterialTextureRole.Emissive, false )]
	[DataRow( (int)GltfMaterialTextureRole.Normal, true )]
	[DataRow( (int)GltfMaterialTextureRole.Occlusion, true )]
	[DataRow( (int)GltfMaterialTextureRole.MetallicRoughness, true )]
	public void RoleEncodingContradictionsCannotSilentlyCorruptResourceIdentity( int roleValue, bool srgb )
	{
		using var doc = Document(); var info = Info( srgb ); var role = (GltfMaterialTextureRole)roleValue;
		var material = new GltfMaterial { BaseColorTexture = info, EmissiveTexture = info, NormalTexture = info, OcclusionTexture = info, MetallicRoughnessTexture = info };
		Assert.ThrowsException<GltfException>( () => GltfMaterialTextureEncodingPlan.CreateForRoles( doc, material, [role] ) );
	}

	[TestMethod]
	public void AbsentMaterialHasNoInventedBindingsAndDuplicateRolesFailBeforeLinkResolution()
	{
		using var doc = Document();
		Assert.AreEqual( 0, GltfMaterialTextureEncodingPlan.Create( doc, null ).Bindings.Count );
		Assert.ThrowsException<ArgumentException>( () => GltfMaterialTextureEncodingPlan.CreateForRoles( doc, new GltfMaterial { BaseColorTexture = Info( true, 999 ) }, [GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.BaseColor] ) );
	}

	[TestMethod]
	public void InvalidRoleIsRejectedBeforeAnyImageResolution()
	{
		using var doc = Document(); Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfMaterialTextureEncodingPlan.CreateForRoles( doc, new GltfMaterial { BaseColorTexture = Info( true, 999 ) }, [GltfMaterialTextureRole.BaseColor, (GltfMaterialTextureRole)999] ) );
	}
}
