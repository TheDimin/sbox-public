using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Rendering;

namespace Sandbox.Tests;

[TestClass]
public class GltfMaterialSamplerCacheIdentityTests
{
	private sealed class FakeMaterial { public bool Alive = true; }
	private static GltfMaterialKey Key( object template, object texture, SamplerState sampler, string samplerParameter ) =>
		new( template, 0, [], [], [new( "g_tColor", texture, 0, sampler, samplerParameter )] );

	[TestMethod]
	public void UnboundSamplerDifferencesDoNotDuplicateEquivalentMaterial()
	{
		var template = new object(); var texture = new object();
		var a = Key( template, texture, new SamplerState { Filter = FilterMode.Point }, null );
		var b = Key( template, texture, new SamplerState { Filter = FilterMode.Trilinear, AddressModeU = TextureAddressMode.Clamp }, null );
		Assert.AreEqual( a, b );
		Assert.AreEqual( a.GetHashCode(), b.GetHashCode() );
		var creates = 0; var releases = 0;
		using var cache = new GltfMaterialCacheCore<FakeMaterial>( _ => { creates++; return new FakeMaterial(); }, x => { releases++; x.Alive = false; }, x => x.Alive );
		var first = cache.Acquire( a );
		var second = cache.Acquire( b );
		Assert.AreSame( first.Value, second.Value );
		Assert.AreEqual( 1, creates );
		first.Dispose(); Assert.AreEqual( 0, releases );
		second.Dispose(); Assert.AreEqual( 1, releases );
	}

	[TestMethod]
	public void BoundSamplerDifferencesAndPresenceRemainDistinct()
	{
		var template = new object(); var texture = new object();
		var point = new SamplerState { Filter = FilterMode.Point };
		var linear = new SamplerState { Filter = FilterMode.Trilinear };
		var unbound = Key( template, texture, point, null );
		var boundPoint = Key( template, texture, point, "sampler" );
		var sameBoundPoint = Key( template, texture, point, "sampler" );
		var boundLinear = Key( template, texture, linear, "sampler" );
		var boundOtherName = Key( template, texture, point, "otherSampler" );
		Assert.AreNotEqual( unbound, boundPoint );
		Assert.AreEqual( boundPoint, sameBoundPoint );
		Assert.AreEqual( boundPoint.GetHashCode(), sameBoundPoint.GetHashCode() );
		Assert.AreNotEqual( boundPoint, boundLinear );
		Assert.AreNotEqual( boundPoint, boundOtherName );
	}
}
