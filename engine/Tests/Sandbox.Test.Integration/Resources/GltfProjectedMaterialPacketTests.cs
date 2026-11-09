using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using System;
using System.Text;

namespace ResourceTests;

[TestClass]
public sealed class GltfProjectedMaterialPacketTests
{
	private const string Source = """
		{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
		""";

	[TestMethod]
	public void NullPlanDoesNotTransferMaterialLease()
	{
		var material = Material.Load( "materials/default/white.vmat" ); var released = 0;
		var lease = new GltfMaterialLease<Material>( material, () => released++, null );
		Assert.ThrowsException<ArgumentNullException>( () => GltfProjectedMaterialPacket.Acquire( null, lease ) );
		Assert.AreEqual( 0, released ); lease.Dispose(); Assert.AreEqual( 1, released );
	}

	[TestMethod]
	public void RawPlanFailureReleasesTransferredMaterialLease()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var material = Material.Load( "materials/default/white.vmat" ); var released = 0;
		var lease = new GltfMaterialLease<Material>( material, () => released++, null );
		Assert.ThrowsException<GltfException>( () => GltfProjectedMaterialPacket.Acquire( plan, lease ) );
		Assert.AreEqual( 1, released ); lease.Dispose(); Assert.AreEqual( 1, released );
	}

	[TestMethod]
	[System.Diagnostics.CodeAnalysis.SuppressMessage( "Reliability", "CA2000", Justification = "The packet takes ownership of the lease; the test disposes the packet." )]
	public void ProjectedPacketOwnsItsMaterialLeaseUntilDisposed()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ).ProjectMaterial( null, 5 );
		var material = Material.Load( "materials/default/white.vmat" ); var released = 0;
		var lease = new GltfMaterialLease<Material>( material, () => released++, null );
		var packet = GltfProjectedMaterialPacket.Acquire( plan, lease );
		Assert.AreSame( plan, packet.Plan ); Assert.AreSame( material, packet.Material ); Assert.IsNotNull( packet.OutputDescriptors );
		Assert.AreEqual( 0, released ); packet.Dispose(); packet.Dispose(); Assert.AreEqual( 1, released );
	}
}
