using System;
using System.Collections.Generic;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Coordinator supplies an already available native vmdl with a known authored morph.</summary>
public static class NativeMorphFadeObservation
{
	public sealed record Sample( string Operation, float Elapsed, float NativeOverrideReadback, float AccessorValue );
	public sealed record Result( string Name, bool Passed, Sample[] Observations, string Scope, string Error );

	/// <summary>
	/// One bounded observation case, not a native interpolation-curve acceptance.
	/// SBox_GetFlexOverride may expose a target rather than the applied per-vertex
	/// weight; record that distinction before using these samples as an oracle.
	/// </summary>
	public static Result[] Run( string modelPath, string morphName )
	{
		var observations = new List<Sample>();
		Scene scene = null;
		string error = null;
		try
		{
			Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread outside graphics." );
			var model = Model.Load( modelPath );
			Require( model is { IsValid: true, IsError: false } && !model.native.IsNull, "Supply an existing native vmdl; no synthetic native model is constructed." );
			Require( Array.Exists( model.Morphs.Names, name => string.Equals( name, morphName, StringComparison.OrdinalIgnoreCase ) ), "The supplied native model must contain the selected authored morph." );
			scene = Scene.CreateEditorScene();
			using var scope = scene.Push();
			var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.Model = model; renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.Enabled = true;
			Require( renderer.SceneModel.IsValid() && renderer.GltfInstance is null, "The control must stay on the native renderer branch." );
			var now = 0f;
			foreach ( var operation in new[] { "set_default", "set_explicit_005", "set_explicit_zero", "clear_default" } )
			{
				renderer.Morphs.Clear( morphName, 0 );
				scene.EditorTick( now += .1f, .1f );
				if ( operation == "clear_default" )
				{
					renderer.Morphs.Set( morphName, 1, 0 );
					scene.EditorTick( now += .1f, .1f );
					renderer.Morphs.Clear( morphName );
				}
				else if ( operation == "set_default" ) renderer.Morphs.Set( morphName, 1 );
				else renderer.Morphs.Set( morphName, 1, operation == "set_explicit_zero" ? 0 : .05f );
				var previous = 0f;
				foreach ( var elapsed in new[] { 0f, .01f, .025f, .05f, .075f } )
				{
					var delta = elapsed - previous; previous = elapsed;
					scene.EditorTick( now += delta, delta );
					var native = renderer.SceneModel.Morphs.Get( morphName );
					Require( float.IsFinite( native ), "Native diagnostic returned a nonfinite value." );
					observations.Add( new( operation, elapsed, native, renderer.Morphs.Get( morphName ) ) );
				}
			}
		}
		catch ( Exception failure ) { error = failure.ToString(); }
		finally { try { scene?.Destroy(); } catch ( Exception cleanup ) { error += "\nCleanup: " + cleanup; } }
		return new[] { new Result( "existing_vmdl_default_and_explicit_morph_fade_observation", error is null, observations.ToArray(),
			"Native override getter observations only; actual applied deformation/curve and GPU pixels remain unproven.", error ) };
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
