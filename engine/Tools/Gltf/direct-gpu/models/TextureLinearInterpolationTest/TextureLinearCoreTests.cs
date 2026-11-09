using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureLinearValidation;

[TestClass]
internal sealed class TextureLinearCoreTests
{
	public static string CheckoutRoot { get; set; }
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExactSourceMaterialSamplerImageAndAllConstantUvs( bool compiled )
	{
		using var reference = new TextureLinearReference( CheckoutRoot );
		byte[] input = compiled ? GltfCompiler.Compile( reference.Source ) : reference.Source;
		using var doc = GltfReader.Read( input );
		var a = doc.ReadMaterial( 0 ); var b = doc.ReadMaterial( 1 ); var label = doc.ReadMaterial( 2 );
		Assert.AreEqual( new System.Numerics.Vector3( 0, .5f, 0 ), a.EmissiveFactor ); Assert.IsNull( a.EmissiveTexture );
		Assert.AreEqual( System.Numerics.Vector3.One, b.EmissiveFactor ); Assert.AreEqual( 0, b.EmissiveTexture.Index ); Assert.IsTrue( b.EmissiveTexture.IsSrgb );
		Assert.AreEqual( new System.Numerics.Vector4( 0, 0, 0, 1 ), a.BaseColorFactor ); Assert.AreEqual( a.BaseColorFactor, b.BaseColorFactor );
		Assert.AreEqual( "MASK", label.AlphaMode ); Assert.IsTrue( label.DoubleSided );
		var sampler = doc.ReadSampler( 0 ); Assert.AreEqual( 9729, sampler.MinFilter ); Assert.AreEqual( 9729, sampler.MagFilter ); Assert.AreEqual( 10497, sampler.WrapS ); Assert.AreEqual( 10497, sampler.WrapT );
		CollectionAssert.AreEqual( reference.Image( 0 ), doc.ReadImage( 0 ).Bytes ); CollectionAssert.AreEqual( reference.Image( 1 ), doc.ReadImage( 1 ).Bytes );
		Assert.AreEqual( "b187aa937b175ec5149c05d01bd0ab4707d604d7487d4fa736eb5bef180db065", TextureLinearReference.Hash( doc.ReadImage( 0 ).Bytes ) );
		var uv = doc.MaterializePrimitiveUpload( 1, 0 ).Attributes["TEXCOORD_0"];
		Assert.AreEqual( 205, uv.Count ); Assert.AreEqual( 5126, uv.ComponentType ); Assert.AreEqual( 2, uv.Components );
		for ( int i = 0; i < uv.Count; i++ )
		{
			var expected = reference.Float( 2, i ); Assert.AreEqual( .5f, expected[0] ); Assert.AreEqual( 0f, expected[1] );
			for ( int c = 0; c < 2; c++ ) Assert.AreEqual( expected[c], BitConverter.ToSingle( uv.Storage.Bytes.Span.Slice( uv.ByteOffset + i * uv.ByteStride + c * 4, 4 ) ) );
		}
	}
	[TestMethod]
	public void IndependentTransferOrderAndPointFilterControlsAreSensitive()
	{
		double decodedFirst = (TextureLinearReference.Decode( 0 ) + TextureLinearReference.Decode( 1 )) * .5;
		double encodedFirst = TextureLinearReference.Decode( .5 );
		Assert.AreEqual( .5, decodedFirst, 1e-15 ); Assert.AreEqual( .21404114048223255, encodedFirst, 1e-15 );
		Assert.IsTrue( decodedFirst - encodedFirst > .28 && Math.Abs( decodedFirst - 1 ) > .49 && Math.Abs( decodedFirst ) > .49 );
	}
}
