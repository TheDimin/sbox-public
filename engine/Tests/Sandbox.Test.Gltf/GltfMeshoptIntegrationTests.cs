using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfMeshoptIntegrationTests
{
	// Known-answer compressed stream from meshoptimizer demo/tests.cpp, commit
	// ce321f27536cdbc61ef86622d8ef11fedf5492a8; MIT license in Fixtures.
	private static readonly byte[] VerticesV0 = GltfMeshoptTests.VertexFixtureV0;
	private static readonly byte[] VerticesV1 = GltfMeshoptTests.VertexFixtureV1;
	private static readonly byte[] IndexSequenceV1 = [0xd1, 0, 4, 0xcd, 1, 4, 7, 0x98, 0x1f, 0, 0, 0, 0];
	// Four zero 4-byte elements encoded with meshoptimizer's v1 encoder.
	private static readonly byte[] ColorV1 = Convert.FromHexString( "a1aa000000000000000000000000000000000000000000000000" );

	[TestMethod]
	[DataRow( "EXT_meshopt_compression", false )]
	[DataRow( "KHR_meshopt_compression", false )]
	[DataRow( "KHR_meshopt_compression", true )]
	public void SelectedExtensionDecodesAnInterleavedAccessorFromItsSourceOffset( string extension, bool versionOne )
	{
		using var document = Open( versionOne ? VerticesV1 : VerticesV0, extension );
		float[] expected = [0, 0, 0, 300, 0, 0, 0, 300, 0, 300, 300, 0];
		CollectionAssert.AreEqual( expected, document.ReadFloatAccessor( 0 ) );
		CollectionAssert.AreEqual( expected, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void UnsupportedOptionalExtensionUsesUncompressedFallback()
	{
		using var document = Open( VerticesV0, "EXT_meshopt_compression", selected: false );
		CollectionAssert.AreEqual( new float[] { 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7, 7 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	[DataRow( "EXT_meshopt_compression" )]
	[DataRow( "KHR_meshopt_compression" )]
	public void RequiredExtensionCanUseAnUnbackedFallbackBuffer( string extension )
	{
		using var document = Open( VerticesV0, extension, required: true, placeholder: true );
		CollectionAssert.AreEqual( new float[] { 0, 0, 0, 300, 0, 0, 0, 300, 0, 300, 300, 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void RequiredUnsupportedExtensionFailsClearly()
	{
		Assert.ThrowsException<GltfException>( () => Open( VerticesV0, "EXT_meshopt_compression", selected: false, required: true, placeholder: true ) );
	}

	[TestMethod]
	public void RequiredPlaceholderIsNotAcceptedWhenExtensionIsOptional()
	{
		Assert.ThrowsException<GltfException>( () => Open( VerticesV0, "EXT_meshopt_compression", placeholder: true ) );
	}

	[TestMethod]
	public void ExtRejectsVersionOneAttributeStream()
	{
		using var document = Open( VerticesV1, "EXT_meshopt_compression" );
		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	[DataRow( "EXT_meshopt_compression" )]
	[DataRow( "KHR_meshopt_compression" )]
	public void TriangleVersionZeroIsNotAValidGltfExtensionStream( string extension )
	{
		using var document = Open( [0xe0, 0, 0, 0], extension, mode: "TRIANGLES", count: 12, stride: 4, parentLength: 48 );
		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void KHRSupportsColorFilterWhileExtRejectsIt()
	{
		using var khr = Open( ColorV1, "KHR_meshopt_compression", count: 4, stride: 4, parentLength: 16, parentStride: 4, filter: "COLOR", accessor: "{\"bufferView\":0,\"componentType\":5121,\"count\":4,\"type\":\"VEC4\"}" );
		Assert.AreEqual( 16, khr.ReadFloatAccessor( 0 ).Length );
		using var ext = Open( ColorV1, "EXT_meshopt_compression", count: 4, stride: 4, parentLength: 16, parentStride: 4, filter: "COLOR", accessor: "{\"bufferView\":0,\"componentType\":5121,\"count\":4,\"type\":\"VEC4\"}" );
		var error = Assert.ThrowsException<GltfException>( () => ext.ReadFloatAccessor( 0 ) );
		StringAssert.Contains( error.Message, "COLOR" );
	}

	[TestMethod]
	public void KhrAllowsDifferentDecompressionAndParentViewStrides()
	{
		using var document = Open( VerticesV0, "KHR_meshopt_compression", parentStride: 8 );
		CollectionAssert.AreEqual( new float[] { 0, 0, 0, 0, 0, 300, 0, 0, 500, 0, 300, 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void IndexSequenceDecodesThroughUnsignedIndexAccessor()
	{
		using var document = Open( IndexSequenceV1, "KHR_meshopt_compression", mode: "INDICES", count: 6, stride: 4, parentLength: 24, parentStride: 0, accessor: "{\"bufferView\":0,\"componentType\":5125,\"count\":6,\"type\":\"SCALAR\"}" );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 51, 2, 49, 1000 }, document.ReadIndexAccessor( 0 ) );
	}

	[TestMethod]
	[DataRow( "EXT_meshopt_compression" )]
	[DataRow( "KHR_meshopt_compression" )]
	public void CompressedSourceCannotBeMarkedAsAFallbackBuffer( string extension )
	{
		using var document = Open( VerticesV0, extension, sourceIsFallback: true );
		Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void ConfiguredDecodedByteBudgetRejectsBeforeDecompression()
	{
		using var document = Open( VerticesV0, "EXT_meshopt_compression", maxDecodedBufferBytes: 47 );
		var error = Assert.ThrowsException<GltfException>( () => document.ReadFloatAccessor( 0 ) );
		StringAssert.Contains( error.Message, "budget" );
	}

	[TestMethod]
	public void InvalidLengthSourceRangeAndExtParentStrideFail()
	{
		using var length = Open( VerticesV0, "EXT_meshopt_compression", parentLength: 47 );
		Assert.ThrowsException<GltfException>( () => length.ReadFloatAccessor( 0 ) );
		using var range = Open( VerticesV0, "EXT_meshopt_compression", sourceLengthAdjustment: 1 );
		Assert.ThrowsException<GltfException>( () => range.ReadFloatAccessor( 0 ) );
		using var stride = Open( VerticesV0, "EXT_meshopt_compression", parentStride: 8 );
		Assert.ThrowsException<GltfException>( () => stride.ReadFloatAccessor( 0 ) );
	}

	private static GltfDocument Open( byte[] encoded, string extension, bool selected = true, bool required = false, bool placeholder = false,
		string mode = "ATTRIBUTES", int count = 4, int stride = 12, int parentLength = 48, int parentStride = 12, string filter = null,
		string accessor = "{\"bufferView\":0,\"componentType\":5123,\"count\":4,\"type\":\"VEC3\"}", int sourceLengthAdjustment = 0, long maxDecodedBufferBytes = long.MaxValue, bool sourceIsFallback = false )
	{
		var compressed = new byte[encoded.Length + 3];
		encoded.CopyTo( compressed, 3 );
		var fallback = new byte[parentLength];
		for ( var i = 0; i + 1 < fallback.Length; i += 2 ) fallback[i] = 7;
		var fallbackUri = placeholder ? "" : $",\"uri\":\"data:application/octet-stream;base64,{Convert.ToBase64String( fallback )}\"";
		var requiredJson = required ? $",\"extensionsRequired\":[\"{extension}\"]" : "";
		var parentStrideJson = parentStride == 0 ? "" : $",\"byteStride\":{parentStride}";
		var filterJson = filter is null ? "" : $",\"filter\":\"{filter}\"";
		var sourceExtension = sourceIsFallback ? $",\"extensions\":{{\"{extension}\":{{\"fallback\":true}}}}" : "";
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"" + extension + "\"]" + requiredJson
			+ ",\"buffers\":[{\"byteLength\":" + parentLength + fallbackUri + "},{\"uri\":\"data:application/octet-stream;base64,"
			+ Convert.ToBase64String( compressed ) + "\",\"byteLength\":" + compressed.Length + sourceExtension + "}]"
			+ ",\"bufferViews\":[{\"buffer\":0,\"byteLength\":" + parentLength + parentStrideJson
			+ ",\"extensions\":{\"" + extension + "\":{\"buffer\":1,\"byteOffset\":3,\"byteLength\":" + (encoded.Length + sourceLengthAdjustment)
			+ ",\"byteStride\":" + stride + ",\"count\":" + count + ",\"mode\":\"" + mode + "\"" + filterJson + "}}}]"
			+ ",\"accessors\":[" + accessor + "]}";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: selected ? new HashSet<string> { extension } : null, maxDecodedBufferBytes: maxDecodedBufferBytes );
	}
}
