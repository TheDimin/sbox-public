using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using SimpleSkin = Sandbox.Gltf.DirectGpu.Validation.SimpleSkinImageProbe;

/// <summary>
/// Bounded real-model execution before the complete model matrices. Model-owned
/// oracles and probes supply expectations; the coordinator owns presentation and
/// shader execution. A successful smoke does not approve unreviewed tolerances.
/// </summary>
[TestClass]
public sealed class GltfGpuModelSmokeTests
{
	public static string CheckoutRoot { get; set; }
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkoutRoot ) { CheckoutRoot = checkoutRoot; Measurements.Clear(); }

	[TestMethod]
	public async Task SimpleSkinSourceBindAndAnimatedKeyRenderWithIndependentControls()
	{
		var output = Output( "SimpleSkin" );
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, new global::Vector3( 200, 0, 40 ), new global::Vector3( 0, 0, 40 ), 120 );
		using var compute = await AnonymousComputeLease.Create( CheckoutRoot );
		var actualFrames = new List<global::Color[]>();
		try
		{
			var rows = await SimpleSkin.MeasureSelected( CheckoutRoot, capture.Scene, compute.Material, capture.Material,
				async label =>
				{
					var frame = await capture.Take( label );
					if ( label.EndsWith( "actual_own_gpu_buffers", StringComparison.Ordinal ) ) actualFrames.Add( (global::Color[])frame.Pixels.Clone() );
					return new SimpleSkin.Frame( frame.Width, frame.Height, frame.Pixels, frame.Path, frame.Sha256,
						checked((long)frame.EngineFrame), frame.ObservedWallclockSeconds, frame.CameraStateJson );
				}, capture.WaitFrames, new[] { "glTF/source" }, new[] { "bind", "positive_rotation_key" } );
			Assert.AreEqual( 2, rows.Length ); Assert.AreEqual( 12, rows.Sum( row => row.Captures.Length ) );
			Assert.AreEqual( 2, actualFrames.Count );
			var changedPixels = actualFrames[0].Zip( actualFrames[1], (a, b) => a.r != b.r || a.g != b.g || a.b != b.b ).Count( changed => changed );
			Assert.IsTrue( changedPixels > 0, "The actual own-buffer model stayed visually static across distinct source poses." );
			foreach ( var row in rows )
			{
				Assert.IsTrue( row.GpuMeasurement.Finite && row.GpuMeasurement.SourceTopologyMatches, "Actual GPU output is nonfinite or changed source topology." );
				Assert.IsTrue( row.ActualDrawCalls > 0 && row.RequestedRevision == row.SubmittedRevision );
				Assert.IsNull( row.ObservedPlaybackTime, "Deterministic samples must not claim an observed playback clock." );
			}
			Measurements.Add( new { model = "SimpleSkin", form = "glTF/source", image_rows = rows, changed_pixels_between_actual_poses = changedPixels, compute_source_sha256 = compute.SourceHash, output,
				scope = "Two actual own-buffer poses with independent analytic image/readback expectations and fresh deletion controls. Numeric/image errors are measured; thresholds and the full matrix remain unapproved/pending. Diagnostic stock white material; no Model.Load or wall-clock proof." } );
		}
		finally
		{
			await MainThread.Wait();
			Measurements.Add( new { model = "SimpleSkin", native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}

	[TestMethod]
	public async Task FoxThreeClipsDrawFromOwnedBuffersWithIndependentInstances()
	{
		var output = Output( "Fox" );
		GltfGpuTriangleGateTests.Measurements.Clear();
		var source = Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/Fox/glTF/Fox.gltf" );
		var bytes = File.ReadAllBytes( source );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
		using var reference = Tests.FoxReference.Read( bytes, Resolve );
		using var document = GltfReader.Read( bytes, Resolve );
		var fit = Tests.FoxDiagnostics.FitSmokeCamera( reference );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, fit.Position, fit.Target, fit.OrthographicHeight );
		capture.Camera.ZNear = fit.Near; capture.Camera.ZFar = fit.Far;
		var light = new SceneDirectionalLight( capture.Scene.SceneWorld, Rotation.LookAt( fit.DirectionalLightForward ), global::Color.White * 4 );
		using var compute = await AnonymousComputeLease.Create( CheckoutRoot );
		try
		{
			Measurements.Add( new { model = "Fox", setup = "fixed independent union bounds; no geometry scale adjustment", fit.MinimumInches, fit.MaximumInches, fit.ExtentInches, fit.PoseCount,
				camera_position = fit.Position.ToString(), target = fit.Target.ToString(), fit.OrthographicHeight, fit.Near, fit.Far, second_instance_offset = fit.SecondInstanceOffset.ToString(),
				owned_directional_forward = fit.DirectionalLightForward.ToString(), owned_directional_color = light.LightColor.ToString() } );
			Measurements.Add( new { model = "Fox", source_arithmetic_diagnostics = Tests.FoxDiagnostics.AuditArithmetic( document, reference ), scope = "Original absolute gates retained; this is not tolerance approval." } );
			Assert.AreEqual( 0, (await capture.Take( "empty_before" )).Coverage );
			var captured = 0;
			var frames = new Dictionary<string, global::Color[]>( StringComparer.Ordinal );
			var rows = await Tests.FoxGpuSmokeProbe.Run( document, reference, capture.Scene.SceneWorld, compute.Material, capture.WaitFrames,
				async (label, instance) =>
				{
					var frame = await capture.Take( label );
					Assert.IsTrue( instance.CallbackErrors.Length == 0, string.Join( "\n", instance.CallbackErrors ) );
					Assert.AreEqual( instance.PoseRevision, instance.SubmittedRevision );
					Assert.IsTrue( instance.Readback().All( row => row.IndexDataOrigin == "gpu-readback" ) );
					if ( label == "clip_0_start" )
					{
						var ownedAsset = (GltfGpuAsset)typeof( GltfGpuInstance ).GetField( "asset", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( instance );
						var materials = ownedAsset.Primitives.Select( primitive => primitive.Material ).ToArray();
						try
						{
							foreach ( var primitive in ownedAsset.Primitives ) primitive.Material = capture.Material;
							var white = await capture.Take( label + "_paired_diagnostic_white" );
							Measurements.Add( new { model = "Fox", paired_material_control = label, source_coverage = frame.Coverage, diagnostic_white_coverage = white.Coverage,
								source_shader_names = materials.Select( material => material.ShaderName ).ToArray(), source_path = frame.Path, diagnostic_path = white.Path,
								identical_camera_state = frame.CameraStateJson == white.CameraStateJson, scope = "Only test-owned primitive material references changed; no cached material was edited and the GPU buffers/pose/camera/light stayed fixed." } );
							Assert.IsTrue( white.Coverage > 64, "The fitted white-material Fox control is empty." );
						}
						finally { for ( var i = 0; i < materials.Length; ++i ) ownedAsset.Primitives[i].Material = materials[i]; await capture.WaitFrames(); }
					}
					Assert.IsTrue( frame.Coverage > 64, "Fox actual stock-material capture is empty: " + label );
					frames.Add( label, (global::Color[])frame.Pixels.Clone() );
					captured++;
				}, completedRow: row => Measurements.Add( new { model = "Fox", completed_gpu_row = row } ) );
			Assert.AreEqual( 9, rows.Count ); Assert.AreEqual( 6, captured );
			var motion = Enumerable.Range( 0, 3 ).Select( clip => new { clip,
				changed_pixels = frames["clip_" + clip + "_start"].Zip( frames["clip_" + clip + "_middle"], (a, b) => a.r != b.r || a.g != b.g || a.b != b.b ).Count( changed => changed ) } ).ToArray();
			Assert.IsTrue( motion.Any( row => row.changed_pixels > 0 ), "None of the actual Fox clip samples changed the rendered image." );
			Assert.AreEqual( 0, (await capture.Take( "empty_after_instances" )).Coverage );
			Measurements.Add( new { model = "Fox", form = "glTF/source", measurement_rows = rows, captures = captured, per_clip_actual_motion = motion, compute_source_sha256 = compute.SourceHash, output,
				scope = "Three clips at two explicit source times, real GPU outputs, two independent instances and survivor/deletion controls. Default model-owned glTF material through stock adapter. Error measurements are not tolerance approval; no Model.Load, full matrix or wall-clock proof." } );
		}
		finally
		{
			await MainThread.Wait();
			if ( light.IsValid() ) light.Delete();
			Measurements.Add( new { model = "Fox", native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}

	[TestMethod]
	public async Task AnimatedMorphCubeThreeAuthoredKeysUseCurrentGpuOutputs()
	{
		var output = Output( "AnimatedMorphCube" );
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/AnimatedMorphCube-reference.json" ) ) );
		var form = oracle.RootElement.GetProperty( "forms" ).EnumerateArray().Single( value => value.GetProperty( "source" ).GetString().EndsWith( "/glTF/AnimatedMorphCube.gltf", StringComparison.Ordinal ) );
		var bounds = Validation.AnimatedMorphCubeGpuSmokeProbe.ReferenceBounds( form );
		global::Vector3 Vector( float[] value ) => new( value[0], value[1], value[2] );
		var position = Vector( bounds.SuggestedCameraPositionInches ); var target = Vector( bounds.CenterInches );
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, position, target, bounds.SuggestedOrthographicHeightInches );
		capture.Camera.ZFar = Math.Max( 1000, (position - target).Length * 3 );
		var light = new SceneDirectionalLight( capture.Scene.SceneWorld, Rotation.LookAt( target - position ), global::Color.White * 4 );
		using var compute = await AnonymousComputeLease.Create( CheckoutRoot );
		var source = Path.Combine( CheckoutRoot, "Temp/gltf-reference/glTF-Sample-Assets", form.GetProperty( "source" ).GetString() );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( Path.GetDirectoryName( source ), Uri.UnescapeDataString( uri ) ) );
		using var document = GltfReader.Read( File.ReadAllBytes( source ), Resolve );
		try
		{
			Measurements.Add( new { model = "AnimatedMorphCube", independent_bounds = bounds, camera_position = position.ToString(), target = target.ToString(),
				owned_directional_forward = (target - position).Normal.ToString(), owned_directional_color = light.LightColor.ToString() } );
			Assert.AreEqual( 0, (await capture.Take( "empty_before" )).Coverage );
			var frames = new Dictionary<string, global::Color[]>( StringComparer.Ordinal );
			var rows = await Validation.AnimatedMorphCubeGpuSmokeProbe.Run( document, form, capture.Scene.SceneWorld, compute.Material, capture.WaitFrames,
				async (label, instance) =>
				{
					var frame = await capture.Take( label );
					Assert.IsTrue( frame.Coverage > 64, "Cube GPU capture is empty: " + label );
					frames.Add( label, (global::Color[])frame.Pixels.Clone() );
				} );
			Assert.AreEqual( 3, rows.Count ); Assert.AreEqual( 3, frames.Count );
			var changed = frames["authored_start"].Zip( frames["authored_middle_key"], (a, b) => a.r != b.r || a.g != b.g || a.b != b.b ).Count( value => value );
			Assert.IsTrue( changed > 0, "Distinct authored Cube morph poses did not change the actual rendered image." );
			Assert.AreEqual( 0, (await capture.Take( "empty_after_instance" )).Coverage );
			Measurements.Add( new { model = "AnimatedMorphCube", form = "glTF/source", measurement_rows = rows, changed_pixels_start_to_middle = changed,
				compute_source_sha256 = compute.SourceHash, output, scope = "Three explicit authored-key GPU/morph/tangent measurements and actual source-material stills. Numeric/image parity,252-frame media, real wallclock and Model.Load are separate pending gates." } );
		}
		finally
		{
			await MainThread.Wait(); if ( light.IsValid() ) light.Delete();
			Measurements.Add( new { model = "AnimatedMorphCube", native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
	}

	private static string Output( string model )
	{
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
		var path = Path.Combine( CheckoutRoot, "Temp/gltf-validation/direct-gpu/model-smokes", model, Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( path ); return path;
	}

	private sealed class AnonymousComputeLease : IDisposable
	{
		private readonly Shader shader;
		internal readonly Material Material;
		internal readonly string SourceHash;
		private AnonymousComputeLease( Shader shader, Material material, string hash ) => (this.shader, Material, SourceHash) = (shader, material, hash);
		internal static async Task<AnonymousComputeLease> Create( string checkout )
		{
			var path = Path.Combine( checkout, "engine/Tools/Gltf/direct-gpu/shaders/gltf_direct_deform.shader" );
			var source = File.ReadAllBytes( path ); var hash = Convert.ToHexStringLower( SHA256.HashData( source ) );
			using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			var shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( source ), timeout.Token ); await MainThread.Wait();
			try
			{
				Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
				Assert.AreEqual( hash, Convert.ToHexStringLower( SHA256.HashData( File.ReadAllBytes( path ) ) ) );
				var material = Material.Create( "gltf_model_smoke_" + Guid.NewGuid().ToString( "N" ), shader );
				Assert.IsTrue( material is { IsValid: true, IsError: false } );
				return new AnonymousComputeLease( shader, material, hash );
			}
			catch { shader?.Destroy(); throw; }
		}
		public void Dispose() { Material.Destroy(); shader.Destroy(); }
	}
}
