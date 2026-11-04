using Sandbox.Rendering;
using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class GltfMaterialCacheTests
{
	private sealed class Fake { internal bool Alive = true; }
	private sealed class Collisions : IEqualityComparer<GltfMaterialKey>
	{
		public bool Equals( GltfMaterialKey x, GltfMaterialKey y ) => x.Equals( y );
		public int GetHashCode( GltfMaterialKey obj ) => 1;
	}
	private static GltfMaterialKey Key( object template, int revision = 1, int feature = 2, float value = .5f, object texture = null, int textureRevision = 0, SamplerState sampler = default ) => new(
		template, revision, [new( "F_TEST", feature )], [new( "g_vColorTint", new Vector4( value, 0, 0, 1 ) )],
		texture is null ? [] : [new( "g_tColor", texture, textureRevision, sampler )] );

	[TestMethod]
	public void ScalarAndVectorParametersHaveSeparateTypedIdentity()
	{
		var template = new object();
		var scalar = new GltfMaterialKey( template, 1, [], [], [], scalars: [new( "g_flRoughnessScaleFactor", .5f )] );
		var same = new GltfMaterialKey( template, 1, [], [], [], scalars: [new( "g_flRoughnessScaleFactor", .5f )] );
		var vector = new GltfMaterialKey( template, 1, [], [new( "g_flRoughnessScaleFactor", new Vector4( .5f, 0, 0, 0 ) )], [] );
		Assert.AreEqual( scalar, same ); Assert.AreNotEqual( scalar, vector );
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 1, [], [new( "p", new Vector4( 1, 0, 0, 0 ) )], [], scalars: [new( "p", 1 )] ) );
	}

	[TestMethod]
	public void DescriptorRejectsNonFiniteInputsAndConflictingSamplerBindings()
	{
		var template = new object(); var texture = new object();
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 1, [], [], [], scalars: [new( "x", float.NaN )] ) );
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 1, [], [new( "x", new Vector4( 0, float.PositiveInfinity, 0, 0 ) )], [] ) );
		var a = new SamplerState(); var b = new SamplerState { MaxLod = 0 };
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 1, [], [],
			[new( "a", texture, 0, a, "sameSampler" ), new( "b", texture, 0, b, "sameSampler" )] ) );
		_ = new GltfMaterialKey( template, 1, [], [], [new( "a", texture, 0, a, "sameSampler" ), new( "b", texture, 0, a, "sameSampler" )] );
	}

	[TestMethod]
	public void ThreadGuardRejectsAcquireAndReleaseBeforeChangingLeaseState()
	{
		var allowed = false; var creates = 0; var releases = 0; var template = new object();
		using var cache = new GltfMaterialCacheCore<Fake>( _ => { creates++; return new Fake(); }, _ => releases++, x => x.Alive,
			verifyThread: () => { if ( !allowed ) throw new InvalidOperationException( "wrong thread" ); } );
		Assert.ThrowsException<InvalidOperationException>( () => cache.Acquire( Key( template ) ) );
		Assert.AreEqual( 0, creates );
		allowed = true; var lease = cache.Acquire( Key( template ) );
		allowed = false; Assert.ThrowsException<InvalidOperationException>( lease.Dispose );
		Assert.IsNotNull( lease.Value ); Assert.AreEqual( 0, releases );
		allowed = true; lease.Dispose(); Assert.AreEqual( 1, releases );
	}

	[TestMethod]
	public void ConflictingTextureAndValueNamesAreRejectedBeforeBinding()
	{
		var template = new object(); var texture = new object();
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 0, [], [new( "same", new Vector4( 1, 0, 0, 0 ) )], [new( "same", texture, 0, default )] ) );
		Assert.ThrowsException<ArgumentException>( () => new GltfMaterialKey( template, 0, [], [], [new( "same", texture, 0, default )], scalars: [new( "same", 1 )] ) );
	}

	[TestMethod]
	public void CreationIsReleasedIfLivenessValidationThrows()
	{
		var releases = 0; var aliveCheckThrows = true; var template = new object();
		using var cache = new GltfMaterialCacheCore<Fake>( _ => new Fake(), _ => releases++, _ => aliveCheckThrows ? throw new InvalidOperationException( "failed to inspect" ) : true );
		Assert.ThrowsException<InvalidOperationException>( () => cache.Acquire( Key( template ) ) );
		Assert.AreEqual( 1, releases, "A created resource must be released even if its validation throws." );
		aliveCheckThrows = false;
		using var lease = cache.Acquire( Key( template ) );
		Assert.IsNotNull( lease.Value );
	}

	[TestMethod]
	public void RecursiveCreationForSameKeyFailsWithoutLeakingAndCanBeRetried()
	{
		var template = new object(); var recursive = true; var releases = 0;
		GltfMaterialCacheCore<Fake> cache = null;
		GltfMaterialLease<Fake> nested = null;
		cache = new GltfMaterialCacheCore<Fake>( key =>
		{
			if ( recursive ) { recursive = false; nested = cache.Acquire( key ); }
			return new Fake();
		}, _ => releases++, x => x.Alive );
		using ( cache )
		{
			try
			{
				Assert.ThrowsException<InvalidOperationException>( () => cache.Acquire( Key( template ) ) );
				Assert.IsNull( nested, "A recursive lookup must fail before an inner lease is published." );
				using var retry = cache.Acquire( Key( template ) );
				Assert.IsNotNull( retry.Value );
			}
			finally { nested?.Dispose(); }
		}
		Assert.AreEqual( 1, releases );
	}

	[TestMethod]
	public void EquivalentKeysShareAndDistinctEffectiveStatesNeverAliasEvenWithHashCollisions()
	{
		var template = new object(); var texture = new object(); var creates = 0;
		using var cache = new GltfMaterialCacheCore<Fake>( _ => { creates++; return new Fake(); }, x => x.Alive = false, x => x.Alive, new Collisions() );
		using var a = cache.Acquire( Key( template, texture: texture ) );
		using var b = cache.Acquire( Key( template, texture: texture ) );
		Assert.AreSame( a.Value, b.Value );
		using var c = cache.Acquire( Key( template, revision: 2, texture: texture ) );
		using var d = cache.Acquire( Key( template, feature: 3, texture: texture ) );
		using var e = cache.Acquire( Key( template, value: .7f, texture: texture ) );
		using var f = cache.Acquire( Key( template, texture: new object() ) );
		using var g = cache.Acquire( Key( template, texture: texture, textureRevision: 1 ) );
		using var h = cache.Acquire( Key( template, texture: texture, sampler: new SamplerState { MaxLod = 0 } ) );
		using var i = cache.Acquire( Key( new object(), texture: texture ) );
		Assert.AreEqual( 8, creates );
		Assert.AreNotSame( a.Value, c.Value ); Assert.AreNotSame( a.Value, d.Value ); Assert.AreNotSame( a.Value, e.Value );
		Assert.AreNotSame( a.Value, f.Value ); Assert.AreNotSame( a.Value, g.Value ); Assert.AreNotSame( a.Value, h.Value ); Assert.AreNotSame( a.Value, i.Value );
	}

	[TestMethod]
	public void KeySnapshotsMutableCollectionsAndCanonicalizesOrder()
	{
		var template = new object();
		var features = new List<KeyValuePair<string, int>> { new( "b", 2 ), new( "a", 1 ) };
		var parameters = new List<KeyValuePair<string, Vector4>> { new( "p", new Vector4( 1, 2, 3, 4 ) ) };
		var original = new GltfMaterialKey( template, 0, features, parameters, [] );
		features.Clear(); parameters.Clear();
		var equivalent = new GltfMaterialKey( template, 0, [new( "a", 1 ), new( "b", 2 )], [new( "p", new Vector4( 1, 2, 3, 4 ) )], [] );
		Assert.AreEqual( original, equivalent ); Assert.AreEqual( original.GetHashCode(), equivalent.GetHashCode() );
	}

	[TestMethod]
	public void ClearRetiresButDoesNotDestroyLiveLeaseAndDisposalIsIdempotent()
	{
		var releases = 0; var template = new object();
		using var cache = new GltfMaterialCacheCore<Fake>( _ => new Fake(), x => { releases++; x.Alive = false; }, x => x.Alive );
		var first = cache.Acquire( Key( template ) ); var shared = cache.Acquire( Key( template ) ); var old = first.Value;
		cache.Clear(); Assert.IsTrue( old.Alive );
		using var fresh = cache.Acquire( Key( template ) ); Assert.AreNotSame( old, fresh.Value );
		first.Dispose(); first.Dispose(); Assert.IsTrue( old.Alive ); Assert.AreEqual( 0, releases );
		shared.Dispose(); Assert.IsFalse( old.Alive ); Assert.AreEqual( 1, releases );
	}

	[TestMethod]
	public void FailedOrDeadEntryIsNotReusedAndShutdownProtectsOutstandingLeases()
	{
		var attempts = 0; var releases = 0; var template = new object();
		var cache = new GltfMaterialCacheCore<Fake>( _ => { if ( attempts++ == 0 ) throw new InvalidOperationException(); return new Fake(); }, x => { releases++; x.Alive = false; }, x => x.Alive );
		Assert.ThrowsException<InvalidOperationException>( () => cache.Acquire( Key( template ) ) );
		var dead = cache.Acquire( Key( template ) ); dead.Value.Alive = false;
		var active = cache.Acquire( Key( template ) ); Assert.AreNotSame( dead.Value, active.Value );
		cache.Dispose(); Assert.IsTrue( active.Value.Alive );
		Assert.ThrowsException<ObjectDisposedException>( () => cache.Acquire( Key( template ) ) );
		dead.Dispose(); active.Dispose(); Assert.AreEqual( 2, releases );
	}
}
