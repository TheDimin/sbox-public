using System;
using System.Buffers.Binary;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.Validation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NMatrix = System.Numerics.Matrix4x4;

/// <summary>Verifier sensitivity controls. Synthetic rows do not establish any GPU execution.</summary>
[TestClass]
public class AnimatedMorphCubeGpuVerifierTests
{
	[TestMethod]
	public void VerifierReadsDeclaredOffsetsAndIgnoresUnusedPadding()
	{
		using var form = Form(); using var sample = Sample(); var row = Snapshot();
		for ( var vertex = 0; vertex < 3; vertex++ ) BinaryPrimitives.WriteInt32LittleEndian( row.Bytes.AsSpan( vertex * 36 ), 0x7fc00000 );
		var measured = AnimatedMorphCubeGpuChecks.Measure( new[] { row }, form.RootElement, sample.RootElement, NMatrix.Identity );
		Assert.IsTrue( measured.Finite ); Assert.IsTrue( measured.SourceTopologyMatches );
		Assert.AreEqual( 0f, measured.MaximumPositionErrorInches ); Assert.AreEqual( 0f, measured.MaximumNormalError );
		AnimatedMorphCubeGpuChecks.RequireWithin( measured, 0, 0 );
	}

	[TestMethod]
	public void PositionAndNormalMutationsAreBothDetected()
	{
		using var form = Form(); using var sample = Sample();
		var position = Snapshot(); BinaryPrimitives.WriteSingleLittleEndian( position.Bytes.AsSpan( 4 ), 2 );
		var positionError = AnimatedMorphCubeGpuChecks.Measure( new[] { position }, form.RootElement, sample.RootElement, NMatrix.Identity );
		Assert.AreEqual( 2f, positionError.MaximumPositionErrorInches );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireWithin( positionError, 0, 0 ) );
		var normal = Snapshot(); BinaryPrimitives.WriteSingleLittleEndian( normal.Bytes.AsSpan( 20 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( normal.Bytes.AsSpan( 24 ), 1 );
		var normalError = AnimatedMorphCubeGpuChecks.Measure( new[] { normal }, form.RootElement, sample.RootElement, NMatrix.Identity );
		Assert.IsTrue( normalError.MaximumNormalError > 1 );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireWithin( normalError, 0, 0 ) );
	}

