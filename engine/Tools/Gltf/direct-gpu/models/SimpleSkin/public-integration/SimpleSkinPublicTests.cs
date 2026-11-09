using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Positive public API requirements, frozen before the managed skinned adapter.
/// Compile in the coordinator's Sandbox.Test.Unit helper for internal diagnostics.
/// No producer evaluator supplies expected skeleton values. These synchronous
/// cases prove API state and lifecycle only, not GPU submission or wall time.
/// </summary>
public static class SimpleSkinPublicTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	private static readonly string[] Variants = ["glTF", "glTF-Embedded"];
	private const float InchPerMetre = 1f / .0254f;
	public sealed record NativeHandleResult( string Name, bool Passed, bool? WrapperIsNull, bool? StrongHandleValid,
		bool? PublicModelIsNull, bool? NativeResourceAbsent, string Error );

	/// <summary>Three fresh controls for the actual native wrapper/strong-handle distinction.</summary>
	public static NativeHandleResult[] RunHandleControls( string checkout )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread." );
		var results = new List<NativeHandleResult>();
		foreach ( var kind in new[] { "bare_custom", "public_skinned_gltf", "native_vmdl_skinned" } )
		{
			Scene scene = null; Fixture fixture = null; SceneObject sceneObject = null;
			bool? wrapperNull = null, strong = null, publicNull = null, resourceAbsent = null;
			string error = null;
			try
			{
				scene = Scene.CreateEditorScene();
				using var scope = scene.Push();
				if ( kind == "bare_custom" ) sceneObject = new SceneCustomObject( scene.SceneWorld );
				else if ( kind == "public_skinned_gltf" )
				{
					fixture = new Fixture( checkout, "glTF", false );
					var renderer = Add( fixture, scene );
					resourceAbsent = renderer.Model.native.IsNull;
					sceneObject = renderer.SceneObject;
					Require( sceneObject is SceneCustomObject && renderer.SceneModel is null && renderer.GltfInstance is not null, "Managed control must use its actual public skinned own-buffer instance." );
				}
				else
				{
					var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
					renderer.Model = Model.Load( "models/dev/box.vmdl" ); renderer.Enabled = true;
					resourceAbsent = renderer.Model.native.IsNull; sceneObject = renderer.SceneObject;
					Require( renderer.SceneModel.IsValid() && renderer.GltfInstance is null, "Native control must retain its real vmdl scene model." );
				}
				var handle = sceneObject.native.GetModelHandle();
				try { wrapperNull = handle.IsNull; strong = !handle.IsNull && handle.IsStrongHandleValid(); }
				finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
				publicNull = sceneObject.Model is null;
				var nativeExpected = kind == "native_vmdl_skinned";
				Require( strong == nativeExpected && publicNull == !nativeExpected, "Strong-handle/public-model observations disagree with the matched control." );
				if ( resourceAbsent.HasValue ) Require( resourceAbsent.Value == !nativeExpected, "Resource native identity disagrees with control." );
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			finally
			{
				try { if ( kind == "bare_custom" ) sceneObject?.Delete(); scene?.Destroy(); } catch ( Exception cleanup ) { error += "\nScene cleanup: " + cleanup; }
				try { fixture?.Dispose(); } catch ( Exception cleanup ) { error += "\nFixture cleanup: " + cleanup; }
			}
			results.Add( new( kind + "_native_model_handle_observation", error is null, wrapperNull, strong, publicNull, resourceAbsent, error ) );
		}
		return results.ToArray();
	}

	/// <summary>Twelve cases: three independent resource gates in four actual forms.</summary>
	public static Result[] RunResources( string checkout ) => RunForms( checkout, false,
		("authored_joint_identity_parent_and_rest_transform", (f, _) =>
		{
			var model = f.Load(); NativeFree( model );
			Require( model.BoneCount == 2, "Two authored joints must be exposed; no synthetic mesh/palette bones." );
			Require( model.GetBoneParent( 0 ) == -1 && model.GetBoneParent( 1 ) == 0, "The authored node 1 -> node 2 joint hierarchy must survive." );
			Require( model.Bones.AllBones.Count == 2 && ReferenceEquals( model.Bones.AllBones[1].Parent, model.Bones.AllBones[0] ), "BoneCollection must expose the same hierarchy." );
			var names = Enumerable.Range( 0, 2 ).Select( model.GetBoneName ).ToArray();
			Require( names.All( name => !string.IsNullOrWhiteSpace( name ) ) && names.Distinct( StringComparer.Ordinal ).Count() == 2, "Unnamed source joints need distinct stable public identities." );
			Near( Vector3.Zero, model.GetBoneTransform( 0 ).Position, "Root rest translation" );
			Near( new Vector3( 0, 0, InchPerMetre ), model.GetBoneTransform( 1 ).Position, "Child authored rest translation, not inverse-bind palette identity" );
			Near( model.GetBoneTransform( 1 ).Position, model.GetBoneTransform( names[1] ).Position, "Name/index lookup agrees" );
			Throws<ArgumentOutOfRangeException>( () => model.GetBoneName( 2 ) );
		}),
		("authored_clip_and_graph_metadata_without_native_handles", (f, _) =>
		{
			var model = f.Load(); NativeFree( model );
			Require( model.AnimationCount == 1 && model.AnimationNames.Count == 1, "The authored rotation animation must be exposed." );
			Require( model.GetAnimationName( 0 ) == "animation_0" && model.AnimationNames[0] == "animation_0", "Unnamed clip fallback must retain existing glTF naming policy." );
			Require( model.AnimGraph is null, "The sample has no authored animation graph." );
			Throws<ArgumentOutOfRangeException>( () => model.GetAnimationName( 1 ) );
		}),
		("authored_absent_morphs_have_empty_public_semantics", (f, _) =>
		{
			var model = f.Load(); NativeFree( model );
			Require( model.MorphCount == 0 && model.Morphs.Names.Length == 0, "SimpleSkin has no morph targets and must not fabricate native controllers." );
			Require( model.Morphs.GetName( 0 ) is null && model.Morphs.GetVisemeMorph( 0, 0 ) == 0, "Missing morph/viseme read semantics must stay safe." );
			Throws<ArgumentOutOfRangeException>( () => model.GetMorphName( 0 ) );
		}) );

	/// <summary>Four explicit API limitation cases, using a valid derived inherited-shear hierarchy.</summary>
	public static Result[] RunAffineTransformLimits( string checkout )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread." );
		var results = new List<Result>();
		foreach ( var variant in Variants ) foreach ( var compiled in new[] { false, true } )
		{
			string error = null;
			try
			{
				using var fixture = new Fixture( checkout, variant, compiled, inheritedShear: true );
				var model = fixture.Load(); NativeFree( model );
				Require( model.BoneCount == 2, "The derived hierarchy retains both joints." );
				var rest = model.GltfResource.Metadata.Bones[1].Rest;
				Near( MathF.Sqrt( 2 ), rest.M22, "Full affine metadata retains scaled first rotation row." );
				Near( MathF.Sqrt( .5f ), rest.M23, "Full affine metadata retains rotated first row." );
				Near( -MathF.Sqrt( 2 ), rest.M32, "Full affine metadata retains scaled second rotation row." );
				Near( MathF.Sqrt( .5f ), rest.M33, "Full affine metadata retains rotated second row." );
				Throws<NotSupportedException>( () => model.GetBoneTransform( 1 ) );
				// This is an honest public Transform expressibility limitation, not
				// full API parity. Exact GPU deformation has its own affine gate.
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			results.Add( new( "inherited_shear_retained_as_matrix_and_rejected_as_lossy_public_TRS", variant + (compiled ? "/compiled_glb" : "/source"), error is null, error ) );
		}
		return results.ToArray();
	}

	/// <summary>Forty cases plus one existing-vmdl control. No renderer helper is used to load or seek.</summary>
	public static async Task<Result[]> RunComponents( string checkout )
	{
		var results = (await RunFormsAsync( checkout,
			("public_skinned_component_uses_real_custom_object", async (f, scene) =>
			{
				var renderer = Add( f, scene );
				Require( renderer.SceneObject is SceneCustomObject && renderer.SceneModel is null && renderer.GltfInstance is not null, "SkinnedModelRenderer must dispatch to the direct GPU instance without a native SceneModel." );
				Require( renderer.SceneObject.Component == renderer && renderer.SceneObject.IsValid(), "Normal component/scene ownership must be retained." );
				var handle = renderer.SceneObject.native.GetModelHandle();
				try { Require( handle.IsNull || !handle.IsStrongHandleValid(), "The custom draw object may not contain a valid native model handle." ); }
				finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
				Require( renderer.SceneObject.Model is null, "The custom draw object's public Model must remain null." );
				Require( renderer.ShouldShowSequenceEditor && !renderer.ShouldShowMorphsEditor && renderer.Morphs.Names.Length == 0, "Existing editors must see the authored clip and absent morphs." );
			}),
			("public_sequence_seek_changes_actual_joint_transforms", async (f, scene) =>
			{
				var renderer = Add( f, scene );
				Require( renderer.Sequence.SequenceNames.SequenceEqual( new[] { "animation_0" } ), "Sequence accessor must expose authored clips." );
				Near( 5.5f, renderer.Sequence.Duration, "Authored duration" );
				foreach ( var time in new[] { 0f, .5f, 1f, 3.5f, 5.5f } )
				{
					renderer.Sequence.Time = time;
					Near( time, renderer.Sequence.Time, "Public seconds seek" );
					Require( renderer.TryGetBoneTransformLocal( renderer.Model.Bones.AllBones[1], out var child ), "Animated child joint must be available through existing API." );
					Near( new Vector3( 0, 0, InchPerMetre ), child.Position, "Joint pivot must stay at its authored translation" );
					var angle = SimpleSkinReference.Angle( time );
					Near( new Vector3( 0, (float)Math.Cos( angle ), (float)Math.Sin( angle ) ), child.Rotation * new Vector3( 0, 1, 0 ), "Authored rotation converted once to engine basis" );
				}
			}),
			("public_seek_endpoints_finite_controls_and_signed_rate", async (f, scene) =>
			{
				var renderer = Add( f, scene );
				renderer.Sequence.Looping = false;
				renderer.Sequence.TimeNormalized = .25f;
				Near( 1.375f, renderer.Sequence.Time, "Normalized seek" );
				renderer.Sequence.Time = 90;
				Near( 5.5f, renderer.Sequence.Time, "Explicit seek clamps at duration" );
				Require( renderer.Sequence.IsFinished, "Nonlooping end seek is finished." );
				renderer.Sequence.Time = -1;
				Near( 0, renderer.Sequence.Time, "Explicit seek clamps below zero" );
				Require( !renderer.Sequence.IsFinished, "Forward sequence at start is not finished." );
				renderer.PlaybackRate = -2;
				Near( -2, renderer.Sequence.PlaybackRate, "Signed playback rate" );
				Throws<ArgumentOutOfRangeException>( () => renderer.PlaybackRate = float.NaN );
				Near( -2, renderer.PlaybackRate, "Rejected rate leaves prior state" );
				Throws<ArgumentOutOfRangeException>( () => renderer.Sequence.Time = float.PositiveInfinity );
				Near( 0, renderer.Sequence.Time, "Rejected seek leaves prior state" );
			}),
			("independent_public_instances_keep_pose_clock_and_placement", async (f, scene) =>
			{
				var a = Add( f, scene ); var b = Add( f, scene );
				Require( ReferenceEquals( a.Model, b.Model ) && !ReferenceEquals( a.GltfInstance, b.GltfInstance ), "Immutable model sharing must not share instance state." );
				a.Sequence.Time = 1;
				b.Sequence.Time = 3.5f;
				b.WorldPosition = new Vector3( 7, 13, 19 );
				Near( 1, a.Sequence.Time, "A clock" ); Near( 3.5f, b.Sequence.Time, "B clock" );
				var hasA = a.TryGetBoneTransform( a.Model.Bones.AllBones[1], out var at );
				var hasB = b.TryGetBoneTransform( b.Model.Bones.AllBones[1], out var bt );
				Require( hasA && hasB, "Both instances expose world joint transforms." );
				Near( new Vector3( 0, 0, InchPerMetre ), at.Position, "A world pivot" );
				Near( new Vector3( 7, 13, 19 + InchPerMetre ), bt.Position, "B world pivot includes placement once" );
				Require( (at.Rotation * new Vector3( 0, 1, 0 ) - bt.Rotation * new Vector3( 0, 1, 0 )).Length > 1, "Distinct authored phases must produce distinct joint directions." );
			}),
			("preview_end_restores_paused_selected_pose_at_zero_delta", async (f, scene) =>
			{
				var renderer = Add( f, scene ); renderer.Sequence.Time = 1;
				QueueTick( renderer, 0, true ); renderer.FinishUpdate();
				Require( renderer.TryGetBoneTransformLocal( renderer.Model.Bones.AllBones[1], out var rest ), "Preview joint exists." );
				Near( new Vector3( 0, 1, 0 ), rest.Rotation * new Vector3( 0, 1, 0 ), "Preview shows authored rest." );
				QueueTick( renderer, 0, false ); renderer.FinishUpdate();
				Require( renderer.TryGetBoneTransformLocal( renderer.Model.Bones.AllBones[1], out var animated ), "Selected joint exists." );
				Near( new Vector3( 0, 0, 1 ), animated.Rotation * new Vector3( 0, 1, 0 ), "Ending preview restores selected key while paused." );
				Near( 1, renderer.Sequence.Time, "Preview does not alter clock." );
			}),
			("queued_elapsed_intervals_are_consumed_once_in_full", async (f, scene) =>
			{
				var renderer = Add( f, scene ); renderer.PlaybackRate = 2;
				QueueTick( renderer, .2f, false ); QueueTick( renderer, .3f, false );
				renderer.FinishUpdate(); Near( 1, renderer.Sequence.Time, "Entire summed interval times signed rate." );
				renderer.FinishUpdate(); Near( 1, renderer.Sequence.Time, "No repeated advancement without scheduled delta." );
			}),
			("absolute_seek_supersedes_old_pending_interval_only", async (f, scene) =>
			{
				var renderer = Add( f, scene ); renderer.PlaybackRate = 1;
				QueueTick( renderer, .4f, false ); renderer.Sequence.Time = 1;
				renderer.FinishUpdate(); Near( 1, renderer.Sequence.Time, "Absolute seek supersedes old queued phase." );
				QueueTick( renderer, .2f, false ); renderer.FinishUpdate();
				Near( 1.2f, renderer.Sequence.Time, "The complete new interval advances from the explicit seek." );
			}),
			("worker_animation_cannot_upload_or_flush_after_native_replacement", async (f, scene) =>
			{
				var renderer = Add( f, scene ); renderer.Sequence.Time = 1;
				var instance = renderer.GltfInstance; var revision = instance.PoseRevision;
				// Mirrors SceneAnimationSystem's parallel scheduling. Only main-thread
				// FinishUpdate may publish worker animation to GPU buffers.
				await RunAnimationWorker( renderer );
				Require( instance.PoseRevision == revision, "Worker animation must not mutate GPU instance revision or upload state." );
				renderer.Model = Model.Load( "models/dev/box.vmdl" );
				renderer.FinishUpdate();
				Require( renderer.GltfInstance is null && renderer.SceneModel.IsValid(), "A queued old glTF pose must not survive replacement by native vmdl." );
			}),
			("queued_worker_pose_is_discarded_after_disable", async (f, scene) =>
			{
				var renderer = Add( f, scene ); renderer.Sequence.Time = 1;
				await RunAnimationWorker( renderer );
				var old = renderer.SceneObject;
				renderer.Enabled = false;
				renderer.FinishUpdate();
				Require( renderer.GltfInstance is null && !old.IsValid(), "Pending animation must not recreate or upload a disabled instance." );
			}),
			("disable_reenable_and_destroy_preserve_shared_resource", async (f, scene) =>
			{
				var a = Add( f, scene ); var b = Add( f, scene );
				var old = a.SceneObject;
				a.Enabled = false;
				Require( !old.IsValid() && a.GltfInstance is null, "Disable removes own draw callback and instance." );
				Require( b.SceneObject.IsValid() && b.Model.IsValid, "Other instance survives." );
				a.Enabled = true;
				Require( a.SceneObject.IsValid() && !ReferenceEquals( old, a.SceneObject ), "Enable recreates one live presentation." );
				var replacement = a.SceneObject;
				a.GameObject.Destroy(); scene.ProcessDeletes();
				Require( !replacement.IsValid() && b.SceneObject.IsValid() && ReferenceEquals( b.Model, f.Load() ), "Destroy removes only owned instance, retaining cached model." );
			}) )).ToList();
		if ( PendingWorkerCount > 0 ) return results.ToArray();
		Scene control = null;
		try
		{
			control = Scene.CreateEditorScene(); using var scope = control.Push();
			var model = Model.Load( "models/dev/box.vmdl" );
			Require( model is { IsValid: true, IsError: false } && !model.native.IsNull, "vmdl remains native." );
			var renderer = control.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.Model = model; renderer.Enabled = true;
			Require( renderer.SceneModel.IsValid() && renderer.GltfInstance is null && ReferenceEquals( renderer.SceneModel.Model, model ), "Existing skinned vmdl component keeps its SceneModel." );
			results.Add( new( "existing_vmdl_skinned_native_control", "models/dev/box.vmdl", true, null ) );
		}
		catch ( Exception error ) { results.Add( new( "existing_vmdl_skinned_native_control", "models/dev/box.vmdl", false, error.ToString() ) ); }
		finally { control?.Destroy(); }
		return results.ToArray();
	}

	private static Result[] RunForms( string checkout, bool createScene, params (string Name, Action<Fixture, Scene> Body)[] cases )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires idle editor main thread outside graphics." );
		var results = new List<Result>();
		foreach ( var variant in Variants ) foreach ( var compiled in new[] { false, true } ) foreach ( var test in cases )
		{
			string error = null; Fixture fixture = null; Scene scene = null;
			try
			{
				fixture = new Fixture( checkout, variant, compiled );
				if ( createScene ) { scene = Scene.CreateEditorScene(); using var scope = scene.Push(); test.Body( fixture, scene ); }
				else test.Body( fixture, null );
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			finally
			{
				try { scene?.Destroy(); } catch ( Exception failure ) { error += "\nScene cleanup: " + failure; }
				try { fixture?.Dispose(); } catch ( Exception failure ) { error += "\nFixture cleanup: " + failure; }
			}
			results.Add( new( test.Name, variant + (compiled ? "/compiled_glb" : "/source"), error is null, error ) );
		}
		return results.ToArray();
	}

	private sealed class WorkerWaitTimeout( Task worker ) : Exception( "Animation worker exceeded its two-second join bound; its private scene and fixture remain owned. Recover CompletePendingWorkerCleanupAsync on ORIGINAL helper MVID=" + typeof( SimpleSkinPublicTests ).Assembly.ManifestModule.ModuleVersionId )
	{
		internal Task Worker { get; } = worker;
	}
	private sealed record PendingWorker( Task Worker, Scene Scene, Fixture Fixture );
	private static readonly List<PendingWorker> pendingWorkers = new();
	public static int PendingWorkerCount => pendingWorkers.Count;
	public static Guid HelperMvid => typeof( SimpleSkinPublicTests ).Assembly.ManifestModule.ModuleVersionId;

	private static void QueueTick( SkinnedModelRenderer renderer, float delta, bool preview )
	{
		// Time.Scope is process-global: use it only synchronously on the main
		// thread, restore it before yielding. Actual worker behavior has separate cases.
		using var time = Time.Scope( 0, delta );
		renderer.AnimationUpdate( preview );
	}

	private static async Task RunAnimationWorker( SkinnedModelRenderer renderer )
	{
		var worker = Task.Run( () => renderer.AnimationUpdate( previewBindPose: true ) );
		await Task.WhenAny( worker, Task.Delay( 2000 ) );
		await MainThread.Wait();
		if ( !worker.IsCompleted ) throw new WorkerWaitTimeout( worker );
		await worker;
		await MainThread.Wait();
	}

	/// <summary>Coordinator recovery entry: bounded, main-thread-yielding, never destroys a worker's live fixture.</summary>
	public static async Task<int> CompletePendingWorkerCleanupAsync()
	{
		await MainThread.Wait();
		foreach ( var pending in pendingWorkers.ToArray() )
		{
			await Task.WhenAny( pending.Worker, Task.Delay( 2000 ) );
			await MainThread.Wait();
			if ( !pending.Worker.IsCompleted ) continue;
			try { await pending.Worker; } catch { /* The original case already failed on its join timeout. */ }
			await MainThread.Wait();
			pending.Scene.Destroy();
			pending.Fixture.Dispose();
			pendingWorkers.Remove( pending );
		}
		return pendingWorkers.Count;
	}

	private static async Task<Result[]> RunFormsAsync( string checkout, params (string Name, Func<Fixture, Scene, Task> Body)[] cases )
	{
		var results = new List<Result>();
		foreach ( var variant in Variants ) foreach ( var compiled in new[] { false, true } ) foreach ( var test in cases )
		{
			await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires idle editor main thread outside graphics." );
			string error = null; Fixture fixture = null; Scene scene = null;
			try
			{
				fixture = new Fixture( checkout, variant, compiled );
				scene = Scene.CreateEditorScene();
				// Add owns its short synchronous Scene.Push scope. No ambient scene
				// context is held while awaiting a worker or real editor frames.
				await test.Body( fixture, scene );
			}
			catch ( WorkerWaitTimeout timeout )
			{
				error = timeout.ToString();
				pendingWorkers.Add( new( timeout.Worker, scene, fixture ) );
				scene = null; fixture = null;
			}
			catch ( Exception failure ) { error = failure.ToString(); }
			finally
			{
				await MainThread.Wait();
				try { scene?.Destroy(); } catch ( Exception failure ) { error += "\nScene cleanup: " + failure; }
				try { fixture?.Dispose(); } catch ( Exception failure ) { error += "\nFixture cleanup: " + failure; }
			}
			results.Add( new( test.Name, variant + (compiled ? "/compiled_glb" : "/source"), error is null, error ) );
			if ( pendingWorkers.Count > 0 ) return results.ToArray(); // Remaining declared cases stay incomplete.
		}
		return results.ToArray();
	}

	private static SkinnedModelRenderer Add( Fixture fixture, Scene scene )
	{
		using var scope = scene.Push();
		var model = fixture.Load(); NativeFree( model );
		var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		renderer.UseAnimGraph = false; renderer.Model = model;
		renderer.Sequence.Name = "animation_0"; renderer.PlaybackRate = 0;
		renderer.Enabled = true;
		return renderer;
	}
	private static void NativeFree( Model model ) => Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Public Model.Load must provide a valid managed resource with no native model placeholder." );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	private static void Near( float expected, float actual, string message ) => Require( float.IsFinite( actual ) && MathF.Abs( expected - actual ) <= .0002f, message + $": expected {expected}, got {actual}" );
	private static void Near( Vector3 expected, Vector3 actual, string message ) { Near( expected.x, actual.x, message + " x" ); Near( expected.y, actual.y, message + " y" ); Near( expected.z, actual.z, message + " z" ); }
	private static void Throws<T>( Action action ) where T : Exception { try { action(); } catch ( T ) { return; } throw new InvalidOperationException( "Expected " + typeof( T ).Name ); }

	internal sealed class Fixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "simple_skin_public_" + Guid.NewGuid().ToString( "N" ) );
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount;
		private Model model;
		private readonly string path;
		internal string ResourcePath => path;
		internal Fixture( string checkout, string variant, bool compiled, bool inheritedShear = false )
		{
			try
			{
				var parent = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/SimpleSkin", variant );
				var bytes = File.ReadAllBytes( Path.Combine( parent, "SimpleSkin.gltf" ) );
				var expected = variant == "glTF" ? "8d92e9888340eb98e82a65a1f8b037d9a1d9f09e5fd0b6ba6d40fd93700b3239" : "7d0c3f48d0510d101269cb4fdd3ee035eaada0e04809a0899e0b4cfe8c38e68f";
				Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == expected, "Pinned source changed." );
				// Existing independent reference checks all four dependency hashes too.
				_ = SimpleSkinReference.OpenForms( checkout );
				if ( inheritedShear )
				{
					var root = JsonNode.Parse( bytes );
					root["nodes"][1]["scale"] = new JsonArray( 2f, 1f, 1f );
					root["nodes"][2]["rotation"] = new JsonArray( 0f, 0f, .3826834323650898f, .9238795325112867f );
					bytes = System.Text.Encoding.UTF8.GetBytes( root.ToJsonString() );
				}
				byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( parent, uri ) );
				Directory.CreateDirectory( directory );
				path = "simple_skin_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, path ), compiled ? GltfCompiler.Compile( bytes, Resolve ) : bytes );
				if ( !compiled && variant == "glTF" ) foreach ( var dependency in Directory.GetFiles( parent, "*.bin" ) ) File.Copy( dependency, Path.Combine( directory, Path.GetFileName( dependency ) ) );
				aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
				mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load() => model ??= Model.Load( path );
		public void Dispose()
		{
			var errors = new List<Exception>();
			void Clean( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
			if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) Clean( model.Destroy );
			if ( mount is not null ) { Clean( () => aggregate.UnMount( mount ) ); Clean( mount.Dispose ); mount = null; }
			Clean( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
			if ( errors.Count > 0 ) throw new AggregateException( errors );
		}
	}
}
