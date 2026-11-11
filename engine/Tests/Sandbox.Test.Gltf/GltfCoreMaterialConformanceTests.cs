using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

/// <summary>Reader and container evidence only; these tests do not render materials.</summary>
[TestClass]
public sealed class GltfCoreMaterialConformanceTests
{
	// The existing GltfImageTests fixture: self-generated 1x1 white PNG.
	private const string WhitePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";
	private static readonly IReadOnlySet<string> TransformSupport = new HashSet<string>( StringComparer.Ordinal ) { "KHR_texture_transform" };

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SharedImageRolesKeepIndependentTransformsColorSpacesAndSamplers( bool compile )
	{
		// Five roles share one image through two texture objects. Explicit, different
		// UV sets and transforms must stay attached to the role, not to the image.
		var json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],
			 "images":[{"uri":"data:image/png;base64,PNG_DATA"}],
			 "samplers":[{"magFilter":9728,"minFilter":9987,"wrapS":33071,"wrapT":33648},{"magFilter":9729,"minFilter":9728,"wrapS":10497,"wrapT":33071}],
			 "textures":[{"source":0,"sampler":0},{"source":0,"sampler":1}],
			 "materials":[{"pbrMetallicRoughness":{
			   "baseColorTexture":{"index":0,"texCoord":0,"extensions":{"KHR_texture_transform":{"offset":[0.1,0.2],"texCoord":1}}},
			   "metallicRoughnessTexture":{"index":1,"texCoord":0,"extensions":{"KHR_texture_transform":{"offset":[0.2,0.3],"texCoord":2}}}},
			   "normalTexture":{"index":1,"texCoord":0,"extensions":{"KHR_texture_transform":{"offset":[0.3,0.4],"texCoord":3}}},
			   "occlusionTexture":{"index":1,"texCoord":0,"extensions":{"KHR_texture_transform":{"offset":[0.4,0.5],"texCoord":4}}},
			   "emissiveTexture":{"index":0,"texCoord":0,"extensions":{"KHR_texture_transform":{"offset":[0.5,0.6],"texCoord":5}}}}]}
			""".Replace( "PNG_DATA", WhitePng, StringComparison.Ordinal );
		var bytes = Encoding.UTF8.GetBytes( json );
		if ( compile ) bytes = GltfCompiler.Compile( bytes, supportedExtensions: TransformSupport );
		using var document = GltfReader.Read( bytes, supportedExtensions: TransformSupport );
		var material = document.ReadMaterial( 0 );
		var roles = new[] { material.BaseColorTexture, material.MetallicRoughnessTexture, material.NormalTexture, material.OcclusionTexture, material.EmissiveTexture };
		var srgb = new[] { true, false, false, false, true };
		var textureIndices = new[] { 0, 1, 1, 1, 0 };
		var offsets = new[] { new Vector2( .1f, .2f ), new Vector2( .2f, .3f ), new Vector2( .3f, .4f ), new Vector2( .4f, .5f ), new Vector2( .5f, .6f ) };
		for ( var i = 0; i < roles.Length; i++ )
		{
			var role = roles[i] ?? throw new AssertFailedException( $"Missing role {i}." );
			Assert.AreEqual( textureIndices[i], role.Index, $"role {i} texture" );
			Assert.AreEqual( i + 1, role.TexCoord, $"role {i} UV set" );
			Assert.AreEqual( srgb[i], role.IsSrgb, $"role {i} transfer" );
			Assert.AreEqual( offsets[i].X + .25f, role.TransformUv( new Vector2( .25f, .5f ) ).X, 1e-6f );
			Assert.AreEqual( offsets[i].Y + .5f, role.TransformUv( new Vector2( .25f, .5f ) ).Y, 1e-6f );
			var texture = document.ReadTexture( role.Index );
			Assert.AreEqual( 0, texture.Image );
			Assert.AreEqual( textureIndices[i], texture.Sampler );
		}
		var first = document.ReadSampler( 0 );
		var second = document.ReadSampler( 1 );
		Assert.AreEqual( 9728, first.MagFilter ); Assert.AreEqual( 9987, first.MinFilter );
		Assert.AreEqual( 33071, first.WrapS ); Assert.AreEqual( 33648, first.WrapT );
		Assert.AreEqual( 9729, second.MagFilter ); Assert.AreEqual( 9728, second.MinFilter );
		Assert.AreEqual( 10497, second.WrapS ); Assert.AreEqual( 33071, second.WrapT );
		CollectionAssert.AreEqual( Convert.FromBase64String( WhitePng ), document.ReadImage( 0 ).Bytes );
	}

	[TestMethod]
	[DataRow( 9728, 9728, 10497, 33071 )]
	[DataRow( 9729, 9729, 33071, 33648 )]
	[DataRow( 9728, 9984, 33648, 10497 )]
	[DataRow( 9729, 9985, 10497, 33648 )]
	[DataRow( 9728, 9986, 33071, 10497 )]
	[DataRow( 9729, 9987, 33648, 33071 )]
	public void CoreSamplerModesRetainIndependentMinMagAndAxes( int mag, int min, int wrapS, int wrapT )
	{
		var json = JsonSerializer.SerializeToUtf8Bytes( new { asset = new { version = "2.0" }, samplers = new[] { new { magFilter = mag, minFilter = min, wrapS, wrapT } } } );
		using var document = GltfReader.Read( json );
		var sampler = document.ReadSampler( 0 );
		Assert.AreEqual( mag, sampler.MagFilter ); Assert.AreEqual( min, sampler.MinFilter );
		Assert.AreEqual( wrapS, sampler.WrapS ); Assert.AreEqual( wrapT, sampler.WrapT );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void MaskMaterialAndNormalizedVertexAlphaSurviveTogether( bool compile )
	{
		var data = new byte[48];
		var positions = new[] { 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f };
		for ( var i = 0; i < positions.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteSingleLittleEndian( data.AsSpan( i * 4, 4 ), positions[i] );
		new byte[] { 255, 128, 0, 64, 0, 255, 0, 128, 0, 0, 255, 255 }.CopyTo( data, 36 );
		var json = JsonSerializer.SerializeToUtf8Bytes( new
		{
			asset = new { version = "2.0" },
			buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( data ), byteLength = data.Length } },
			bufferViews = new object[] { new { buffer = 0, byteOffset = 0, byteLength = 36 }, new { buffer = 0, byteOffset = 36, byteLength = 12 } },
			accessors = new object[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0, 0, 0 }, max = new[] { 1, 1, 0 } }, new { bufferView = 1, componentType = 5121, normalized = true, count = 3, type = "VEC4" } },
			materials = new[] { new { alphaMode = "MASK", alphaCutoff = .4f, pbrMetallicRoughness = new { baseColorFactor = new[] { .5f, .25f, .75f, .3f } } } },
			meshes = new[] { new { primitives = new[] { new { attributes = new Dictionary<string, int> { ["POSITION"] = 0, ["COLOR_0"] = 1 }, material = 0 } } } }
		} );
		if ( compile ) json = GltfCompiler.Compile( json );
		using var document = GltfReader.Read( json );
		var draw = document.CreateDrawData( 0, 0 );
		Assert.AreEqual( 0, draw.Material );
		var material = document.ReadMaterial( draw.Material.Value );
		Assert.AreEqual( "MASK", material.AlphaMode ); Assert.AreEqual( .4f, material.AlphaCutoff, 1e-6f );
		Assert.AreEqual( new Vector4( .5f, .25f, .75f, .3f ), material.BaseColorFactor );
		var first = draw.Colors.Span[0];
		Assert.AreEqual( 1f, first.X, 1e-6f ); Assert.AreEqual( 128f / 255, first.Y, 1e-6f );
		Assert.AreEqual( 0f, first.Z, 1e-6f ); Assert.AreEqual( 64f / 255, first.W, 1e-6f );
		Assert.AreEqual( 128f / 255, draw.Colors.Span[1].W, 1e-6f );
		Assert.AreEqual( 1f, draw.Colors.Span[2].W, 1e-6f );
		// The renderer must multiply factor, texture and vertex alpha before MASK;
		// preserved inputs here are not evidence that it actually does so.
	}

	[TestMethod]
	[DataRow( "{\"pbrMetallicRoughness\":{\"metallicFactor\":-0.01}}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"metallicFactor\":1.01}}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"roughnessFactor\":-0.01}}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"roughnessFactor\":1.01}}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,-0.01]}}" )]
	[DataRow( "{\"pbrMetallicRoughness\":{\"baseColorFactor\":[1,1,1,1.01]}}" )]
	[DataRow( "{\"emissiveFactor\":[1,1,-0.01]}" )]
	[DataRow( "{\"alphaCutoff\":-0.01}" )]
	public void OutOfRangeCoreMaterialFactorsAreRejected( string material )
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"materials\":[" + material + "]}" ) );
		Assert.ThrowsException<GltfException>( () => document.ReadMaterial( 0 ) );
	}
}
