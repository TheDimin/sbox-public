using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfVariantDrawStateTests
{
	private sealed record Packet( int Mesh, int? Material );
	private static readonly GltfMaterialVariantSlot[] Slots = [new( 0, 0, 0 ), new( 1, 1, 0 ), new( 2, 0, 0 )];
	private const string Fixture = """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
		 "extensions":{"KHR_materials_variants":{"variants":[{"name":"changed"},{"name":"unmapped"}]}},
		 "materials":[{},{},{}],"meshes":[
		 {"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]}]}}}]},
		 {"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":2,"variants":[0]}]}}}]}]}
		""";
	private static GltfDocument Open() => GltfReader.Read( Encoding.UTF8.GetBytes( Fixture ), supportedExtensions: new HashSet<string> { "KHR_materials_variants" } );

	[TestMethod]
	public void EachPrimitiveGetsItsOwnPacketWhileRepeatedNodesReuseThePacket()
	{
		using var document = Open(); var acquisitions = new List<Packet>(); var releases = new List<Packet>();
		using var asset = new GltfVariantDrawAsset<Packet>( document, Slots, (slot, material) =>
		{
			var packet = new Packet( slot.Mesh, material ); acquisitions.Add( packet );
			return new GltfMaterialLease<Packet>( packet, () => releases.Add( packet ), null );
		}, _ => true );
		using var first = asset.CreateState(); using var second = asset.CreateState();
		Assert.AreEqual( 2, acquisitions.Count );
		first.SetVariant( 0 );
		CollectionAssert.AreEqual( new int?[] { 1, 2, 1 }, first.Current.Bindings.Select( x => x.MaterialIndex ).ToArray() );
		CollectionAssert.AreEqual( new int?[] { 0, 0, 0 }, second.Current.Bindings.Select( x => x.MaterialIndex ).ToArray() );
		Assert.AreSame( first.Current.Bindings[0].Value, first.Current.Bindings[2].Value );
		Assert.AreNotSame( second.Current.Bindings[0].Value, second.Current.Bindings[1].Value );
		first.SetVariant( 1 ); CollectionAssert.AreEqual( new int?[] { 0, 0, 0 }, first.Current.Bindings.Select( x => x.MaterialIndex ).ToArray() );
		first.SetVariant( null ); Assert.IsNull( first.Current.ActiveVariant ); Assert.AreEqual( 4, acquisitions.Count );
	}

	[TestMethod]
	public void FailedLaterPrimitiveRollsBackAllNewPacketsAndPreservesPublishedState()
	{
		using var document = Open(); var released = new List<Packet>(); var fail = false;
		using var asset = new GltfVariantDrawAsset<Packet>( document, Slots, (slot, material) =>
		{
			if ( fail && slot.Mesh == 1 && material == 2 ) throw new InvalidOperationException( "injected" );
			var packet = new Packet( slot.Mesh, material ); return new GltfMaterialLease<Packet>( packet, () => released.Add( packet ), null );
		}, _ => true );
		using var state = asset.CreateState(); var before = state.Current; fail = true;
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0 ) );
		Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, released.Count ); Assert.AreEqual( 1, released[0].Material );
		fail = false; state.SetVariant( 0 ); Assert.AreEqual( 0, state.Current.ActiveVariant );
	}

	[TestMethod]
	public void ActiveStatesPreventOwnerDisposalAndEachPacketReleasesExactlyOnce()
	{
		using var document = Open(); var released = new List<Packet>();
		var asset = new GltfVariantDrawAsset<Packet>( document, Slots, (slot, material) =>
		{
			var packet = new Packet( slot.Mesh, material ); return new GltfMaterialLease<Packet>( packet, () => released.Add( packet ), null );
		}, _ => true );
		var first = asset.CreateState(); var second = asset.CreateState(); first.SetVariant( 0 ); first.Dispose();
		Assert.ThrowsException<InvalidOperationException>( asset.Dispose ); Assert.AreEqual( 0, released.Count );
		second.Dispose(); asset.Dispose(); asset.Dispose(); Assert.AreEqual( 4, released.Count );
	}

	[TestMethod]
	public void FailedClassificationPreflightReleasesStagedPacketsBeforePublication()
	{
		using var document = Open(); var released = new List<Packet>(); var created = 0;
		using var asset = new GltfVariantDrawAsset<Packet>( document, Slots, (slot, material) =>
		{
			created++; var packet = new Packet( slot.Mesh, material );
			return new GltfMaterialLease<Packet>( packet, () => released.Add( packet ), null );
		}, _ => true );
		using var state = asset.CreateState(); var before = state.Current;
		Assert.ThrowsException<InvalidOperationException>( () => state.SetVariant( 0, _ => throw new InvalidOperationException( "classification" ) ) );
		Assert.AreSame( before, state.Current ); Assert.AreEqual( 2, released.Count ); Assert.AreEqual( 4, created );
		state.SetVariant( 0 ); Assert.AreEqual( 6, created ); Assert.AreEqual( 0, state.Current.ActiveVariant );
	}

	[TestMethod]
	public void MissingAlternateImageDoesNotPublishOrRereadAfterSourceAppears()
	{
		var json = Fixture.Replace( "\"materials\":[{},{},{}]", "\"images\":[{\"uri\":\"later.png\"}],\"textures\":[{\"source\":0}],\"materials\":[{},{\"normalTexture\":{\"index\":0}},{}]" );
		var available = false; var reads = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ =>
		{
			reads++; if ( !available ) throw new System.IO.FileNotFoundException();
			return [137, 80, 78, 71, 13, 10, 26, 10];
		}, new HashSet<string> { "KHR_materials_variants" } );
		var snapshot = document.SnapshotMaterialImages( [0, 1, 2], 8 );
		using var asset = new GltfVariantDrawAsset<Packet>( document, Slots, (slot, material) =>
		{
			if ( material == 1 ) _ = snapshot.ReadImage( 0 );
			return new GltfMaterialLease<Packet>( new Packet( slot.Mesh, material ), () => { }, null );
		}, _ => true );
		using var state = asset.CreateState(); var before = state.Current; available = true;
		Assert.ThrowsException<GltfException>( () => state.SetVariant( 0 ) );
		Assert.AreSame( before, state.Current ); Assert.AreEqual( 1, reads );
	}
}
