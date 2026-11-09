using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Bounded public Model.Load and default-component checks in private editor scenes.</summary>
public static class CurrentDefaultRendererSmoke
{
	public static void VerifyStage( string engineMvid, string engineSha, string gltfMvid, string gltfSha, string toolsMvid, string toolsSha )
	{
		CheckIdentity( typeof( Model ).Assembly, engineMvid, engineSha );
		CheckIdentity( typeof( Sandbox.Gltf.GltfReader ).Assembly, gltfMvid, gltfSha );
		CheckIdentity( typeof( Editor.GltfAssetImport ).Assembly, toolsMvid, toolsSha );
	}

	private static void CheckIdentity( System.Reflection.Assembly assembly, string mvid, string hash )
	{
		if ( System.Runtime.Loader.AssemblyLoadContext.GetLoadContext( assembly ) != System.Runtime.Loader.AssemblyLoadContext.Default || assembly.ManifestModule.ModuleVersionId.ToString() != mvid )
			throw new InvalidOperationException( "Wrong loaded producer identity: " + assembly.GetName().Name );
		using var stream = File.OpenRead( assembly.Location );
		if ( Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( stream ) ) != hash )
			throw new InvalidOperationException( "Wrong on-disk producer hash: " + assembly.GetName().Name );
	}
	public sealed record Result( string Name, bool Passed, string Error );

	public static Result[] Run( string checkout, string oraclePath )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires the idle editor main thread." );
		var results = new List<Result>();
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		foreach ( var compiled in new[] { false, true } )
		{
			RunCase( "default_ModelRenderer_gltf_" + (compiled ? "compiled" : "source"), () =>
			{
				var name = compiled ? "glTF/compiled_glb" : "glTF/source";
				var form = OrientationCoreTests.OpenForms( checkout, oracle.RootElement ).First( value => value.Name == name );
				using var mount = new OrientationModelMount( form );
				var model = mount.Load();
				Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.HasRenderMeshes(), "Public load did not yield a renderable managed glTF model." );
				WithScene( scene =>
				{
					var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
					renderer.Model = model; renderer.Enabled = true;
					Require( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.IsValid(), "Default ModelRenderer did not create its managed scene object." );
					Require( ReferenceEquals( renderer.Model, model ) && renderer.GltfInstance is not null, "Managed instance or model identity was lost." );
				} );
			} );
			RunCase( "default_SkinnedModelRenderer_gltf_" + (compiled ? "compiled" : "source"), () =>
			{
				using var fixture = new SimpleSkinPublicTests.Fixture( checkout, "glTF", compiled );
				var model = fixture.Load();
				Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.HasRenderMeshes(), "Public skinned load did not yield a renderable managed glTF model." );
				WithScene( scene =>
				{
					var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
					// Leave animation graph, sequence, rate and controller settings at their defaults.
					renderer.Model = model; renderer.Enabled = true;
					Require( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.IsValid(), "Default SkinnedModelRenderer did not create its managed scene object." );
					Require( ReferenceEquals( renderer.Model, model ) && renderer.GltfInstance is not null, "Managed skinned instance or model identity was lost." );
				} );
			} );
		}
		RunCase( "existing_vmdl_default_ModelRenderer", () => NativeControl( false ) );
		RunCase( "existing_vmdl_default_SkinnedModelRenderer", () => NativeControl( true ) );
		return results.ToArray();

		void RunCase( string name, Action action )
		{
			string error = null;
			try { action(); } catch ( Exception failure ) { error = failure.ToString(); }
			results.Add( new Result( name, error is null, error ) );
		}
		void NativeControl( bool skinned )
		{
			var model = Model.Load( "models/dev/box.vmdl" );
			Require( model is { IsValid: true, IsError: false } && !model.native.IsNull && model.HasRenderMeshes(), "Native vmdl control is unavailable." );
			WithScene( scene =>
			{
				if ( skinned )
				{
					var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
					renderer.Model = model; renderer.Enabled = true;
					Require( renderer.SceneModel.IsValid() && renderer.GltfInstance is null && ReferenceEquals( renderer.SceneModel.Model, model ), "Skinned vmdl did not retain its native scene model." );
				}
				else
				{
					var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
					renderer.Model = model; renderer.Enabled = true;
					Require( renderer.SceneObject.IsValid() && renderer.SceneObject is not SceneCustomObject && ReferenceEquals( renderer.SceneObject.Model, model ), "vmdl did not retain its native scene model." );
				}
			} );
		}
	}

	private static void WithScene( Action<Scene> action )
	{
		var scene = Scene.CreateEditorScene();
		try { using var scope = scene.Push(); action( scene ); }
		finally { scene.Destroy(); }
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
