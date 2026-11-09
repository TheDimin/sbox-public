using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Hold the invalid public Model wrapper while checking its generation can retire.</summary>
public static class OrientationModelRetirementTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	private sealed record Generation( WeakReference<byte[]> Arena, WeakReference<GltfGpuRig> Rig );

	public static async Task<Result[]> RunAsync( string checkoutRoot, string oraclePath, CancellationToken cancellationToken )
	{
		using var oracle = JsonDocument.Parse( File.ReadAllBytes( oraclePath ) );
		var forms = OrientationCoreTests.OpenForms( checkoutRoot, oracle.RootElement ).Where( form => form.Name is "glTF/source" or "glTF-Binary/compiled_glb" ).ToArray();
		Require( forms.Length == 2, "Retirement form inventory changed." );
		var results = new List<Result>();
		foreach ( var form in forms )
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Retirement tests require the idle physical editor outside rendering." );
			OrientationModelMount mount = null;
			Scene scene = null;
			string failure = null;
			try
			{
				mount = new OrientationModelMount( form );
				var retainedInvalidModel = mount.Load();
				Require( retainedInvalidModel is { IsValid: true, IsError: false } && retainedInvalidModel.native.IsNull, "Control must use a real public managed Model.Load resource." );
				var generation = Capture( retainedInvalidModel );
				scene = Scene.CreateEditorScene();
				var first = Create( scene, retainedInvalidModel );
				var survivor = Create( scene, retainedInvalidModel );
				retainedInvalidModel.Destroy();
				Destroy( scene, first );
				await Frames( cancellationToken );
				Collect();
				Require( !retainedInvalidModel.IsValid && survivor.SceneObject.IsValid(), "Destroying the owner or sibling invalidated the retained instance." );
				Require( IsAlive( generation.Arena ) && IsAlive( generation.Rig ), "Live renderer lost its source arena or rig." );
				Require( IsDocumentReadable( retainedInvalidModel ), "Live instance no longer retains a readable source document." );
				Destroy( scene, survivor );
				await Frames( cancellationToken );
				Collect();
				Require( !IsAlive( generation.Arena ) && !IsAlive( generation.Rig ), "Retained invalid public Model or disposed component still owns retired geometry." );
				GC.KeepAlive( retainedInvalidModel );
				GC.KeepAlive( first );
				GC.KeepAlive( survivor );
			}
			catch ( Exception error ) { failure = error.ToString(); }
			finally
			{
				await MainThread.Wait();
				try { scene?.Destroy(); }
				catch ( Exception error ) { failure = (failure ?? "") + "\nScene cleanup: " + error; }
				try { mount?.Dispose(); }
				catch ( Exception error ) { failure = (failure ?? "") + "\nMount cleanup: " + error; }
			}
			results.Add( new Result( "destroyed_public_Model_does_not_retain_retired_CPU_generation", form.Name, failure is null, failure ) );
		}
		return results.ToArray();
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static Generation Capture( Model model ) => new( new( model.GltfResource.Asset.Primitives[0].Plan.InputBytes ), new( model.GltfResource.Asset.Nodes[0].Rig ) );
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static bool IsAlive<T>( WeakReference<T> reference ) where T : class => reference.TryGetTarget( out _ );
	[MethodImpl( MethodImplOptions.NoInlining )]
	private static bool IsDocumentReadable( Model model ) => model.GltfResource.Asset.Document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() == "2.0";
	private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
	private static ModelRenderer Create( Scene scene, Model model )
	{
		using var scope = scene.Push();
		var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
		renderer.Model = model; renderer.Enabled = true; scene.EditorTick( 0, 0 );
		Require( renderer.SceneObject.IsValid(), "Control did not create an instance." );
		return renderer;
	}
	private static void Destroy( Scene scene, ModelRenderer renderer )
	{
		using var scope = scene.Push();
		renderer.GameObject.Destroy(); scene.EditorTick( 0, 0 ); scene.ProcessDeletes();
	}
	private static async Task Frames( CancellationToken cancellationToken )
	{
		var before = Application.FrameCount;
		var timer = Stopwatch.StartNew();
		while ( Application.FrameCount < before + 3 )
		{
			Require( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Physical editor stopped receiving real frames." );
			await Task.Delay( 20, cancellationToken ); await MainThread.Wait();
		}
	}
	private static void Require( bool condition, string message ) => OrientationCoreTests.Require( condition, message );
}
