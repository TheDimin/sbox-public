using System.Buffers.Binary;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfCompilerTests
{
	[TestMethod]
	public void CompiledOutputIsAReadableGlbAndRetainsSourceMetadata()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"materials":[{"name":"Paint","extras":{"authorId":"a"}}],"extras":{"custom":{"keep":true}}}
			""" );
		var output = GltfCompiler.Compile( source );
		Assert.AreEqual( 0x46546C67u, BinaryPrimitives.ReadUInt32LittleEndian( output ) );
		Assert.AreEqual( 2u, BinaryPrimitives.ReadUInt32LittleEndian( output.AsSpan( 4 ) ) );
		Assert.AreEqual( (uint)output.Length, BinaryPrimitives.ReadUInt32LittleEndian( output.AsSpan( 8 ) ) );
		using var document = GltfReader.Read( output );
		Assert.IsTrue( document.Root.GetProperty( "extras" ).GetProperty( "custom" ).GetProperty( "keep" ).GetBoolean() );
		Assert.AreEqual( "a", document.Root.GetProperty( "materials" )[0].GetProperty( "extras" ).GetProperty( "authorId" ).GetString() );
	}

	[TestMethod]
	public void ExternalBuffersAreMergedWithAlignedViewsAndUnchangedAccessorValues()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"a.bin","byteLength":3},{"uri":"b.bin","byteLength":4}],"bufferViews":[{"buffer":0,"byteLength":3},{"buffer":1,"byteLength":4}],"accessors":[{"bufferView":0,"componentType":5121,"count":3,"type":"SCALAR"},{"bufferView":1,"componentType":5123,"count":2,"type":"SCALAR"}]}
			""" );
		var output = GltfCompiler.Compile( source, path => path switch
		{
			"a.bin" => [1, 2, 3],
			"b.bin" => [4, 0, 5, 0],
			_ => throw new InvalidOperationException( path )
		} );
		using var document = GltfReader.Read( output );
		Assert.AreEqual( 1, document.Root.GetProperty( "buffers" ).GetArrayLength() );
		Assert.IsFalse( document.Root.GetProperty( "buffers" )[0].TryGetProperty( "uri", out _ ) );
		Assert.AreEqual( 0, document.Root.GetProperty( "bufferViews" )[1].GetProperty( "byteOffset" ).GetInt32() % 4 );
		CollectionAssert.AreEqual( new uint[] { 1, 2, 3 }, document.ReadIndexAccessor( 0 ) );
		CollectionAssert.AreEqual( new uint[] { 4, 5 }, document.ReadIndexAccessor( 1 ) );
	}

	[TestMethod]
	public void ExternalImagesRemainIndependentAndPortable()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"images":[{"uri":"textures/paint.png"}],"textures":[{"source":0}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}]}
			""" );
		using var document = GltfReader.Read( GltfCompiler.Compile( source ) );
		Assert.AreEqual( "textures/paint.png", document.Root.GetProperty( "images" )[0].GetProperty( "uri" ).GetString() );
		Assert.AreEqual( 0, document.ReadMaterial( 0 ).BaseColorTexture!.Index );
	}

	[TestMethod]
	public void CompilationIsDeterministicAndIdempotent()
	{
		var source = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"extras\":{\"keep\":17}}" );
		var first = GltfCompiler.Compile( source );
		CollectionAssert.AreEqual( first, GltfCompiler.Compile( source ) );
		CollectionAssert.AreEqual( first, GltfCompiler.Compile( first ) );
	}

	[TestMethod]
	public void CompilationDoesNotMutateTheInput()
	{
		var source = Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"}}" );
		var expected = (byte[])source.Clone();
		_ = GltfCompiler.Compile( source );
		CollectionAssert.AreEqual( expected, source );
	}

	[TestMethod]
	public void MissingExternalBufferAndUnsupportedRequiredExtensionFail()
	{
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( Encoding.UTF8.GetBytes(
			"{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"missing.bin\",\"byteLength\":4}]}" ) ) );
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( Encoding.UTF8.GetBytes(
			"{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"EXT_missing\"],\"extensionsRequired\":[\"EXT_missing\"]}" ) ) );
	}

	[TestMethod]
	public void DefaultCompilationPreservesFiniteFloatBitsIncludingNegativeZero()
	{
		int[] patterns = [unchecked((int)0x80000000), 0x3f800001, 0x00000001, 0x7f7fffff];
		var bytes = new byte[patterns.Length * 4];
		for ( var i = 0; i < patterns.Length; i++ )
			BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( i * 4 ), patterns[i] );
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"values.bin","byteLength":16}],"bufferViews":[{"buffer":0,"byteLength":16}],"accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"SCALAR"}]}
			""" );
		using var document = GltfReader.Read( GltfCompiler.Compile( source, _ => bytes ) );
		var decoded = document.ReadFloatAccessor( 0 );
		for ( var i = 0; i < patterns.Length; i++ )
			Assert.AreEqual( patterns[i], BitConverter.SingleToInt32Bits( decoded[i] ), $"Value {i} was changed by compilation." );
	}

	[TestMethod]
	public void EmbeddedImageSurvivesBufferPacking()
	{
		const string png = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGP4////fwAJ+wP9KobjigAAAABJRU5ErkJggg==";
		var bytes = Convert.FromBase64String( png );
		var source = Encoding.UTF8.GetBytes( $$"""
			{"asset":{"version":"2.0"},"buffers":[{"uri":"image.bin","byteLength":{{bytes.Length}}}],"bufferViews":[{"buffer":0,"byteLength":{{bytes.Length}}}],"images":[{"bufferView":0,"mimeType":"image/png"}],"textures":[{"source":0}]}
			""" );
		using var document = GltfReader.Read( GltfCompiler.Compile( source, _ => bytes ) );
		CollectionAssert.AreEqual( bytes, document.ReadImage( 0 ).Bytes );
		Assert.AreEqual( "image/png", document.ReadImage( 0 ).MimeType );
	}

	[TestMethod]
	public void UnknownOptionalExtensionIsPreservedWhenNoReferencesAreRemapped()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_unknown"],"extensions":{"EXT_unknown":{"settings":{"threshold":0.123456789,"label":"keep"}}}}
			""" );
		using var document = GltfReader.Read( GltfCompiler.Compile( source ) );
		var settings = document.Root.GetProperty( "extensions" ).GetProperty( "EXT_unknown" ).GetProperty( "settings" );
		Assert.AreEqual( "keep", settings.GetProperty( "label" ).GetString() );
		Assert.AreEqual( 0.123456789, settings.GetProperty( "threshold" ).GetDouble() );
	}

	[TestMethod]
	public void UnknownExtensionBlocksBufferRemappingThatCannotBeProvenSafe()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"extensionsUsed":["EXT_unknown"],"extensions":{"EXT_unknown":{"buffer":1}},"buffers":[{"uri":"a.bin","byteLength":1},{"uri":"b.bin","byteLength":1}]}
			""" );
		Assert.ThrowsException<GltfException>( () => GltfCompiler.Compile( source, _ => [7] ) );
	}
}
