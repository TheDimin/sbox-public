using System;
using System.Text;
using Sandbox.Gltf;
using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfUnlitSourceMaterialAdapterTests
{
	private const string Pixel = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";

	[TestMethod]
	public void UnlitPreparationDoesNotReadIgnoredLitRoles()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"}}""" ), _ => throw new InvalidOperationException( "Ignored texture was read." ) );
		var ignored = new GltfTextureInfo { Index = 999 };
		var material = new GltfMaterial { Unlit = true, AlphaMode = "OPAQUE", BaseColorFactor = new( .2f, .4f, .8f, .1f ),
			MetallicRoughnessTexture = ignored, NormalTexture = ignored, OcclusionTexture = ignored, EmissiveTexture = ignored, EmissiveFactor = NVector3.One };
		var prepared = GltfUnlitSourceMaterialAdapter.Prepare( document, material );
		Assert.IsNull( prepared.BaseColor ); Assert.AreEqual( new NVector4( .2f, .4f, .8f, 1 ), prepared.Plan.LinearFactorForShader );
	}

	[TestMethod]
	[DataRow( "MASK", .5f )]
	[DataRow( "BLEND", 2f )]
	public void NonOpaqueFactorsRetainAuthoredAlphaAndUseRetainedImage( string mode, float cutoff )
	{
		var reads = 0;
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"textures":[{"source":0}]}""" ), _ => { reads++; return Convert.FromBase64String( Pixel ); } );
		var snapshot = document.ReadImage( 0 );
		var material = new GltfMaterial { Unlit = true, AlphaMode = mode, AlphaCutoff = cutoff, BaseColorFactor = new( .8f, .4f, .2f, .5f ), BaseColorTexture = new() { Index = 0, Tiling = NVector2.One } };
		var prepared = GltfUnlitSourceMaterialAdapter.Prepare( document, material, 2, _ => snapshot );
		Assert.AreEqual( 1, reads ); Assert.AreEqual( new NVector4( .8f, .4f, .2f, .5f ), prepared.Plan.LinearFactorForShader );
		Assert.IsTrue( prepared.BaseColor.Encoding.IsSrgb ); Assert.IsTrue( prepared.BaseColor.Encoding.HasLinearAlpha );
		Assert.IsTrue( prepared.BaseColor.Resource.Length > 16 );
	}
}
