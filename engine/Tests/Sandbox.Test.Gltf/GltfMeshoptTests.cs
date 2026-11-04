using System;
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

// Known-answer vectors adapted from meshoptimizer demo/tests.cpp, commit
// ce321f27536cdbc61ef86622d8ef11fedf5492a8. Copyright (c) 2016-2026
// Arseny Kapoulkine; MIT license in Fixtures/meshoptimizer-LICENSE.md.
// Encoded arrays checked byte-for-byte against that immutable source. Source SHA256:
// 0afe6ada60aa904c3ea82d8c2c340064f1df9bfcb81361e9b36be419257fd0e4.
[TestClass]
public sealed class GltfMeshoptTests
{
	private static readonly byte[] IndexSequenceV1 =
	[
		0xd1, 0x00, 0x04, 0xcd, 0x01, 0x04, 0x07, 0x98, 0x1f, 0x00, 0x00, 0x00, 0x00
	];

	private static readonly byte[] TriangleIndicesV0 =
	[
		0xe0, 0xf0, 0x10, 0xfe, 0xff, 0xf0, 0x0c, 0xff, 0x02, 0x02, 0x02, 0x00, 0x76, 0x87, 0x56, 0x67,
		0x78, 0xa9, 0x86, 0x65, 0x89, 0x68, 0x98, 0x01, 0x69, 0x00, 0x00
	];

	private static readonly byte[] VerticesV0 =
	[
		0xa0, 0x01, 0x3f, 0x00, 0x00, 0x00, 0x58, 0x57, 0x58, 0x01, 0x26, 0x00, 0x00, 0x00, 0x01, 0x0c,
		0x00, 0x00, 0x00, 0x58, 0x01, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x3f, 0x00,
		0x00, 0x00, 0x17, 0x18, 0x17, 0x01, 0x26, 0x00, 0x00, 0x00, 0x01, 0x0c, 0x00, 0x00, 0x00, 0x17,
		0x01, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00
	];

	private static readonly byte[] VerticesV1 =
	[
		0xa1, 0xee, 0xaa, 0xee, 0x00, 0x4b, 0x4b, 0x4b, 0x00, 0x00, 0x4b, 0x00, 0x00, 0x7d, 0x7d, 0x7d,
		0x00, 0x00, 0x7d, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
		0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x62, 0x00, 0x62
	];

	internal static byte[] VertexFixtureV0 => (byte[])VerticesV0.Clone();
	internal static byte[] VertexFixtureV1 => (byte[])VerticesV1.Clone();

