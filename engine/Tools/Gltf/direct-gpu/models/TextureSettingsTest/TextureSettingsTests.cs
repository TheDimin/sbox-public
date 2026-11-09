using System;
using System.Buffers.Binary;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Ref = TextureSettingsReference;
using V4 = System.Numerics.Vector4;

[TestClass]
public sealed class TextureSettingsTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( "glTF", false )]
	[DataRow( "glTF", true )]
	[DataRow( "glTF-Binary", false )]
	[DataRow( "glTF-Binary", true )]
	[DataRow( "glTF-Embedded", false )]
	[DataRow( "glTF-Embedded", true )]
	public void ActualModelSamplersMaterialsAndImagesPreserveAuthoredState( string form, bool compiled )
	{
		using var source = new Ref( CheckoutRoot ); using var document = source.Open( form, compiled );
		Assert.AreEqual( 5, document.Root.GetProperty( "samplers" ).GetArrayLength() );
		for ( var i = 0; i < 5; i++ )
		{
			var sampler = document.ReadSampler( i );
			Assert.AreEqual( 9986, sampler.MinFilter ); Assert.AreEqual( 9729, sampler.MagFilter );
			Assert.AreEqual( Ref.WrapS[i], sampler.WrapS ); Assert.AreEqual( Ref.WrapT[i], sampler.WrapT );
			var mapped = global::Sandbox.GltfSamplerMapping.Map( sampler, 9987, 9729 );
			Assert.AreEqual( 5, (int)mapped.State.Filter, "NEAREST min + LINEAR mag + LINEAR mip must remain independent." );
			Assert.IsFalse( mapped.RequiresBaseMipOnly ); Assert.IsNull( mapped.State.MaxLod );
		}
		for ( var i = 0; i < 10; i++ )
		{
			var material = document.ReadMaterial( i );
			Assert.AreEqual( i == 7, material.DoubleSided ); Assert.AreEqual( "OPAQUE", material.AlphaMode );
			Assert.AreEqual( 0f, material.MetallicFactor ); Assert.AreEqual( .9f, material.RoughnessFactor );
			if ( i == 4 ) { Assert.IsNull( material.BaseColorTexture ); continue; }
			var textureIndex = Ref.MaterialTextures[i]; Assert.AreEqual( textureIndex, material.BaseColorTexture.Index );
			Assert.AreEqual( 0, material.BaseColorTexture.TexCoord ); Assert.IsTrue( material.BaseColorTexture.IsSrgb );
			var texture = document.ReadTexture( textureIndex );
			Assert.AreEqual( Ref.TextureSamplers[textureIndex], texture.Sampler ); Assert.AreEqual( Ref.TextureImages[textureIndex], texture.Image );
		}
		for ( var i = 0; i < 3; i++ ) CollectionAssert.AreEqual( source.Resolve( Ref.ImageNames[i] ), document.ReadImage( i ).Bytes );
	}

	[TestMethod]
	[DataRow( "glTF", false )]
	[DataRow( "glTF", true )]
	[DataRow( "glTF-Binary", false )]
	[DataRow( "glTF-Binary", true )]
	[DataRow( "glTF-Embedded", false )]
	[DataRow( "glTF-Embedded", true )]
	public void ActualGeometryPreservesOutsideUvAndBackfaceInputs( string form, bool compiled )
	{
		using var source = new Ref( CheckoutRoot ); using var document = source.Open( form, compiled );
		for ( var mesh = 0; mesh < 10; mesh++ )
		{
			var upload = document.MaterializePrimitiveUpload( mesh, 0 );
			foreach ( var semantic in mesh == 4 ? new[] { "POSITION", "NORMAL" } : new[] { "POSITION", "NORMAL", "TEXCOORD_0" } )
			{
				var expected = source.Attribute( mesh, semantic ); var stream = upload.Attributes[semantic];
				Assert.AreEqual( expected.Length, stream.Count ); Assert.AreEqual( 5126, stream.ComponentType );
				for ( var row = 0; row < expected.Length; row++ ) for ( var c = 0; c < expected[row].Length; c++ )
					Assert.AreEqual( expected[row][c], BinaryPrimitives.ReadSingleLittleEndian( stream.Storage.Bytes.Span.Slice( stream.ByteOffset + row * stream.ByteStride + c * 4, 4 ) ), $"mesh {mesh} {semantic} row {row} component {c}" );
			}
			var indices = source.Indices( mesh ); Assert.AreEqual( indices.Length, upload.Indices.Count );
			Assert.AreEqual( 5123, upload.Indices.ComponentType );
			for ( var i = 0; i < indices.Length; i++ ) Assert.AreEqual( indices[i], (uint)BinaryPrimitives.ReadUInt16LittleEndian( upload.Indices.Storage.Bytes.Span.Slice( upload.Indices.ByteOffset + i * upload.Indices.ByteStride, 2 ) ) );
		}
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	[DataRow( 3 )]
	[DataRow( 5 )]
	[DataRow( 6 )]
	public void ActualTestColumnRejectsEveryWrongAddressMode( int mesh )
	{
		using var source = new Ref( CheckoutRoot );
		var texture = Ref.MaterialTextures[mesh]; var sampler = Ref.TextureSamplers[texture]; var image = source.Images[Ref.TextureImages[texture]];
		var s = Ref.WrapS[sampler]; var t = Ref.WrapT[sampler]; var axisS = mesh is 1 or 5 or 6;
		foreach ( var wrong in new[] { 10497, 33071, 33648 }.Where( x => x != (axisS ? s : t) ) )
		{
			var changed = source.TestColumnUvs( mesh ).Count( uv => V4.Distance( Ref.Sample( image, uv.X, uv.Y, s, t, true ), Ref.Sample( image, uv.X, uv.Y, axisS ? wrong : s, axisS ? t : wrong, true ) ) > .1f );
			Assert.IsTrue( changed > 0, $"mesh {mesh}: actual source checkmark/X must distinguish wrong wrap {wrong}; oracle geometry may target the wrong column." );
		}
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	public void ActualImageDistinguishesNearestFromLinearAtTexelBoundary( int imageIndex )
	{
		using var source = new Ref( CheckoutRoot ); var image = source.Images[imageIndex]; var differences = 0;
		for ( var y = 1; y < image.Height; y += 7 ) for ( var x = 1; x < image.Width; x += 7 )
		{
			var u = (x + .25) / image.Width; var v = (y + .5) / image.Height;
			if ( V4.Distance( Ref.Sample( image, u, v, 10497, 10497, false ), Ref.Sample( image, u, v, 10497, 10497, true ) ) > .02f ) differences++;
		}
		Assert.IsTrue( differences > 0, "Pinned image must reveal conflating authored minification and magnification." );
	}

	[TestMethod]
	public void AddressOracleHandlesNegativeTilesAndDuplicatedMirrorEdgeTexels()
	{
		// Texel-index addressing is needed after half-texel bilinear offsets.
		CollectionAssert.AreEqual( new[] { 3, 0, 3, 0, 3, 0 }, new[] { -5, -4, -1, 0, 3, 4 }.Select( x => Ref.Address( x, 4, 10497 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 3, 3, 0, 0, 3, 3 }, new[] { -5, -4, -1, 0, 3, 4 }.Select( x => Ref.Address( x, 4, 33648 ) ).ToArray() );
		CollectionAssert.AreEqual( new[] { 0, 0, 0, 0, 3, 3 }, new[] { -5, -4, -1, 0, 3, 4 }.Select( x => Ref.Address( x, 4, 33071 ) ).ToArray() );
	}
}
