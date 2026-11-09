using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>Public component GPU key readbacks; camera recipe capture is separate.</summary>
[TestClass]
public sealed class AnimatedMorphCubeFeedbackGpuTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }
	[TestMethod] public Task Source() => Capture( false );
	[TestMethod] public Task Compiled() => Capture( true );
	private static async Task Capture( bool compiled )
	{
		await MainThread.Wait();
		var result = await Validation.AnimatedMorphCubePublicGpuProbe.RunCapturedAuthored( checkout,
			Path.Combine( checkout, "Temp/gltf-validation/animated-morph-worker/reference.json" ), compiled );
		Measurements.Add( new { compiled, result } );
	}

	[DataTestMethod]
	[DataRow( "glTF", false )] [DataRow( "glTF", true )]
	[DataRow( "glTF-Binary", false )] [DataRow( "glTF-Binary", true )]
	[DataRow( "glTF-Quantized", false )] [DataRow( "glTF-Quantized", true )]
	public async Task AllSourceAndCompiledForms( string variant, bool compiled )
	{
		await MainThread.Wait();
		using var reference = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkout, "Temp/gltf-validation/animated-morph-worker/reference.json" ) ) );
		var form = reference.RootElement.GetProperty( "forms" ).EnumerateArray().Single( x => x.GetProperty( "source" ).GetString().Contains( "/" + variant + "/", StringComparison.Ordinal ) );
		var source = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets", form.GetProperty( "source" ).GetString() );
		var input = File.ReadAllBytes( source );
		Assert.AreEqual( form.GetProperty( "sha256" ).GetString(), Convert.ToHexStringLower( SHA256.HashData( input ) ) );
		var directory = Path.Combine( Path.GetTempPath(), "cube_feedback_" + Guid.NewGuid().ToString( "N" ) );
		var output = Path.Combine( checkout, "Temp/gltf-validation/animated-morph-worker", "gpu-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory ); Directory.CreateDirectory( output );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
		var isBinary = source.EndsWith( ".glb", StringComparison.OrdinalIgnoreCase );
		var filename = "cube_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : isBinary ? ".glb" : ".gltf");
		File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( input, Resolve, GltfModelLoader.SupportedExtensions ) : input );
		if ( !compiled && !isBinary ) File.WriteAllBytes( Path.Combine( directory, "AnimatedMorphCube.bin" ), Resolve( "AnimatedMorphCube.bin" ) );
		var aggregate = FileSystem.Mounted; var mount = new LocalFileSystem( directory ); Model model = null; SkinnedModelRenderer renderer = null;
		var bounds = Validation.AnimatedMorphCubeGpuSmokeProbe.ReferenceBounds( form );
		Vector3 V( float[] a ) => new( a[0], a[1], a[2] );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, V( bounds.SuggestedCameraPositionInches ), V( bounds.CenterInches ), bounds.SuggestedOrthographicHeightInches );
		try
		{
			aggregate.Mount( mount );
			var before = await capture.Take( "empty_before" ); Assert.AreEqual( 0, before.Coverage );
			model = Model.Load( filename ); Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull );
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.PlaybackRate = 0; renderer.Model = model; renderer.Enabled = true;
			}
			var frames = new List<GltfGpuTriangleGateTests.NativeCapture.Frame>();
			var rows = await Validation.AnimatedMorphCubePublicGpuProbe.RunAuthored( renderer, form, capture.WaitFrames, async ( label, _ ) =>
			{
				var frame = await capture.Take( label ); Assert.IsTrue( frame.Coverage > 64 ); frames.Add( frame );
			} );
			Assert.AreEqual( 3, rows.Count );
			Assert.AreEqual( 3, frames.Count );
			foreach ( var row in rows )
			{
				Assert.IsTrue( row.Geometry.Finite && row.Geometry.SourceTopologyMatches && row.Tangents.Finite );
				Assert.IsTrue( row.Geometry.MaximumPositionErrorInches <= 1e-4f && row.Geometry.MaximumNormalError <= 1e-5f );
				Assert.IsTrue( row.Tangents.MaximumDirectionError <= 1e-5f && row.Tangents.MaximumLengthError <= 1e-5f && row.Tangents.MaximumNormalDot <= 1e-5f && row.Tangents.MaximumHandednessError == 0 );
				Assert.AreEqual( row.PoseRevision, row.SubmittedRevision );
			}
			using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.ProcessDeletes(); } renderer = null;
			var after = await capture.Take( "empty_after" ); Assert.AreEqual( 0, after.Coverage );
			Measurements.Add( new { variant, compiled, rows, captures = frames.Select( f => new { f.Path, f.Sha256, f.Coverage } ).ToArray(), empty_before = before.Coverage, empty_after = after.Coverage, output } );
		}
		finally
		{
			if ( renderer.IsValid() ) { using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.ProcessDeletes(); }
			model?.Destroy(); aggregate.UnMount( mount ); mount.Dispose(); Directory.Delete( directory, true );
		}
	}
}
