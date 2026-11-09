using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Lossless source-described morph streams, independently owned before GPU allocation.</summary>
[TestClass]
public class GltfMorphUploadDataTests
{
	[TestMethod]
	public void InterleavedTargetsShareOneOwnedViewAndRetainAccessorOffsetsAfterDisposal()
	{
		var bytes = new byte[96];
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 16 + vertex * 24 ), vertex + .5f );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 28 + vertex * 24 ), -vertex - .25f );
		}
		var json = Fixture( "[{\"POSITION\":6,\"NORMAL\":7},{\"POSITION\":6}]",
			"{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0.5,0,0],\"max\":[2.5,0,0]}," +
			"{\"bufferView\":0,\"byteOffset\":12,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}",
			"[{\"buffer\":0,\"byteOffset\":16,\"byteLength\":72,\"byteStride\":24}]", bytes, external: true );
		GltfMorphTargetsUploadData upload;
		using ( var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => bytes ) )
			upload = document.MaterializeMorphTargetsUpload( 0, 0, 72 );
		Array.Fill( bytes, (byte)0 );
		var position = upload.Targets[0]["POSITION"];
		var normal = upload.Targets[0]["NORMAL"];
		Assert.AreEqual( 3, upload.VertexCount ); Assert.AreEqual( 72L, upload.OwnedBytes );
		Assert.AreSame( position.Storage, normal.Storage ); Assert.AreSame( position.Storage, upload.Targets[1]["POSITION"].Storage );
		Assert.AreEqual( 0, position.ByteOffset ); Assert.AreEqual( 12, normal.ByteOffset );
		Assert.AreEqual( 24, position.ByteStride ); Assert.AreEqual( 24, normal.ByteStride );
		Assert.AreEqual( 16, normal.Binding.ViewByteOffset ); Assert.AreEqual( 12, normal.Binding.AccessorByteOffset );
		Assert.AreEqual( .5f, BinaryPrimitives.ReadSingleLittleEndian( position.Storage.Bytes.Span ) );
		Assert.AreEqual( -2.25f, BinaryPrimitives.ReadSingleLittleEndian( normal.Storage.Bytes.Span[60..] ) );
		Assert.IsFalse( position.IsMaterialized );
	}

	[TestMethod]
	public void SharedViewBudgetCountsOwnedCopiesSeparatelyFromEagerSourceResolution()
	{
		var bytes = new byte[72]; var reads = 0;
		var json = Fixture( "[{\"POSITION\":6,\"NORMAL\":7}]",
			"{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			"{\"bufferView\":0,\"byteOffset\":12,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}",
			"[{\"buffer\":0,\"byteLength\":72,\"byteStride\":24}]", bytes, external: true );
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => { reads++; return bytes; } );
		// GltfReader resolves declared buffers eagerly. This method's budget
		// covers detached upload copies, not the already-resolved source bytes.
		Assert.AreEqual( 1, reads );
		Assert.ThrowsException<GltfException>( () => document.MaterializeMorphTargetsUpload( 0, 0, 71 ) );
		Assert.AreEqual( 1, reads );
		Assert.AreEqual( 72L, document.MaterializeMorphTargetsUpload( 0, 0, 72 ).OwnedBytes );
		Assert.AreEqual( 1, reads );
	}

	[TestMethod]
	public void SparseInterleavedBaseUsesTightOwnedStorageAndPreservesSignedZero()
	{
		var bytes = new byte[64];
		BinaryPrimitives.WriteInt32LittleEndian( bytes, unchecked((int)0x80000000) );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 32 ), 5 );
		bytes[48] = 1;
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 52 ), -7 );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 56 ), 8 );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 60 ), -9 );
		using var document = Read( Fixture( "[{\"POSITION\":6},{\"POSITION\":6}]",
			"{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[-7,0,-9],\"max\":[5,8,0]," +
			"\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":1,\"componentType\":5121},\"values\":{\"bufferView\":2}}}",
			"[{\"buffer\":0,\"byteLength\":48,\"byteStride\":16},{\"buffer\":0,\"byteOffset\":48,\"byteLength\":1},{\"buffer\":0,\"byteOffset\":52,\"byteLength\":12}]", bytes ) );
		Assert.ThrowsException<GltfException>( () => document.MaterializeMorphTargetsUpload( 0, 0, 35 ) );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0, 36 );
		var stream = upload.Targets[0]["POSITION"];
		Assert.AreSame( stream.Storage, upload.Targets[1]["POSITION"].Storage );
		Assert.AreEqual( 36L, upload.OwnedBytes ); Assert.AreEqual( 0, stream.ByteOffset ); Assert.AreEqual( 12, stream.ByteStride );
		Assert.IsTrue( stream.IsMaterialized );
		Assert.AreEqual( unchecked((int)0x80000000), BinaryPrimitives.ReadInt32LittleEndian( stream.Storage.Bytes.Span ) );
		Assert.AreEqual( -7f, BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span[12..] ) );
		Assert.AreEqual( 5f, BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span[24..] ) );
	}

	[TestMethod]
	public void SparseZeroBaseAndMissingTargetSemanticsRemainDistinct()
	{
		var bytes = new byte[16]; bytes[0] = 2;
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 4 ), -2 );
		using var document = Read( Fixture( "[{\"POSITION\":6},{}]",
			"{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[-2,0,0],\"max\":[0,0,0]," +
			"\"sparse\":{\"count\":1,\"indices\":{\"bufferView\":0,\"componentType\":5121},\"values\":{\"bufferView\":1}}}",
			"[{\"buffer\":0,\"byteLength\":1},{\"buffer\":0,\"byteOffset\":4,\"byteLength\":12}]", bytes ) );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0 );
		Assert.AreEqual( 2, upload.Targets.Count ); Assert.AreEqual( 0, upload.Targets[1].Count );
		var stream = upload.Targets[0]["POSITION"];
		Assert.AreEqual( 36L, upload.OwnedBytes ); Assert.IsTrue( stream.IsMaterialized );
		Assert.IsTrue( stream.Storage.Bytes.Span[..24].ToArray().All( b => b == 0 ) );
		Assert.AreEqual( -2f, BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span[24..] ) );
	}

	[TestMethod]
	public void ImplicitZeroTargetsAreMaterializedOncePerAccessor()
	{
		using var document = Read( Fixture( "[{\"POSITION\":6},{\"POSITION\":6}]", FloatTarget ) );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0, 36 );
		Assert.AreEqual( 36L, upload.OwnedBytes );
		Assert.AreSame( upload.Targets[0]["POSITION"].Storage, upload.Targets[1]["POSITION"].Storage );
		Assert.IsTrue( upload.Targets[0]["POSITION"].Storage.Bytes.ToArray().All( b => b == 0 ) );
	}

	[TestMethod]
	[DataRow( 5120, false )]
	[DataRow( 5120, true )]
	[DataRow( 5122, false )]
	[DataRow( 5122, true )]
	public void QuantizedSignedPositionDeltasKeepRawFormatAndNormalization( int componentType, bool normalized )
	{
		var stride = componentType == 5120 ? 4 : 8;
		var bytes = new byte[stride * 3];
		if ( componentType == 5120 ) bytes[0] = unchecked((byte)-127);
		else BinaryPrimitives.WriteInt16LittleEndian( bytes, -32767 );
		var minimum = componentType == 5120 ? -127 : -32767;
		var accessor = $"{{\"bufferView\":0,\"componentType\":{componentType},\"normalized\":{normalized.ToString().ToLowerInvariant()},\"count\":3,\"type\":\"VEC3\",\"min\":[{minimum},0,0],\"max\":[0,0,0]}}";
		using var document = Read( Fixture( "[{\"POSITION\":6}]", accessor,
			$"[{{\"buffer\":0,\"byteLength\":{bytes.Length},\"byteStride\":{stride}}}]", bytes, quantized: true ), quantized: true );
		var stream = document.MaterializeMorphTargetsUpload( 0, 0 ).Targets[0]["POSITION"];
		Assert.AreEqual( componentType, stream.ComponentType ); Assert.AreEqual( normalized, stream.Normalized );
		Assert.AreEqual( stride, stream.ByteStride ); Assert.IsFalse( stream.IsMaterialized );
		CollectionAssert.AreEqual( bytes, stream.Storage.Bytes.ToArray() );
	}

	[TestMethod]
	public void OptionalAndApplicationMorphStreamsArePreservedWithoutClaimingRendererSupport()
	{
		using var document = Read( Fixture( "[{\"TEXCOORD_0\":6,\"COLOR_0\":7,\"_CUSTOM\":8}]",
			"{\"componentType\":5122,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"}," +
			"{\"componentType\":5120,\"normalized\":true,\"count\":3,\"type\":\"VEC3\"}," +
			"{\"componentType\":5126,\"count\":3,\"type\":\"SCALAR\"}" ) );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0 );
		CollectionAssert.AreEquivalent( new[] { "TEXCOORD_0", "COLOR_0", "_CUSTOM" }, upload.Targets[0].Keys.ToArray() );
		Assert.AreEqual( 2, upload.Targets[0]["TEXCOORD_0"].Components );
		Assert.AreEqual( 5120, upload.Targets[0]["COLOR_0"].ComponentType );
		Assert.AreEqual( 1, upload.Targets[0]["_CUSTOM"].Components );
	}

	[TestMethod]
	[DataRow( "[{\"POSITION\":6}]", "{\"componentType\":5126,\"count\":2,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}" )]
	[DataRow( "[{\"TANGENT\":6}]", "{\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"}" )]
	[DataRow( "[{\"POSITION\":6}]", "{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"}" )]
	[DataRow( "[{\"NORMAL\":6}]", "{\"componentType\":5121,\"normalized\":true,\"count\":3,\"type\":\"VEC3\"}" )]
	[DataRow( "[{\"TEXCOORD_1\":6}]", "{\"componentType\":5126,\"count\":3,\"type\":\"VEC2\"}" )]
	[DataRow( "[{\"JOINTS_0\":6}]", "{\"componentType\":5121,\"count\":3,\"type\":\"VEC4\"}" )]
	[DataRow( "[{\"POSITION\":6,\"POSITION\":6}]", FloatTarget )]
	[DataRow( "[]", FloatTarget )]
	[DataRow( "[3]", FloatTarget )]
	public void MalformedMorphBindingsFailBeforeUploading( string targets, string accessor )
	{
		using var document = Read( Fixture( targets, accessor ) );
		Assert.ThrowsException<GltfException>( () => document.MaterializeMorphTargetsUpload( 0, 0 ) );
	}

	[TestMethod]
	[DataRow( 0x7fc00000 )]
	[DataRow( 0x7f800000 )]
	[DataRow( unchecked((int)0xff800000) )]
	public void NonFiniteMorphComponentsFailWhileUnusedInterleavePaddingIsPreserved( int bits )
	{
		var bytes = new byte[48];
		for ( var vertex = 0; vertex < 3; vertex++ ) BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( vertex * 16 + 12 ), bits );
		string Json() => Fixture( "[{\"POSITION\":6}]", FloatTarget.Replace( "{", "{\"bufferView\":0,", StringComparison.Ordinal ),
			"[{\"buffer\":0,\"byteLength\":48,\"byteStride\":16}]", bytes );
		using ( var document = Read( Json() ) ) Assert.AreEqual( 48L, document.MaterializeMorphTargetsUpload( 0, 0 ).OwnedBytes );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 16 ), bits );
		using var invalid = Read( Json() );
		Assert.ThrowsException<GltfException>( () => invalid.MaterializeMorphTargetsUpload( 0, 0 ) );
	}

	[TestMethod]
	public void AbsentTargetsAllocateNothingButNegativeBudgetsRemainInvalid()
	{
		using var document = Read( Fixture( null, null ) );
		var upload = document.MaterializeMorphTargetsUpload( 0, 0, 0 );
		Assert.AreEqual( 0, upload.Targets.Count ); Assert.AreEqual( 0L, upload.OwnedBytes ); Assert.AreEqual( 3, upload.VertexCount );
		Assert.ThrowsException<GltfException>( () => document.MaterializeMorphTargetsUpload( 0, 0, -1 ) );
	}

	private const string FloatTarget = "{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}";

	private static string Fixture( string targets, string accessors, string views = null, byte[] bytes = null, bool external = false, bool quantized = false )
	{
		// Valid base normals/tangents belong to a distinct view. They are not
		// returned/copied by a morph-only upload and cannot alter target budgets.
		var baseBytes = new byte[84];
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			BinaryPrimitives.WriteSingleLittleEndian( baseBytes.AsSpan( vertex * 28 + 8 ), 1 );
			BinaryPrimitives.WriteSingleLittleEndian( baseBytes.AsSpan( vertex * 28 + 12 ), 1 );
			BinaryPrimitives.WriteSingleLittleEndian( baseBytes.AsSpan( vertex * 28 + 24 ), 1 );
		}
		using var viewJson = JsonDocument.Parse( views ?? "[]" );
		var baseView = viewJson.RootElement.GetArrayLength();
		var baseBuffer = bytes is null ? 0 : 1;
		var sourceBuffer = bytes is null ? "" : $"{{\"uri\":\"{(external ? "mesh.bin" : "data:application/octet-stream;base64," + Convert.ToBase64String( bytes ))}\",\"byteLength\":{bytes.Length}}},";
		var sourceViews = baseView == 0 ? "" : views.Trim()[1..^1] + ",";
		var buffer = $",\"buffers\":[{sourceBuffer}{{\"uri\":\"data:application/octet-stream;base64,{Convert.ToBase64String( baseBytes )}\",\"byteLength\":84}}]," +
			$"\"bufferViews\":[{sourceViews}{{\"buffer\":{baseBuffer},\"byteLength\":84,\"byteStride\":28}}]";
		var extension = quantized ? ",\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"]" : "";
		var suffix = accessors is null ? "" : "," + accessors;
		var morph = targets is null ? "" : ",\"targets\":" + targets;
		return $$$"""
			{"asset":{"version":"2.0"}{{{extension}}}{{{buffer}}},"accessors":[
			{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"bufferView":{{{baseView}}},"componentType":5126,"count":3,"type":"VEC3"},
			{"bufferView":{{{baseView}}},"byteOffset":12,"componentType":5126,"count":3,"type":"VEC4"},
			{"componentType":5126,"count":3,"type":"VEC2"},
			{"componentType":5126,"count":3,"type":"VEC4"},
			{"componentType":5126,"count":3,"type":"SCALAR"}{{{suffix}}}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TANGENT":2,"TEXCOORD_0":3,"COLOR_0":4,"_CUSTOM":5}{{{morph}}}}]}]}
			""";
	}

	private static GltfDocument Read( string json, bool quantized = false ) => GltfReader.Read( Encoding.UTF8.GetBytes( json ),
		supportedExtensions: quantized ? new HashSet<string>( StringComparer.Ordinal ) { "KHR_mesh_quantization" } : null );
}
