using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Measures the native handle contract before changing the original public
/// acceptance predicate. No native Model or Mesh is constructed by this probe.
/// </summary>
public static class OrientationSceneModelHandleProbe
{
	public sealed record HandleObservation( bool IsNull, bool WrapperIsValid, bool StrongHandleValid, long? ValidBinding, bool PublicModelIsNull, bool PublicModelMatchesExpected );
	public sealed record Result( string Name, bool Passed, string Error, string SceneObjectType, HandleObservation Observation );

	public static Result[] Run( string checkoutRoot, string oraclePath )
	{
		OrientationCoreTests.Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Handle audit requires an idle editor main thread outside rendering." );
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var form = OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ).Single( candidate => candidate.Name == "glTF/source" );
		var results = new List<Result>();
		RunControl( "bare_SceneCustomObject_has_no_valid_native_model", scene => new SceneCustomObject( scene.SceneWorld ), null, expectNative: false );
		// Capture the public path load once in the current resource context. The
		// static Model.Cube convenience wrapper need not be this context's wrapper.
		var vmdl = Model.Load( "models/dev/box.vmdl" );
		OrientationCoreTests.Require( vmdl is { IsValid: true, IsError: false } && !vmdl.native.IsNull, "Existing public vmdl path control is unavailable." );
		RunControl( "existing_vmdl_ModelRenderer_has_valid_native_model", scene => Create( scene, vmdl ), vmdl, expectNative: true );
		using var mount = new OrientationModelMount( form );
		var model = mount.Load();
		OrientationCoreTests.Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Managed control must originate from the real public Model.Load route without a native IModel." );
		RunControl( "managed_ModelRenderer_has_no_valid_native_model", scene => Create( scene, model ), null, expectNative: false );
		return results.ToArray();

		void RunControl( string name, Func<Scene, SceneObject> create, Model expectedModel, bool expectNative )
		{
			Scene scene = null;
			HandleObservation observation = null;
			string failure = null;
			string type = null;
			try
			{
				scene = Scene.CreateEditorScene();
				var sceneObject = create( scene );
				OrientationCoreTests.Require( sceneObject.IsValid(), "Control did not create a valid scene object." );
				type = sceneObject.GetType().FullName;
				observation = Observe( sceneObject, expectedModel );
				OrientationCoreTests.Require( observation.StrongHandleValid == expectNative, "Measured strong-handle state differs from this control's known model ownership." );
				OrientationCoreTests.Require( observation.PublicModelIsNull == !expectNative && observation.PublicModelMatchesExpected, "Public SceneObject.Model disagrees with the expected control identity." );
				OrientationCoreTests.Require( expectNative ? observation.ValidBinding is not null and not 0 : observation.ValidBinding is null, "Binding was read for an invalid handle, or the native model lacks a valid binding." );
			}
			catch ( Exception error ) { failure = error.ToString(); }
			finally
			{
				try { scene?.Destroy(); }
				catch ( Exception cleanup ) { failure = (failure ?? "") + "\nScene cleanup: " + cleanup; }
			}
			results.Add( new Result( name, failure is null, failure, type, observation ) );
		}
	}

	private static HandleObservation Observe( SceneObject sceneObject, Model expectedModel )
	{
		var handle = sceneObject.native.GetModelHandle();
		bool isNull;
		bool wrapperIsValid;
		bool strongHandleValid;
		long? binding;
		try
		{
			isNull = handle.IsNull;
			wrapperIsValid = handle.IsValid;
			strongHandleValid = !isNull && handle.IsStrongHandleValid();
			binding = strongHandleValid ? handle.GetBindingPtr().ToInt64() : null;
		}
		finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
		var publicModel = sceneObject.Model;
		return new HandleObservation( isNull, wrapperIsValid, strongHandleValid, binding, publicModel is null, ReferenceEquals( publicModel, expectedModel ) );
	}

	private static SceneObject Create( Scene scene, Model model )
	{
		OrientationCoreTests.Require( model is { IsValid: true, IsError: false }, "Model control is not available." );
		using var scope = scene.Push();
		var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
		renderer.Model = model;
		renderer.Enabled = true;
		scene.EditorTick( 0, 0 );
		return renderer.SceneObject;
	}
}
