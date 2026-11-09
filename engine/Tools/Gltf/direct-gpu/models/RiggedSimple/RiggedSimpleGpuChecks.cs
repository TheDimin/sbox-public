using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.Tests;

using Vector3 = global::System.Numerics.Vector3;
using Quaternion = global::System.Numerics.Quaternion;
using Matrix4x4 = global::System.Numerics.Matrix4x4;

/// <summary>
/// Measures the submitted GPU output against the independent source reference.
/// This class never renders or creates a model and never chooses an acceptance
/// tolerance. The validation receipt must record the measured bounds separately.
/// </summary>
internal static class RiggedSimpleGpuChecks
{
	public static RiggedSimpleGpuComparison Measure( RiggedSimpleReference reference, float? time,
		Matrix4x4 placement, IReadOnlyList<GltfGpuPrimitiveSnapshot> snapshots )
	{
		ArgumentNullException.ThrowIfNull( reference ); ArgumentNullException.ThrowIfNull( snapshots );
		if ( snapshots.Count != 1 ) throw new InvalidDataException( "RiggedSimple requires exactly one drawn primitive snapshot." );
		var snapshot = snapshots[0];
		if ( snapshot.Node != reference.MeshNode || snapshot.Mesh != reference.MeshIndex || snapshot.Primitive != 0 )
			throw new InvalidDataException( "GPU readback identifies a different source primitive." );
		if ( snapshot.VertexCount <= 0 || snapshot.VertexStride <= 0 || snapshot.Bytes is null ||
			(long)snapshot.VertexCount * snapshot.VertexStride != snapshot.Bytes.LongLength )
			throw new InvalidDataException( "GPU readback byte range differs from its vertex declaration." );
		if ( snapshot.SourceVertices is null || snapshot.SourceVertices.Length != snapshot.VertexCount )
			throw new InvalidDataException( "GPU readback is missing the source vertex mapping." );
		if ( snapshot.IndexDataOrigin != "gpu-readback" )
			throw new InvalidDataException( "Bound index evidence must come from actual GPU readback, not CPU submission metadata." );
		if ( snapshot.Indices is null || snapshot.Indices.Length != reference.Indices.Length )
			throw new InvalidDataException( "GPU readback topology count differs from the source triangles." );
		foreach ( var source in snapshot.SourceVertices )
			if ( source >= reference.VertexCount ) throw new InvalidDataException( "GPU output maps outside the source vertex count." );
		var reverseWinding = placement.GetDeterminant() < 0;
		for ( var i = 0; i < snapshot.Indices.Length; i++ )
		{
			// RiggedSimple authored ancestors and animated joint scales have positive
			// determinant. An object reflection reverses each triangle for culling.
			var sourceIndex = reverseWinding && i % 3 != 0 ? i + (i % 3 == 1 ? 1 : -1) : i;
			var output = snapshot.Indices[i];
			if ( output >= snapshot.VertexCount || snapshot.SourceVertices[output] != reference.Indices[sourceIndex] )
				throw new InvalidDataException( "GPU output source mapping changes triangle topology or winding." );
		}
		var position = Field( snapshot, "POSITION" ); var normal = Field( snapshot, "NORMAL" );
		var expected = reference.ToEngine( reference.Evaluate( time ), placement );
		var actualPositions = new Vector3[snapshot.VertexCount]; var actualNormals = new Vector3[snapshot.VertexCount];
		var expectedPositions = new Vector3[snapshot.VertexCount]; var expectedNormals = new Vector3[snapshot.VertexCount];
		var positionErrors = new double[snapshot.VertexCount]; var normalErrors = new double[snapshot.VertexCount];
		var angularErrors = new double[snapshot.VertexCount];
		for ( var output = 0; output < snapshot.VertexCount; output++ )
		{
			var source = snapshot.SourceVertices[output];
			actualPositions[output] = Read( snapshot.Bytes, output * snapshot.VertexStride + position.Offset );
			actualNormals[output] = Read( snapshot.Bytes, output * snapshot.VertexStride + normal.Offset );
			expectedPositions[output] = expected.Positions[source]; expectedNormals[output] = expected.Normals[source];
			positionErrors[output] = Distance( expectedPositions[output], actualPositions[output] );
			normalErrors[output] = Distance( expectedNormals[output], actualNormals[output] );
			var normalLength = actualNormals[output].Length();
			if ( normalLength == 0 || !float.IsFinite( normalLength ) ) throw new InvalidDataException( "GPU produced a zero/nonfinite normal." );
			var expectedNormal = expectedNormals[output]; var actualNormal = actualNormals[output];
			var dot = (double)expectedNormal.X * actualNormal.X + (double)expectedNormal.Y * actualNormal.Y + (double)expectedNormal.Z * actualNormal.Z;
			var expectedLength = Math.Sqrt( (double)expectedNormal.X * expectedNormal.X + (double)expectedNormal.Y * expectedNormal.Y + (double)expectedNormal.Z * expectedNormal.Z );
			var actualLength = Math.Sqrt( (double)actualNormal.X * actualNormal.X + (double)actualNormal.Y * actualNormal.Y + (double)actualNormal.Z * actualNormal.Z );
			var cosine = dot / (expectedLength * actualLength);
			angularErrors[output] = Math.Acos( Math.Clamp( cosine, -1, 1 ) ) * (180 / Math.PI);
		}
		return new RiggedSimpleGpuComparison
		{
			Status = "measured-not-accepted", SourceTime = time, VertexCount = snapshot.VertexCount,
			PositionMaximumInches = positionErrors.Max(), PositionRmsInches = Rms( positionErrors ),
			NormalMaximumDistance = normalErrors.Max(), NormalRmsDistance = Rms( normalErrors ),
			NormalMaximumAngleDegrees = angularErrors.Max(), NormalRmsAngleDegrees = Rms( angularErrors ),
			ExpectedPositions = expectedPositions, ActualPositions = actualPositions,
			ExpectedNormals = expectedNormals, ActualNormals = actualNormals,
			PositionErrorsInches = positionErrors, NormalErrors = normalErrors, NormalAnglesDegrees = angularErrors
		};
	}

