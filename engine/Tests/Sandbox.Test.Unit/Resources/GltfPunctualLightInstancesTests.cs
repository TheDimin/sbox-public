using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu.Lighting;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfPunctualLightInstancesTests
{
	[TestMethod]
	public void InstancesAreOwnedSeparatelyAndUpdatesKeepInheritedVisibility()
	{
		using var document = Document();
		var plan = GltfPunctualLightPlan.Create( document );
		var first = new FakeFactory(); var second = new FakeFactory();
		using var a = GltfPunctualLightInstances.Create( plan, first, null, Matrix4x4.Identity, [false, false, true] );
		using var b = GltfPunctualLightInstances.Create( plan, second, null, Matrix4x4.CreateTranslation( 10, 0, 0 ), [true, true, true] );
		Assert.AreEqual( 2, first.Lights.Count ); Assert.AreEqual( 2, second.Lights.Count );
		Assert.IsFalse( first.Lights[0].Visible ); Assert.IsTrue( first.Lights[1].Visible );
		Assert.IsTrue( second.Lights[0].Visible );
		Assert.AreEqual( 10f, second.Lights[0].State.PositionInches.X - first.Lights[0].State.PositionInches.X, .00001f );
		a.Update( document.EvaluatePose(), Matrix4x4.Identity, [true, true, false] );
		Assert.IsTrue( first.Lights[0].Visible ); Assert.IsFalse( first.Lights[1].Visible );
		Assert.IsTrue( second.Lights[1].Visible, "Updating one instance must not alter another." );
	}

	[TestMethod]
	public void CreationFailureReleasesAllPreviouslyCreatedLights()
	{
		using var document = Document();
		var factory = new FakeFactory { ThrowAt = 1 };
		Assert.ThrowsException<InvalidOperationException>( () => GltfPunctualLightInstances.Create( GltfPunctualLightPlan.Create( document ), factory, null, Matrix4x4.Identity, [true, true, true] ) );
		Assert.AreEqual( 1, factory.Lights.Count ); Assert.AreEqual( 1, factory.Lights[0].Disposals );
	}

	[TestMethod]
	public void FailedPreflightDoesNotMutateAndDisposalIsIdempotent()
	{
		using var document = Document();
		var factory = new FakeFactory();
		var owner = GltfPunctualLightInstances.Create( GltfPunctualLightPlan.Create( document ), factory, null, Matrix4x4.Identity, [true, true, true] );
		var previous = factory.Lights[0].Updates;
		Assert.ThrowsException<GltfException>( () => owner.Update( null, Matrix4x4.Identity, [true] ) );
		Assert.AreEqual( previous, factory.Lights[0].Updates );
		owner.Dispose(); owner.Dispose();
		Assert.AreEqual( 1, factory.Lights[0].Disposals ); Assert.AreEqual( 1, factory.Lights[1].Disposals );
		Assert.ThrowsException<ObjectDisposedException>( () => owner.Update( null, Matrix4x4.Identity, [true, true, true] ) );
	}

	[TestMethod]
	public void ApplyFailureRollsBackAndCleanupAttemptsEveryLight()
	{
		using var document = Document();
		var factory = new FakeFactory { ThrowOnApplyAt = 1, ThrowOnDisposeAt = 0 };
		var error = Assert.ThrowsException<InvalidOperationException>( () => GltfPunctualLightInstances.Create( GltfPunctualLightPlan.Create( document ), factory, null, Matrix4x4.Identity, [true, true, true] ) );
		Assert.AreEqual( "Injected apply failure.", error.Message );
		Assert.AreEqual( 2, factory.Lights.Count );
		Assert.AreEqual( 1, factory.Lights[0].Disposals ); Assert.AreEqual( 1, factory.Lights[1].Disposals );
	}

	private static GltfDocument Document()
	{
		const string json = """{"asset":{"version":"2.0"},"extensionsUsed":["KHR_lights_punctual"],"scene":0,"scenes":[{"nodes":[0,2]}],"nodes":[{"children":[1]},{"extensions":{"KHR_lights_punctual":{"light":0}}},{"extensions":{"KHR_lights_punctual":{"light":1}}}],"extensions":{"KHR_lights_punctual":{"lights":[{"type":"point"},{"type":"spot","spot":{}}]}}}""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_lights_punctual" } );
	}

	private sealed class FakeFactory : GltfPunctualLightInstances.IFactory
	{
		public readonly List<FakeLight> Lights = new();
		public int ThrowAt = -1;
		public int ThrowOnApplyAt = -1, ThrowOnDisposeAt = -1;
		public GltfPunctualLightInstances.ILight Create( GltfPunctualLightPlan.Binding binding )
		{
			if ( Lights.Count == ThrowAt ) throw new InvalidOperationException( "Injected failure." );
			var light = new FakeLight { ThrowOnApply = Lights.Count == ThrowOnApplyAt, ThrowOnDispose = Lights.Count == ThrowOnDisposeAt }; Lights.Add( light ); return light;
		}
	}

	private sealed class FakeLight : GltfPunctualLightInstances.ILight
	{
		public GltfPunctualLightPlan.State State;
		public bool Visible;
		public bool ThrowOnApply, ThrowOnDispose;
		public int Updates, Disposals;
		public void Apply( GltfPunctualLightPlan.State state, bool visible ) { if ( ThrowOnApply ) throw new InvalidOperationException( "Injected apply failure." ); State = state; Visible = visible; Updates++; }
		public void Dispose() { Disposals++; if ( ThrowOnDispose ) throw new InvalidOperationException( "Injected disposal failure." ); }
	}
}
