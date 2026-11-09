using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
public class GltfGpuMorphsTests
{
	[TestMethod]
	public void BindingsKeepNodeTargetSemanticAndStreamWithoutRepacking()
	{
		using var document = Fixture();
		var upload = document.MaterializeMorphTargetsUpload( 0, 0 );
		var bindings = GltfGpuMorphs.CreateBindings( 0, upload );
		Assert.AreEqual( 2, bindings.Count );
		Assert.AreEqual( 0, bindings[0].Node ); Assert.AreEqual( 0, bindings[0].TargetIndex );
		Assert.AreEqual( "POSITION", bindings[0].Semantic );
		Assert.AreSame( upload.Targets[0]["POSITION"], bindings[0].Stream );
		Assert.AreEqual( 1, bindings[1].TargetIndex ); Assert.AreEqual( "NORMAL", bindings[1].Semantic );
		Assert.AreSame( upload.Targets[1]["NORMAL"], bindings[1].Stream );
		Assert.IsTrue( bindings.All( binding => binding.Stream.ByteStride == 12 ) );
	}

	[TestMethod]
	public void PoseWeightsAreSignedUnboundedAndOwnedByEachConsumer()
	{
		using var document = Fixture();
		var pose = document.EvaluatePose();
		var first = GltfGpuMorphs.ResolveWeights( pose, 0, 2 );
		var second = GltfGpuMorphs.ResolveWeights( pose, 0, 2 );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, first );
		Assert.AreNotSame( first, second ); first[0] = 99;
		CollectionAssert.AreEqual( new[] { -2f, 3f }, second );
		CollectionAssert.AreEqual( new[] { -2f, 3f }, pose.GetMorphWeights( 0 ) );
		CollectionAssert.AreEqual( new[] { 0f, 0f }, GltfGpuMorphs.ResolveWeights( pose, 1, 2 ) );
	}

	[TestMethod]
	public void MismatchedTargetCountsAreErrorsRatherThanTruncationOrPadding()
	{
		using var document = Fixture(); var pose = document.EvaluatePose();
		Assert.ThrowsException<GltfException>( () => GltfGpuMorphs.ResolveWeights( pose, 0, 1 ) );
		Assert.ThrowsException<GltfException>( () => GltfGpuMorphs.ResolveWeights( pose, 0, 3 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfGpuMorphs.ResolveWeights( pose, 0, -1 ) );
	}

	private static GltfDocument Fixture()
	{
		var normalBytes = new byte[36];
		for ( var vertex = 0; vertex < 3; vertex++ ) BinaryPrimitives.WriteSingleLittleEndian( normalBytes.AsSpan( vertex * 12 + 8 ), 1 );
		return GltfReader.Read( Encoding.UTF8.GetBytes( $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( normalBytes )}}}","byteLength":36}],
			"bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]},
			{"bufferView":0,"componentType":5126,"type":"VEC3","count":3}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"targets":[{"POSITION":0},{"NORMAL":0}]}]}],
			"nodes":[{"mesh":0,"weights":[-2,3]},{"mesh":0}]}
			""" ) );
	}
}
