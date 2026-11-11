using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>Owned source bytes and lossless sparse materialization; no GPU upload.</summary>
[TestClass]
public class GltfUploadDataTests
{
	[TestMethod]
	public void InterleavedViewsAreCopiedOnceAndSurviveSourceAndDocumentLifetime()
	{
		var bytes = new byte[54];
		for ( var i = 0; i < 48; i++ ) bytes[i] = (byte)i;
		bytes[48] = 0; bytes[50] = 1; bytes[52] = 2;
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"mesh.bin","byteLength":54}],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":16},{"buffer":0,"byteOffset":48,"byteLength":6}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,1]},
			 {"bufferView":0,"byteOffset":12,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"},
			 {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"COLOR_0":1},"indices":2}]}]}
			""";
		GltfPrimitiveUploadData data;
		using ( var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), _ => bytes ) )
			data = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 54 );
		Array.Fill( bytes, (byte)0 );
		var position = data.Attributes["POSITION"]; var color = data.Attributes["COLOR_0"];
		Assert.AreSame( position.Storage, color.Storage );
		Assert.AreEqual( 0, position.ByteOffset ); Assert.AreEqual( 12, color.ByteOffset );
		Assert.AreEqual( 16, position.ByteStride ); Assert.IsFalse( color.IsMaterialized );
		Assert.AreEqual( 5121, color.Binding.ComponentType ); Assert.IsTrue( color.Binding.Normalized );
		Assert.AreEqual( (byte)12, color.Storage.Bytes.Span[12] );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 1, 0, 2, 0 }, data.Indices.Storage.Bytes.ToArray() );
		Assert.AreEqual( 54L, data.OwnedBytes );
	}

	[TestMethod]
	public void SharedViewBudgetCountsUniqueCopiesAndRejectsInsufficientSpace()
	{
		var data = Convert.ToBase64String( new byte[48] );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{data}}}}}}}}","byteLength":48}],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":16}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":0,"byteOffset":12,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"COLOR_0":1}}]}]}
			""";
		using var document = Read( json );
		Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 47 ) );
		var output = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 48 );
		Assert.AreEqual( 48L, output.OwnedBytes ); Assert.IsTrue( output.Layout.RequiresGeneratedIndices );
		Assert.IsNull( output.Indices ); Assert.AreSame( output.Attributes["POSITION"].Storage, output.Attributes["COLOR_0"].Storage );
	}

	[TestMethod]
	public void SparseZeroBaseMaterializesOriginalFloatBits()
	{
		var bytes = new byte[16]; bytes[0] = 1;
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 4 ), unchecked((int)0x80000000) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 8 ), BitConverter.SingleToInt32Bits( 2 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 12 ), BitConverter.SingleToInt32Bits( 3 ) );
		using var document = Read( SparseFixture( bytes ) );
		var stream = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 36 ).Attributes["POSITION"];
		Assert.IsTrue( stream.IsMaterialized ); Assert.AreEqual( 0, stream.ByteOffset ); Assert.AreEqual( 12, stream.ByteStride );
		var result = stream.Storage.Bytes.Span;
		Assert.AreEqual( 36, result.Length ); Assert.AreEqual( unchecked((int)0x80000000), BinaryPrimitives.ReadInt32LittleEndian( result[12..] ) );
		Assert.AreEqual( 2f, BinaryPrimitives.ReadSingleLittleEndian( result[16..] ) ); Assert.AreEqual( 3f, BinaryPrimitives.ReadSingleLittleEndian( result[20..] ) );
		Assert.IsTrue( result[..12].ToArray().All( b => b == 0 ) ); Assert.IsTrue( result[24..].ToArray().All( b => b == 0 ) );
	}

	[TestMethod]
	public void SparseInterleavedBasePreservesOtherComponentBits()
	{
		var bytes = new byte[64];
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 0 ), unchecked((int)0x80000000) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 32 ), BitConverter.SingleToInt32Bits( 5 ) );
		bytes[48] = 1;
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 52 ), BitConverter.SingleToInt32Bits( 7 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 56 ), BitConverter.SingleToInt32Bits( 8 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 60 ), BitConverter.SingleToInt32Bits( 9 ) );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":64}],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":16},{"buffer":0,"byteOffset":48,"byteLength":1},{"buffer":0,"byteOffset":52,"byteLength":12}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0],
			 "sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""";
		using var document = Read( json );
		var stream = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 36 ).Attributes["POSITION"];
		Assert.AreEqual( 12, stream.ByteStride ); Assert.AreEqual( 0, stream.ByteOffset );
		var data = stream.Storage.Bytes.Span;
		Assert.AreEqual( unchecked((int)0x80000000), BinaryPrimitives.ReadInt32LittleEndian( data ) );
		Assert.AreEqual( 7f, BinaryPrimitives.ReadSingleLittleEndian( data[12..] ) );
		Assert.AreEqual( 8f, BinaryPrimitives.ReadSingleLittleEndian( data[16..] ) );
		Assert.AreEqual( 9f, BinaryPrimitives.ReadSingleLittleEndian( data[20..] ) );
		Assert.AreEqual( 5f, BinaryPrimitives.ReadSingleLittleEndian( data[24..] ) );
	}

	[TestMethod]
	public void MissingBaseWithoutSparseProducesOwnedZeroes()
	{
		using var document = Read( """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""" );
		var data = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 36 );
		Assert.IsTrue( data.Attributes["POSITION"].IsMaterialized );
		Assert.AreEqual( 36L, data.OwnedBytes ); Assert.AreEqual( 12, data.Attributes["POSITION"].ByteStride );
		Assert.IsTrue( data.Attributes["POSITION"].Storage.Bytes.ToArray().All( value => value == 0 ) );
	}

	[TestMethod]
	public void CopiedViewUsesOwnedOffsetsRatherThanItsSourceBufferOrigin()
	{
		var positions = Convert.ToBase64String( new byte[36] ); var colors = Convert.ToBase64String( new byte[13] );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{positions}}}}}}}}","byteLength":36},
			 {"uri":"data:application/octet-stream;base64,{{{{{{{{colors}}}}}}}}","byteLength":13}],
			 "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":1,"byteOffset":1,"byteLength":12,"byteStride":4}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"COLOR_0":1}}]}]}
			""";
		using var document = Read( json );
		var color = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 48 ).Attributes["COLOR_0"];
		Assert.AreEqual( 1, color.Binding.ViewByteOffset ); Assert.AreEqual( 0, color.ByteOffset );
		Assert.AreEqual( 12, color.Storage.Bytes.Length ); Assert.AreEqual( 4, color.ByteStride );
	}

	[TestMethod]
	public void SparseIndicesMustBeInRangeBeforeReturningMaterializedData()
	{
		var bytes = new byte[16]; bytes[0] = 3;
		using var document = Read( SparseFixture( bytes ) );
		Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 36 ) );
	}

	[TestMethod]
	public void SparseByteMatrixKeepsPaddedColumnsAndAllowsMissingLastPadding()
	{
		var bytes = new byte[27]; bytes[12] = 0;
		new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9 }.CopyTo( bytes, 16 );
		var data = Convert.ToBase64String( bytes );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{data}}}}}}}}","byteLength":27}],
			 "bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":1},{"buffer":0,"byteOffset":16,"byteLength":11}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"componentType":5121,"count":1,"type":"MAT3","sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}],
			 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0,"_MATRIX":1}}]}]}
			""";
		using var document = Read( json );
		var matrix = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 24 ).Attributes["_MATRIX"];
		Assert.AreEqual( 12, matrix.ByteStride ); Assert.AreEqual( 4, matrix.Binding.ColumnStride );
		CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0 }, matrix.Storage.Bytes.ToArray() );
	}

	[TestMethod]
	public void SelectedMeshoptCopiesDecodedViewAndOptionalFallbackRemainsRaw()
	{
		// Known-answer stream from meshoptimizer demo/tests.cpp, commit
		// ce321f27536cdbc61ef86622d8ef11fedf5492a8; MIT license in Fixtures.
		var encoded = Convert.FromHexString( "a0013f0000005857580126000000010c00000058010800000000000000013f0000001718170126000000010c0000001701080000000000000000000000000000000000000000000000000000000000000000000000" );
		var zeros = Convert.ToBase64String( new byte[48] ); var compressed = Convert.ToBase64String( encoded );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_meshopt_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{zeros}}}}}}}}","byteLength":48},
			 {"uri":"data:application/octet-stream;base64,{{{{{{{{zeros}}}}}}}}","byteLength":48},
			 {"uri":"data:application/octet-stream;base64,{{{{{{{{compressed}}}}}}}}","byteLength":{{{{{{{{encoded.Length}}}}}}}} }],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":12},
			 {"buffer":1,"byteLength":48,"byteStride":12,"extensions":{"EXT_meshopt_compression":{"buffer":2,"byteLength":{{{{{{{{encoded.Length}}}}}}}},"byteStride":12,"count":4,"mode":"ATTRIBUTES"}}}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"componentType":5123,"count":4,"type":"VEC3"}],
			 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0,"_PACKED":1}}]}]}
			""";
		using var selected = Read( json, "EXT_meshopt_compression" );
		var stream = selected.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 96 ).Attributes["_PACKED"];
		Assert.AreEqual( GltfUploadMaterialization.Meshopt, stream.Binding.Materialization ); Assert.IsTrue( stream.IsMaterialized );
		Assert.AreEqual( (ushort)300, BinaryPrimitives.ReadUInt16LittleEndian( stream.Storage.Bytes.Span[12..] ) );
		Assert.AreEqual( 12, stream.ByteStride );
		using var fallback = Read( json );
		var ordinary = fallback.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 96 ).Attributes["_PACKED"];
		Assert.AreEqual( GltfUploadMaterialization.None, ordinary.Binding.Materialization ); Assert.IsFalse( ordinary.IsMaterialized );
		Assert.IsTrue( ordinary.Storage.Bytes.ToArray().All( value => value == 0 ) );
	}

	[TestMethod]
	public void DracoAndOrdinaryPrimitivesUseTheirOwnSourceWhenAccessorIsShared()
	{
		var bytes = Convert.ToBase64String( new byte[48] );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{bytes}}}}}}}}","byteLength":48}],
			 "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":12}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,1]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}},
			 {"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":1,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		SeedDraco( document, 1 );
		var ordinary = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 36 );
		Assert.IsTrue( ordinary.Attributes["POSITION"].Storage.Bytes.ToArray().All( x => x == 0 ) );
		Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 1, maxOwnedBytes: 47 ) );
		var compressed = document.MaterializePrimitiveUpload( 0, 1, maxOwnedBytes: 48 );
		Assert.AreEqual( 1f, BinaryPrimitives.ReadSingleLittleEndian( compressed.Attributes["POSITION"].Storage.Bytes.Span ) );
		Assert.AreEqual( 3, compressed.Indices.Count ); Assert.AreEqual( 5125, compressed.Indices.ComponentType );
		Assert.AreEqual( 4, compressed.Indices.ByteStride ); Assert.IsNull( compressed.Indices.Binding );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0 }, compressed.Indices.Storage.Bytes.ToArray() );
		Assert.AreEqual( 48L, compressed.OwnedBytes );
	}

	[TestMethod]
	public void SparseOverlayOnSharedDracoAttributeDoesNotChangeOtherSemantic()
	{
		var bytes = new byte[20]; bytes[4] = 1;
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 8 ), BitConverter.SingleToInt32Bits( 7 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 12 ), BitConverter.SingleToInt32Bits( 8 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 16 ), BitConverter.SingleToInt32Bits( 9 ) );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":20}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":0,"byteOffset":4,"byteLength":1},{"buffer":0,"byteOffset":8,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[9,9,9],
			 "sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}},
			 {"componentType":5126,"count":3,"type":"VEC3"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},
			 "extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0,"NORMAL":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		SeedDraco( document, 0 );
		Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 83 ) );
		var output = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 84 );
		var position = output.Attributes["POSITION"]; var normal = output.Attributes["NORMAL"];
		Assert.AreNotSame( position.Storage, normal.Storage ); Assert.AreEqual( 84L, output.OwnedBytes );
		Assert.AreEqual( 7f, BinaryPrimitives.ReadSingleLittleEndian( position.Storage.Bytes.Span[12..] ) );
		Assert.AreEqual( 1f, BinaryPrimitives.ReadSingleLittleEndian( normal.Storage.Bytes.Span[12..] ) );
	}

	[TestMethod]
	public void DracoStripReportsActualDecodedIndexCountAndWidth()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAA==","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":4}],
			 "accessors":[{"componentType":5126,"count":4,"type":"VEC3","min":[0,0,0],"max":[1,1,1]},
			 {"componentType":5123,"count":4,"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"mode":5,"attributes":{"POSITION":0},"indices":1,
			 "extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		SeedDraco( document, 0, 4, [0, 1, 2, 2, 1, 3] );
		var output = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 60 );
		Assert.AreEqual( 5, output.Layout.SourceMode ); Assert.AreEqual( 4, output.Layout.DecodedMode );
		Assert.IsFalse( output.Layout.RequiresTopologyExpansion ); Assert.AreEqual( 4, output.Indices.Binding.Count );
		Assert.AreEqual( 6, output.Indices.Count ); Assert.AreEqual( 5123, output.Indices.ComponentType ); Assert.AreEqual( 2, output.Indices.ByteStride );
		CollectionAssert.AreEqual( new byte[] { 0, 0, 1, 0, 2, 0, 2, 0, 1, 0, 3, 0 }, output.Indices.Storage.Bytes.ToArray() );
	}

	[TestMethod]
	public void DracoStripWithSparseSourceIndicesFailsExplicitly()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAA=","byteLength":8}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":0,"byteOffset":4,"byteLength":1},{"buffer":0,"byteOffset":6,"byteLength":2}],
			 "accessors":[{"componentType":5126,"count":4,"type":"VEC3","min":[0,0,0],"max":[1,1,1]},
			 {"componentType":5123,"count":4,"type":"SCALAR","sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}],
			 "meshes":[{"primitives":[{"mode":5,"attributes":{"POSITION":0},"indices":1,
			 "extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var error = Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0 ) );
		StringAssert.Contains( error.Message, "sparse strip" );
	}

	[TestMethod]
	[DataRow( 2, true )]
	[DataRow( 3, false )]
	public void SparseDracoTriangleIndicesAreAppliedAndRangeChecked( int replacement, bool valid )
	{
		var bytes = new byte[8]; BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 6 ), (ushort)replacement );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":8}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":0,"byteOffset":4,"byteLength":1},{"buffer":0,"byteOffset":6,"byteLength":2}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,1]},
			 {"componentType":5123,"count":3,"type":"SCALAR","sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"indices":1,
			 "extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		SeedDraco( document, 0 );
		if ( !valid ) { Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 42 ) ); return; }
		var output = document.MaterializePrimitiveUpload( 0, 0, maxOwnedBytes: 42 );
		CollectionAssert.AreEqual( new byte[] { 2, 0, 1, 0, 2, 0 }, output.Indices.Storage.Bytes.ToArray() );
	}

	[TestMethod]
	[DataRow( 0x7fc00000 )]
	[DataRow( 0x7f800000 )]
	[DataRow( unchecked((int)0xff800000) )]
	public void NonFiniteFloatComponentsAreRejectedInDirectSparseAndDracoUploads( int bits )
	{
		var direct = new byte[48];
		BinaryPrimitives.WriteInt32LittleEndian( direct.AsSpan( 20 ), bits );
		var directJson = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( direct )}}}}}}}}","byteLength":48}],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":16}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""";
		using ( var document = Read( directJson ) )
			Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0 ) );

		var sparse = new byte[16]; sparse[0] = 1;
		BinaryPrimitives.WriteInt32LittleEndian( sparse.AsSpan( 8 ), bits );
		using ( var document = Read( SparseFixture( sparse ) ) )
			Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0 ) );

		var encoded = new byte[20]; encoded[4] = 1;
		BinaryPrimitives.WriteInt32LittleEndian( encoded.AsSpan( 12 ), bits );
		var dracoJson = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( encoded )}}}}}}}}","byteLength":20}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":0,"byteOffset":4,"byteLength":1},{"buffer":0,"byteOffset":8,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,1],
			 "sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var draco = Read( dracoJson, "KHR_draco_mesh_compression" );
		SeedDraco( draco, 0 );
		Assert.ThrowsException<GltfException>( () => draco.MaterializePrimitiveUpload( 0, 0 ) );
	}

	[TestMethod]
	public void FloatValidationIgnoresPaddingAndIntegerFieldsAndPreservesSignedZero()
	{
		var bytes = new byte[48];
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 0 ), unchecked((int)0x80000000) );
		for ( var i = 0; i < 3; i++ ) BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( i * 16 + 12 ), 0x7fc00000 );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":48}],
			 "bufferViews":[{"buffer":0,"byteLength":48,"byteStride":16}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":0,"byteOffset":12,"componentType":5121,"normalized":true,"count":3,"type":"VEC4"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"COLOR_0":1}}]}]}
			""";
		using var document = Read( json );
		var output = document.MaterializePrimitiveUpload( 0, 0 );
		Assert.AreEqual( unchecked((int)0x80000000), BinaryPrimitives.ReadInt32LittleEndian( output.Attributes["POSITION"].Storage.Bytes.Span ) );
		Assert.AreEqual( 0x7fc00000, BinaryPrimitives.ReadInt32LittleEndian( output.Attributes["COLOR_0"].Storage.Bytes.Span[12..] ) );
	}

	[TestMethod]
	public void FloatMatrixValidationExaminesComponentsButNotStridePadding()
	{
		var bytes = new byte[64];
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 52 ), 0x7fc00000 );
		string Fixture() => $$$$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":64}],
			 "bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":16,"byteLength":48,"byteStride":48}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"componentType":5126,"count":1,"type":"MAT3"}],
			 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0,"_MATRIX":1}}]}]}
			""";
		using ( var document = Read( Fixture() ) ) Assert.IsNotNull( document.MaterializePrimitiveUpload( 0, 0 ) );
		BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( 48 ), 0x7f800000 );
		using ( var document = Read( Fixture() ) ) Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0 ) );
	}

	[TestMethod]
	public void SharedDracoIdWithDistinctSparseAccessorsAndAliasesUsesIsolatedCopies()
	{
		var bytes = new byte[36]; bytes[4] = 1; bytes[5] = 1;
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 8 ), 7 );
		BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 20 ), 9 );
		var json = $$$$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":36}],
			 "bufferViews":[{"buffer":0,"byteLength":4},{"buffer":0,"byteOffset":4,"byteLength":1},{"buffer":0,"byteOffset":5,"byteLength":1},
			 {"buffer":0,"byteOffset":8,"byteLength":12},{"buffer":0,"byteOffset":20,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[9,9,9],"sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":3}}},
			 {"componentType":5126,"count":3,"type":"VEC3","sparse":{"count":1,"indices":{"bufferView":2,"componentType":5121},"values":{"bufferView":4}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":0,"_OTHER":1},"extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0,"NORMAL":0,"_OTHER":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		SeedDraco( document, 0 );
		Assert.ThrowsException<GltfException>( () => document.MaterializePrimitiveUpload( 0, 0, 83 ) );
		var output = document.MaterializePrimitiveUpload( 0, 0, 84 );
		var position = output.Attributes["POSITION"]; var normal = output.Attributes["NORMAL"]; var other = output.Attributes["_OTHER"];
		Assert.AreSame( position.Storage, normal.Storage ); Assert.AreNotSame( position.Storage, other.Storage );
		Assert.AreEqual( 84L, output.OwnedBytes );
		Assert.AreEqual( 7f, BinaryPrimitives.ReadSingleLittleEndian( position.Storage.Bytes.Span[12..] ) );
		Assert.AreEqual( 9f, BinaryPrimitives.ReadSingleLittleEndian( other.Storage.Bytes.Span[12..] ) );
	}

	// Test-only seeded decoded mesh avoids invoking native Draco; separate codec
	// goldens cover the native bridge. This isolates primitive selection and ownership.
	private static void SeedDraco( GltfDocument document, int primitive, int vertexCount = 3, uint[] indices = null )
	{
		var raw = new byte[vertexCount * 12];
		for ( var i = 0; i < vertexCount * 3; i++ ) BinaryPrimitives.WriteInt32LittleEndian( raw.AsSpan( i * 4 ), BitConverter.SingleToInt32Bits( 1 ) );
		var field = typeof( GltfDocument ).GetField( "dracoCache", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
		Assert.IsNotNull( field );
		var cache = (Dictionary<(int, int), GltfDracoMesh>)field.GetValue( document );
		cache.Add( (0, primitive), new GltfDracoMesh
		{
			VertexCount = vertexCount,
			Indices = indices ?? [0, 1, 2],
			Attributes = new Dictionary<uint, GltfDracoAttribute>
			{
				[0] = new GltfDracoAttribute { ComponentType = 5126, Components = 3, Bytes = raw }
			}
		} );
	}

	private static string SparseFixture( byte[] bytes ) => $$$$$$$$"""
		{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{{{Convert.ToBase64String( bytes )}}}}}}}}","byteLength":16}],
		 "bufferViews":[{"buffer":0,"byteLength":1},{"buffer":0,"byteOffset":4,"byteLength":12}],
		 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0],
		 "sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
		""";

	private static GltfDocument Read( string json, string extension = null ) => GltfReader.Read( Encoding.UTF8.GetBytes( json ),
		supportedExtensions: extension is null ? null : new HashSet<string>( StringComparer.Ordinal ) { extension } );
}