	private static GltfGpuField Field( GltfGpuPrimitiveSnapshot snapshot, string semantic )
	{
		if ( snapshot.Fields is null ) throw new InvalidDataException( "GPU output declaration is absent." );
		var matches = snapshot.Fields.Where( field => field.Semantic == semantic && field.SemanticIndex == 0 ).ToArray();
		if ( matches.Length != 1 ) throw new InvalidDataException( "GPU output requires one unambiguous " + semantic + " field." );
		var value = matches[0];
		if ( value.Components != 3 || value.ComponentType != 5126 || value.Offset < 0 || value.Offset > snapshot.VertexStride - 12 || value.Offset % 4 != 0 )
			throw new InvalidDataException( "Unsupported or invalid output field declaration for " + semantic + "." );
		return value;
	}

	private static Vector3 Read( byte[] bytes, int at )
	{
		var result = new Vector3( BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( at, 4 ) ),
			BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( at + 4, 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( at + 8, 4 ) ) );
		if ( !float.IsFinite( result.X ) || !float.IsFinite( result.Y ) || !float.IsFinite( result.Z ) )
			throw new InvalidDataException( "GPU output contains a nonfinite coordinate." );
		return result;
	}
	private static double Distance( Vector3 a, Vector3 b ) => Math.Sqrt( Math.Pow( (double)a.X - b.X, 2 ) + Math.Pow( (double)a.Y - b.Y, 2 ) + Math.Pow( (double)a.Z - b.Z, 2 ) );
	private static double Rms( double[] values ) => Math.Sqrt( values.Sum( value => value * value ) / values.Length );
}

public sealed class RiggedSimpleGpuComparison
{
	public string Status { get; init; }
	public float? SourceTime { get; init; }
	public int VertexCount { get; init; }
	public double PositionMaximumInches { get; init; }
	public double PositionRmsInches { get; init; }
	public double NormalMaximumDistance { get; init; }
	public double NormalRmsDistance { get; init; }
	public double NormalMaximumAngleDegrees { get; init; }
	public double NormalRmsAngleDegrees { get; init; }
	public Vector3[] ExpectedPositions { get; init; }
	public Vector3[] ActualPositions { get; init; }
	public Vector3[] ExpectedNormals { get; init; }
	public Vector3[] ActualNormals { get; init; }
	public double[] PositionErrorsInches { get; init; }
	public double[] NormalErrors { get; init; }
	public double[] NormalAnglesDegrees { get; init; }
}
