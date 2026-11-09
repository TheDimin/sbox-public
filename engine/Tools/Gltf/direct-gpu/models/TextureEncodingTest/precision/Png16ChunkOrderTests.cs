using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16ChunkOrderTests
{
	public static string CheckoutRoot { get; set; }
	[TestMethod]
	public void OptionalPaletteCannotFollowTransparency()
	{
		var path = Path.Combine( CheckoutRoot, "research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision/invalid-plte-after-trns.b64" );
		var png = Convert.FromBase64String( File.ReadAllText( path ).Trim() );
		Assert.ThrowsException<GltfException>( () => global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = png }, false ) );
	}
}
