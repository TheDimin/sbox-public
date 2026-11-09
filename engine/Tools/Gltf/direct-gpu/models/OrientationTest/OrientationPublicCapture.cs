using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// One bounded public-front-door image/readback gate. The coordinator owns the
/// private scene, native camera capture, pixel files, frame fences and receipt.
/// Borrowed native materials isolate geometry/binding; they are not a separate
/// reference renderer for glTF material fidelity.
/// </summary>
internal static class OrientationPublicCapture
{
	public sealed record Result( string Form, string PublicResourcePath, string MaterialProofScope,
		int Captures, int OwnedCustomObjects, OrientationGpuChecks.Result[] GpuRows,
		OrientationImageChecks.Result Image, long RequestedRevision, long SubmittedRevision,
		int DrawCalls, bool Passed, string Error );

	public static async Task<Result> Run( string checkoutRoot, string oraclePath,
		Scene scene, CameraComponent camera, Func<Task> waitForActualFrames,
		Func<string, Task<Color[]>> capture )
	{
		ArgumentNullException.ThrowIfNull( scene );
		ArgumentNullException.ThrowIfNull( camera );
		ArgumentNullException.ThrowIfNull( waitForActualFrames );
		ArgumentNullException.ThrowIfNull( capture );
		Require( ReferenceEquals( camera.Scene, scene ), "Camera must belong to the coordinator's private scene." );
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var form = OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ).Single( row => row.Name == "glTF/source" );
		OrientationModelMount mount = null;
		SceneDirectionalLight light = null;
		ModelRenderer renderer = null;
		GltfGpuInstance instance = null;
		OrientationReferenceScene literal = null;
		OrientationGpuChecks.Result[] gpu = null;
		OrientationImageChecks.Result image = null;
		string resourcePath = null;
		string failure = null;
		var captures = 0;
		var ownedCustom = 0;
		var calls = 0;
		long requested = -1, submitted = -1;
		try
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run on the idle editor main thread outside rendering." );
			ConfigureSourceCamera( camera, form.Expected );
			light = new SceneDirectionalLight( scene.SceneWorld, camera.WorldRotation, Color.White * 2 );
			mount = new OrientationModelMount( form );
			var model = mount.Load(); // The real public Model.Load; no helper asset creation.
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Public Model.Load must return a valid managed resource without a native IModel." );
			resourcePath = model.ResourcePath;
			var sourceSlots = model.GltfResource.MaterialSlotSourceIndices.ToArray();
			var materials = model.Materials;
			Require( sourceSlots.Length == materials.Length && materials.Length == 7, "Public material/source-slot mapping differs from the authored seven slots." );
			var materialMap = sourceSlots.Select( (source, slot) => (Source: source ?? -1, Material: materials[slot]) ).ToDictionary( pair => pair.Source, pair => pair.Material );
			Material Resolve( int? source ) => materialMap[source ?? -1];
			Require( OrientationImageChecks.IsEmpty( await Take( "empty_before" ) ), "Fresh empty control failed." );
			literal = new OrientationReferenceScene( scene.SceneWorld, form.Expected, NMatrix.Identity, Resolve );
			var reference = await Take( "independent_reference" );
			Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Literal reference did not draw successfully." );
			literal.Dispose(); literal = null;
			await waitForActualFrames(); ProcessDeletes();
			Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_reference" ) ), "Deleted reference remains visible." );
			literal = new OrientationReferenceScene( scene.SceneWorld, form.Expected, NMatrix.Identity, Resolve, reverseWinding: true );
			var reversed = await Take( "reversed_winding_negative" );
			Require( literal.DrawCalls > 0 && literal.CallbackErrors.Length == 0, "Reversed reference did not draw successfully." );
			literal.Dispose(); literal = null;
			await waitForActualFrames(); ProcessDeletes();
			Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_negative" ) ), "Deleted reversed reference remains visible." );
			using ( scene.Push() )
			{
				renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.WorldTransform = Transform.Zero;
				renderer.Model = model;
				renderer.Enabled = true;
				scene.EditorTick( 0, 0 );
			}
			instance = renderer.GltfInstance;
			Require( instance is not null && ReferenceEquals( renderer.SceneObject, instance.SceneObject ) && renderer.SceneObject is SceneCustomObject, "Ordinary public ModelRenderer did not own the real direct-GPU custom object." );
			Require( renderer.SceneObject.Model is null && renderer.SceneObject.Transform == Transform.Zero && instance.Placement == NMatrix.Identity, "Public presentation added a native model or applied placement twice." );
			ownedCustom = scene.SceneWorld.SceneObjects.Count( row => row.IsValid() && ReferenceEquals( row.Component, renderer ) && row is SceneCustomObject );
			Require( ownedCustom == 1, "Ordinary renderer must own exactly one primary custom object in this gate." );
			var actual = await Take( "actual_public_Model_Load_ModelRenderer" );
			requested = instance.PoseRevision; submitted = instance.SubmittedRevision; calls = instance.DrawCalls;
			gpu = OrientationGpuChecks.ValidateCurrent( instance, form.Expected, NMatrix.Identity );
			Require( gpu.Length == 13 && gpu.All( row => row.Passed ), "Independent real-GPU vertex/normal/index comparison failed." );
			DestroyActual();
			await waitForActualFrames(); ProcessDeletes();
			Require( OrientationImageChecks.IsEmpty( await Take( "empty_after_actual" ) ), "Deleted public renderer remains visible." );
			Require( instance.DrawCalls == calls, "Deleted public renderer continued receiving callbacks." );
			Require( model.IsValid && ReferenceEquals( model, mount.Load() ), "Destroying an instance invalidated the public cached resource." );
			image = OrientationImageChecks.Compare( actual, reference, reversed );
			Require( image.Passed, "Independent public rendered geometry/front-face comparison failed." );
			Require( captures == 7, "Public capture inventory changed." );
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait();
			Cleanup( () => literal?.Dispose(), "Reference cleanup" );
			Cleanup( DestroyActual, "Public renderer cleanup" );
			Cleanup( () => { if ( light.IsValid() ) light.Delete(); ProcessDeletes(); }, "Light cleanup" );
			Cleanup( () => mount?.Dispose(), "Resource mount cleanup" );
		}
		return new Result( form.Name, resourcePath, "shared native material binding; independent geometry/normals/winding, not independent material fidelity",
			captures, ownedCustom, gpu, image, requested, submitted, calls, failure is null, failure );

		async Task<Color[]> Take( string suffix ) { captures++; return await capture( "OrientationTest_public_glTF_source_identity_source_camera_plus_x_" + suffix ); }
		void ProcessDeletes() { using var scope = scene.Push(); scene.ProcessDeletes(); }
		void DestroyActual()
		{
			if ( renderer is null ) return;
			using var scope = scene.Push();
			renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); renderer = null;
		}
		void Cleanup( Action action, string label )
		{
			try { action(); }
			catch ( Exception error ) { failure = (failure ?? "") + "\n" + label + ": " + error; }
		}
	}

	private static void ConfigureSourceCamera( CameraComponent camera, JsonElement expected )
	{
		var positions = expected.GetProperty( "primitives" ).EnumerateArray().SelectMany( row => row.GetProperty( "engine_world_positions" ).EnumerateArray() ).Select( OrientationCoreTests.JsonVector ).ToArray();
		var min = positions.Aggregate( NVector.Min ); var max = positions.Aggregate( NVector.Max ); var midpoint = (min + max) * .5f;
		var center = new Vector3( midpoint.X, midpoint.Y, midpoint.Z );
		// Source camera (+1,+.65,+1) maps to engine (+1,+1,+.65), no reflection.
		camera.WorldPosition = center + new Vector3( 1, 1, .65f ).Normal * 1200;
		camera.WorldRotation = Rotation.LookAt( center - camera.WorldPosition, Vector3.Up );
		camera.Orthographic = true; camera.OrthographicHeight = MathF.Max( (max - min).Length() * 1.25f, 1 );
		camera.ZNear = 1; camera.ZFar = 5000; camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
		camera.CustomSize = new Vector2( 192, 192 ); camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
	}
	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
}
