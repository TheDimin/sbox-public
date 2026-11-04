using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Text;
using Sandbox;
using Sandbox.Gltf;

namespace ResourceTests;

[TestClass]
public sealed class GltfModelPrimitiveBuilderTests
{
	[TestMethod]
	public void PreparedVerticesRetainLinearFloatColorAndStockUvSets()
	{
		var prepared = GltfModelPrimitiveBuilder.Prepare( Triangle() );
		Assert.AreEqual( new Vector4( 0.123456f, 0.2f, 0.3f, 0.4f ), prepared.Vertices[0].Color );
		Assert.AreEqual( new Vector2( 0.25f, 0.75f ), prepared.Vertices[0].TexCoord0 );
		Assert.AreEqual( new Vector2( 0.5f, 0.125f ), prepared.Vertices[0].TexCoord1 );
		Assert.AreEqual( new Vector4( 1, 0, 0, -1 ), prepared.Vertices[0].Tangent );
		CollectionAssert.AreEqual( new[] { 0, 1, 2 }, prepared.Indices );
	}

	[TestMethod]
	public void MissingTriangleNormalsSplitSharedVerticesAndGenerateFlatNormals()
	{
		var data = new GltfDrawData
		{
			Mode = 4,
			Positions = new System.Numerics.Vector3[] { new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ), new( 0, 0, 1 ) },
			Indices = new uint[] { 0, 1, 2, 0, 3, 1 },
			Colors = new System.Numerics.Vector4[] { new( 0.123456f, 1, 1, 1 ), System.Numerics.Vector4.One, System.Numerics.Vector4.One, System.Numerics.Vector4.One },
			TexCoords = new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>>()
		};
		var prepared = GltfModelPrimitiveBuilder.Prepare( data );
		Assert.AreEqual( 6, prepared.Vertices.Length );
		CollectionAssert.AreEqual( new[] { 0, 1, 2, 3, 4, 5 }, prepared.Indices );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), prepared.Vertices[0].Normal );
		Assert.AreEqual( new Vector3( 0, 1, 0 ), prepared.Vertices[3].Normal );
		Assert.AreEqual( prepared.Vertices[0].Color, prepared.Vertices[3].Color );
	}

	[TestMethod]
	public void LargeFiniteTriangleProducesFiniteFlatNormals()
	{
		var data = new GltfDrawData
		{
			Mode = 4, Indices = new uint[] { 0, 1, 2 },
			Positions = new System.Numerics.Vector3[] { new( 0, 0, 0 ), new( 1e30f, 0, 0 ), new( 0, 1e30f, 0 ) }
		};
		var prepared = GltfModelPrimitiveBuilder.Prepare( data );
		Assert.AreEqual( new Vector3( 0, 0, 1 ), prepared.Vertices[0].Normal );
	}

	[TestMethod]
	public void RejectsMalformedOrDeformableStreamsBeforeNativeAllocation()
	{
		Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( Triangle( skinning: true ) ) );
		Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( Triangle( morphs: true ) ) );
		Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( Triangle( indices: [0, 1, 3] ) ) );
		Assert.ThrowsException<GltfException>( () => GltfModelPrimitiveBuilder.Prepare( Triangle( indices: [0, 1] ) ) );
	}

	[TestMethod]
	[DataRow( 0, 0, 6 )]
	[DataRow( 1, 1, 6 )]
	[DataRow( 2, 1, 12 )]
	[DataRow( 3, 1, 10 )]
	[DataRow( 4, 4, 6 )]
	[DataRow( 5, 4, 12 )]
	[DataRow( 6, 4, 12 )]
	public void SourceAndCompiledPrimitiveReachNativeModelWithExpectedDrawMetadata( int sourceMode, int expectedMode, int expectedCount )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var source = CreateSource( sourceMode );
		var compiled = GltfCompiler.Compile( source );
		var material = Material.Load( "materials/default/white.vmat" );
		Assert.IsTrue( material.IsValid );
		foreach ( var bytes in new[] { source, compiled } )
		{
			using var document = GltfReader.Read( bytes );
			var data = document.CreateDrawData( 0, 0 );
			Assert.AreEqual( expectedMode, data.Mode );
			Assert.AreEqual( expectedCount, data.Indices.Length );
			var prepared = GltfModelPrimitiveBuilder.Prepare( data );
			for ( var i = 0; i < data.Indices.Length; i++ )
				Assert.AreEqual( (Vector3)data.Positions.Span[(int)data.Indices.Span[i]], prepared.Vertices[prepared.Indices[i]].Position );
			var model = GltfModelPrimitiveBuilder.Build( data, material );
			Assert.IsTrue( model.IsValid && !model.IsError );
			Assert.AreEqual( 1, model.MeshInfo.Meshes.Length );
			var draw = model.MeshInfo.Meshes[0].DrawCalls[0];
			Assert.AreEqual( expectedCount, draw.Indices );
			var nativeMode = expectedMode switch
			{
				0 => NativeEngine.RenderPrimitiveType.RENDER_PRIM_POINTS,
				1 => NativeEngine.RenderPrimitiveType.RENDER_PRIM_LINES,
				_ => NativeEngine.RenderPrimitiveType.RENDER_PRIM_TRIANGLES
			};
			Assert.AreEqual( (int)nativeMode, draw.PrimitiveType );
			Assert.IsTrue( model.RenderBounds.Mins.x <= 0 && model.RenderBounds.Maxs.y >= 39 );
		}
	}

	private static GltfDrawData Triangle( bool skinning = false, bool morphs = false, uint[] indices = null ) => new()
	{
		Mode = 4, HasSkinning = skinning, HasMorphTargets = morphs,
		Positions = new System.Numerics.Vector3[] { new( 0, 0, 0 ), new( 1, 0, 0 ), new( 0, 1, 0 ) },
		Normals = new System.Numerics.Vector3[] { new( 0, 0, 1 ), new( 0, 0, 1 ), new( 0, 0, 1 ) },
		Tangents = new System.Numerics.Vector4[] { new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ) },
		Colors = new System.Numerics.Vector4[] { new( 0.123456f, 0.2f, 0.3f, 0.4f ), System.Numerics.Vector4.One, System.Numerics.Vector4.One },
		Indices = indices ?? [0, 1, 2],
		TexCoords = new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>>
		{
			[0] = new System.Numerics.Vector2[] { new( 0.25f, 0.75f ), default, default },
			[1] = new System.Numerics.Vector2[] { new( 0.5f, 0.125f ), default, default },
			[2] = new System.Numerics.Vector2[] { default, default, default }
		}
	};

	private static byte[] CreateSource( int mode )
	{
		float[] coordinates = [0, 0, 0, 1, 0, 0, 0, 1, 0, 2, 0, 0, 3, 0, 0, 2, 1, 0];
		var bytes = new byte[coordinates.Length * sizeof(float)];
		for ( var i = 0; i < coordinates.Length; i++ ) BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i * 4, 4 ), coordinates[i] );
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,"
			+ Convert.ToBase64String( bytes ) + "\",\"byteLength\":" + bytes.Length + "}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":" + bytes.Length
			+ "}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":6,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[3,1,0]}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0},\"mode\":" + mode + "}]}]}";
		return Encoding.UTF8.GetBytes( json );
	}
}
