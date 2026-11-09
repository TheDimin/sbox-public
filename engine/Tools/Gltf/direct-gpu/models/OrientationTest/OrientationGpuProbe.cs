using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Model-local execution matrix. Coordinator supplies the private scene, camera,
/// compiled compute material, real-frame waits and native captures. Targets the
/// reviewed lazy backend metadata interface; execution waits for that extraction.
/// No editor launch, shader compilation, native Model/Mesh or alternate loader.
/// </summary>
internal static class OrientationGpuProbe
{
	public sealed record Result( string Form, string Placement, int SourceCameraX, int Captures,
		OrientationGpuChecks.Result[] GpuRows, OrientationImageChecks.Result Image,
		long RequestedRevision, long SubmittedRevision, int DrawCalls, bool Passed, string Error );

	public static async Task<Result[]> RunSelected( string checkoutRoot, string oraclePath,
		string interleavedSourcePath, string interleavedOraclePath,
		Scene scene, CameraComponent camera, Material computeMaterial,
		Func<Task> waitForActualFrames, Func<string, Task<Color[]>> capture,
		string[] selectedForms = null, string[] selectedPlacements = null, int[] selectedCameraX = null )
	{
		ArgumentNullException.ThrowIfNull( scene );
		ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( computeMaterial );
		ArgumentNullException.ThrowIfNull( waitForActualFrames );
		ArgumentNullException.ThrowIfNull( capture );
		Require( ReferenceEquals( camera.Scene, scene ), "Capture camera must belong to the coordinator's private scene." );
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		using var synthetic = JsonDocument.Parse( File.ReadAllBytes( interleavedOraclePath ) );
		var forms = OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ).ToList();
		var syntheticBytes = File.ReadAllBytes( interleavedSourcePath );
		Require( Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( syntheticBytes ) ) == synthetic.RootElement.GetProperty( "source_sha256" ).GetString(), "Synthetic fixture changed after its independent oracle was authored." );
		forms.Add( new OrientationCoreTests.Form( "synthetic_interleaved/source", syntheticBytes, null, synthetic.RootElement ) );
		forms.Add( new OrientationCoreTests.Form( "synthetic_interleaved/compiled_glb", GltfCompiler.Compile( syntheticBytes ), null, synthetic.RootElement ) );
		var placements = new[]
		{
			(Name: "identity", Matrix: NMatrix.Identity),
			(Name: "source_x_reflection", Matrix: NMatrix.CreateScale( 1, -1, 1 ) * NMatrix.CreateTranslation( 37, -19, 11 )),
			(Name: "translated_shear", Matrix: new NMatrix( 1,.2f,0,0, 0,1,.15f,0, .1f,0,1,0, 37,-19,11,1 ))
		};
		var cameras = selectedCameraX ?? new[] { -1, 1 };
		Require( cameras.Length > 0 && cameras.Distinct().Count() == cameras.Length && cameras.All( value => value is -1 or 1 ), "Camera selection must contain unique source X signs." );
		if ( selectedForms is not null ) Require( selectedForms.Length > 0 && selectedForms.Distinct().Count() == selectedForms.Length && selectedForms.All( name => forms.Any( form => form.Name == name ) ), "Unknown or duplicate source form." );
		if ( selectedPlacements is not null ) Require( selectedPlacements.Length > 0 && selectedPlacements.Distinct().Count() == selectedPlacements.Length && selectedPlacements.All( name => placements.Any( placement => placement.Name == name ) ), "Unknown or duplicate placement." );
		var results = new List<Result>();
		foreach ( var form in forms.Where( value => selectedForms is null || selectedForms.Contains( value.Name ) ) )
		{
			using var document = form.Read();
			using var asset = GltfGpuAsset.Create( document, computeMaterial );
			foreach ( var placement in placements.Where( value => selectedPlacements is null || selectedPlacements.Contains( value.Name ) ) )
				foreach ( var cameraX in cameras )
				{
					var label = form.Name.Replace( '/', '_' ) + "_" + placement.Name + "_camera_source_x_" + cameraX;
					var captures = 0;
					OrientationGpuChecks.Result[] gpu = null;
					OrientationImageChecks.Result image = null;
					long requested = -1, submitted = -1;
					var calls = 0;
					string failure = null;
					GltfGpuInstance instance = null;
					SceneDirectionalLight light = null;
					try
					{
						await MainThread.Wait();
						Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run on the idle editor main thread outside rendering." );
						ConfigureCamera( camera, form.Expected, placement.Matrix, cameraX );
						light = new SceneDirectionalLight( scene.SceneWorld, camera.WorldRotation, Color.White * 2 );
						Require( OrientationImageChecks.IsEmpty( await Take( "empty_before" ) ), "Fresh empty control failed." );
						instance = asset.CreateInstance( scene.SceneWorld );
						instance.Placement = placement.Matrix;
						var actual = await Take( "actual_own_gpu_buffers" );
						requested = instance.PoseRevision; submitted = instance.SubmittedRevision; calls = instance.DrawCalls;
						gpu = OrientationGpuChecks.ValidateCurrent( instance, form.Expected, placement.Matrix );
						Require( gpu.Length == 13 && gpu.All( row => row.Passed ), "Independent GPU position/normal/index comparison failed." );
						// Borrow the asset's actual model-owned material resources for the
						// independent geometry reference, without modifying their inputs.
						var sourceSlots = asset.MaterialSlotSourceIndices.ToArray();
						var materials = asset.GetMaterials();
						Require( sourceSlots.Length == materials.Count, "Material slot metadata/resource count mismatch." );
						var materialMap = sourceSlots.Select( (source, slot) => (Source: source ?? -1, Material: materials[slot]) ).ToDictionary( pair => pair.Source, pair => pair.Material );
						Material Resolve( int? source ) => materialMap[source ?? -1];
						calls = instance.DrawCalls;
						instance.Dispose();
						await waitForActualFrames(); ProcessDeletes();
						Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_actual" ) ), "Deleted actual model remains visible." );
						Require( instance.DrawCalls == calls, "Deleted actual model continued receiving draw callbacks." );
						Color[] reference, reversed;
						using ( var literal = new OrientationReferenceScene( scene.SceneWorld, form.Expected, placement.Matrix, Resolve ) )
						{
							reference = await Take( "independent_reference" );
							Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Independent reference did not draw successfully." );
						}
						await waitForActualFrames(); ProcessDeletes();
						Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_reference" ) ), "Deleted reference remains visible." );
						using ( var literal = new OrientationReferenceScene( scene.SceneWorld, form.Expected, placement.Matrix, Resolve, reverseWinding: true ) )
						{
							reversed = await Take( "reversed_winding_negative" );
							Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Negative reference did not draw successfully." );
						}
						await waitForActualFrames(); ProcessDeletes();
						Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_negative" ) ), "Deleted negative reference remains visible." );
						image = OrientationImageChecks.Compare( actual, reference, reversed );
						Require( image.Passed, "Independent rendered geometry/front-face comparison failed." );
						Require( captures == 7, "Capture inventory changed." );
					}
					catch ( Exception error ) { failure = error.ToString(); }
					finally
					{
						await MainThread.Wait();
						try { instance?.Dispose(); }
						catch ( Exception error ) { failure = (failure ?? "") + "\nInstance cleanup: " + error; }
						try { if ( light.IsValid() ) light.Delete(); ProcessDeletes(); }
						catch ( Exception error ) { failure = (failure ?? "") + "\nScene cleanup: " + error; }
					}
					results.Add( new Result( form.Name, placement.Name, cameraX, captures, gpu, image, requested, submitted, calls, failure is null, failure ) );

					async Task<Color[]> Take( string suffix ) { captures++; return await capture( label + "_" + suffix ); }
					void ProcessDeletes() { using var scope = scene.Push(); scene.ProcessDeletes(); }
				}
		}
		var expectedCount = (selectedForms?.Length ?? 8) * (selectedPlacements?.Length ?? 3) * cameras.Length;
		Require( results.Count == expectedCount, "Model execution case inventory changed." );
		return results.ToArray();
	}

	private static void ConfigureCamera( CameraComponent camera, JsonElement expected, NMatrix placement, int sourceCameraX )
	{
		var positions = expected.GetProperty( "primitives" ).EnumerateArray().SelectMany( row => row.GetProperty( "engine_world_positions" ).EnumerateArray() )
			.Select( value => NVector.Transform( OrientationCoreTests.JsonVector( value ), placement ) ).ToArray();
		var min = positions.Aggregate( NVector.Min );
		var max = positions.Aggregate( NVector.Max );
		var midpoint = (min + max) * .5f;
		var center = new Vector3( midpoint.X, midpoint.Y, midpoint.Z );
		var direction = new Vector3( 1, sourceCameraX, .65f ).Normal;
		camera.WorldPosition = center + direction * 1200;
		camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, Vector3.Up );
		camera.Orthographic = true;
		camera.OrthographicHeight = MathF.Max( (max - min).Length() * 1.25f, 1 );
		camera.ZNear = 1;
		camera.ZFar = 5000;
		camera.BackgroundColor = Color.Black;
		camera.ClearFlags = ClearFlags.All;
		camera.CustomSize = new Vector2( 192, 192 );
		camera.EnablePostProcessing = true;
		camera.DebugMode = SceneCameraDebugMode.Normal;
	}

	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
}
