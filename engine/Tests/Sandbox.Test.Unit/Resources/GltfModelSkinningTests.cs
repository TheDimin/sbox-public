using NativeEngine;
using Sandbox.Gltf;
using System;
using System.Buffers.Binary;
using System.Linq;
using System.Text;

namespace Sandbox.Tests;

[TestClass]
public class GltfModelSkinningTests
{
	[TestMethod]
	public void JointRemappingUsesAccessorStrideAndPreservesEveryWeightSet()
	{
		using var document = GltfReader.Read( Source( secondSet: true ) );
		var binding = GltfVertexSkinning.Read( document, 0, 0, [9, 4] );
		var prepared = GltfModelPrimitiveBuilder.Prepare( document.CreateDrawData( 0, 0 ), upload: document.MaterializePrimitiveUpload( 0, 0 ), skinning: binding );
		Assert.AreEqual( 8, binding.Influences );
		Assert.AreEqual( 4, prepared.VertexFields.Count( f => f.Semantic is "blendindices" or "blendweight" ) );
		for ( var set = 0; set < 2; set++ )
		{
			var joints = prepared.VertexFields.Single( f => f.Semantic == "blendindices" && f.SemanticIndex == set );
			var weights = prepared.VertexFields.Single( f => f.Semantic == "blendweight" && f.SemanticIndex == set );
			Assert.AreEqual( ColorFormat.COLOR_FORMAT_R16G16B16A16_UINT, joints.Format );
			Assert.AreEqual( ColorFormat.COLOR_FORMAT_R32G32B32A32_FLOAT, weights.Format );
			for ( var vertex = 0; vertex < 3; vertex++ )
			{
				var at = prepared.VertexStride * vertex;
				Assert.AreEqual( (ushort)9, BinaryPrimitives.ReadUInt16LittleEndian( prepared.VertexBytes.AsSpan( at + joints.Offset, 2 ) ) );
				Assert.AreEqual( (ushort)4, BinaryPrimitives.ReadUInt16LittleEndian( prepared.VertexBytes.AsSpan( at + joints.Offset + 2, 2 ) ) );
				Assert.AreEqual( .3f, BinaryPrimitives.ReadSingleLittleEndian( prepared.VertexBytes.AsSpan( at + weights.Offset, 4 ) ) );
			}
		}
	}

	[TestMethod]
	public void IdentityNativeBonesAreDistinctForDifferentSkinBindMatrices()
	{
		using var document = GltfReader.Read( Source( secondSet: false ) );
		var rig = GltfModelRig.Prepare( document );
		Assert.AreEqual( 2, rig.BoneCount );
		Assert.AreEqual( 1, rig.Primitives.Count );
		var pose = document.EvaluatePose();
		var matrices = rig.EvaluateModelPalettes( pose );
		Assert.AreEqual( 2, matrices.Length );
		Assert.AreEqual( 0, matrices[0].M41 );
		Assert.AreEqual( 39.37008f, matrices[1].M42, .0001f );
	}

	[TestMethod]
	public void NativeTrsConversionRejectsShearInsteadOfApproximating()
	{
		var shear = System.Numerics.Matrix4x4.Identity;
		shear.M12 = .3f;
		Assert.IsFalse( GltfModelRig.TryNativeTransform( shear, out _ ) );
		var trs = System.Numerics.Matrix4x4.CreateScale( 2, 3, 4 ) * System.Numerics.Matrix4x4.CreateRotationZ( .45f ) * System.Numerics.Matrix4x4.CreateTranslation( 7, 8, 9 );
		Assert.IsTrue( GltfModelRig.TryNativeTransform( trs, out var value ) );
		Assert.AreEqual( new Vector3( 7, 8, 9 ), value.Position );
	}

	[TestMethod]
	[DataRow( .01f )]
	[DataRow( 1f )]
	[DataRow( 100f )]
	public void PureTrsRemainsRepresentableAtSampleAssetScales( float scale )
	{
		var rotation = System.Numerics.Quaternion.Normalize( new System.Numerics.Quaternion( 0, .7071067f, -.7071068f, 0 ) );
		var source = System.Numerics.Matrix4x4.CreateScale( scale ) * System.Numerics.Matrix4x4.CreateFromQuaternion( rotation );
		var matrix = GltfGeometry.ConvertTransform( source );
		Assert.IsTrue( GltfModelRig.TryNativeTransform( matrix, out _ ), "A constructed pure TRS must not be mislabeled as inherited shear, including AnimatedMorphCube's scale100 orientation." );
	}

	[TestMethod]
	[DataRow( 1f )]
	[DataRow( 100f )]
	[DataRow( 1000000f )]
	public void LargeOtherAxesDoNotHideRealShearOnANegativeSmallAxis( float otherScale )
	{
		var matrix = System.Numerics.Matrix4x4.CreateScale( otherScale, -.01f, 2 ) * System.Numerics.Matrix4x4.CreateRotationZ( .35f );
		matrix.M21 += .0001f;
		Assert.IsFalse( GltfModelRig.TryNativeTransform( matrix, out _ ), "A global scale-dependent tolerance must not swallow real shear on a much smaller axis." );
	}

	private static byte[] Source( bool secondSet )
	{
		// Nonzero accessor offsets and padded 48-byte records deliberately prevent
		// treating the glTF payload as a native vertex struct.
		var bytes = new byte[144];
		for ( var vertex = 0; vertex < 3; vertex++ )
		{
			var at = vertex * 48;
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at, 4 ), vertex == 1 ? 1 : 0 );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 4, 4 ), vertex == 2 ? 1 : 0 );
			bytes[at + 16] = 0; bytes[at + 17] = 1;
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 24, 4 ), .3f );
			BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( at + 28, 4 ), secondSet ? .2f : .7f );
		}
		var extra = secondSet ? ",\"JOINTS_1\":1,\"WEIGHTS_1\":2" : "";
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{\"nodes\":[0,1,2]}],\"nodes\":[{\"mesh\":0,\"skin\":0},{},{\"translation\":[1,0,0]}],\"skins\":[{\"joints\":[1,2]}],\"buffers\":[{\"byteLength\":144,\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\"}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":144,\"byteStride\":48}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":0,\"byteOffset\":16,\"componentType\":5121,\"count\":3,\"type\":\"VEC4\"},{\"bufferView\":0,\"byteOffset\":24,\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"}],\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"JOINTS_0\":1,\"WEIGHTS_0\":2" + extra + "}}]}]}" );
	}
}
