using System;
using System.Buffers.Binary;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Verifier self-tests only; synthetic snapshots are never engine GPU evidence.</summary>
[TestClass]
public sealed class TextureTransformGpuVerifierTests
{
	[TestMethod]
	public void ExplicitRuntimeOffsetsAndPoisonedPaddingAreUsed()
	{
		var snapshots = Fixture();
		var measurement = TextureTransformGpuChecks.MeasureSnapshots( snapshots, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
		TextureTransformGpuChecks.RequireWithin( measurement, 0, 0, 0 );
		Assert.AreEqual( 12, measurement.Primitives ); Assert.AreEqual( 72, measurement.Vertices );
	}

	[TestMethod]
	public void IncorrectUvAndAnUndeclaredBakedMappingCannotPass()
	{
		var snapshots = Fixture();
		var field = snapshots[0].Fields.Single( value => value.Semantic == "TEXCOORD" );
		BinaryPrimitives.WriteSingleLittleEndian( snapshots[0].Bytes.AsSpan( field.Offset ), .25f );
		var measurement = TextureTransformGpuChecks.MeasureSnapshots( snapshots, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.RequireWithin( measurement, 0, 0, 0 ) );
		var wrongPolicy = TextureTransformGpuChecks.MeasureSnapshots( Fixture(), NMatrix.Identity, TextureTransformUvRepresentation.BakedBaseColor );
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.RequireWithin( wrongPolicy, 0, 0, 0 ) );
	}

	[TestMethod]
	public void NonfinitePositionsAndReversedTrianglesAreRejected()
	{
		var snapshots = Fixture();
		BinaryPrimitives.WriteSingleLittleEndian( snapshots[0].Bytes.AsSpan( 8 ), float.NaN );
		var nonfinite = TextureTransformGpuChecks.MeasureSnapshots( snapshots, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.RequireWithin( nonfinite, 0, 0, 0 ) );
		snapshots = Fixture(); (snapshots[0].Indices[1], snapshots[0].Indices[2]) = (snapshots[0].Indices[2], snapshots[0].Indices[1]);
		var winding = TextureTransformGpuChecks.MeasureSnapshots( snapshots, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.RequireWithin( winding, 0, 0, 0 ) );
	}

	[TestMethod]
	public void MissingNodeAndIncorrectNormalsDoNotBecomeEmptyPasses()
	{
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.MeasureSnapshots( Fixture()[..11], NMatrix.Identity, TextureTransformUvRepresentation.RawSource ) );
		var snapshots = Fixture(); BinaryPrimitives.WriteSingleLittleEndian( snapshots[0].Bytes.AsSpan( 28 ), -1 );
		var badNormal = TextureTransformGpuChecks.MeasureSnapshots( snapshots, NMatrix.Identity, TextureTransformUvRepresentation.RawSource );
		Assert.ThrowsException<InvalidOperationException>( () => TextureTransformGpuChecks.RequireWithin( badNormal, 0, 0, 0 ) );
	}

	private static GltfGpuPrimitiveSnapshot[] Fixture()
	{
		return Enumerable.Range( 0, 12 ).Select( node =>
		{
			var bytes = Enumerable.Repeat( (byte)0xD7, 6 * 68 ).ToArray();
			for ( var output = 0; output < 6; output++ )
			{
				var source = (int)TextureTransformReference.Indices[output];
				var position = TextureTransformReference.EnginePosition( node, source );
				var uv = TextureTransformReference.SourceUv( TextureTransformReference.NodeMeshes[node], source );
				Float( bytes, output * 68 + 8, position.X ); Float( bytes, output * 68 + 12, position.Y ); Float( bytes, output * 68 + 16, position.Z );
				Float( bytes, output * 68 + 28, 1 ); Float( bytes, output * 68 + 32, 0 ); Float( bytes, output * 68 + 36, 0 );
				Float( bytes, output * 68 + 52, uv.X ); Float( bytes, output * 68 + 56, uv.Y );
			}
			return new GltfGpuPrimitiveSnapshot { Node = node, Mesh = TextureTransformReference.NodeMeshes[node], Primitive = 0,
				VertexCount = 6, VertexStride = 68, Bytes = bytes, SourceVertices = (uint[])TextureTransformReference.Indices.Clone(),
				Indices = new uint[] { 0, 1, 2, 3, 4, 5 }, Fields = new[] { new GltfGpuField( "NORMAL", 0, 28, 3 ), new GltfGpuField( "TEXCOORD", 0, 52, 2 ), new GltfGpuField( "POSITION", 0, 8, 3 ) } };
		} ).ToArray();
	}

	private static void Float( byte[] bytes, int offset, float value ) => BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( offset ), value );
}
