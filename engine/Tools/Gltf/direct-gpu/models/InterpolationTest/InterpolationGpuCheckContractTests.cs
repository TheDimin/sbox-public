using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

using Assert = global::Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>CPU-only checks of the measurement boundary, never evidence of a GPU draw.</summary>
[TestClass]
public sealed class InterpolationGpuCheckContractTests
{
	[DataTestMethod]
	[DataRow( "" )]
	[DataRow( "cpu-submission-metadata" )]
	public void SubmittedIndexMetadataCannotMasqueradeAsGpuReadback( string origin )
	{
		using var input = new InterpolationConformanceTests.Inputs( InterpolationConformanceTests.CheckoutRoot, "glTF" );
		var rows = Enumerable.Range( 0, 10 ).Select( node => new GltfGpuPrimitiveSnapshot
		{
			Node = node, Mesh = node == 9 ? 1 : 0, Primitive = 0,
			VertexCount = 1, VertexStride = 24,
			Fields = [new GltfGpuField( "POSITION", 0, 0, 3 ), new GltfGpuField( "NORMAL", 0, 12, 3 )],
			Bytes = new byte[24], SourceVertices = [0], Indices = new uint[node == 9 ? 6 : 36],
			IndexDataOrigin = origin
		} ).ToArray();
		var exception = Assert.ThrowsException<InvalidDataException>( () => InterpolationGpuChecks.Measure(
			InterpolationConformanceTests.CheckoutRoot, input.Reference, null, 0,
			global::System.Numerics.Matrix4x4.Identity, rows ) );
		Assert.IsTrue( exception.Message.Contains( "actual GPU index", StringComparison.Ordinal ),
			"The control must fail on absent GPU index provenance, not a later numeric mismatch." );
	}
}
