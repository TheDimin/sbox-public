using Sandbox.Gltf;
using System;
using System.Collections.Generic;

namespace Sandbox.Tests;

[TestClass]
public class GltfGeometryBufferTests
{
	[TestMethod]
	public void UploadLayoutRetainsFloatColorAndTangentSign()
	{
		var data = Triangle();
		var vertices = GltfGeometryBuffer.PackVertices( data );
		Assert.AreEqual( new Vector3( 1, 2, 3 ), vertices[0].Position );
		Assert.AreEqual( new Vector4( 0.123456f, 0.2f, 0.3f, 0.4f ), vertices[0].Color );
		Assert.AreEqual( new Vector4( 1, 0, 0, -1 ), vertices[0].Tangent );
	}

	[TestMethod]
	public void EachMaterialRoleSelectsItsOwnUvSetAndTransform()
	{
		var material = new GltfMaterial
		{
			BaseColorTexture = new GltfTextureInfo
			{
				TexCoord = 2, Tiling = new System.Numerics.Vector2( 2, 3 ),
				Rotation = MathF.PI / 2, Offset = new System.Numerics.Vector2( 1, 2 )
			},
			NormalTexture = new GltfTextureInfo { TexCoord = 1, Tiling = System.Numerics.Vector2.One }
		};
		var vertices = GltfGeometryBuffer.PackVertices( Triangle(), material );
		Assert.IsTrue( vertices[0].BaseColorUv.AlmostEqual( new Vector2( -0.5f, 2.5f ), 0.00001f ) );
		Assert.AreEqual( new Vector2( 3, 4 ), vertices[0].NormalUv );
		Assert.AreEqual( new Vector2( 0, 1 ), vertices[0].MetallicRoughnessUv );
	}

	[TestMethod]
	public void MissingMaterialUvSetFailsBeforeAnyGpuAllocation()
	{
		var material = new GltfMaterial { EmissiveTexture = new GltfTextureInfo { TexCoord = 7 } };
		var error = Assert.ThrowsException<GltfException>( () => GltfGeometryBuffer.PackVertices( Triangle(), material ) );
		StringAssert.Contains( error.Message, "TEXCOORD_7" );
	}

	private static GltfDrawData Triangle() => new()
	{
		Mode = 4, Indices = new uint[] { 0, 1, 2 },
		Positions = new System.Numerics.Vector3[] { new( 1, 2, 3 ), new( 4, 5, 6 ), new( 7, 8, 9 ) },
		Normals = new System.Numerics.Vector3[] { new( 0, 0, 1 ), new( 0, 0, 1 ), new( 0, 0, 1 ) },
		Tangents = new System.Numerics.Vector4[] { new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ), new( 1, 0, 0, -1 ) },
		Colors = new System.Numerics.Vector4[] { new( 0.123456f, 0.2f, 0.3f, 0.4f ), System.Numerics.Vector4.One, System.Numerics.Vector4.One },
		TexCoords = new Dictionary<int, ReadOnlyMemory<System.Numerics.Vector2>>
		{
			[0] = new System.Numerics.Vector2[] { new( 0, 1 ), new( 0, 0 ), new( 0, 0 ) },
			[1] = new System.Numerics.Vector2[] { new( 3, 4 ), new( 0, 0 ), new( 0, 0 ) },
			[2] = new System.Numerics.Vector2[] { new( 0.25f, 0.5f ), new( 0, 0 ), new( 0, 0 ) }
		}
	};
}
