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
public sealed class GltfMaterialUvProjectionTests
{
	[TestMethod]
	public void FiveArbitrarySourceSetsMapToCompactSlotsWithoutChangingDescriptors()
	{
		using var document = Read();
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var uv = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload );
		var projection = GltfMaterialUvProjection.Create( uv, 5 );
		CollectionAssert.AreEqual( new[] { 2, 4, 7, 9, 12 }, projection.Sources.Select( x => x.TexCoord ).ToArray() );
		foreach ( var source in projection.Sources )
			Assert.AreSame( upload.Attributes["TEXCOORD_" + source.TexCoord], source.Stream );
		Assert.AreEqual( 4, projection.Slots[GltfMaterialTextureRole.BaseColor] ); // extension overrides texCoord 2
		Assert.AreEqual( 0, projection.Slots[GltfMaterialTextureRole.MetallicRoughness] );
		Assert.AreEqual( 1, projection.Slots[GltfMaterialTextureRole.Normal] );
		Assert.AreEqual( 2, projection.Slots[GltfMaterialTextureRole.Occlusion] );
		Assert.AreEqual( 3, projection.Slots[GltfMaterialTextureRole.Emissive] );
		Assert.AreEqual( 5121, projection.Sources[4].Stream.ComponentType );
		Assert.IsTrue( projection.Sources[4].Stream.Normalized );
		Assert.AreEqual( new System.Numerics.Vector2( .25f, -.5f ), uv.Bindings[GltfMaterialTextureRole.BaseColor].Transform.Apply( System.Numerics.Vector2.Zero ) );
	}

	[TestMethod]
	public void SharedSetUsesOneSlotEvenWhenRolesHaveDifferentTransforms()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var material = new GltfMaterial
		{
			BaseColorTexture = new GltfTextureInfo { Index = 0, TexCoord = 12, Tiling = System.Numerics.Vector2.One, Offset = new( .25f, 0 ) },
			NormalTexture = new GltfTextureInfo { Index = 1, TexCoord = 12, Tiling = new( 2, 3 ) }
		};
		var uv = GltfMaterialUvPlan.Create( material, upload );
		var projection = GltfMaterialUvProjection.Create( uv, 1 );
		Assert.AreEqual( 1, projection.Sources.Count );
		Assert.AreEqual( 0, projection.Slots[GltfMaterialTextureRole.BaseColor] );
		Assert.AreEqual( 0, projection.Slots[GltfMaterialTextureRole.Normal] );
		Assert.AreNotEqual( uv.Bindings[GltfMaterialTextureRole.BaseColor].Transform.RowU, uv.Bindings[GltfMaterialTextureRole.Normal].Transform.RowU );
	}

	[TestMethod]
	public void CapacityIsExplicitAndInsufficientSlotsDoNotAlias()
	{
		using var document = Read(); var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var uv = GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), upload );
		var error = Assert.ThrowsException<GltfException>( () => GltfMaterialUvProjection.Create( uv, 2 ) );
		StringAssert.Contains( error.Message, "5" ); StringAssert.Contains( error.Message, "2" );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => GltfMaterialUvProjection.Create( uv, -1 ) );
		Assert.ThrowsException<ArgumentNullException>( () => GltfMaterialUvProjection.Create( null, 5 ) );
		var empty = GltfMaterialUvProjection.Create( GltfMaterialUvPlan.Create( null, upload ), 0 );
		Assert.AreEqual( 0, empty.Sources.Count ); Assert.AreEqual( 0, empty.Slots.Count );
	}

	[TestMethod]
	public void ProjectionCollectionsCannotBeMutatedByConsumers()
	{
		using var document = Read();
		var projection = GltfMaterialUvProjection.Create( GltfMaterialUvPlan.Create( document.ReadMaterial( 0 ), document.MaterializePrimitiveUpload( 0, 0 ) ), 5 );
		Assert.ThrowsException<NotSupportedException>( () => ((IDictionary<GltfMaterialTextureRole, int>)projection.Slots).Clear() );
		Assert.ThrowsException<NotSupportedException>( () => ((IList<GltfMaterialUvProjection.Source>)projection.Sources).Clear() );
	}

	[TestMethod]
	public void ProjectedGpuOutputsPointAtRetainedSourceDescriptorsForEachCompactSlot()
	{
		using var document = Read();
		var raw = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var projected = GltfGpuPrimitivePlan.Create( document, 0, 0, shaderUvInputs: 5 );
		Assert.IsNull( raw.UvProjection );
		Assert.AreEqual( 13, raw.Fields.Count( x => x.Semantic == "TEXCOORD" ) );
		Assert.AreEqual( 5, projected.Fields.Count( x => x.Semantic == "TEXCOORD" ) );
		for ( var slot = 0; slot < 5; slot++ )
		{
			var source = projected.UvProjection.Sources[slot];
			var fieldIndex = projected.Fields.ToList().FindIndex( x => x.Semantic == "TEXCOORD" && x.SemanticIndex == slot );
			Assert.IsTrue( fieldIndex >= 0 );
			var output = projected.OutputDescriptors[fieldIndex];
			var binding = projected.Sources["TEXCOORD_" + source.TexCoord];
			Assert.AreSame( source.Stream, binding.Stream );
			Assert.AreEqual( (uint)binding.DescriptorIndex, output.Source );
			Assert.AreEqual( (uint)slot, output.SemanticIndex );
			Assert.AreEqual( (uint)source.Stream.ComponentType, projected.InputDescriptors[output.Source].ComponentType );
			Assert.AreEqual( (uint)source.Stream.ByteStride, projected.InputDescriptors[output.Source].ByteStride );
		}
		Assert.ThrowsException<GltfException>( () => GltfGpuPrimitivePlan.Create( document, 0, 0, shaderUvInputs: 2 ) );
	}

	[TestMethod]
	public void UnusedShaderInputsAreZeroFallbacksAndIgnoredRolesAreNotRequired()
	{
		using var document = Read();
		var projected = GltfGpuPrimitivePlan.Create( document, 0, 0, shaderUvInputs: 5, consumedRoles: [GltfMaterialTextureRole.BaseColor] );
		Assert.AreEqual( 1, projected.UvProjection.Sources.Count );
		Assert.AreEqual( 12, projected.UvProjection.Sources[0].TexCoord );
		for ( var slot = 0; slot < 5; slot++ )
		{
			var fieldIndex = projected.Fields.ToList().FindIndex( x => x.Semantic == "TEXCOORD" && x.SemanticIndex == slot );
			Assert.AreNotEqual( -1, fieldIndex );
			Assert.AreEqual( slot == 0 ? (uint)projected.Sources["TEXCOORD_12"].DescriptorIndex : uint.MaxValue, projected.OutputDescriptors[fieldIndex].Source );
		}
		Assert.ThrowsException<ArgumentException>( () => GltfGpuPrimitivePlan.Create( document, 0, 0, consumedRoles: [GltfMaterialTextureRole.BaseColor] ) );
	}

	[TestMethod]
	public void VariantProjectionsShareRetainedInputsButHaveIndependentOutputRoutes()
	{
		using var document = Read(); var raw = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var first = raw.ProjectMaterial( 0, 5 );
		var second = raw.ProjectMaterial( 1, 5 );
		Assert.AreSame( raw.Upload, first.Upload ); Assert.AreSame( first.Upload, second.Upload );
		Assert.AreSame( first.InputDescriptors, second.InputDescriptors ); Assert.AreSame( first.InputBytes, second.InputBytes );
		Assert.AreNotSame( first.OutputDescriptors, second.OutputDescriptors );
		Assert.AreEqual( first.VertexStride, second.VertexStride );
		CollectionAssert.AreEqual( first.Fields.Select( f => (f.Semantic, f.SemanticIndex, f.Offset, f.Components) ).ToArray(),
			second.Fields.Select( f => (f.Semantic, f.SemanticIndex, f.Offset, f.Components) ).ToArray() );
		var fieldIndex = first.Fields.ToList().FindIndex( x => x.Semantic == "TEXCOORD" && x.SemanticIndex == 0 );
		Assert.AreEqual( (uint)first.Sources["TEXCOORD_2"].DescriptorIndex, first.OutputDescriptors[fieldIndex].Source );
		Assert.AreEqual( (uint)second.Sources["TEXCOORD_12"].DescriptorIndex, second.OutputDescriptors[fieldIndex].Source );
		Assert.AreEqual( (uint)first.Sources["TEXCOORD_2"].DescriptorIndex, first.OutputDescriptors[fieldIndex].Source );
		Assert.IsNull( raw.UvProjection );
	}

	[TestMethod]
	public void ShaderPhysicalSemanticsCanAvoidStockVertexInputsWithoutChangingRoleSlots()
	{
		using var document = Read(); var raw = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var projected = raw.ProjectMaterial( 0, 5, shaderUvSemantics: [0, 1, 4, 5, 6] );
		CollectionAssert.AreEqual( new[] { 0, 1, 4, 5, 6 }, projected.Fields.Where( f => f.Semantic == "TEXCOORD" ).Select( f => f.SemanticIndex ).ToArray() );
		Assert.AreEqual( 4, projected.UvProjection.Slots[GltfMaterialTextureRole.BaseColor] );
		var fifth = projected.Fields.ToList().FindIndex( f => f.Semantic == "TEXCOORD" && f.SemanticIndex == 6 );
		Assert.AreEqual( (uint)projected.Sources["TEXCOORD_12"].DescriptorIndex, projected.OutputDescriptors[fifth].Source );
		Assert.ThrowsException<ArgumentException>( () => raw.ProjectMaterial( 0, 5, shaderUvSemantics: [0, 1, 1, 5, 6] ) );
		Assert.ThrowsException<ArgumentException>( () => raw.ProjectMaterial( 0, 5, shaderUvSemantics: [0, 1] ) );
		Assert.ThrowsException<ArgumentException>( () => raw.ProjectMaterial( 0, 5, shaderUvSemantics: [0, 1, 4, 5, -1] ) );
	}

	[TestMethod]
	public void ProjectedOutputUsesInterleavedAccessorOffsetStrideAndNormalization()
	{
		var bytes = Enumerable.Repeat( (byte)0xa7, 44 ).ToArray();
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 12 + vertex * 12 ), (ushort)(1000 + vertex) );
			BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 14 + vertex * 12 ), (ushort)(2000 + vertex) );
		}
		var json = $$$$$$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{Convert.ToBase64String( bytes )}}}}}}","byteLength":44}],
			"bufferViews":[{"buffer":0,"byteOffset":8,"byteLength":36,"byteStride":12}],
			"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
			{"bufferView":0,"byteOffset":4,"componentType":5123,"normalized":true,"count":3,"type":"VEC2"}],
			"textures":[{}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":2}}}],
			"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1,"TEXCOORD_1":1,"TEXCOORD_2":1},"material":0}]}]}
			""";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0, shaderUvInputs: 5 );
		var index = plan.Fields.ToList().FindIndex( field => field.Semantic == "TEXCOORD" && field.SemanticIndex == 0 );
		var binding = plan.Sources["TEXCOORD_2"]; var descriptor = plan.InputDescriptors[plan.OutputDescriptors[index].Source];
		Assert.AreEqual( (uint)binding.ArenaByteOffset, descriptor.ByteOffset );
		Assert.AreEqual( 4, binding.Stream.ByteOffset ); Assert.AreEqual( 12u, descriptor.ByteStride );
		Assert.AreEqual( 5123u, descriptor.ComponentType ); Assert.AreEqual( 1u, descriptor.Normalized );
		Assert.AreEqual( (ushort)1001, BinaryPrimitives.ReadUInt16LittleEndian( plan.InputBytes.AsSpan( binding.ArenaByteOffset + 12 ) ) );
		Assert.AreEqual( (ushort)2001, BinaryPrimitives.ReadUInt16LittleEndian( plan.InputBytes.AsSpan( binding.ArenaByteOffset + 14 ) ) );
	}

	private static GltfDocument Read() => GltfReader.Read( Encoding.UTF8.GetBytes( """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],
		"accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]},
		{"componentType":5123,"normalized":true,"count":3,"type":"VEC2"},{"componentType":5126,"count":3,"type":"VEC2"},
		{"componentType":5123,"normalized":true,"count":3,"type":"VEC2"},{"componentType":5126,"count":3,"type":"VEC2"},
		{"componentType":5121,"normalized":true,"count":3,"type":"VEC2"}],
		"textures":[{},{},{},{},{}],
		"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":2,"extensions":{"KHR_texture_transform":{"texCoord":12,"offset":[0.25,-0.5]}}},"metallicRoughnessTexture":{"index":1,"texCoord":2}},
		"normalTexture":{"index":2,"texCoord":4},"occlusionTexture":{"index":3,"texCoord":7},"emissiveTexture":{"index":4,"texCoord":9}},
		{"pbrMetallicRoughness":{"baseColorTexture":{"index":0,"texCoord":12}}}],
		"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1,"TEXCOORD_1":1,"TEXCOORD_2":1,"TEXCOORD_3":1,"TEXCOORD_4":2,"TEXCOORD_5":1,"TEXCOORD_6":1,"TEXCOORD_7":3,"TEXCOORD_8":1,"TEXCOORD_9":4,"TEXCOORD_10":1,"TEXCOORD_11":1,"TEXCOORD_12":5},"material":0}]}]}
		""" ), supportedExtensions: new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_transform" } );
}
