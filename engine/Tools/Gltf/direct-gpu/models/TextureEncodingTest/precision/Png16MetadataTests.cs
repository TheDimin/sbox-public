using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

[TestClass]
public sealed class Png16MetadataTests
{
	public static string CheckoutRoot { get; set; }
	[TestMethod]
	public void GammaChunkIsIgnoredForRawLinearData()
	{
		using var doc = JsonDocument.Parse( File.ReadAllText( Path.Combine( CheckoutRoot, "research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision/metadata-fixture.json" ) ) );
		var png = Convert.FromBase64String( doc.RootElement.GetProperty( "png" ).GetString() );
		var expected = doc.RootElement.GetProperty( "expected" ).EnumerateArray().Select( x => x.GetUInt16() ).ToArray();
		var decoded = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = png }, false ) );
		Assert.AreEqual( 14, decoded.Format );
		for ( var i = 0; i < expected.Length; i++ ) Assert.IsTrue( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( decoded.Payload.AsSpan( i * 4 ) ) - expected[i] / 65535.0 ) < .0000001, "Embedded gAMA must not change raw glTF data values." );
	}
}
