using System;
using System.Buffers.Binary;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Tests;

[TestClass]
public sealed class GltfSourceNormalPayloadTests
{
	// A literal RGB(0,136,255) PNG with a nonstandard gamma chunk. glTF normal
	// samples are linear data, so neither that chunk nor sRGB conversion applies.
	private const string GammaPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAABGdBTUEAAE4g3rEiDgAAAAxJREFUCNdjYOj4DwACEwGItIREUAAAAABJRU5ErkJggg==";

	[TestMethod]
	public void NormalTextureResourceRetainsLiteralLinearRgbAndOpaqueAlpha()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( """{"asset":{"version":"2.0"},"images":[{"uri":"literal.png"}],"samplers":[{"minFilter":9729}],"textures":[{"source":0,"sampler":0}],"materials":[{"normalTexture":{"index":0}}]}""" ), _ => Convert.FromBase64String( GammaPng ) );
		var prepared = GltfStockMaterialAdapter.PrepareSource( document, document.ReadMaterial( 0 ) );
		Assert.AreEqual( 1f, prepared.NormalScale );
		var resource = prepared.Resources[GltfMaterialTextureRole.Normal].Resource;
		var offset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan() ));
		CollectionAssert.AreEqual( new byte[] { 0, 136, 255, 255 }, resource.AsSpan( offset ).ToArray() );
	}
}
