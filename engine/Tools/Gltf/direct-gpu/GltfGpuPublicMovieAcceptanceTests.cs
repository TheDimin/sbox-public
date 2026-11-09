using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Hash-pinned artifact acceptance, separate from capturing or synthesizing images.</summary>
[TestClass]
public sealed class GltfGpuPublicMovieAcceptanceTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	public void ReviewedCapturedMovieMeetsNumericAndArtifactGates()
	{
		const string captureReceiptHash = "79acddca38896af76cecc65e3b099a02f80cfacad51d99c7cc05cb7e48aa5530";
		var receiptPath = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/c5c0-public-cube-movie-1/probe-01-editor_direct_gpu_tests/receipt-01.json" );
		Assert.AreEqual( captureReceiptHash, Hash( receiptPath ) );
		using var captured = JsonDocument.Parse( File.ReadAllBytes( receiptPath ) );
		var original = captured.RootElement.GetProperty( "measurements" )[0].GetProperty( "value" )[0].GetProperty( "result" );
		var recordPath = original.GetProperty( "movie" ).GetProperty( "path" ).GetString();
		Assert.AreEqual( "12dc7c16c7bbb22a54b3483d15b1ef41167e159dcb5588ed5010457cd565276c", Hash( recordPath ) );
		var recording = JsonSerializer.Deserialize<Validation.AnimatedMorphCubeMovieProbe.Recording>( File.ReadAllBytes( recordPath ) );
		Assert.AreEqual( typeof( Model ).Assembly.ManifestModule.ModuleVersionId, recording.EngineMvid );
		Assert.AreEqual( typeof( GltfDocument ).Assembly.ManifestModule.ModuleVersionId, recording.GltfMvid );
		Assert.IsTrue( recording.PublicModelLoadVerified && recording.DefaultSourceMaterials );
		Assert.AreEqual( 252, recording.Samples.Count );
		foreach ( var sample in recording.Samples.Append( recording.Endpoint ) )
		{
			Assert.AreEqual( sample.Capture.Sha256, Hash( sample.Capture.Path ) );
			Assert.AreEqual( 192, sample.Capture.Width ); Assert.AreEqual( 192, sample.Capture.Height );
		}
		var rendererMvid = AppDomain.CurrentDomain.GetAssemblies().Single( assembly => assembly.GetName().Name == "Sandbox.SceneRenderer" ).ManifestModule.ModuleVersionId;
		Assert.AreEqual( new Guid( "c22b305b-109d-4166-a212-a7d4bc301e40" ), rendererMvid );
		// Approved for this measured recording only, with authored undefined tangents explicit.
		var limits = new Validation.AnimatedMorphCubeMovieProbe.NumericLimits( 1e-4f, 1e-5f, 1e-5f, 1e-5f, 1e-5f );
		var writerCase = Validation.AnimatedMorphCubeMovieProbe.ToWriterCase( recording, limits, rendererMvid );
		var node = JsonNode.Parse( JsonSerializer.Serialize( writerCase ) ).AsObject();
		node["provenance"]["capture_receipt_sha256"] = captureReceiptHash;
		node["provenance"]["recording_sha256"] = Hash( recordPath );
		node["provenance"]["empty_before"] = JsonNode.Parse( original.GetProperty( "empty_before" ).GetRawText() );
		node["provenance"]["empty_after"] = JsonNode.Parse( original.GetProperty( "empty_after" ).GetRawText() );
		node["provenance"]["separate_wallclock_manifest_sha256"] = "3620384aa2b8f4e8286e2a4b8975d327b0973f372cfe6e6b9fbe0ba216d4d7fa";
		node["provenance"]["capture_controls_scope"] = "Same-run fresh empty-before and deleted-empty-after, all253 positive actual captures; deterministic source movie. Separate monotonic cadence evidence does not change movie flags.";
		var wrapper = new { phase = "complete", error = (string)null, cleanup_errors = Array.Empty<string>(),
			engine_mvid = recording.EngineMvid, gltf_mvid = recording.GltfMvid, renderer_mvid = rendererMvid,
			corpus_commit = "90d7ede14c7e280af263824604b427a1ca02cb66", cases = new[] { node } };
		var output = Path.Combine( Path.GetDirectoryName( recordPath ), "accepted-writer-receipt.json" );
		using ( var stream = new FileStream( output, FileMode.CreateNew, FileAccess.Write, FileShare.Read ) )
			JsonSerializer.Serialize( stream, wrapper, new JsonSerializerOptions { WriteIndented = true } );
		Measurements.Add( new { path = output, sha256 = Hash( output ), samples = 252, endpoint_separate = true, limits,
			scope = "All253 PNG hashes and retained actual GPU buffers revalidated; bounded independent numeric gates accepted. APNG timing/pixel/container validation is the next separate operation." } );
	}

	private static string Hash( string path ) => Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( path ) ) );
}