	[TestMethod]
	public void ReversedWindingCannotPassAsReorderedTriangles()
	{
		using var form = Form(); using var sample = Sample(); var row = Snapshot();
		(row.Indices[1], row.Indices[2]) = (row.Indices[2], row.Indices[1]);
		var measured = AnimatedMorphCubeGpuChecks.Measure( new[] { row }, form.RootElement, sample.RootElement, NMatrix.Identity );
		Assert.IsFalse( measured.SourceTopologyMatches );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireWithin( measured, 0, 0 ) );
	}

	[TestMethod]
	public void NonfiniteOutputCannotBeHiddenByNumericalTolerance()
	{
		using var form = Form(); using var sample = Sample(); var row = Snapshot();
		BinaryPrimitives.WriteInt32LittleEndian( row.Bytes.AsSpan( 4 ), 0x7fc00000 );
		var measured = AnimatedMorphCubeGpuChecks.Measure( new[] { row }, form.RootElement, sample.RootElement, NMatrix.Identity );
		Assert.IsFalse( measured.Finite );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireWithin( measured, 100, 100 ) );
	}

	[TestMethod]
	public void AuthoredTangentDirectionAndHandednessMutationsAreDetected()
	{
		using var sample = Sample(); var row = TangentSnapshot();
		var correct = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.IsTrue( correct.AuthoredDirectionCompared );
		AnimatedMorphCubeGpuChecks.RequireTangentsWithin( correct, 0, 0, 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 48 ), -1 );
		var handedness = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.AreEqual( 2f, handedness.MaximumHandednessError );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( handedness, 100, 100, 100 ) );
		row = TangentSnapshot();
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 40 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 44 ), 1 );
		var direction = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.IsTrue( direction.MaximumDirectionError > 1 );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( direction, 0, 0, 0 ) );
	}

	[TestMethod]
	public void GeneratedTangentCannotPassWithWrongLengthOrNormalOrthogonality()
	{
		using var sample = JsonDocument.Parse( "{\"tangents\":null,\"nodeHandedness\":1}" );
		var row = TangentSnapshot();
		var correct = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.IsFalse( correct.AuthoredDirectionCompared );
		AnimatedMorphCubeGpuChecks.RequireTangentsWithin( correct, 0, 0, 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 36 ), 2 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 40 ), 0 );
		var wrong = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.AreEqual( 1f, wrong.MaximumLengthError ); Assert.AreEqual( 2f, wrong.MaximumNormalDot );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( wrong, 0, 0, 0 ) );
	}

	[TestMethod]
	public void OneUndefinedSourceDirectionKeepsFallbackAndOtherDirectionChecks()
	{
		using var sample = UndefinedSample(); var row = TangentSnapshot();
		// A different finite/unit/perpendicular fallback is allowed at vertex 0
		// only. Its exact source TBN is degenerate, so no XYZ oracle is invented.
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 40 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 44 ), 1 );
		var observed = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.AreEqual( 2, observed.DirectionComparedVertices ); Assert.AreEqual( 1, observed.UndefinedAuthoredDirectionVertices );
		AnimatedMorphCubeGpuChecks.RequireTangentsWithin( observed, 0, 0, 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 44 ), 0 );
		var zeroFallback = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( zeroFallback, 0, 0, 0 ) );
		row = TangentSnapshot();
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 64 + 40 ), 0 );
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 64 + 44 ), 1 );
		var wrongDefinedDirection = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.IsTrue( wrongDefinedDirection.MaximumDirectionError > 1 );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( wrongDefinedDirection, 0, 0, 0 ) );
	}

	[TestMethod]
	public void UndefinedDirectionCannotWaiveHandednessOrOmitIndependentReason()
	{
		using var sample = UndefinedSample(); var row = TangentSnapshot();
		BinaryPrimitives.WriteSingleLittleEndian( row.Bytes.AsSpan( 48 ), -1 );
		var wrongSign = AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, sample.RootElement, NMatrix.Identity );
		Assert.AreEqual( 2f, wrongSign.MaximumHandednessError );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.RequireTangentsWithin( wrongSign, 100, 100, 100 ) );
		using var unexplained = JsonDocument.Parse( sample.RootElement.GetRawText().Replace( "morphed_tangent_parallel_to_normal", "not_a_source_diagnosis", StringComparison.Ordinal ) );
		Assert.ThrowsException<InvalidOperationException>( () => AnimatedMorphCubeGpuChecks.MeasureTangents( new[] { row }, unexplained.RootElement, NMatrix.Identity ) );
	}

	private static JsonDocument Form() => JsonDocument.Parse( """
		{"node":0,"mesh":0,"vertexCount":3,"sourceIndices":[0,1,2]}
		""" );
	private static JsonDocument Sample() => JsonDocument.Parse( """
		{"time":0,"positionsInches":[[0,0,0],[0,1,0],[0,0,1]],"normals":[[1,0,0],[1,0,0],[1,0,0]],
		"nodeHandedness":1,"tangents":[[0,1,0,1],[0,1,0,1],[0,1,0,1]],
		"tangentHandedness":[1,1,1],"undefinedTangentDirections":[]}
		""" );
	private static JsonDocument UndefinedSample() => JsonDocument.Parse( """
		{"time":0,"positionsInches":[[0,0,0],[0,1,0],[0,0,1]],"normals":[[1,0,0],[1,0,0],[1,0,0]],
		"nodeHandedness":1,"tangents":[null,[0,1,0,1],[0,1,0,1]],"tangentHandedness":[1,1,1],
		"undefinedTangentDirections":[{"vertex":0,"reason":"morphed_tangent_parallel_to_normal"}]}
		""" );
	private static GltfGpuPrimitiveSnapshot Snapshot()
	{
		var bytes = new byte[108];
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 36 + 8 ), 1 );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 72 + 12 ), 1 );
		for ( var vertex = 0; vertex < 3; vertex++ ) BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 36 + 20 ), 1 );
		return new GltfGpuPrimitiveSnapshot { Node = 0, Mesh = 0, Primitive = 0, VertexCount = 3, VertexStride = 36,
			Fields = new[] { new GltfGpuField( "POSITION", 0, 4, 3 ), new GltfGpuField( "NORMAL", 0, 20, 3 ) },
			SourceVertices = new uint[] { 0, 1, 2 }, Indices = new uint[] { 0, 1, 2 }, Bytes = bytes };
	}

	private static GltfGpuPrimitiveSnapshot TangentSnapshot()
	{
		var basic = Snapshot(); var bytes = new byte[192];
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			basic.Bytes.AsSpan( vertex * 36, 36 ).CopyTo( bytes.AsSpan( vertex * 64 ) );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 64 + 40 ), 1 );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( vertex * 64 + 48 ), 1 );
		}
		return new GltfGpuPrimitiveSnapshot { Node = 0, Mesh = 0, Primitive = 0, VertexCount = 3, VertexStride = 64,
			Fields = new[] { new GltfGpuField( "POSITION", 0, 4, 3 ), new GltfGpuField( "NORMAL", 0, 20, 3 ), new GltfGpuField( "TANGENT", 0, 36, 4 ) },
			SourceVertices = basic.SourceVertices, Indices = basic.Indices, Bytes = bytes };
	}
}
