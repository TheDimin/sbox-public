using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfGeneratedTangentRouteTests
{
	[TestMethod]
	public void MirroredIndexedSeamProducesCornerDescriptorWithoutChangingSource()
	{
		using var document = Fixture();
		var basePlan = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var plan = basePlan.ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] );
		var bytesBefore = (byte[])plan.InputBytes.Clone(); var sourceBefore = (uint[])plan.SourceVertices.Clone(); var indicesBefore = (uint[])plan.Indices.Clone();
		var route = GltfGpuGeneratedTangentRoute.Prepare( plan, false );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 0, 2, 3 }, route.SourceVertices );
		CollectionAssert.AreEqual( new uint[] { 0, 1, 2, 3, 4, 5 }, route.Indices );
		CollectionAssert.AreEqual( bytesBefore, plan.InputBytes ); CollectionAssert.AreEqual( sourceBefore, plan.SourceVertices ); CollectionAssert.AreEqual( indicesBefore, plan.Indices );
		var tangent = route.OutputDescriptors.Single( x => x.Role == 2 );
		Assert.AreEqual( 1u, tangent.Padding0 ); Assert.AreEqual( (uint)plan.BaseAttributeCount, tangent.Source );
		Assert.AreEqual( plan.BaseAttributeCount + 1, route.BaseAttributeCount );
		var descriptor = route.InputDescriptors[tangent.Source]; Assert.AreEqual( uint.MaxValue, descriptor.Target ); Assert.AreEqual( 16u, descriptor.ByteStride );
		for ( var corner = 0; corner < 6; corner++ )
		{
			var offset = (int)descriptor.ByteOffset + corner * 16;
			Assert.AreEqual( corner < 3 ? 1f : -1f, BinaryPrimitives.ReadSingleLittleEndian( route.InputBytes.AsSpan( offset ) ), .00001f );
			Assert.AreEqual( corner < 3 ? -1f : 1f, BinaryPrimitives.ReadSingleLittleEndian( route.InputBytes.AsSpan( offset + 12 ) ), .00001f );
		}
	}

	[TestMethod]
	public void MaterialTransformsCreateDistinctFramesOnTheSameStableCornerTopology()
	{
		using var document = Fixture(); var raw = GltfGpuPrimitivePlan.Create( document, 0, 0 );
		var original = GltfGpuGeneratedTangentRoute.Prepare( raw.ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] ), false );
		var rotated = GltfGpuGeneratedTangentRoute.Prepare( raw.ProjectMaterial( 1, 5, null, [0, 1, 4, 5, 6] ), false );
		var shared = GltfGpuGeneratedTangentRoute.ExpandTopology( raw );
		CollectionAssert.AreEqual( original.SourceVertices, rotated.SourceVertices ); CollectionAssert.AreEqual( shared.SourceVertices, rotated.SourceVertices );
		CollectionAssert.AreEqual( original.Indices, rotated.Indices ); CollectionAssert.AreEqual( shared.Indices, rotated.Indices );
		var a = original.InputDescriptors[original.BaseAttributeCount - 1]; var b = rotated.InputDescriptors[rotated.BaseAttributeCount - 1];
		Assert.AreEqual( 1f, BinaryPrimitives.ReadSingleLittleEndian( original.InputBytes.AsSpan( (int)a.ByteOffset ) ), .00001f );
		Assert.AreEqual( 0f, BinaryPrimitives.ReadSingleLittleEndian( rotated.InputBytes.AsSpan( (int)b.ByteOffset ) ), .00001f );
		Assert.AreEqual( 1f, BinaryPrimitives.ReadSingleLittleEndian( rotated.InputBytes.AsSpan( (int)b.ByteOffset + 4 ) ), .00001f );
	}

	[TestMethod]
	public void AuthoredTangentBypassesGenerationAndMorphGenerationFailsExplicitly()
	{
		using ( var authored = Fixture( authored: true ) )
		{
			var plan = GltfGpuPrimitivePlan.Create( authored, 0, 0 ).ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] );
			Assert.IsNull( GltfGpuGeneratedTangentRoute.Prepare( plan, true ) );
		}
		using ( var document = Fixture() )
		{
			var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ).ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] );
			Assert.IsNotNull( GltfGpuGeneratedTangentRoute.Prepare( plan, true ), "Skinning transforms the generated base frame through the normal authored-tangent path." );
		}
		using ( var document = Fixture( morphed: true ) )
		{
			var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ).ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] );
			StringAssert.Contains( Assert.ThrowsException<GltfException>( () => GltfGpuGeneratedTangentRoute.Prepare( plan, false ) ).Message, "morphed" );
		}
	}

	[TestMethod]
	public void CornerDescriptorPrecedesMorphDescriptorsAndPreservesReferences()
	{
		GltfGpuInputDescriptor[] inputs = [new() { Role = 0, Target = uint.MaxValue }, new() { Role = 3, Target = 0 }];
		GltfGpuOutputDescriptor[] outputs = [new() { Role = 0, Source = 0 }, new() { Role = 2, Source = uint.MaxValue }, new() { Role = 3, Source = 1 }];
		var tables = GltfGpuGeneratedTangentRoute.InsertCornerDescriptor( inputs, 1, outputs, 16 );
		Assert.AreEqual( 3, tables.Inputs.Length );
		Assert.AreEqual( 2u, tables.Inputs[1].Role ); Assert.AreEqual( uint.MaxValue, tables.Inputs[1].Target );
		Assert.AreEqual( 3u, tables.Inputs[2].Role ); Assert.AreEqual( 0u, tables.Inputs[2].Target );
		Assert.AreEqual( 0u, tables.Outputs[0].Source ); Assert.AreEqual( 1u, tables.Outputs[1].Source ); Assert.AreEqual( 1u, tables.Outputs[1].Padding0 );
		Assert.AreEqual( 2u, tables.Outputs[2].Source ); Assert.AreEqual( 1u, outputs[2].Source );
	}

	[TestMethod]
	public void AuthoredTangentIsIgnoredWhenNormalsAreAbsent()
	{
		using var document = Fixture( authored: true, withoutNormal: true );
		var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ).ProjectMaterial( 0, 5, null, [0, 1, 4, 5, 6] );
		var route = GltfGpuGeneratedTangentRoute.Prepare( plan, false );
		Assert.IsNotNull( route ); Assert.AreEqual( 1u, route.OutputDescriptors.Single( x => x.Role == 2 ).Padding0 );
	}

	private static GltfDocument Fixture( bool authored = false, bool morphed = false, bool withoutNormal = false )
	{
		var data = new byte[124];
		float[] p = [0,0,0, 1,0,0, 0,1,0, -1,0,0];
		for ( var i = 0; i < p.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( i * 4 ), p[i] );
		for ( var i = 0; i < 4; i++ ) BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( 48 + i * 12 + 8 ), 1 );
		byte[] uv = [0,0,0,0, 255,0,0,0, 0,255,0,0, 255,0,0,0]; uv.CopyTo( data, 96 );
		ushort[] indices = [0,1,2,0,2,3]; for ( var i = 0; i < indices.Length; i++ ) BinaryPrimitives.WriteUInt16LittleEndian( data.AsSpan( 112 + i * 2 ), indices[i] );
		var tangent = authored ? ",\"TANGENT\":4" : "";
		var normalAttribute = withoutNormal ? "" : ",\"NORMAL\":1";
		var targets = morphed ? ",\"targets\":[{\"POSITION\":5}]" : "";
		var json = $$$$$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{{{{Convert.ToBase64String(data)}}}}}}","byteLength":124}],
			 "bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":16,"byteStride":4},{"buffer":0,"byteOffset":112,"byteLength":12}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[-1,0,0],"max":[1,1,0]},
			 {"bufferView":1,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":2,"componentType":5121,"normalized":true,"count":4,"type":"VEC2"},
			 {"bufferView":3,"componentType":5123,"count":6,"type":"SCALAR"},{"componentType":5126,"count":4,"type":"VEC4"},{"componentType":5126,"count":4,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
			 "textures":[{}],"materials":[{"normalTexture":{"index":0}},{"normalTexture":{"index":0,"extensions":{"KHR_texture_transform":{"rotation":1.5707963267948966}}}}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":2{{{{{{normalAttribute}}}}}}{{{{{{tangent}}}}}} },"indices":3,"material":0{{{{{{targets}}}}}} }]}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_texture_transform" } );
	}
}
