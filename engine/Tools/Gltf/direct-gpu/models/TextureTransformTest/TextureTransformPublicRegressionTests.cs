using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

[TestClass]
public sealed class TextureTransformPublicRegressionTests
{
	public static string CheckoutRoot { get; set; }
	public static List<object> Measurements { get; } = new();

	public static void VerifyTools( string expectedSha256 )
	{
		var assembly = typeof( Editor.SceneRenderingWidget ).Assembly;
		using var file = File.OpenRead( assembly.Location );
		using var pe = new PEReader( file );
		var metadata = pe.GetMetadataReader();
		var diskMvid = metadata.GetGuid( metadata.GetModuleDefinition().Mvid );
		var hash = Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( assembly.Location ) ) );
		Assert.AreEqual( assembly.ManifestModule.ModuleVersionId, diskMvid, "Loaded Tools differs from its disk image." );
		Assert.AreEqual( expectedSha256, hash, "Loaded Tools image differs from the requested stage." );
	}

	[TestMethod]
	public Task Source() => Capture( false );
	[TestMethod]
	public Task Compiled() => Capture( true );

	private static async Task Capture( bool compiled )
	{
		await MainThread.Wait();
		var output = Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/public-model-smokes/TextureTransformTest", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		using var native = new GltfGpuTriangleGateTests.NativeCapture( output );
		var result = await TextureTransformPublicRegression.Run( CheckoutRoot, native.Scene, native.Camera, native.WaitFrames, native.Take, compiled );
		Measurements.Add( new { model = "TextureTransformTest", form = compiled ? "derived compiled GLB" : "upstream glTF", output, result,
			scope = "Public Model.Load and ModelRenderer; actual GPU UV, position, normal, source-index and winding readbacks, raw override and restoration controls; screenshots require visual review and do not establish sampler fidelity." } );
		Assert.IsTrue( result.Passed, result.Error );
	}
}
