using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Gltf;
using NVector = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Original public API acceptance, authored before managed Model integration.
/// No alternate GPU helper load, native model placeholder, skipped assertion or
/// forced-green fallback is allowed. The currently installed native glTF loader
/// is expected to fail the explicit native-absence/component-backend cases.
/// Compile as Sandbox.Test.Unit against the actually loaded engine for IVT access.
/// </summary>
public static class OrientationPublicModelTests
{
	public sealed record Result( string Name, string Form, bool Passed, bool? NativeModelPresent, string Error );
	private static readonly string[] Variants = ["glTF", "glTF-Embedded", "glTF-Binary"];

	/// <summary>18 resource cases. Metadata-only rows identify observed backend.</summary>
	public static Result[] RunResources( string checkoutRoot, string oraclePath )
	{
		RequireSafe();
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var results = new List<Result>();
		foreach ( var variant in Variants )
			foreach ( var compiled in new[] { false, true } )
			{
				Run( "public_Model_Load_valid_managed_resource_without_native_placeholder", variant, compiled, fixture =>
				{
					var model = fixture.Load();
					Require( model is { IsValid: true, IsError: false }, "Model.Load must return a valid non-error Model." );
					Require( model.ResourcePath == Resource.FixPath( fixture.Path ), "Original public resource identity must use the requested canonical path." );
					Require( model.native.IsNull, "glTF Model.Load still constructed a native IModel; managed GPU backend is missing. Native placeholders are prohibited." );
					Require( model.HasRenderMeshes(), "Managed geometry must remain renderable through the established API." );
				} );
				Run( "public_Model_Load_cache_identity_invalidation_and_reload", variant, compiled, fixture =>
				{
					var first = fixture.Load();
					Require( first is { IsValid: true, IsError: false }, "First public load failed." );
					var repeated = fixture.Load();
					var canonical = fixture.Load( first.ResourcePath );
					Require( ReferenceEquals( first, repeated ) && ReferenceEquals( first, canonical ), "Repeated/canonical path loads must reuse one live Model resource." );
					first.Destroy();
					Require( !first.IsValid, "Destroy must invalidate managed resources, too." );
					var replacement = fixture.Load();
					Require( replacement is { IsValid: true, IsError: false } && !ReferenceEquals( first, replacement ), "Destroyed managed resource must not be resurrected by the path cache." );
					Require( replacement.native.IsNull, "Replacement glTF resource is still backed by a native IModel rather than the managed GPU asset." );
				} );
				Run( "public_Model_Load_bounds_and_materials_data_only", variant, compiled, fixture =>
				{
					var model = fixture.Load();
					Require( model is { IsValid: true, IsError: false }, "Public model load failed." );
					var points = fixture.Expected.GetProperty( "primitives" ).EnumerateArray()
						.SelectMany( row => row.GetProperty( "engine_world_positions" ).EnumerateArray() )
						.Select( OrientationCoreTests.JsonVector ).ToArray();
					var min = points.Aggregate( NVector.Min );
					var max = points.Aggregate( NVector.Max );
					Vector( min, model.Bounds.Mins, .001f, "Public Model.Bounds min" );
					Vector( max, model.Bounds.Maxs, .001f, "Public Model.Bounds max" );
					Require( model.Materials.Length == 7 && model.Materials.All( material => material is { IsValid: true, IsError: false } ), "All seven authored native-material slots must remain accessible on a managed Model." );
				} );
			}
		return results.ToArray();

		void Run( string name, string variant, bool compiled, Action<Fixture> test )
		{
			Fixture fixture = null;
			string error = null;
			bool? native = null;
			try
			{
				fixture = new Fixture( checkoutRoot, variant, compiled, oracle.RootElement.GetProperty( "variants" ).GetProperty( variant ) );
				test( fixture );
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			finally
			{
				if ( fixture?.Last is { } observed ) native = !observed.native.IsNull;
				try { fixture?.Dispose(); }
				catch ( Exception cleanup ) { error = (error ?? "") + "\nCleanup: " + cleanup; }
			}
			results.Add( new Result( name, variant + (compiled ? "/compiled_glb" : "/source"), error is null, native, error ) );
		}
	}

	/// <summary>
	/// Six managed-component structure/lifecycle cases plus one existing-vmdl control.
	/// Actual public-component image parity is a separate native camera capture gate.
	/// </summary>
	public static async Task<Result[]> RunRendererStructureAsync( string checkoutRoot, string oraclePath, CancellationToken cancellationToken )
	{
		RequireSafe();
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var results = new List<Result>();
		foreach ( var variant in Variants )
			foreach ( var compiled in new[] { false, true } )
			{
				Fixture fixture = null;
				Scene scene = null;
				string failure = null;
				bool? native = null;
				try
				{
					await MainThread.Wait(); RequireSafe();
					fixture = new Fixture( checkoutRoot, variant, compiled, oracle.RootElement.GetProperty( "variants" ).GetProperty( variant ) );
					var model = fixture.Load();
					Require( model is { IsValid: true, IsError: false }, "Public component requires a valid Model.Load resource." );
					native = !model.native.IsNull;
					scene = Scene.CreateEditorScene();
					ModelRenderer renderer;
					using ( scene.Push() )
					{
						var gameObject = scene.CreateObject();
						renderer = gameObject.Components.Create<ModelRenderer>( false );
						renderer.Model = model;
						renderer.Enabled = true;
						scene.EditorTick( 0, 0 );
					}
					Require( renderer.SceneObject.IsValid(), "ModelRenderer must create its normal scene presentation object." );
					var owned = scene.SceneWorld.SceneObjects.Where( value => ReferenceEquals( value.Component, renderer ) ).ToArray();
					Require( owned.Any( value => value is SceneCustomObject ), "Public ModelRenderer still creates only the native model scene object; direct GPU presentation is missing." );
					Require( model.native.IsNull && owned.All( value => !HasNativeSceneModel( value ) ), "ModelRenderer must not retain a hidden native model or native scene mesh placeholder." );
					Require( renderer.Materials.Count == 7, "Existing component material access must delegate to managed model slots." );
					var min = model.Bounds.Mins;
					var max = model.Bounds.Maxs;
					Vector( new NVector( min.x, min.y, min.z ), renderer.LocalBounds.Mins, .001f, "Managed component local minimum" );
					Vector( new NVector( max.x, max.y, max.z ), renderer.LocalBounds.Maxs, .001f, "Managed component local maximum" );
					using ( scene.Push() )
					{
						renderer.GameObject.Destroy();
						scene.EditorTick( 0, 0 );
						scene.ProcessDeletes();
					}
					await Frames( cancellationToken );
					Require( owned.All( value => !value.IsValid() ), "Destroying the component must detach/delete its own-buffer draw objects." );
					Require( model.IsValid && ReferenceEquals( model, fixture.Load() ), "Destroying one instance must preserve the shared loaded asset." );
				}
				catch ( Exception error ) { failure = error.ToString(); }
				finally
				{
					await MainThread.Wait();
					try { scene?.Destroy(); }
					catch ( Exception cleanup ) { failure = (failure ?? "") + "\nScene cleanup: " + cleanup; }
					try { fixture?.Dispose(); }
					catch ( Exception cleanup ) { failure = (failure ?? "") + "\nResource cleanup: " + cleanup; }
				}
				results.Add( new Result( "public_ModelRenderer_managed_scene_branch_lifetime_and_metadata", variant + (compiled ? "/compiled_glb" : "/source"), failure is null, native, failure ) );
			}
		await MainThread.Wait();
		Scene controlScene = null;
		string controlError = null;
		bool? controlNative = null;
		try
		{
			RequireSafe();
			var vmdl = Model.Load( "models/dev/box.vmdl" );
			controlNative = vmdl is null ? null : !vmdl.native.IsNull;
			Require( vmdl is { IsValid: true, IsError: false } && !vmdl.native.IsNull && vmdl.HasRenderMeshes(), "Existing vmdl loading must retain its usable native branch." );
			controlScene = Scene.CreateEditorScene();
			using var scope = controlScene.Push();
			var renderer = controlScene.CreateObject().Components.Create<ModelRenderer>( false );
			renderer.Model = vmdl;
			renderer.Enabled = true;
			controlScene.EditorTick( 0, 0 );
			Require( renderer.SceneObject.IsValid() && renderer.SceneObject is not SceneCustomObject, "The existing vmdl component must retain its native scene branch." );
			Require( HasNativeSceneModel( renderer.SceneObject ) && ReferenceEquals( renderer.SceneObject.Model, vmdl ), "Native vmdl scene identity must be preserved." );
		}
		catch ( Exception error ) { controlError = error.ToString(); }
		finally
		{
			try { controlScene?.Destroy(); }
			catch ( Exception cleanup ) { controlError = (controlError ?? "") + "\nScene cleanup: " + cleanup; }
		}
		results.Add( new Result( "existing_vmdl_public_load_and_ModelRenderer_native_branch", "models/dev/box.vmdl", controlError is null, controlNative, controlError ) );
		return results.ToArray();
	}

	private static async Task Frames( CancellationToken cancellationToken )
	{
		var before = Application.FrameCount;
		var timer = Stopwatch.StartNew();
		while ( Application.FrameCount < before + 3 )
		{
			Require( timer.Elapsed < TimeSpan.FromSeconds( 8 ), "The physical editor must continue receiving real frames." );
			await Task.Delay( 20, cancellationToken );
			await MainThread.Wait();
		}
	}

	private static void RequireSafe() => Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Public model tests require an idle editor main thread outside rendering." );
	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
	private static void Vector( NVector expected, Vector3 actual, float tolerance, string label ) => OrientationCoreTests.Vector( expected, new NVector( actual.x, actual.y, actual.z ), tolerance, label );
	private static bool HasNativeSceneModel( SceneObject sceneObject )
	{
		var handle = sceneObject.native.GetModelHandle();
		// GetModelHandle returns a non-null empty wrapper for custom objects.
		// Match Model.FromNative's measured strong-model validity contract.
		try { return !handle.IsNull && handle.IsStrongHandleValid(); }
		finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
	}

	private sealed class Fixture : IDisposable
	{
		private readonly string directory;
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private readonly List<Model> owned = new();
		internal string Path { get; }
		internal JsonElement Expected { get; }
		internal Model Last { get; private set; }

		internal Fixture( string checkoutRoot, string variant, bool compiled, JsonElement expected )
		{
			Expected = expected;
			directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "gltf_orientation_public_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory );
			try
			{
				aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
				var suffix = variant == "glTF-Binary" ? ".glb" : ".gltf";
				var sourcePath = System.IO.Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/OrientationTest", variant, "OrientationTest" + suffix );
				var source = File.ReadAllBytes( sourcePath );
				Require( Convert.ToHexStringLower( SHA256.HashData( source ) ) == expected.GetProperty( "source_sha256" ).GetString(), "Source sample differs from independent reference." );
				byte[] Resolve( string uri )
				{
					var parent = System.IO.Path.GetFullPath( System.IO.Path.GetDirectoryName( sourcePath ) ) + System.IO.Path.DirectorySeparatorChar;
					var dependency = System.IO.Path.GetFullPath( System.IO.Path.Combine( parent, Uri.UnescapeDataString( uri ) ) );
					Require( dependency.StartsWith( parent, StringComparison.Ordinal ), "Dependency escapes the source variant." );
					return File.ReadAllBytes( dependency );
				}
				Path = "orientation_public_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : suffix);
				File.WriteAllBytes( System.IO.Path.Combine( directory, Path ), compiled ? GltfCompiler.Compile( source, Resolve ) : source );
				if ( !compiled && variant == "glTF" ) File.WriteAllBytes( System.IO.Path.Combine( directory, "OrientationTest.bin" ), Resolve( "OrientationTest.bin" ) );
				mount = new LocalFileSystem( directory );
				aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}

		internal Model Load( string path = null )
		{
			Last = Model.Load( path ?? Path );
			if ( Last is not null && string.Equals( Last.ResourcePath, Resource.FixPath( Path ), StringComparison.OrdinalIgnoreCase ) && !owned.Any( value => ReferenceEquals( value, Last ) ) ) owned.Add( Last );
			return Last;
		}

		public void Dispose()
		{
			var failures = new List<Exception>();
			foreach ( var model in owned )
			{
				try { model.Destroy(); }
				catch ( Exception error ) { failures.Add( error ); }
			}
			owned.Clear();
			if ( mount is not null )
			{
				try { aggregate.UnMount( mount ); }
				catch ( Exception error ) { failures.Add( error ); }
				try { mount.Dispose(); }
				catch ( Exception error ) { failures.Add( error ); }
				mount = null;
			}
			try { if ( Directory.Exists( directory ) ) Directory.Delete( directory, recursive: true ); }
			catch ( Exception error ) { failures.Add( error ); }
			if ( failures.Count > 0 ) throw new AggregateException( "Public-model fixture cleanup failed.", failures );
		}
	}
}
