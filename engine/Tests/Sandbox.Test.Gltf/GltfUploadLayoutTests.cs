using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>Source layout metadata does not imply GPU compatibility or decoded data.</summary>
[TestClass]
public class GltfUploadLayoutTests
{
	[TestMethod]
	public void InterleavedAttributesPreserveActualViewsOffsetsTypesAndStrides()
	{
		using var document = Read( $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[92] )}}}","byteLength":92},
			 {"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[10] )}}}","byteLength":10}],
			 "bufferViews":[{"buffer":0,"byteOffset":8,"byteLength":84,"byteStride":28},{"buffer":1,"byteOffset":4,"byteLength":6}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":0,"byteOffset":12,"componentType":5126,"count":3,"type":"VEC3"},
			 {"bufferView":0,"byteOffset":24,"componentType":5123,"normalized":true,"count":3,"type":"VEC2"},
			 {"bufferView":1,"componentType":5123,"count":3,"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"indices":3}]}]}
			""" );
		var description = document.DescribePrimitiveUpload( 0, 0 );
		Assert.AreEqual( 4, description.SourceMode );
		Assert.AreEqual( 3, description.VertexCount );
		var position = description.Attributes["POSITION"];
		Assert.AreEqual( 0, position.AccessorIndex ); Assert.AreEqual( 0, position.SourceBufferView );
		Assert.AreEqual( 0, position.SourceBuffer ); Assert.AreEqual( 8, position.ViewByteOffset );
		Assert.AreEqual( 84, position.ViewByteLength ); Assert.AreEqual( 0, position.AccessorByteOffset );
		Assert.AreEqual( 28, position.ByteStride ); Assert.AreEqual( 5126, position.ComponentType );
		Assert.AreEqual( 3, position.Components ); Assert.IsFalse( position.Normalized );
		Assert.AreEqual( 12, description.Attributes["NORMAL"].AccessorByteOffset );
		var uv = description.Attributes["TEXCOORD_0"];
		Assert.AreEqual( 24, uv.AccessorByteOffset ); Assert.AreEqual( 28, uv.ByteStride );
		Assert.AreEqual( 5123, uv.ComponentType ); Assert.AreEqual( 2, uv.Components ); Assert.IsTrue( uv.Normalized );
		Assert.AreEqual( GltfUploadMaterialization.None, uv.Materialization );
		Assert.AreEqual( 1, description.Indices.SourceBufferView ); Assert.AreEqual( 1, description.Indices.SourceBuffer );
		Assert.AreEqual( 4, description.Indices.ViewByteOffset ); Assert.AreEqual( 2, description.Indices.ByteStride );
		Assert.IsFalse( description.RequiresGeneratedIndices ); Assert.IsFalse( description.RequiresTopologyExpansion );
	}

	[TestMethod]
	public void SparseAndMissingBaseRequireMaterializationWithoutInventingStorage()
	{
		using var document = Read( $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[16] )}}}","byteLength":16}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":1},{"buffer":0,"byteOffset":4,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0],
			 "sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"mode":5}]}]}
			""" );
		var description = document.DescribePrimitiveUpload( 0, 0 );
		var position = description.Attributes["POSITION"];
		Assert.IsNull( position.SourceBufferView ); Assert.IsNull( position.SourceBuffer );
		Assert.AreEqual( GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized, position.Materialization );
		Assert.IsNull( description.Indices ); Assert.IsTrue( description.RequiresGeneratedIndices ); Assert.IsTrue( description.RequiresTopologyExpansion );
	}

	[TestMethod]
	public void SelectedMeshoptViewIsIdentifiedWithoutInvokingTheCodec()
	{
		// Metadata-only fixture: intentionally not a valid meshopt stream. Describing
		// the layout must not call native decode or treat fallback bytes as decoded.
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_meshopt_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[36] )}}}","byteLength":36}],
			 "bufferViews":[{"buffer":0,"byteLength":36,"byteStride":12,"extensions":{"EXT_meshopt_compression":{"buffer":0,"byteLength":1,"byteStride":12,"count":3,"mode":"ATTRIBUTES"}}}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}]}
			""";
		using var selected = Read( json, "EXT_meshopt_compression" );
		Assert.AreEqual( GltfUploadMaterialization.Meshopt, selected.DescribePrimitiveUpload( 0, 0 ).Attributes["POSITION"].Materialization );
		using var fallback = Read( json );
		Assert.AreEqual( GltfUploadMaterialization.None, fallback.DescribePrimitiveUpload( 0, 0 ).Attributes["POSITION"].Materialization );
	}

	[TestMethod]
	public void SelectedDracoMarksOnlyItsMappedAttributesAndIndices()
	{
		// Metadata-only: no native Draco decode occurs in the description path.
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAA","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"componentType":5126,"count":3,"type":"VEC3"},{"componentType":5123,"count":3,"type":"SCALAR"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"indices":2,
			 "extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var description = document.DescribePrimitiveUpload( 0, 0 );
		Assert.AreEqual( GltfUploadMaterialization.Draco, description.Attributes["POSITION"].Materialization );
		Assert.AreEqual( GltfUploadMaterialization.ZeroInitialized, description.Attributes["NORMAL"].Materialization );
		Assert.AreEqual( GltfUploadMaterialization.Draco, description.Indices.Materialization );
		Assert.IsTrue( description.RequiresDracoIndices );
	}

	[TestMethod]
	public void DracoWithoutAnIndexAccessorStillRequiresCodecTopology()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAA","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var description = document.DescribePrimitiveUpload( 0, 0 );
		Assert.IsNull( description.Indices ); Assert.IsTrue( description.RequiresDracoIndices ); Assert.IsFalse( description.RequiresGeneratedIndices );
	}

	[TestMethod]
	public void SharedAccessorUsesTheRequestedPrimitivesOwnDracoMapping()
	{
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[48] )}}}","byteLength":48}],
			 "bufferViews":[{"buffer":0,"byteLength":36,"byteStride":12},{"buffer":0,"byteOffset":36,"byteLength":12}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}},
			 {"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":1,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var ordinary = document.DescribePrimitiveUpload( 0, 0 );
		var compressed = document.DescribePrimitiveUpload( 0, 1 );
		Assert.AreEqual( GltfUploadMaterialization.None, ordinary.Attributes["POSITION"].Materialization );
		Assert.AreEqual( 0, ordinary.Attributes["POSITION"].SourceBufferView ); Assert.AreEqual( 12, ordinary.Attributes["POSITION"].ByteStride );
		Assert.IsTrue( ordinary.RequiresGeneratedIndices ); Assert.IsFalse( ordinary.RequiresDracoIndices );
		Assert.AreEqual( GltfUploadMaterialization.Draco, compressed.Attributes["POSITION"].Materialization );
		Assert.IsNull( compressed.Attributes["POSITION"].ByteStride ); Assert.IsTrue( compressed.RequiresDracoIndices );
	}

	[TestMethod]
	public void DracoTriangleStripReportsDecodedTriangleListWithoutDoubleExpansion()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAA","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],
			 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"mode":5,"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var description = document.DescribePrimitiveUpload( 0, 0 );
		Assert.AreEqual( 5, description.SourceMode ); Assert.AreEqual( 4, description.DecodedMode );
		Assert.IsTrue( description.RequiresDracoIndices ); Assert.IsFalse( description.RequiresGeneratedIndices ); Assert.IsFalse( description.RequiresTopologyExpansion );
	}

	[TestMethod]
	public void SelectedDracoDoesNotInspectAnUnusableFallbackView()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_draco_mesh_compression"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAA","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":12}],
			 "accessors":[{"bufferView":99,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"extensions":{"KHR_draco_mesh_compression":{"bufferView":0,"attributes":{"POSITION":0}}}}]}]}
			""";
		using var document = Read( json, "KHR_draco_mesh_compression" );
		var position = document.DescribePrimitiveUpload( 0, 0 ).Attributes["POSITION"];
		Assert.AreEqual( 99, position.SourceBufferView ); Assert.IsNull( position.SourceBuffer ); Assert.IsNull( position.ByteStride );
		Assert.AreEqual( GltfUploadMaterialization.Draco, position.Materialization );
	}

	[TestMethod]
	public void CustomMatrixReportsPaddedColumnsWithoutAssumingAStructLayout()
	{
		using var document = Read( $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String( new byte[24] )}}}","byteLength":24}],
			 "bufferViews":[{"buffer":0,"byteLength":12},{"buffer":0,"byteOffset":12,"byteLength":12}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":1,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"componentType":5121,"count":1,"type":"MAT3"}],
			 "meshes":[{"primitives":[{"mode":0,"attributes":{"POSITION":0,"_MATRIX":1}}]}]}
			""" );
		var matrix = document.DescribePrimitiveUpload( 0, 0 ).Attributes["_MATRIX"];
		Assert.AreEqual( 3, matrix.Rows ); Assert.AreEqual( 3, matrix.Columns ); Assert.AreEqual( 9, matrix.Components );
		Assert.AreEqual( 4, matrix.ColumnStride ); Assert.AreEqual( 12, matrix.ByteStride );
	}

	[TestMethod]
	[DataRow( "COLOR_0", 5121, 1 )]
	[DataRow( "TEXCOORD_0", 5123, 2 )]
	public void VertexOffsetsNeedFourByteAlignmentRelativeToTheirView( string semantic, int component, int viewOffset )
	{
		using var document = AlignmentFixture( semantic, component, viewOffset, 0 );
		var primitive = document.ReadPrimitive( 0, 0 );
		Assert.AreEqual( 1, primitive.Attributes[semantic] );
		var binding = document.DescribePrimitiveUpload( 0, 0 ).Attributes[semantic];
		Assert.AreEqual( viewOffset, binding.ViewByteOffset ); Assert.AreEqual( 0, binding.AccessorByteOffset );
		Assert.AreEqual( 4, binding.ByteStride ); Assert.AreEqual( GltfUploadMaterialization.None, binding.Materialization );
		CollectionAssert.AreEqual( new float[semantic == "COLOR_0" ? 12 : 6], document.ReadFloatAccessor( 1 ) );
	}

	[TestMethod]
	public void AlignedAbsoluteOffsetDoesNotExcuseMisalignedRelativeVertexOffset()
	{
		using var document = AlignmentFixture( "COLOR_0", 5121, 3, 1 );
		Assert.ThrowsException<GltfException>( () => document.ReadPrimitive( 0, 0 ) );
		Assert.ThrowsException<GltfException>( () => document.DescribePrimitiveUpload( 0, 0 ) );
	}

	private static GltfDocument AlignmentFixture( string semantic, int component, int viewOffset, int accessorOffset )
	{
		var vertexData = Convert.ToBase64String( new byte[36] );
		var viewLength = accessorOffset + 12;
		var extraData = Convert.ToBase64String( new byte[viewOffset + viewLength] );
		var type = semantic == "COLOR_0" ? "VEC4" : "VEC2";
		var json = $$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{vertexData}}}","byteLength":36},
			 {"uri":"data:application/octet-stream;base64,{{{extraData}}}","byteLength":{{{viewOffset + viewLength}}} }],
			 "bufferViews":[{"buffer":0,"byteLength":36},{"buffer":1,"byteOffset":{{{viewOffset}}},"byteLength":{{{viewLength}}},"byteStride":4}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			 {"bufferView":1,"byteOffset":{{{accessorOffset}}},"componentType":{{{component}}},"normalized":true,"count":3,"type":"{{{type}}}"}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"{{{semantic}}}":1}}]}]}
			""";
		return Read( json );
	}

	private static GltfDocument Read( string json, string extension = null ) => GltfReader.Read( Encoding.UTF8.GetBytes( json ),
		supportedExtensions: extension is null ? null : new HashSet<string>( StringComparer.Ordinal ) { extension } );
}
