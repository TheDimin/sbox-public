using System;
using Sandbox.Engine;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.SimpleInstancingValidation;

using NMatrix = System.Numerics.Matrix4x4;
using static SimpleInstancingReference;

/// <summary>Public resource/component acceptance after loader capability promotion. No alternate asset creation or explicit SetPose.</summary>
internal static class SimpleInstancingPublicTests
{
	public const int ExpectedResourceCases = 8;
	public const int ExpectedPublicDrawForms = 4;
	public sealed record Result( string Name, string Form, bool Passed, string Scope, string Error );
	public sealed record DrawResult( string Form, string ResourcePath, string InputSha256, bool Passed,
		SimpleInstancingGpuChecks.Measurement Gpu, string ComputeShader, string[] MaterialShaders,
		long PoseRevision, long SubmittedRevision, int DrawCalls, bool ComponentDeleted, string Scope, string Error );

	/// <summary>Two cases per real upstream source/derived compiled form; no scene or GPU buffer realization.</summary>
	public static Result[] RunResources( string checkoutRoot )
	{
		RequireSafe(); var reference = new SimpleInstancingReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "public_Model_Load_selects_all125_native_free_bounds_and_default_material", form, fixture =>
			{
				var model = fixture.Load(); CheckModel( model, reference );
				Require( model.ResourcePath == Resource.FixPath( fixture.Path ), "Public canonical resource identity differs." );
				Require( ReferenceEquals( model, fixture.Load() ) && ReferenceEquals( model, fixture.Load( model.ResourcePath ) ), "Repeated and canonical public loads must share the live resource." );
				Require( !model.GltfResource.Asset.HasGpuResources, "CPU Model.Load must not realize instance GPU buffers." );
				Require( model.Materials.Length == 1 && model.Materials[0] is { IsValid: true, IsError: false }, "Absent authored material must retain the one ordinary default material slot." );
				Require( !model.GltfResource.Asset.HasGpuResources, "Reading material slots must not create geometry GPU buffers." );
			} );
			Run( "public_destroy_and_cold_canonical_reload_preserves125_instances", form, fixture =>
			{
				var first = fixture.Load(); CheckModel( first, reference ); var canonical = first.ResourcePath;
				first.Destroy(); Require( !first.IsValid, "Destroy must invalidate the managed resource owner." );
				var replacement = fixture.Load( canonical ); CheckModel( replacement, reference );
				Require( !ReferenceEquals( first, replacement ) && ReferenceEquals( replacement, fixture.Load() ), "Cold canonical reload must create a new generation then cache it." );
			} );
		}
		Require( results.Count == ExpectedResourceCases, "Public resource case inventory changed." ); return results.ToArray();
		void Run( string name, Form form, Action<Fixture> body )
		{
			Fixture fixture = null; string error = null;
			try { fixture = new Fixture( form ); body( fixture ); }
			catch ( Exception failure ) { error = failure.ToString(); }
			finally { try { fixture?.Dispose(); } catch ( Exception cleanup ) { error = (error ?? "") + "\nCleanup: " + cleanup; } }
			results.Add( new( name, form.Name, error is null, "public Model.Load resource/metadata/cache only; no draw, image or performance claim", error ) );
		}
	}

	/// <summary>
	/// One real form per call. The coordinator supplies its private capture scene and
	/// actual frames. Optional observation checkpoints can retain empty/positive/deleted
	/// native captures; this method does not accept or manufacture pixel parity.
	/// </summary>
	public static async Task<DrawResult> RunRenderer( string checkoutRoot, string formName, Scene scene,
		Func<Task> waitForActualFrames, Func<string, Task> observe = null )
	{
		ArgumentNullException.ThrowIfNull( scene ); ArgumentNullException.ThrowIfNull( waitForActualFrames );
		await MainThread.Wait(); RequireSafe();
		var reference = new SimpleInstancingReference( checkoutRoot ); var form = reference.Forms.Single( x => x.Name == formName );
		Fixture fixture = null; GameObject gameObject = null; ModelRenderer renderer = null; GltfGpuInstance instance = null; SceneObject owned = null;
		SimpleInstancingGpuChecks.Measurement measurement = null; string error = null; string path = null; string compute = null; string[] materials = Array.Empty<string>(); var deleted = false;
		long requested = -1, submitted = -1; var draws = 0;
		try
		{
			if ( observe is not null ) await observe( "empty_before_public_instancing" ); await MainThread.Wait();
			fixture = new Fixture( form ); var model = fixture.Load(); path = fixture.Path; CheckModel( model, reference );
			Require( !model.GltfResource.Asset.HasGpuResources, "Cold public resource unexpectedly already owns GPU geometry." );
			var shader = Shader.Load( GltfManagedModelResource.ComputeShaderPath ); Require( shader is { IsValid: true, IsError: false, IsAnonymous: false }, "Public renderer requires the named compiled default deformation shader." );
			using ( scene.Push() )
			{
				gameObject = scene.CreateObject(); renderer = gameObject.Components.Create<ModelRenderer>( false ); renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
			}
			instance = renderer.GltfInstance; owned = renderer.SceneObject;
			Require( instance is not null && owned is SceneCustomObject && model.native.IsNull && owned.Model is null, "Public ModelRenderer must render a managed native-model-free custom object." );
			Require( scene.SceneWorld.SceneObjects.Count( x => x.IsValid() && ReferenceEquals( x.Component, renderer ) && x is SceneCustomObject ) == 1, "The component must own one primary custom object, with instance rows inside it." );
			Require( instance.Placement == NMatrix.Identity && owned.Transform == global::Transform.Zero, "Public placement must be applied once." );
			requested = instance.PoseRevision; await waitForActualFrames(); await MainThread.Wait();
			if ( observe is not null ) await observe( "actual_public_instancing" ); await MainThread.Wait();
			submitted = instance.SubmittedRevision; draws = instance.DrawCalls;
			Require( submitted == requested && draws >= InstanceCount && instance.CallbackErrors.Length == 0, "Public default-shader submission is incomplete: " + string.Join( "\n", instance.CallbackErrors ) );
			var snapshots = instance.Readback(); measurement = SimpleInstancingGpuChecks.AssertSnapshots( reference, snapshots, instance.Placement );
			Require( snapshots.All( x => x.SourceMode == 4 && x.SubmittedPrimitiveType == Graphics.PrimitiveType.Triangles && x.DrawCalls > 0 && x.SubmittedRevision == requested ), "Every source instance must submit the current triangle draw command." );
			using ( new GlobalContext.GlobalContextScope( model.GltfResource.Context ) )
			{
				var actualCompute = model.GltfResource.Asset.ComputeMaterial;
				var namedDefault = Material.FromShader( GltfManagedModelResource.ComputeShaderPath );
				Require( ReferenceEquals( actualCompute, namedDefault ) && actualCompute.Shader is { IsValid: true, IsError: false, IsAnonymous: false }, "Realized public asset must use the cached named default shader, not a helper/anonymous replacement." );
				compute = actualCompute.ShaderName;
			}
			materials = model.Materials.Select( x => x.ShaderName ).ToArray();
			DestroyRenderer(); var finalCalls = instance.DrawCalls; await waitForActualFrames(); await MainThread.Wait();
			if ( observe is not null ) await observe( "empty_after_public_instancing" ); await MainThread.Wait();
			deleted = !owned.IsValid() && instance.DrawCalls == finalCalls; Require( deleted, "Destroyed public component retained its callback or continued submitting draws." );
			Require( model.IsValid && ReferenceEquals( model, fixture.Load() ), "Deleting the component must preserve the shared public resource generation." );
		}
		catch ( Exception failure ) { error = failure.ToString(); }
		finally
		{
			await MainThread.Wait(); Clean( DestroyRenderer, "component" ); Clean( () => fixture?.Dispose(), "fixture" );
		}
		return new( formName, path, Hash( form.Bytes ), error is null, measurement, compute, materials, requested, submitted, draws, deleted,
			"Public Model.Load + ordinary ModelRenderer + named default compute shader; all125 IDs/3000 positions+normals/4500 bound indices and per-row triangle submission. Optional native captures are diagnostics only. No independent material/pixel parity, hardware instancing or performance claim.", error );
		void DestroyRenderer() { if ( gameObject is null ) return; using var scope = scene.Push(); gameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes(); renderer = null; gameObject = null; }
		void Clean( Action action, string label ) { try { action(); } catch ( Exception cleanup ) { error = (error ?? "") + "\n" + label + " cleanup: " + cleanup; } }
	}

	private static void CheckModel( Model model, SimpleInstancingReference reference )
	{
		Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null && model.HasRenderMeshes(), "Public load must create a valid managed resource without native Model/Mesh placeholders." );
		var asset = model.GltfResource.Asset;
		Require( model.MeshCount == InstanceCount && asset.Nodes.Count == InstanceCount && asset.Nodes.Select( x => x.InstanceIndex ).SequenceEqual( Enumerable.Range( 0, InstanceCount ).Select( x => (int?)x ) ), "Public loader selected an ordinary fallback or lost authored instance identities." );
		Require( asset.Primitives.Count == 1 && asset.Nodes.All( x => ReferenceEquals( x.Primitive, asset.Primitives[0] ) ) && asset.MaterialSlotSourceIndices.Count == 1 && asset.MaterialSlotSourceIndices[0] is null, "Public instancing duplicated immutable geometry/material slots." );
		var min = Numbers( reference.Oracle.GetProperty( "world_bounds" ).GetProperty( "min" ) ); var max = Numbers( reference.Oracle.GetProperty( "world_bounds" ).GetProperty( "max" ) ); var axes = new[] { 2, 0, 1 };
		var actualMin = new double[] { model.Bounds.Mins.x, model.Bounds.Mins.y, model.Bounds.Mins.z }; var actualMax = new double[] { model.Bounds.Maxs.x, model.Bounds.Maxs.y, model.Bounds.Maxs.z };
		for ( var c = 0; c < 3; c++ ) { Close( min[axes[c]] / .0254, actualMin[c], .0005, "Public all-instance minimum" ); Close( max[axes[c]] / .0254, actualMax[c], .0005, "Public all-instance maximum" ); }
	}
	private static void RequireSafe() => Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread outside rendering." );

	/// <summary>Unique mounted temporary namespace; public calls keep the normal cache/canonical loading route.</summary>
	private sealed class Fixture : IDisposable
	{
		private readonly string directory;
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private readonly List<Model> models = new();
		internal string Path { get; }
		internal Fixture( Form form )
		{
			directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "gltf_instancing_public_" + Guid.NewGuid().ToString( "N" ) );
			try
			{
				var prefix = "instancing_public_" + Guid.NewGuid().ToString( "N" ); var relative = prefix + "/model" + (form.Compiled ? ".glb_c" : form.Variant == "glTF-Binary" ? ".glb" : ".gltf"); Path = relative;
				var folder = System.IO.Path.Combine( directory, prefix ); Directory.CreateDirectory( folder ); File.WriteAllBytes( System.IO.Path.Combine( directory, relative ), form.Bytes );
				using var d = form.Read(); foreach ( var buffer in d.Root.GetProperty( "buffers" ).EnumerateArray() )
				{
					if ( !buffer.TryGetProperty( "uri", out var value ) ) continue; var uri = value.GetString(); if ( uri.StartsWith( "data:", StringComparison.Ordinal ) ) continue;
					Require( uri == "SimpleInstancing.bin", "Unexpected fixture dependency." ); File.WriteAllBytes( System.IO.Path.Combine( folder, uri ), form.Resolve( uri ) );
				}
				aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." ); mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load( string path = null )
		{
			var model = Model.Load( path ?? Path ); if ( model is not null && model.ResourcePath == Resource.FixPath( Path ) && !models.Any( x => ReferenceEquals( x, model ) ) ) models.Add( model ); return model;
		}
		public void Dispose()
		{
			var errors = new List<Exception>(); void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
			foreach ( var model in models ) Clean( model.Destroy ); models.Clear();
			if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
			Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } ); if ( errors.Count != 0 ) throw new AggregateException( "Public instancing fixture cleanup failed.", errors );
		}
	}
}