	private static uint[] UInt32s( byte[] data )
	{
		Assert.AreEqual( 0, data.Length % 4 );
		var values = new uint[data.Length / 4];
		for ( var i = 0; i < values.Length; i++ )
			values[i] = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( i * 4, 4 ) );
		return values;
	}

	private static ushort[] UInt16s( byte[] data )
	{
		Assert.AreEqual( 0, data.Length % 2 );
		var values = new ushort[data.Length / 2];
		for ( var i = 0; i < values.Length; i++ )
			values[i] = BinaryPrimitives.ReadUInt16LittleEndian( data.AsSpan( i * 2, 2 ) );
		return values;
	}

	[TestMethod]
	public void DecodesUpstreamIndexSequenceV1()
	{
		CollectionAssert.AreEqual( new uint[] { 0, 1, 51, 2, 49, 1000 },
			UInt32s( GltfMeshopt.Decode( IndexSequenceV1, 6, 4, "INDICES" ) ) );
	}

	[TestMethod]
	public void DecodesUpstreamIndexSequenceToSixteenBitValues()
	{
		CollectionAssert.AreEqual( new ushort[] { 0, 1, 51, 2, 49, 1000 },
			UInt16s( GltfMeshopt.Decode( IndexSequenceV1, 6, 2, "INDICES" ) ) );
	}

	[TestMethod]
	public void DecodesUpstreamTriangleIndicesV0()
	{
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 2, 1, 3, 4, 6, 5, 7, 8, 9 },
			UInt32s( GltfMeshopt.Decode( TriangleIndicesV0, 12, 4, "TRIANGLES" ) ) );
	}

	[TestMethod]
	public void DecodesUpstreamAttributeStreamsV0AndV1()
	{
		ushort[] expected =
		[
			0, 0, 0, 0, 0, 0,
			300, 0, 0, 0, 500, 0,
			0, 300, 0, 0, 0, 500,
			300, 300, 0, 0, 500, 500
		];
		CollectionAssert.AreEqual( expected, UInt16s( GltfMeshopt.Decode( VerticesV0, 4, 12, "ATTRIBUTES" ) ) );
		CollectionAssert.AreEqual( expected, UInt16s( GltfMeshopt.Decode( VerticesV1, 4, 12, "ATTRIBUTES" ) ) );
	}

	[TestMethod]
	public void ExponentialFilterRunsAfterAttributeDecode()
	{
		// Apply the spec's signed 24-bit mantissa and 8-bit exponent to the
		// upstream V0 output in 32-bit groups, without an encoder round trip.
		var output = UInt32s( GltfMeshopt.Decode( VerticesV0, 4, 12, "ATTRIBUTES", "EXPONENTIAL" ) );
		float[] actual = Array.ConvertAll( output, bits => BitConverter.Int32BitsToSingle( unchecked((int)bits) ) );
		float[] expected = [0, 0, 0, 300, 0, 500, 5767168, 0, -1572864, 5767768, 0, -1571864];
		CollectionAssert.AreEqual( expected, actual );
	}

	[TestMethod]
	public void RejectsTruncatedAndMalformedInputs()
	{
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( [], 6, 4, "INDICES" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( [0], 6, 4, "INDICES" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( TriangleIndicesV0, 11, 4, "TRIANGLES" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( IndexSequenceV1, -1, 4, "INDICES" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( IndexSequenceV1, 6, 1, "INDICES" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( IndexSequenceV1, 6, 4, "BAD_MODE" ) );
		Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( VerticesV0, 4, 12, "ATTRIBUTES", "BAD_FILTER" ) );
	}

	[TestMethod]
	[DataRow( 0, 4, "ATTRIBUTES", "NONE" )]
	[DataRow( 1, 0, "ATTRIBUTES", "NONE" )]
	[DataRow( 1, 3, "ATTRIBUTES", "NONE" )]
	[DataRow( 1, 260, "ATTRIBUTES", "NONE" )]
	[DataRow( 1, 12, "ATTRIBUTES", "OCTAHEDRAL" )]
	[DataRow( 1, 4, "ATTRIBUTES", "QUATERNION" )]
	[DataRow( 1, 12, "ATTRIBUTES", "COLOR" )]
	[DataRow( 6, 4, "INDICES", "EXPONENTIAL" )]
	[DataRow( 12, 4, "TRIANGLES", "OCTAHEDRAL" )]
	[DataRow( int.MaxValue, 256, "ATTRIBUTES", "NONE" )]
	public void InvalidLayoutIsRejectedBeforeNativeDecode( int count, int stride, string mode, string filter )
	{
		var error = Assert.ThrowsException<GltfException>( () => GltfMeshopt.Decode( VerticesV0, count, stride, mode, filter ) );
		Assert.IsFalse( error.InnerException is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException,
			"Invalid input must be rejected before attempting to load the native decoder." );
	}

	[TestMethod]
	public void HeaderMustMatchRequestedCodecMode()
	{
		foreach ( var action in new Action[]
		{
			() => GltfMeshopt.Decode( IndexSequenceV1, 6, 4, "ATTRIBUTES" ),
			() => GltfMeshopt.Decode( VerticesV0, 4, 4, "INDICES" ),
			() => GltfMeshopt.Decode( [0xdf, 0, 0, 0, 0], 1, 4, "INDICES" )
		} )
		{
			var error = Assert.ThrowsException<GltfException>( action );
			Assert.IsFalse( error.InnerException is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException );
		}
	}
}
