using System;
using System.Buffers.Binary;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

/// <summary>Source-only numeric checks against the staged engine assembly.</summary>
[TestClass]
public sealed class TextureTransformLegacySourceTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PinnedSixTransformsAndThreeIdentityControlsUseRealRetainedAccessors( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, compiled );
		for ( var mesh = 0; mesh < 9; mesh++ )
		{
			var upload = input.Document.MaterializePrimitiveUpload( mesh, 0 );
			var before = upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray();
			var bake = GltfLegacyUvBakePlan.Create( input.Document, mesh, 0, upload );
			Assert.AreSame( upload.Attributes["TEXCOORD_0"], bake.Source );
			Assert.AreEqual( mesh >= 6, bake.IsIdentityMapping );
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				var source = TextureTransformReference.SourceUv( mesh, vertex );
				var expected = TextureTransformReference.SampleUv( mesh, source.X, source.Y );
				var bytes = bake.Bytes.Span[(vertex * 8)..];
				Assert.AreEqual( expected[0], BinaryPrimitives.ReadSingleLittleEndian( bytes ), 2e-6, $"mesh {mesh}, vertex {vertex}, U" );
				Assert.AreEqual( expected[1], BinaryPrimitives.ReadSingleLittleEndian( bytes[4..] ), 2e-6, $"mesh {mesh}, vertex {vertex}, V" );
			}
			CollectionAssert.AreEqual( before, upload.Attributes["TEXCOORD_0"].Storage.Bytes.ToArray() );
		}
	}
}
