using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Positive resource/component transition tests. Real GPU rendering/image parity
/// is separate; these cases check the existing public APIs and owned lifetimes.
/// </summary>
public static class OrientationRendererLifecycleTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );

	public static async Task<Result[]> RunAsync( string checkoutRoot, string oraclePath, CancellationToken cancellationToken )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var forms = OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ).Where( form => form.Name is "glTF/source" or "glTF-Binary/compiled_glb" ).ToArray();
		Require( forms.Length == 2, "Lifecycle form inventory changed." );
		var results = new List<Result>();
		foreach ( var form in forms )
		{
			await Run( "shadow_only_transitions_restore_normal_render_options", form, (scene, mount) =>
			{
				var renderer = Create( scene, mount.Load() );
				renderer.RenderOptions.Game = true;
				renderer.RenderOptions.Overlay = false;
				renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
				Require( renderer.SceneObject.Flags.ExcludeGameLayer, "ShadowsOnly must exclude the game layer." );
				renderer.RenderType = ModelRenderer.ShadowRenderType.On;
				Require( !renderer.SceneObject.Flags.ExcludeGameLayer && renderer.SceneObject.Flags.CastShadows, "Returning to On must restore the game layer and shadow flag." );
				renderer.RenderType = ModelRenderer.ShadowRenderType.ShadowsOnly;
				renderer.RenderType = ModelRenderer.ShadowRenderType.Off;
				Require( !renderer.SceneObject.Flags.ExcludeGameLayer && !renderer.SceneObject.Flags.CastShadows, "Returning to Off must restore the game layer and disable shadows." );
				renderer.RenderOptions.Game = false;
				renderer.RenderType = ModelRenderer.ShadowRenderType.On;
				Require( renderer.SceneObject.Flags.ExcludeGameLayer, "Explicit RenderOptions.Game=false must still be respected." );
				return Task.CompletedTask;
			} );
			await Run( "failed_model_replacement_preserves_previous_vmdl_presentation", form, async (scene, mount) =>
			{
				var vmdl = Model.Cube;
				Require( vmdl is { IsValid: true, IsError: false } && !vmdl.native.IsNull, "Existing native control unavailable." );
				var renderer = Create( scene, vmdl );
				var previous = renderer.SceneObject;
				renderer.SetMaterialOverride( Material.Load( "materials/default/white.vmat" ), "managed_gltf_unsupported_tag_test" );
				var failed = false;
				try { renderer.Model = mount.Load(); }
				catch ( NotSupportedException ) { failed = true; }
				Require( failed, "An unsupported managed tagged override must fail explicitly." );
				await Frames( cancellationToken );
				Require( ReferenceEquals( renderer.Model, vmdl ) && ReferenceEquals( renderer.SceneObject, previous ) && previous.IsValid(), "Failed replacement deleted or replaced the previous valid presentation/model." );
				Require( OwnedObjects( scene, renderer ).SequenceEqual( new[] { previous } ), "Failed replacement left a live replacement draw object." );
			} );
			await Run( "enable_failure_cleans_replacement_and_can_recover", form, async (scene, mount) =>
			{
				ModelRenderer renderer;
				using ( scene.Push() )
				{
					renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
					renderer.Model = mount.Load();
					renderer.SetMaterialOverride( Material.Load( "materials/default/white.vmat" ), "managed_gltf_unsupported_tag_test" );
					try { renderer.Enabled = true; scene.EditorTick( 0, 0 ); }
					catch ( NotSupportedException ) { }
				}
				await Frames( cancellationToken );
				Require( renderer.GltfInstance is null && !renderer.SceneObject.IsValid() && OwnedObjects( scene, renderer ).Length == 0, "Failed enable retained a GPU instance or custom scene object." );
				using ( scene.Push() )
				{
					renderer.Enabled = false;
					renderer.SetMaterialOverride( null, "managed_gltf_unsupported_tag_test" );
					renderer.Enabled = true;
					scene.EditorTick( 0, 0 );
				}
				Require( renderer.GltfInstance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.IsValid(), "A corrected component did not recover after failed enable." );
			} );
			await Run( "released_model_owner_retains_document_until_last_instance", form, async (scene, mount) =>
			{
				var model = mount.Load();
				var document = model.GltfResource.Asset.Document;
				var renderer = Create( scene, model );
				model.Destroy();
				Require( !model.IsValid && document.Root.GetProperty( "scene" ).GetInt32() == 0 && renderer.SceneObject.IsValid(), "Destroying the resource owner prematurely released its live instance generation." );
				var replacement = mount.Load();
				Require( replacement is { IsValid: true, IsError: false } && replacement.native.IsNull && !ReferenceEquals( model, replacement ), "Cold reload reused the invalidated owner." );
				using ( scene.Push() ) { renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); }
				await Frames( cancellationToken );
				var released = false;
				try { _ = document.Root; }
				catch ( ObjectDisposedException ) { released = true; }
				Require( released && replacement.IsValid, "Final old-instance release must dispose only its old document generation." );
			} );
			await Run( "instances_keep_independent_placement_and_survive_peer_disable", form, (scene, mount) =>
			{
				var model = mount.Load();
				var a = Create( scene, model );
				var b = Create( scene, model );
				a.WorldTransform = new Transform( new Vector3( 17, 23, 31 ), Rotation.From( 10, 20, 30 ), new Vector3( 2, .5f, -1 ) );
				b.WorldTransform = new Transform( new Vector3( -41, 53, 67 ), Rotation.From( -20, 35, 5 ), new Vector3( .75f, 1.25f, 2 ) );
				scene.EditorTick( 0, 0 );
				NMatrix expectedA = Matrix.FromTransform( a.Transform.InterpolatedWorld );
				NMatrix expectedB = Matrix.FromTransform( b.Transform.InterpolatedWorld );
				Require( a.GltfInstance.Placement == expectedA && b.GltfInstance.Placement == expectedB && expectedA != expectedB, "Component placement was shared, decomposed or applied through the wrong API." );
				Require( a.SceneObject.Transform == Transform.Zero && b.SceneObject.Transform == Transform.Zero, "Native scene transform would apply component placement twice." );
				var oldA = a.SceneObject;
				var survivor = b.SceneObject;
				a.Enabled = false;
				Require( !oldA.IsValid() && survivor.IsValid() && b.GltfInstance.Placement == expectedB && model.IsValid, "Disabling one instance invalidated another instance or the shared resource." );
				return Task.CompletedTask;
			} );
		}
		Require( results.Count == 10, "Lifecycle case count changed." );
		return results.ToArray();

		async Task Run( string name, OrientationCoreTests.Form form, Func<Scene, OrientationModelMount, Task> test )
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Lifecycle tests require the idle editor outside rendering." );
			OrientationModelMount mount = null;
			Scene scene = null;
			string failure = null;
			try { mount = new OrientationModelMount( form ); scene = Scene.CreateEditorScene(); await test( scene, mount ); }
			catch ( Exception error ) { failure = error.ToString(); }
			finally
			{
				await MainThread.Wait();
				try { scene?.Destroy(); }
				catch ( Exception error ) { failure = (failure ?? "") + "\nScene cleanup: " + error; }
				try { mount?.Dispose(); }
				catch ( Exception error ) { failure = (failure ?? "") + "\nMount cleanup: " + error; }
			}
			results.Add( new Result( name, form.Name, failure is null, failure ) );
		}
	}

	private static SceneObject[] OwnedObjects( Scene scene, ModelRenderer renderer ) => scene.SceneWorld.SceneObjects.Where( value => value.IsValid() && ReferenceEquals( value.Component, renderer ) ).ToArray();
	private static ModelRenderer Create( Scene scene, Model model )
	{
		Require( model is { IsValid: true, IsError: false }, "Model load failed." );
		using var scope = scene.Push();
		var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
		renderer.Model = model;
		renderer.Enabled = true;
		scene.EditorTick( 0, 0 );
		Require( renderer.SceneObject.IsValid(), "Component did not create a scene object." );
		return renderer;
	}
	private static async Task Frames( CancellationToken cancellationToken )
	{
		var before = Application.FrameCount;
		var timer = Stopwatch.StartNew();
		while ( Application.FrameCount < before + 3 )
		{
			Require( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Physical editor stopped receiving real frames." );
			await Task.Delay( 20, cancellationToken );
			await MainThread.Wait();
		}
	}
	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
}
