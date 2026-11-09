using System;
using System.Buffers.Binary;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using NVector2 = System.Numerics.Vector2;

/// <summary>
/// Explicit source-channel sensitivity for the later role-mapping implementation.
/// This verifies loaded reader data and material selection, not rendered sampling.
/// The earlier frozen 17-case suite is deliberately unchanged.
/// </summary>
[TestClass]
public sealed class TextureTransformCoordinateSelectionTests
{
	public static string CheckoutRoot { get; set; }

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DifferentRolesDemandDifferentActualUvDataNotOnlyDifferentSetNumbers( bool compiled )
	{
		using var input = new TextureTransformReference.Inputs( CheckoutRoot, false );
		var source = input.PerRoleFixture();
		if ( compiled ) source = GltfCompiler.Compile( source, input.Resolve, TextureTransformReference.Selected );
		using var document = GltfReader.Read( source, input.Resolve, TextureTransformReference.Selected );
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var uv0 = Read( upload.Attributes["TEXCOORD_0"], 1 );
		var uv1 = Read( upload.Attributes["TEXCOORD_1"], 1 );
		Assert.AreEqual( new NVector2( .5f, 0 ), uv0 );
		Assert.AreEqual( new NVector2( 1, 0 ), uv1 );
		Assert.IsTrue( NVector2.Distance( uv0, uv1 ) == .5f, "The two channels must have observably distinct data." );
		var material = document.ReadMaterial( 0 );
		Assert.AreEqual( 1, material.BaseColorTexture.TexCoord );
		Assert.AreEqual( 0, material.MetallicRoughnessTexture.TexCoord );
		var correctBase = material.BaseColorTexture.TransformUv( uv1 );
		var wrongAlwaysUv0 = material.BaseColorTexture.TransformUv( uv0 );
		Assert.AreEqual( new NVector2( 1.5f, 0 ), correctBase );
		Assert.IsTrue( NVector2.Distance( correctBase, wrongAlwaysUv0 ) == .5f,
			"A renderer that always selects source UV0 must be distinguishable from the selected extension mapping." );
		var correctMr = material.MetallicRoughnessTexture.TransformUv( uv0 );
		var wrongGlobalUv1 = material.MetallicRoughnessTexture.TransformUv( uv1 );
		Assert.IsTrue( MathF.Abs( correctMr.X ) < 1e-6f && MathF.Abs( correctMr.Y + .75f ) < 1e-6f );
		Assert.IsTrue( NVector2.Distance( correctMr, wrongGlobalUv1 ) > .99f,
			"A global source-channel choice cannot satisfy this material's independent role bindings." );
	}

	private static NVector2 Read( GltfUploadStream stream, int vertex )
	{
		Assert.AreEqual( 5126, stream.ComponentType ); Assert.AreEqual( 2, stream.Components ); Assert.IsFalse( stream.Normalized );
		var data = stream.Storage.Bytes.Span[checked(stream.ByteOffset + vertex * stream.ByteStride)..];
		return new NVector2( BinaryPrimitives.ReadSingleLittleEndian( data ), BinaryPrimitives.ReadSingleLittleEndian( data[4..] ) );
	}
}
