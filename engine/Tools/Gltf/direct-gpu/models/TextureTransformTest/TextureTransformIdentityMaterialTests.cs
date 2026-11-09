using System;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NVector2 = System.Numerics.Vector2;

/// <summary>
/// Establish that the independently baked image reference uses identity material
/// mapping. These fixture checks do not prove native shader or sampler behavior.
/// </summary>
[TestClass]
public sealed class TextureTransformIdentityMaterialTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void IdentityReferenceRetainsSourceTexturesFactorsAndGeometry( bool compiled )
	{
		using var source = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var original = source.Bytes.ToArray();
		using var identity = TextureTransformIdentityMaterialReference.CreateDocument( source, compiled );
		Assert.IsFalse( identity.Root.TryGetProperty( "extensionsUsed", out _ ) );
		Assert.AreEqual( 9, identity.Root.GetProperty( "materials" ).GetArrayLength() );
		Assert.AreEqual( 9, identity.Root.GetProperty( "meshes" ).GetArrayLength() );
		Assert.AreEqual( 12, identity.Root.GetProperty( "nodes" ).GetArrayLength() );
		for ( var materialIndex = 0; materialIndex < 9; materialIndex++ )
		{
			var actual = identity.ReadMaterial( materialIndex );
			var authored = source.Document.ReadMaterial( materialIndex );
			Assert.AreEqual( source.Document.Root.GetProperty( "materials" )[materialIndex].GetProperty( "name" ).GetString(),
				identity.Root.GetProperty( "materials" )[materialIndex].GetProperty( "name" ).GetString() );
			Assert.AreEqual( authored.BaseColorFactor, actual.BaseColorFactor );
			Assert.AreEqual( authored.MetallicFactor, actual.MetallicFactor );
			Assert.AreEqual( authored.RoughnessFactor, actual.RoughnessFactor );
			Assert.AreEqual( authored.DoubleSided, actual.DoubleSided );
			Assert.AreEqual( authored.AlphaMode, actual.AlphaMode );
			Assert.AreEqual( authored.AlphaCutoff, actual.AlphaCutoff );
			Assert.AreEqual( authored.BaseColorTexture.Index, actual.BaseColorTexture.Index );
			Assert.AreEqual( 0, actual.BaseColorTexture.TexCoord );
			Assert.AreEqual( NVector2.Zero, actual.BaseColorTexture.Offset );
			Assert.AreEqual( NVector2.One, actual.BaseColorTexture.Tiling );
			Assert.AreEqual( 0f, actual.BaseColorTexture.Rotation );
			Assert.IsTrue( actual.BaseColorTexture.IsSrgb );
			var authoredTexture = source.Document.ReadTexture( authored.BaseColorTexture.Index );
			var identityTexture = identity.ReadTexture( actual.BaseColorTexture.Index );
			Assert.AreEqual( authoredTexture.Image, identityTexture.Image );
			Assert.AreEqual( authoredTexture.Sampler, identityTexture.Sampler );
			CollectionAssert.AreEqual( source.Document.ReadImage( authoredTexture.Image.Value ).Bytes,
				identity.ReadImage( identityTexture.Image.Value ).Bytes );
			var expected = source.Document.ReadPrimitive( materialIndex, 0 );
			var observed = identity.ReadPrimitive( materialIndex, 0 );
			CollectionAssert.AreEqual( expected.Indices, observed.Indices );
			CollectionAssert.AreEqual( source.Document.ReadFloatAccessor( expected.Attributes["POSITION"] ),
				identity.ReadFloatAccessor( observed.Attributes["POSITION"] ) );
		}
		Assert.IsTrue( JsonNode.DeepEquals( JsonNode.Parse( source.Document.Root.GetProperty( "samplers" ).GetRawText() ),
			JsonNode.Parse( identity.Root.GetProperty( "samplers" ).GetRawText() ) ), "Reference sampler declarations must remain unchanged." );
		CollectionAssert.AreEqual( original, source.Bytes, "Constructing the reference must never rewrite the pinned original source." );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void IndependentBakedCoordinatesDistinguishMissingAndDoubleTransform( bool compiled )
	{
		using var source = new TextureTransformReference.Inputs( CheckoutRoot, false );
		using var identity = TextureTransformIdentityMaterialReference.CreateDocument( source, compiled );
		for ( var materialIndex = 0; materialIndex < 6; materialIndex++ )
		{
			var raw = new NVector2( .173f, .619f );
			var expected = TextureTransformReference.SampleUv( materialIndex, raw.X, raw.Y );
			var baked = new NVector2( (float)expected[0], (float)expected[1] );
			var reference = identity.ReadMaterial( materialIndex ).BaseColorTexture.TransformUv( baked );
			Assert.IsTrue( NVector2.Distance( reference, baked ) <= 1e-7f, "Reference material must not transform the independently baked coordinates again." );
			Assert.IsTrue( NVector2.Distance( reference, raw ) > .02f, "This landmark must reject a missing transform." );
			var doubleApplied = source.Document.ReadMaterial( materialIndex ).BaseColorTexture.TransformUv( baked );
			Assert.IsTrue( NVector2.Distance( reference, doubleApplied ) > .02f, "Borrowing the actual affine material would double-transform the reference and must be detectable." );
		}
	}
}
