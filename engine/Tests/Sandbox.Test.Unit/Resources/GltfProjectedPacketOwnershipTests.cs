using System;
using System.Text;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfProjectedPacketOwnershipTests
{
	private const string Source = """
		{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
		""";
	[TestMethod]
	public void ProjectedRouteRejectsForeignGenerationSlotAndUpload()
	{
		using var owner = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		using var other = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		var raw = GltfGpuPrimitivePlan.Create( owner, 0, 0 );
		var projected = raw.ProjectMaterial( null, 5 );
		projected.RequireSourceOwnership( owner, 0, 0, raw.Upload );
		Assert.ThrowsException<GltfException>( () => projected.RequireSourceOwnership( other, 0, 0, raw.Upload ) );
		Assert.ThrowsException<GltfException>( () => projected.RequireSourceOwnership( owner, 1, 0, raw.Upload ) );
		Assert.ThrowsException<GltfException>( () => projected.RequireSourceOwnership( owner, 0, 1, raw.Upload ) );
		Assert.ThrowsException<GltfException>( () => projected.RequireSourceOwnership( owner, 0, 0, owner.MaterializePrimitiveUpload( 0, 0 ) ) );
	}

	[TestMethod]
	public void CornerTopologyKeepsRawDescriptorsAndCopiesTheExplicitMap()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ) );
		var original = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		uint[] source = [2, 1, 0]; uint[] indices = [0, 1, 2];
		var expanded = original.WithCornerTopology( source, indices ); source[0] = 0; indices[0] = 2;
		CollectionAssert.AreEqual( new uint[] { 2, 1, 0 }, expanded.SourceVertices );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2 }, expanded.Indices );
		Assert.AreSame( original.InputBytes, expanded.InputBytes ); Assert.AreSame( original.InputDescriptors, expanded.InputDescriptors );
		Assert.AreSame( original.Upload, expanded.Upload ); Assert.AreEqual( original.VertexStride, expanded.VertexStride );
		Assert.ThrowsException<GltfException>( () => original.WithCornerTopology( [0, 1, 2], [0, 2, 1] ) );
		Assert.ThrowsException<GltfException>( () => original.WithCornerTopology( [0, 1, 3], [0, 1, 2] ) );
	}
}
