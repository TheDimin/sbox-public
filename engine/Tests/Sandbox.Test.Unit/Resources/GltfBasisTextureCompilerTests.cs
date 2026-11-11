using System;
using System.Buffers.Binary;
using System.Linq;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfBasisTextureCompilerTests
{
	[TestMethod]
	public void MetallicRoughnessKeepsAuthoredMipsBeforeGeneratingFloatTail()
	{
		GltfKtxLevel[] levels = [Level( 4, [0, 0, 255, 255] ), Level( 2, [0, 255, 0, 255] )];
		var resources = GltfTextureCompiler.CompileMetallicRoughnessLevels( levels, .3f, .7f, true );
		var rough = Payload( resources.NormalRoughness ); var metal = Payload( resources.Metalness );
		Assert.AreEqual( 21 * 16, rough.Length ); Assert.AreEqual( 21 * 16, metal.Length );
		Assert.AreEqual( .7f, Read( rough, 0, 2 ), .0000001f );
		Assert.AreEqual( .7f, Read( rough, 1, 2 ), .0000001f );
		Assert.AreEqual( 0f, Read( rough, 5, 2 ) );
		Assert.AreEqual( 0f, Read( metal, 0, 1 ) );
		Assert.AreEqual( 0f, Read( metal, 1, 1 ) );
		Assert.AreEqual( .3f, Read( metal, 5, 1 ), .0000001f );
	}

	[TestMethod]
	public void GeneratedMetallicRoughnessTailAvoidsByteQuantization()
	{
		var pixels = Enumerable.Range( 0, 16 ).SelectMany( i => new byte[] { 0, (byte)(i % 2), 0, 255 } ).ToArray();
		var levels = new[] { new GltfKtxLevel { Width = 4, Height = 4, Pixels = pixels } };
		var payload = Payload( GltfTextureCompiler.CompileMetallicRoughnessLevels( levels, 1, .001f, true ).NormalRoughness );
		Assert.AreEqual( .001f / 510f, Read( payload, 1, 2 ), .0000000001f );
		Assert.IsTrue( Read( payload, 1, 2 ) > 0 && Read( payload, 1, 2 ) < .001f / 255f );
	}

	[TestMethod]
	public void BaseOnlyPackingUsesAuthoredBaseAndRetainsInputMips()
	{
		GltfKtxLevel[] levels = [Level( 4, [0, 32, 64, 255] ), Level( 2, [0, 255, 0, 255] )];
		var resources = GltfTextureCompiler.CompileMetallicRoughnessLevels( levels, 1, 1, false );
		var rough = Payload( resources.NormalRoughness );
		Assert.AreEqual( 16 * 16, rough.Length ); Assert.AreEqual( 2, levels.Length );
		Assert.AreEqual( 32f / 255f, Read( rough, 0, 2 ), .0000001f );
		CollectionAssert.AreEqual( new byte[] { 0, 255, 0, 255 }, levels[1].Pixels[..4] );
	}

	[TestMethod]
	public void InvalidMipShapesFactorsAndBudgetsFailBeforePacking()
	{
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessLevels( [], 1, 1, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessLevels( [Level( 4, [0, 0, 0, 255] ), Level( 1, [0, 0, 0, 255] )], 1, 1, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessLevels( [new GltfKtxLevel { Width = 4, Height = 4, Pixels = new byte[4] }], 1, 1, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessLevels( [Level( 4, [0, 0, 0, 255] )], float.NaN, 1, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.CompileMetallicRoughnessLevels( [Level( 4, [0, 0, 0, 255] )], 1, 1, true, 16 ) );
	}

	[TestMethod]
	[DataRow( 0 )]
	[DataRow( 1 )]
	[DataRow( 2 )]
	public void KtxMimeDispatchReachesBasisValidationBeforeAnyRasterDecoder( int route )
	{
		var image = new GltfImage { MimeType = "image/ktx2", Bytes = new byte[104] };
		var error = Assert.ThrowsException<GltfException>( () =>
		{
			if ( route == 0 ) _ = GltfTextureCompiler.Compile( image, true );
			else if ( route == 1 ) _ = GltfTextureCompiler.CompileWithMipmaps( image, true );
			else _ = GltfTextureCompiler.CompileMetallicRoughness( image, 1, 1, true );
		} );
		StringAssert.Contains( error.Message, "Basis" );
	}

	private static GltfKtxLevel Level( int size, byte[] color ) => new() { Width = size, Height = size, Pixels = Enumerable.Range( 0, size * size ).SelectMany( _ => color ).ToArray() };
	private static byte[] Payload( byte[] resource ) => resource[checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource ))..];
	private static float Read( byte[] bytes, int pixel, int channel ) => BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( (pixel * 4 + channel) * sizeof( float ) ) );
}
