using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Tests;

/// <summary>Pure reader-to-instance transactions; no native material, scene or GPU allocation.</summary>
[TestClass]
public sealed class GltfMaterialVariantStateTests
{
	private const string Fixture = """
		{"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0,1,2]}],
		"nodes":[{"mesh":0},{"mesh":1},{"mesh":2}],
		"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},{"componentType":5126,"count":3,"type":"VEC2"}],
		"materials":[{},{},{}],"extensionsUsed":["KHR_materials_variants"],
		"extensions":{"KHR_materials_variants":{"variants":[{"name":"first"},{"name":"unmapped"},{"name":"second"}]}},
		"meshes":[
		 {"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[2]}]}}}]},
		 {"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":2,"variants":[0]},{"material":1,"variants":[2]}]}}}]},
		 {"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0}]}]}
		""";
	private static readonly GltfMaterialVariantSlot[] Slots = { new( 0, 0, 0 ), new( 1, 1, 0 ), new( 2, 2, 0 ) };
	private sealed class Fake( int index ) { internal int Index = index; internal bool Alive = true; }
	private sealed class Harness : IDisposable
	{
		internal readonly GltfDocument Document;
		internal readonly GltfMaterialVariantAsset<Fake> Asset;
		internal readonly List<int> Attempts = new();
		internal readonly List<Fake> Created = new(), Released = new();
		internal Action<int?> BeforeAcquire;
		internal Action<Fake> OnRelease;
		internal Action<GltfMaterialVariantBinding<Fake>> ValidateBinding;
		internal Func<Fake, bool> Validate = value => value.Alive;
		internal bool Allowed = true;
		internal bool Owned = true;
		internal Harness( string json = Fixture, IReadOnlyList<GltfMaterialVariantSlot> slots = null, bool supports = true )
		{
			Document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: supports ? new HashSet<string> { "KHR_materials_variants" } : null );
			Asset = new( Document, slots ?? Slots, Acquire, value => Validate( value ), () => { if ( !Allowed ) throw new InvalidOperationException( "wrong phase/thread" ); }, binding => ValidateBinding?.Invoke( binding ) );
		}
		private GltfMaterialLease<Fake> Acquire( int? index )
		{
			Attempts.Add( index ?? -1 ); BeforeAcquire?.Invoke( index ); var value = new Fake( index ?? -1 ); Created.Add( value );
			return new( value, () => { Released.Add( value ); if ( Owned ) value.Alive = false; OnRelease?.Invoke( value ); }, null );
		}
		public void Dispose() { Asset.Dispose(); Document.Dispose(); }
	}
	private static void Indices( GltfMaterialVariantState<Fake> state, params int?[] expected ) => CollectionAssert.AreEqual( expected, state.Current.Bindings.Select( x => x.MaterialIndex ).ToArray() );

	[TestMethod]
	public void DefaultAcquisitionIsLazyAndSharedByTwoIndependentInstances()
	{
		using var h = new Harness(); Assert.AreEqual( 0, h.Attempts.Count );
		using var a = h.Asset.CreateState(); using var b = h.Asset.CreateState();
		CollectionAssert.AreEqual( new[] { 0 }, h.Attempts ); Indices( a, 0, 0, 0 ); Indices( b, 0, 0, 0 ); Assert.IsNull( a.Current.ActiveVariant );
		Assert.AreSame( a.Current.Bindings[0].Material, b.Current.Bindings[0].Material );
		a.SetVariant( 0 ); Indices( a, 1, 2, 0 ); Indices( b, 0, 0, 0 ); Assert.IsNull( b.Current.ActiveVariant );
	}

	[TestMethod]
	public void MappedUnmappedAndNullSelectionUseEachPrimitivesCoreFallback()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState();
		state.SetVariant( 0 ); Indices( state, 1, 2, 0 ); state.SetVariant( 2 ); Indices( state, 2, 1, 0 );
		state.SetVariant( 1 ); Indices( state, 0, 0, 0 ); Assert.AreEqual( 1, state.Current.ActiveVariant );
		state.SetVariant( null ); Indices( state, 0, 0, 0 ); Assert.IsNull( state.Current.ActiveVariant );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, h.Attempts );
	}

	[TestMethod]
	public void EveryRowIsResolvedBeforeAnyNewMaterialAcquisition()
	{
		using var h = new Harness( Fixture.Replace( "\"material\":2,\"variants\":[0]", "\"material\":9,\"variants\":[0]" ) ); using var state = h.Asset.CreateState(); var before = state.Current;
		Assert.ThrowsException<GltfException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts );
	}

	[TestMethod]
	public void LateAcquisitionFailureReleasesStagedOwnedLeaseAndPublishesNothing()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current; var failure = new InvalidOperationException( "second material fails" );
		h.BeforeAcquire = index => { if ( index == 2 ) throw failure; };
		Assert.AreSame( failure, Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ) );
		Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, h.Released.Count ); Assert.AreEqual( 1, h.Released[0].Index ); Assert.IsFalse( h.Released[0].Alive ); Assert.IsTrue( before.Bindings[0].Material.Alive );
		h.BeforeAcquire = null; state.SetVariant( 0 ); Indices( state, 1, 2, 0 ); CollectionAssert.AreEqual( new[] { 0, 1, 2, 1, 2 }, h.Attempts );
	}

	[TestMethod]
	public void ValidationFailureReleasesTheJustReturnedLease()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current;
		h.Validate = value => value.Index == 1 ? throw new InvalidOperationException( "liveness query failed" ) : value.Alive;
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, h.Released.Count ); Assert.AreEqual( 1, h.Released[0].Index );
	}

	[TestMethod]
	public void InvalidMaterialDoesNotReplaceValidPublishedBindings()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current; h.Validate = value => value.Index != 1;
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, h.Released.Count );
	}

	[TestMethod]
	public void PerPrimitiveBindingFailureRollsBackEveryNewLeaseBeforePublication()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current;
		h.ValidateBinding = binding => { if ( binding.Slot.Node == 1 && binding.MaterialIndex == 2 ) throw new InvalidOperationException( "material requires an unavailable primitive semantic" ); };
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEquivalent( new[] { 1, 2 }, h.Released.Select( x => x.Index ).ToArray() );
		h.ValidateBinding = null; state.SetVariant( 0 ); Indices( state, 1, 2, 0 ); CollectionAssert.AreEqual( new[] { 0, 1, 2, 1, 2 }, h.Attempts );
	}

	[TestMethod]
	public void EveryPrimitiveBindingIsValidatedEvenWhenItsMaterialIsAlreadyShared()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var checkedNodes = new List<int>(); h.ValidateBinding = binding => checkedNodes.Add( binding.Slot.Node );
		state.SetVariant( 1 ); CollectionAssert.AreEqual( new[] { 0, 1, 2 }, checkedNodes ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts ); Indices( state, 0, 0, 0 );
	}

	[TestMethod]
	public void CreationFailureDoesNotRetainPartialCacheOrLiveState()
	{
		using var h = new Harness( Fixture.Replace( "\"material\":0,\"extensions\"", "\"material\":1,\"extensions\"" ) );
		h.BeforeAcquire = _ => throw new InvalidOperationException( "default unavailable" ); Assert.ThrowsException<InvalidOperationException>( () => h.Asset.CreateState() );
		h.BeforeAcquire = null; using var retry = h.Asset.CreateState(); Indices( retry, 1, 1, 0 );
	}

	[TestMethod]
	public void FailedCreationReleasesEarlierDefaultAndCanDisposeOwner()
	{
		var json = Fixture.Replace( "\"material\":0}]}", "\"material\":1}]}" ); using var h = new Harness( json ); h.BeforeAcquire = index => { if ( index == 1 ) throw new InvalidOperationException(); };
		Assert.ThrowsException<InvalidOperationException>( () => h.Asset.CreateState() ); CollectionAssert.AreEqual( new[] { 0, 1 }, h.Attempts ); Assert.AreEqual( 1, h.Released.Count ); h.Asset.Dispose(); Assert.AreEqual( "2.0", h.Document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}

	[TestMethod]
	public void RollbackAttemptsEveryStagedReleaseAndRetainsOriginalFailure()
	{
		var json = Fixture.Replace( "\"materials\":[{},{},{}]", "\"materials\":[{},{},{},{}]" ).Replace( "\"material\":0}]}", "\"material\":0,\"extensions\":{\"KHR_materials_variants\":{\"mappings\":[{\"material\":3,\"variants\":[0]}]}}}]}" );
		using var h = new Harness( json ); using var state = h.Asset.CreateState(); var before = state.Current; var original = new InvalidOperationException( "third acquisition" );
		h.BeforeAcquire = index => { if ( index == 3 ) throw original; }; h.OnRelease = value => { if ( value.Index == 1 ) throw new InvalidOperationException( "first staged cleanup" ); };
		var error = Assert.ThrowsException<AggregateException>( () => state.SetVariant( 0 ) ); Assert.IsTrue( error.Flatten().InnerExceptions.Contains( original ) );
		CollectionAssert.AreEquivalent( new[] { 1, 2 }, h.Released.Select( x => x.Index ).ToArray() ); Assert.AreSame( before, state.Current ); h.OnRelease = null;
	}

	[TestMethod]
	public void BorrowedResourcesSurviveFailedTransactionAndOwnerDisposal()
	{
		using var h = new Harness { Owned = false }; var state = h.Asset.CreateState(); h.BeforeAcquire = index => { if ( index == 2 ) throw new InvalidOperationException(); };
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); state.Dispose(); h.Asset.Dispose(); Assert.IsTrue( h.Created.All( x => x.Alive ) ); Assert.AreEqual( 2, h.Released.Count );
	}

	[TestMethod]
	public void AssetDisposalRefusesLiveStatesWithoutPartiallyReleasingAnything()
	{
		using var h = new Harness(); var a = h.Asset.CreateState(); var b = h.Asset.CreateState(); a.SetVariant( 0 );
		Assert.ThrowsException<InvalidOperationException>( h.Asset.Dispose ); Assert.AreEqual( 0, h.Released.Count ); a.Dispose(); Assert.ThrowsException<InvalidOperationException>( h.Asset.Dispose ); Assert.AreEqual( 0, h.Released.Count );
		b.SetVariant( 2 ); b.Dispose(); h.Asset.Dispose(); h.Asset.Dispose(); Assert.AreEqual( 3, h.Released.Count ); Assert.IsTrue( h.Created.All( x => !x.Alive ) ); Assert.AreEqual( "2.0", h.Document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}

	[TestMethod]
	public void FinalDisposalAttemptsAllOwnedReleasesEvenWhenOneThrows()
	{
		using var h = new Harness(); var state = h.Asset.CreateState(); state.SetVariant( 0 ); state.Dispose(); h.OnRelease = value => { if ( value.Index == 0 ) throw new InvalidOperationException( "release zero" ); };
		Assert.ThrowsException<AggregateException>( h.Asset.Dispose ); Assert.AreEqual( 3, h.Released.Count ); h.Asset.Dispose(); Assert.AreEqual( 3, h.Released.Count );
	}

	[TestMethod]
	public void ThreadGuardRejectsChangesAndDisposalBeforeMutatingState()
	{
		using var h = new Harness(); var state = h.Asset.CreateState(); var before = state.Current; h.Allowed = false;
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Assert.ThrowsException<InvalidOperationException>( state.Dispose ); Assert.ThrowsException<InvalidOperationException>( h.Asset.Dispose ); Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, h.Attempts.Count );
		h.Allowed = true; state.SetVariant( 0 ); state.Dispose();
	}

	[TestMethod]
	public void RecursiveSelectionCannotPublishDuringAnAcquisitionCallback()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current; h.BeforeAcquire = index => { if ( index == 1 ) state.SetVariant( 2 ); };
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); h.BeforeAcquire = null; state.SetVariant( 0 ); Indices( state, 1, 2, 0 );
	}

	[TestMethod]
	public void RecursiveDisposalCannotRetireOwnerDuringAcquisition()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); h.BeforeAcquire = index => { if ( index == 1 ) h.Asset.Dispose(); };
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) ); Indices( state, 0, 0, 0 ); h.BeforeAcquire = null; state.SetVariant( 2 ); Indices( state, 2, 1, 0 );
	}

	[TestMethod]
	public void SnapshotAndSlotInputsCannotBeMutatedByTheCaller()
	{
		var input = Slots.ToList(); using var h = new Harness( slots: input ); input.Clear(); using var state = h.Asset.CreateState(); var before = state.Current;
		Assert.AreEqual( 3, before.Bindings.Count ); Assert.ThrowsException<NotSupportedException>( () => ((IList<GltfMaterialVariantBinding<Fake>>)before.Bindings)[0] = before.Bindings[1] );
		state.SetVariant( 0 ); Assert.AreNotSame( before, state.Current ); Assert.IsNull( before.ActiveVariant ); CollectionAssert.AreEqual( new int?[] { 0, 0, 0 }, before.Bindings.Select( x => x.MaterialIndex ).ToArray() ); Assert.AreEqual( before.Revision + 1, state.Current.Revision );
	}

	[TestMethod]
	public void RepeatedSameSelectionReusesThePublishedSnapshotAndFiniteCache()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); state.SetVariant( 0 ); var before = state.Current;
		for ( var i = 0; i < 20; i++ ) state.SetVariant( 0 ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEqual( new[] { 0, 1, 2 }, h.Attempts );
	}

	[TestMethod]
	public void ImplicitDefaultMaterialHasItsOwnNullableIdentity()
	{
		using var h = new Harness( Fixture.Replace( ",\"material\":0}]}", "}]}" ) ); using var state = h.Asset.CreateState(); Indices( state, 0, 0, null ); state.SetVariant( 0 ); Indices( state, 1, 2, null ); CollectionAssert.AreEqual( new[] { 0, -1, 1, 2 }, h.Attempts );
	}

	[TestMethod]
	public void InvalidRequestedIndicesLeavePublishedStateAndCacheUnchanged()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var before = state.Current;
		Assert.ThrowsException<GltfException>( () => state.SetVariant( -1 ) ); Assert.ThrowsException<GltfException>( () => state.SetVariant( 3 ) ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts );
	}

	[TestMethod]
	public void UnsupportedOptionalExtensionUsesCoreMaterials()
	{
		using var h = new Harness( supports: false ); using var state = h.Asset.CreateState(); state.SetVariant( 0 ); Indices( state, 0, 0, 0 ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts );
	}

	[TestMethod]
	public void UnsupportedOptionalExtensionDoesNotCreateAnOutOfRangeActiveVariant()
	{
		using var h = new Harness( supports: false ); using var state = h.Asset.CreateState(); var before = state.Current;
		Assert.ThrowsException<GltfException>( () => state.SetVariant( 3 ) ); Assert.ThrowsException<GltfException>( () => state.SetVariant( -1 ) ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts );
	}

	[TestMethod]
	public void CoreAssetWithoutVariantsRejectsNonNullSelection()
	{
		var json = JsonNode.Parse( Fixture ); json.AsObject().Remove( "extensions" ); json.AsObject().Remove( "extensionsUsed" );
		foreach ( var mesh in json["meshes"].AsArray() ) mesh["primitives"][0].AsObject().Remove( "extensions" );
		using var h = new Harness( json.ToJsonString() ); using var state = h.Asset.CreateState(); var before = state.Current;
		Assert.ThrowsException<GltfException>( () => state.SetVariant( 0 ) ); Assert.AreSame( before, state.Current ); CollectionAssert.AreEqual( new[] { 0 }, h.Attempts );
	}

	[TestMethod]
	public void EmptySelectedSceneStillValidatesGlobalVariantIndicesWithoutAcquiringMaterials()
	{
		using var h = new Harness( slots: Array.Empty<GltfMaterialVariantSlot>() ); using var state = h.Asset.CreateState(); state.SetVariant( 0 ); var before = state.Current;
		Assert.AreEqual( 0, before.Bindings.Count ); Assert.AreEqual( 0, before.ActiveVariant ); Assert.AreEqual( 0, h.Attempts.Count ); Assert.ThrowsException<GltfException>( () => state.SetVariant( 3 ) ); Assert.ThrowsException<GltfException>( () => state.SetVariant( -1 ) ); Assert.AreSame( before, state.Current );
	}

	[TestMethod]
	public void SelectionDoesNotRewriteSourceUvsPoseOrDocument()
	{
		using var h = new Harness(); using var state = h.Asset.CreateState(); var json = h.Document.Root.GetRawText(); var uv = h.Document.MaterializePrimitiveUpload( 0, 0 ).Attributes["TEXCOORD_0"].Storage.Bytes.ToArray(); var pose = h.Document.EvaluatePose().GetWorldTransform( 0 );
		state.SetVariant( 0 ); state.SetVariant( 2 ); state.SetVariant( null ); Assert.AreEqual( json, h.Document.Root.GetRawText() ); CollectionAssert.AreEqual( uv, h.Document.MaterializePrimitiveUpload( 0, 0 ).Attributes["TEXCOORD_0"].Storage.Bytes.ToArray() ); Assert.AreEqual( pose, h.Document.EvaluatePose().GetWorldTransform( 0 ) );
	}

	[TestMethod]
	public void DisposedStateAndAssetRejectLaterUseButDoNotOwnTheDocument()
	{
		using var h = new Harness(); var state = h.Asset.CreateState(); state.Dispose(); state.Dispose(); Assert.ThrowsException<ObjectDisposedException>( () => state.SetVariant( 0 ) ); Assert.ThrowsException<ObjectDisposedException>( () => _ = state.Current ); h.Asset.Dispose(); Assert.ThrowsException<ObjectDisposedException>( () => h.Asset.CreateState() ); Assert.AreEqual( "2.0", h.Document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}
}
