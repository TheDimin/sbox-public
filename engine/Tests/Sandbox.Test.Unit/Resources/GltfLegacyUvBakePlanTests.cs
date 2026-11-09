using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfLegacyUvBakePlanTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TransformOverrideUsesTheActualInterleavedNormalizedAccessor( bool compiled )
	{
		using var document = Read( """{"index":0,"texCoord":0,"extensions":{"KHR_texture_transform":{"texCoord":1,"offset":[0.25,-0.5],"rotation":1.5707963267948966,"scale":[2,3]}}}""", compiled );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var before = upload.Attributes["TEXCOORD_1"].Storage.Bytes.ToArray();
		var plan = GltfLegacyUvBakePlan.Create( document, 0, 0, upload );
		Assert.AreEqual( 1, plan.SourceTexCoord ); Assert.AreEqual( 3, plan.VertexCount );
		Assert.AreSame( upload.Attributes["TEXCOORD_1"], plan.Source );
		Vector( plan.Bytes.Span, 0, 1.75f, -1f, 0.00004f ); // (0.25, 0.5) -> (1.75, -1)
		Vector( plan.Bytes.Span, 1, 3.25f, -2.5f, 0.00004f ); // (1, 1) -> (3.25, -2.5)
		Vector( plan.Bytes.Span, 2, 0.25f, -0.5f, 0.00004f );
		CollectionAssert.AreEqual( before, upload.Attributes["TEXCOORD_1"].Storage.Bytes.ToArray() );
	}

	[TestMethod]
	[DataRow( "{\"offset\":[0.5,0]}", .75f, .5f )]
	[DataRow( "{\"offset\":[0,0.5]}", .25f, 1f )]
	[DataRow( "{\"offset\":[0.5,0.5]}", .75f, 1f )]
	[DataRow( "{\"rotation\":-1.5707963267948966}", -.5f, .25f )]
	[DataRow( "{\"scale\":[-2,3]}", -.5f, 1.5f )]
	public void OffsetSignedRotationAndScaleMatchLiteralResults( string transform, float expectedU, float expectedV )
	{
		using var document = Read( "{\"index\":0,\"texCoord\":1,\"extensions\":{\"KHR_texture_transform\":" + transform + "}}", false );
		var plan = GltfLegacyUvBakePlan.Create( document, 0, 0, document.MaterializePrimitiveUpload( 0, 0 ) );
		Vector( plan.Bytes.Span, 0, expectedU, expectedV, 0.00004f );
	}

	[TestMethod]
	public void UnsupportedOptionalTransformRetainsCoreTexCoordAndIdentityFallback()
	{
		using var document = Read( """{"index":0,"texCoord":0,"extensions":{"KHR_texture_transform":{"texCoord":1,"offset":[0.5,0.5]}}}""", false, false );
		var plan = GltfLegacyUvBakePlan.Create( document, 0, 0, document.MaterializePrimitiveUpload( 0, 0 ) );
		Assert.AreEqual( 0, plan.SourceTexCoord ); Vector( plan.Bytes.Span, 0, 0f, 1f, 0.00001f );
	}

	[TestMethod]
	public void DifferentCoreTextureRoleUvMapsAreRejectedRatherThanAliased()
	{
		using var document = Read( """{"index":0,"texCoord":0}""", false, true, """{"index":1,"texCoord":1}""" );
		var error = Assert.ThrowsException<GltfException>( () => GltfLegacyUvBakePlan.Create( document, 0, 0, document.MaterializePrimitiveUpload( 0, 0 ) ) );
		StringAssert.Contains( error.Message, "different" );
	}

	[TestMethod]
	public void DifferentTransformsOnTheSameSetAreRejectedAndEqualTransformsAreAccepted()
	{
		const string first = """{"index":0,"texCoord":1,"extensions":{"KHR_texture_transform":{"offset":[0.5,0]}}}""";
		const string different = """{"index":1,"texCoord":1,"extensions":{"KHR_texture_transform":{"offset":[0,0.5]}}}""";
		const string equal = """{"index":1,"texCoord":1,"extensions":{"KHR_texture_transform":{"offset":[0.5,0]}}}""";
		using var rejected = Read( first, false, metallicTexture: different );
		Assert.ThrowsException<GltfException>( () => GltfLegacyUvBakePlan.Create( rejected, 0, 0, rejected.MaterializePrimitiveUpload( 0, 0 ) ) );
		using var accepted = Read( first, false, metallicTexture: equal );
		var plan = GltfLegacyUvBakePlan.Create( accepted, 0, 0, accepted.MaterializePrimitiveUpload( 0, 0 ) );
		Vector( plan.Bytes.Span, 0, .75f, .5f, .00004f );
	}

	[TestMethod]
	public void SignedNormalizedQuantizedUvUsesTheMaterializedComponentRules()
	{
		var data = new byte[] { 0x80, 0x7f, 0, 0, 0xc0, 0x40, 0, 0, 0, 0, 0, 0 };
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_mesh_quantization\"],\"extensionsRequired\":[\"KHR_mesh_quantization\"]," +
			"\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( data ) + "\",\"byteLength\":12}]," +
			"\"bufferViews\":[{\"buffer\":0,\"byteLength\":12,\"byteStride\":4}]," +
			"\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			"{\"bufferView\":0,\"componentType\":5120,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"}]," +
			"\"textures\":[{}],\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorTexture\":{\"index\":0}}}]," +
			"\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1},\"material\":0}]}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_mesh_quantization" } );
		var plan = GltfLegacyUvBakePlan.Create( document, 0, 0, document.MaterializePrimitiveUpload( 0, 0 ) );
		Vector( plan.Bytes.Span, 0, -1, 1, 0 );
		Vector( plan.Bytes.Span, 1, -64f / 127f, 64f / 127f, 1e-7f );
	}

	[TestMethod]
	public void SparseZeroBaseUsesTheOwnedMaterializedStream()
	{
		var data = new byte[8]; data[0] = 1;
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 4 ), 65535 );
		BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 6 ), 32768 );
		var json = $$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{Convert.ToBase64String( data )}}}}","byteLength":8}],
			"bufferViews":[{"buffer":0,"byteLength":1},{"buffer":0,"byteOffset":4,"byteLength":4}],
			"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"componentType":5123,"normalized":true,"count":3,"type":"VEC2","sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}],
			"textures":[{}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0}]}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) ); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var plan = GltfLegacyUvBakePlan.Create( document, 0, 0, upload );
		Assert.IsTrue( plan.Source.IsMaterialized ); Vector( plan.Bytes.Span, 0, 0, 0, 0 );
		Vector( plan.Bytes.Span, 1, 1, 32768f / 65535f, 1e-7f ); Vector( plan.Bytes.Span, 2, 0, 0, 0 );
	}

	[TestMethod]
	public void RawAndBakedRoutesShareInputArenaAndPreserveMorphDescriptorBoundaries()
	{
		using var document = Read( """{"index":0,"texCoord":1,"extensions":{"KHR_texture_transform":{"offset":[0.5,0]}}}""", false, morph: true );
		var original = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var (baked, raw) = original.CreateLegacyUvPlans();
		Assert.IsNotNull( baked ); Assert.IsNotNull( raw );
		Assert.AreSame( original.Upload, baked.Upload ); Assert.AreSame( original.Upload, raw.Upload );
		Assert.AreSame( baked.InputBytes, raw.InputBytes ); Assert.AreSame( baked.InputDescriptors, raw.InputDescriptors );
		Assert.AreSame( original.SourceVertices, baked.SourceVertices ); Assert.AreSame( original.Indices, raw.Indices );
		Assert.AreEqual( original.BaseAttributeCount + 1, baked.BaseAttributeCount );
		Assert.AreEqual( original.InputDescriptors.Length + 1, baked.InputDescriptors.Length );
		Assert.AreEqual( uint.MaxValue, baked.InputDescriptors[original.BaseAttributeCount].Target );
		Assert.AreEqual( 4u, baked.InputDescriptors[original.BaseAttributeCount].Role );
		Assert.AreEqual( 0u, baked.InputDescriptors[baked.BaseAttributeCount].Target );
		var field = original.Fields.ToList().FindIndex( item => item.Semantic == "TEXCOORD" && item.SemanticIndex == 0 );
		Assert.AreEqual( (uint)original.BaseAttributeCount, baked.OutputDescriptors[field].Source );
		Assert.AreEqual( original.OutputDescriptors[field].Source, raw.OutputDescriptors[field].Source );
		Assert.AreEqual( original.VertexStride, baked.VertexStride ); Assert.AreEqual( original.VertexStride, raw.VertexStride );
	}

	[TestMethod]
	public void IdentityBaseUvDoesNotNeedAnExtraRouteOrModifyTheExistingPlan()
	{
		using var document = Read( """{"index":0}""", false );
		var original = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var (baked, raw) = original.CreateLegacyUvPlans();
		Assert.IsNull( baked ); Assert.IsNull( raw );
		Assert.AreSame( original.Upload.Attributes["TEXCOORD_0"], original.Sources["TEXCOORD_0"].Stream );
	}

	private static void Vector( ReadOnlySpan<byte> bytes, int index, float u, float v, float tolerance )
	{
		Assert.AreEqual( u, BinaryPrimitives.ReadSingleLittleEndian( bytes[(index * 8)..] ), tolerance );
		Assert.AreEqual( v, BinaryPrimitives.ReadSingleLittleEndian( bytes[(index * 8 + 4)..] ), tolerance );
	}

	private static GltfDocument Read( string baseTexture, bool compiled, bool extension = true, string metallicTexture = null, bool morph = false )
	{
		var bytes = new byte[48];
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			var offset = 8 + vertex * 12;
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset ), 0 );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset + 2 ), 65535 );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset + 4 ), vertex == 0 ? (ushort)16384 : vertex == 1 ? ushort.MaxValue : (ushort)0 );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( offset + 6 ), vertex == 0 ? (ushort)32768 : vertex == 1 ? ushort.MaxValue : (ushort)0 );
		}
		var mr = metallicTexture is null ? "" : ",\"metallicRoughnessTexture\":" + metallicTexture;
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_texture_transform\"]," +
			"\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\",\"byteLength\":48}]," +
			"\"bufferViews\":[{\"buffer\":0,\"byteOffset\":8,\"byteLength\":36,\"byteStride\":12}]," +
			"\"accessors\":[{\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[0,0,0]}," +
			"{\"bufferView\":0,\"componentType\":5123,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"}," +
			"{\"bufferView\":0,\"byteOffset\":4,\"componentType\":5123,\"normalized\":true,\"count\":3,\"type\":\"VEC2\"}]," +
			"\"textures\":[{},{}],\"materials\":[{\"pbrMetallicRoughness\":{\"baseColorTexture\":" + baseTexture + mr + "}}]," +
			"\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"TEXCOORD_0\":1,\"TEXCOORD_1\":2},\"material\":0" + (morph ? ",\"targets\":[{\"POSITION\":0}]" : "") + "}]}]}";
		var data = Encoding.UTF8.GetBytes( json );
		var supported = extension ? new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_transform" } : new HashSet<string>( StringComparer.Ordinal );
		if ( compiled ) data = GltfCompiler.Compile( data, supportedExtensions: supported );
		return GltfReader.Read( data, supportedExtensions: supported );
	}
}
