using System;
using System.Buffers.Binary;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfWebpTextureCompilerTests
{
	// Lossless 2x1 fixture generated independently with Pillow: RGBA pixels
	// (12,34,56,255) and (201,104,23,128), respectively.
	private const string Webp = "UklGRiQAAABXRUJQVlA4TBcAAAAvAQAAEA9wEaMz9B7n+4H/gYcKRPQ/AAA=";

	[TestMethod]
	public void WebpDecodesToStraightRgbaForColorAndMaterialData()
	{
		var image = new GltfImage { MimeType = "image/webp", Bytes = Convert.FromBase64String( Webp ) };
		var color = Payload( GltfTextureCompiler.Compile( image, true ) );
		CollectionAssert.AreEqual( new byte[] { 12, 34, 56, 255, 201, 104, 23, 128 }, color );
		var resources = GltfTextureCompiler.CompileMetallicRoughness( image, 1, 1, false );
		var rough = Payload( resources.NormalRoughness );
		var metal = Payload( resources.Metalness );
		Assert.AreEqual( 34f / 255f, BinaryPrimitives.ReadSingleLittleEndian( rough.AsSpan( 8 ) ), .000001f );
		Assert.AreEqual( 23f / 255f, BinaryPrimitives.ReadSingleLittleEndian( metal.AsSpan( 20 ) ), .000001f );
	}

	[TestMethod]
	public void WebpRejectsMismatchedAndTruncatedData()
	{
		var bytes = Convert.FromBase64String( Webp );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = bytes }, true ) );
		Assert.ThrowsException<GltfException>( () => GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/webp", Bytes = bytes[..20] }, true ) );
	}

	private static byte[] Payload( byte[] resource ) => resource[checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource ))..];
}
