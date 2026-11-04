using NativeEngine;
using Sandbox.Rendering;

namespace Sandbox.Tests;

[TestClass]
public class SamplerDescriptorTests
{
	[TestMethod]
	[DataRow( FilterMode.Point, 0x00 )]
	[DataRow( FilterMode.MinMagPointMipLinear, 0x01 )]
	[DataRow( FilterMode.MinPointMagLinearMipPoint, 0x04 )]
	[DataRow( FilterMode.MinPointMagMipLinear, 0x05 )]
	[DataRow( FilterMode.MinLinearMagMipPoint, 0x10 )]
	[DataRow( FilterMode.MinLinearMagPointMipLinear, 0x11 )]
	[DataRow( FilterMode.Bilinear, 0x14 )]
	[DataRow( FilterMode.Trilinear, 0x15 )]
	[DataRow( FilterMode.Anisotropic, 0x55 )]
	public void SamplerDescriptorPreservesAllIndependentFilterBits( FilterMode filter, int expected )
	{
		var descriptor = new CSamplerStateDesc( new SamplerState { Filter = filter } );
		Assert.AreEqual( (byte)expected, descriptor.m_nFilterMode );
	}

	[TestMethod]
	public void ExistingInitializedAndZeroInitializedSamplersKeepTheirMaximumLod()
	{
		var initialized = new CSamplerStateDesc( new SamplerState() );
		var zeroInitialized = new CSamplerStateDesc( default );
		Assert.AreEqual( (byte)0, initialized.m_nMinLod );
		Assert.AreEqual( (byte)15, initialized.m_nMaxLod );
		Assert.AreEqual( (byte)0, zeroInitialized.m_nMinLod );
		Assert.AreEqual( (byte)15, zeroInitialized.m_nMaxLod );
	}

	[TestMethod]
	public void ExplicitMaximumLodZeroIsNotConfusedWithDefault()
	{
		var sampler = new SamplerState { Filter = FilterMode.MinLinearMagMipPoint, MaxLod = 0 };
		var descriptor = new CSamplerStateDesc( sampler );
		Assert.AreEqual( (byte)0x10, descriptor.m_nFilterMode );
		Assert.AreEqual( (byte)0, descriptor.m_nMaxLod );
	}

	[TestMethod]
	public void MaximumLodParticipatesInSamplerCacheIdentity()
	{
		var unclamped = new SamplerState { Filter = FilterMode.Point };
		var clamped = unclamped with { MaxLod = 0 };
		Assert.AreNotEqual( unclamped, clamped );
		Assert.AreEqual( clamped, unclamped with { MaxLod = 0 } );
		var keys = new System.Collections.Generic.Dictionary<SamplerState, int> { [unclamped] = 1, [clamped] = 2 };
		Assert.AreEqual( 2, keys.Count );
	}
}
