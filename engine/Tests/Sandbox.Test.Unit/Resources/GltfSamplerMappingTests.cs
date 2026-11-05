using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Rendering;

namespace Sandbox.Tests;

[TestClass]
public class GltfSamplerMappingTests
{
	[TestMethod]
	[DataRow( 9728, 9728, 0x00, true )]
	[DataRow( 9728, 9729, 0x04, true )]
	[DataRow( 9729, 9728, 0x10, true )]
	[DataRow( 9729, 9729, 0x14, true )]
	[DataRow( 9984, 9728, 0x00, false )]
	[DataRow( 9984, 9729, 0x04, false )]
	[DataRow( 9985, 9728, 0x10, false )]
	[DataRow( 9985, 9729, 0x14, false )]
	[DataRow( 9986, 9728, 0x01, false )]
	[DataRow( 9986, 9729, 0x05, false )]
	[DataRow( 9987, 9728, 0x11, false )]
	[DataRow( 9987, 9729, 0x15, false )]
	public void EachExplicitFilterPairPreservesIndependentFilterBits( int min, int mag, int expected, bool needsBaseMip )
	{
		var sampler = new GltfSampler { WrapS = 10497, WrapT = 10497, MinFilter = min, MagFilter = mag };
		var mapped = GltfSamplerMapping.Map( sampler, 9987, 9729 );
		Assert.AreEqual( expected, (int)mapped.State.Filter );
		Assert.AreEqual( needsBaseMip, mapped.RequiresBaseMipOnly );
		// A zero max LOD can force the magnification filter on Vulkan. The caller
		// must supply a single-mip resource/view when RequiresBaseMipOnly is true.
		Assert.IsNull( mapped.State.MaxLod );
	}

	[TestMethod]
	[DataRow( 10497, TextureAddressMode.Wrap )]
	[DataRow( 33648, TextureAddressMode.Mirror )]
	[DataRow( 33071, TextureAddressMode.Clamp )]
	public void WrapModesMapIndependentlyOnBothAxes( int gltfWrap, TextureAddressMode expected )
	{
		var u = GltfSamplerMapping.Map( new GltfSampler { WrapS = gltfWrap, WrapT = 10497 }, 9987, 9729 );
		var v = GltfSamplerMapping.Map( new GltfSampler { WrapS = 10497, WrapT = gltfWrap }, 9987, 9729 );
		Assert.AreEqual( expected, u.State.AddressModeU );
		Assert.AreEqual( TextureAddressMode.Wrap, u.State.AddressModeV );
		Assert.AreEqual( expected, v.State.AddressModeV );
		Assert.AreEqual( TextureAddressMode.Wrap, v.State.AddressModeU );
	}

	[TestMethod]
	public void OmittedSamplerAndFiltersUseExplicitCallerPolicy()
	{
		var nearest = GltfSamplerMapping.Map( null, 9728, 9728 );
		var linear = GltfSamplerMapping.Map( new GltfSampler { WrapS = 10497, WrapT = 10497 }, 9987, 9729 );
		Assert.AreEqual( FilterMode.Point, nearest.State.Filter );
		Assert.IsTrue( nearest.RequiresBaseMipOnly );
		Assert.AreEqual( TextureAddressMode.Wrap, nearest.State.AddressModeU );
		Assert.AreEqual( TextureAddressMode.Wrap, nearest.State.AddressModeV );
		Assert.AreEqual( FilterMode.Trilinear, linear.State.Filter );
		Assert.IsFalse( linear.RequiresBaseMipOnly );
		Assert.AreEqual( new SamplerState().AddressModeW, linear.State.AddressModeW );
		Assert.AreEqual( new SamplerState().MaxAnisotropy, linear.State.MaxAnisotropy );
	}

	[TestMethod]
	public void ParsedSamplerFeedsTheSameExplicitMapping()
	{
		using var document = GltfReader.Read( System.Text.Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"samplers":[{"wrapS":33648,"wrapT":33071,"minFilter":9986,"magFilter":9729}]}
			""" ) );
		var mapped = GltfSamplerMapping.Map( document.ReadSampler( 0 ), 9728, 9728 );
		Assert.AreEqual( FilterMode.MinPointMagMipLinear, mapped.State.Filter );
		Assert.AreEqual( TextureAddressMode.Mirror, mapped.State.AddressModeU );
		Assert.AreEqual( TextureAddressMode.Clamp, mapped.State.AddressModeV );
		Assert.IsFalse( mapped.RequiresBaseMipOnly );
	}

	[TestMethod]
	public void InvalidParsedOrFallbackEnumsFailInsteadOfChoosingAFilter()
	{
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfSamplerMapping.Map( null, 0, 9729 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfSamplerMapping.Map( null, 9987, 0 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfSamplerMapping.Map( new GltfSampler { WrapS = 0, WrapT = 10497 }, 9987, 9729 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfSamplerMapping.Map( new GltfSampler { WrapS = 10497, WrapT = 10497, MinFilter = 7 }, 9987, 9729 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfSamplerMapping.Map( new GltfSampler { WrapS = 10497, WrapT = 10497, MagFilter = 7 }, 9987, 9729 ) );
	}
}
