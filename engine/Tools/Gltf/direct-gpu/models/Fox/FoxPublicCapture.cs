using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Tests;

using NativeFrame = GltfGpuTriangleGateTests.NativeCapture.Frame;

/// <summary>Small public load/sequence capture using the existing native capture and Fox oracle.</summary>
internal static class FoxPublicCapture
{
	public sealed record FrameRow( string Label, string Path, string Sha256, int Coverage, ulong EngineFrame, double WallSeconds, string CameraState );
	public sealed record PoseRow( string Label, string Clip, float RequestedTime, float ObservedTime,
		long PoseRevision, long SubmittedRevision, int DrawCalls, string VertexSha256, string IndexSha256, object IndependentMeasurement );
	public sealed record HandleRow( string Instance, bool WrapperIsNull, bool StrongHandleValid, bool PublicSceneModelIsNull );
	public sealed record MotionRow( string Clip, int ChangedPixels, bool GpuBytesChanged );
	public sealed record MetadataRow( int AuthoredNodeCount, int SkinJointCount, int ExpectedPublicBoneCount, int ObservedPublicBoneCount,
		int[] ExpectedSourceNodes, int[] ObservedSourceNodes, string[] ExpectedBoneNames, string[] ObservedBoneNames,
		string[] ExpectedClips, string[] ObservedClips, string Policy );
	public sealed record Result( bool Passed, string Form, string ResourcePath, string InputSha256, string ComputeShader,
		FoxDiagnostics.CameraFit CameraFit, string DirectionalLightColor, MetadataRow Metadata, PoseRow[] Poses, FrameRow[] Captures, HandleRow[] NativeHandles, MotionRow[] Motion,
		bool SecondInstanceUnchanged, string[] SourceShaderNames, string Scope, string Error );

	/// <summary>
	/// Default is one pinned glTF source, three clips, two instances, six solo
	/// clip captures and fresh empty/deletion controls. Never creates a helper GPU
	/// asset or calls SetPose: all actual load and seek commands are public APIs.
	/// </summary>
	public static async Task<Result> RunCaptured( string checkout, bool compiled = false )
	{
		await MainThread.Wait();
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires the coordinator's idle physical editor." );
		var source = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/Fox/glTF" );
		var bytes = File.ReadAllBytes( Path.Combine( source, "Fox.gltf" ) );
		Require( Hash( bytes ) == "6ddcabf511c0257b87dedf6ac51f1bdb6f21e570eee5fa7c4fa6162d055cb002", "Pinned Fox source changed." );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( source, Uri.UnescapeDataString( uri ) ) );
		Require( Hash( Resolve( "Fox.bin" ) ) == "c7d0d8de28a84d5b25623037f88e063e1502495a2ee6c55f182c61161ad12f80", "Pinned Fox binary changed." );
		Require( Hash( Resolve( "Texture.png" ) ) == "61c8b109ee7f8bf262791933380fafb1465f7b51cbe6472c2d21eff0b31f83a1", "Pinned Fox texture changed." );
		using var reference = FoxReference.Read( bytes, Resolve );
		using var sourceJson = JsonDocument.Parse( bytes );
		// Public bones preserve authored joint ancestors as well as skin joints.
		// Pinned Fox has joint nodes2..25 beneath node0/root; its separate mesh
		// node1/fox is not a bone. This differs from the24-entry GPU skin palette.
		var expectedBoneNodes = new[] { 0 }.Concat( Enumerable.Range( 2, 24 ) ).ToArray();
		var sourceNodes = sourceJson.RootElement.GetProperty( "nodes" );
		var expectedBoneNames = expectedBoneNodes.Select( node => sourceNodes[node].GetProperty( "name" ).GetString() ).ToArray();
		var expectedClips = Enumerable.Range( 0, 3 ).Select( reference.ClipName ).ToArray();
		var fit = FoxDiagnostics.FitSmokeCamera( reference );
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/public-model-smokes/Fox", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		var frames = new List<FrameRow>(); var poses = new List<PoseRow>(); var handles = new List<HandleRow>(); var motion = new List<MotionRow>();
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, fit.Position, fit.Target, fit.OrthographicHeight );
		capture.Camera.ZNear = fit.Near; capture.Camera.ZFar = fit.Far;
		var light = new SceneDirectionalLight( capture.Scene.SceneWorld, Rotation.LookAt( fit.DirectionalLightForward ), Color.White * 4 );
		var lightColor = light.LightColor.ToString();
		Fixture fixture = null; SkinnedModelRenderer first = null, second = null;
		string failure = null, path = null, computeShader = null, inputHash = null; string[] shaders = [];
		var independentB = false; MetadataRow metadata = null;
		try
		{
			fixture = new Fixture( bytes, Resolve, compiled ); path = fixture.Path; inputHash = fixture.InputSha256;
			Empty( await Take( "empty_before" ) );
			var model = fixture.Load();
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null,
				"Public Model.Load must return a valid managed model without native IModel." );
			Require( ReferenceEquals( model, fixture.Load() ), "Public resource identity is not cached." );
			metadata = new( sourceNodes.GetArrayLength(), reference.JointCount, expectedBoneNodes.Length, model.BoneCount,
				expectedBoneNodes, model.GltfResource.Metadata.Bones.Select( bone => bone.Node ).ToArray(),
				expectedBoneNames, Enumerable.Range( 0, model.BoneCount ).Select( model.GetBoneName ).ToArray(), expectedClips, model.AnimationNames.ToArray(),
				"Public identity includes authored skin-joint ancestors:25bones at source nodes0,2..25; GPU skin palette remains24joints. Meshnode1 isexcluded." );
			Require( metadata.ObservedPublicBoneCount == metadata.ExpectedPublicBoneCount && metadata.ObservedSourceNodes.SequenceEqual( expectedBoneNodes ),
				$"Fox public skeleton mapping changed: expected25nodes[0,2..25], observedCount={metadata.ObservedPublicBoneCount}, observedNodes=[{string.Join( ",", metadata.ObservedSourceNodes )}]." );
			Require( metadata.ObservedBoneNames.SequenceEqual( expectedBoneNames ), "Fox public bone names differ from their pinned authored source identities." );
			Require( metadata.ObservedClips.SequenceEqual( expectedClips ), "Fox public clip order differs: " + string.Join( ",", metadata.ObservedClips ) );
			Require( model.Materials.Length == 1 && model.Materials[0] is { IsValid: true, IsError: false }, "Default source Fox material is unavailable." );
			shaders = model.Materials.Select( material => material.ShaderName ).ToArray();
			var namedShader = Shader.Load( GltfManagedModelResource.ComputeShaderPath );
			Require( namedShader is { IsValid: true, IsError: false, IsAnonymous: false }, "Public model path requires the named compiled deformation shader." );
			first = Add( model, Vector3.Zero ); second = Add( model, fit.SecondInstanceOffset );
			Require( !ReferenceEquals( first.GltfInstance, second.GltfInstance ) && ReferenceEquals( first.Model, second.Model ), "Two public instances must share the model and own separate draw state." );
			handles.Add( NativeFree( first, "A" ) ); handles.Add( NativeFree( second, "B" ) );
			computeShader = model.GltfResource.Asset.ComputeMaterial?.ShaderName;
			Require( computeShader?.Contains( "gltf_deform_cs", StringComparison.OrdinalIgnoreCase ) == true, "Actual public instance did not use the named deformation shader." );
			second.Sequence.Name = reference.ClipName( 2 ); second.Sequence.Time = reference.Duration( 2 ) * .5f;
			Tick(); Positive( await Take( "two_instances_initial" ) );
			poses.Add( Observe( "B_initial_Run_middle", second, 2, reference.Duration( 2 ) * .5f ) );
			var stableB = second.GltfInstance.Readback().Select( row => (byte[])row.Bytes.Clone() ).ToArray();
			var stableBRevision = second.GltfInstance.PoseRevision;
			first.Sequence.Name = reference.ClipName( 2 ); first.Sequence.Time = reference.Duration( 2 ) * .25f;
			Tick(); Positive( await Take( "two_instances_A_Run_quarter" ) );
			poses.Add( Observe( "A_Run_quarter", first, 2, reference.Duration( 2 ) * .25f ) );
			var unchanged = second.GltfInstance.Readback();
			independentB = second.GltfInstance.PoseRevision == stableBRevision && unchanged.Count == stableB.Length
				&& unchanged.Select( (row, index) => row.Bytes.SequenceEqual( stableB[index] ) ).All( same => same );
			Require( independentB, "Seeking public instance A changed paused instance B's real GPU output." );
			var deletedB = second.GltfInstance; Destroy( ref second ); var bCalls = deletedB.DrawCalls;
			Positive( await Take( "A_survives_B_deletion" ) ); Require( deletedB.DrawCalls == bCalls, "Destroyed instance B still submits draws." );
			poses.Add( Observe( "A_survives_B_deletion", first, 2, reference.Duration( 2 ) * .25f ) );
			for ( var clip = 0; clip < 3; clip++ )
			{
				NativeFrame start = null; string initialVertices = null;
				foreach ( var sample in new[] { (Label: "start", Time: 0f), (Label: "middle", Time: reference.Duration( clip ) * .5f) } )
				{
					first.Sequence.Name = reference.ClipName( clip ); first.Sequence.Time = sample.Time; Tick();
					Require( first.Sequence.Duration == reference.Duration( clip ), "Public sequence duration differs from authored time." );
					var label = "clip_" + clip + "_" + sample.Label; var frame = await Take( label ); Positive( frame );
					var row = Observe( label, first, clip, sample.Time ); poses.Add( row );
					if ( start is null ) { start = frame; initialVertices = row.VertexSha256; }
					else
					{
						Require( start.CameraStateJson == frame.CameraStateJson, "The fixed source-fit camera changed between poses." );
						var changed = start.Pixels.Zip( frame.Pixels, (a, b) => a.r != b.r || a.g != b.g || a.b != b.b ).Count( value => value );
						var gpuChanged = initialVertices != row.VertexSha256;
						motion.Add( new( reference.ClipName( clip ), changed, gpuChanged ) );
						Require( gpuChanged && changed > 0, "Public clip samples stayed static: " + reference.ClipName( clip ) );
					}
				}
			}
			var deletedA = first.GltfInstance; Destroy( ref first ); var aCalls = deletedA.DrawCalls;
			Empty( await Take( "empty_after_public_renderers" ) ); Require( deletedA.DrawCalls == aCalls, "Destroyed instance A still submits draws." );
			Require( model.IsValid && ReferenceEquals( model, fixture.Load() ), "Renderer deletion invalidated the shared cached model." );
			Require( frames.Count == 11 && poses.Count == 9 && motion.Count == 3 && handles.Count == 2, "Public Fox smoke inventory changed." );
		}
		catch ( Exception error ) { failure = error.ToString(); }
		finally
		{
			await MainThread.Wait();
			Clean( () => Destroy( ref second ), "B" ); Clean( () => Destroy( ref first ), "A" );
			Clean( () => fixture?.Dispose(), "mount" ); Clean( () => { if ( light.IsValid() ) light.Delete(); }, "light" );
		}
		return new( failure is null, compiled ? "glTF/compiled_glb_c" : "glTF/source", path, inputHash, computeShader, fit, lightColor, metadata,
			poses.ToArray(), frames.ToArray(), handles.ToArray(), motion.ToArray(), independentB, shaders,
			"Public Model.Load/SkinnedModelRenderer, named compiled deformation shader, three paused clips, two instances and native PNG/deletion controls. GPU errors are measurements only; original Fox CPU12 failures and thresholds remain unchanged. No sampler/residency/PBR image parity, high-cadence wall-clock or performance claim.", failure );

		SkinnedModelRenderer Add( Model model, Vector3 position )
		{
			using var scope = capture.Scene.Push(); var renderer = capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.WorldPosition = position; renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true;
			renderer.PlaybackRate = 0; renderer.Model = model; renderer.Sequence.Name = reference.ClipName( 0 ); renderer.Enabled = true; Tick();
			return renderer;
		}
		void Tick() { using var scope = capture.Scene.Push(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); }
		PoseRow Observe( string label, SkinnedModelRenderer renderer, int clip, float time )
		{
			var instance = renderer.GltfInstance ?? throw new InvalidOperationException( "Public component lost its GPU instance." );
			Require( renderer.Sequence.Name == reference.ClipName( clip ) && renderer.Sequence.Time == time, "Paused public clip/time changed." );
			Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "Current public pose was not submitted: " + string.Join( "\n", instance.CallbackErrors ) );
			var rows = instance.Readback(); Require( rows.All( row => row.IndexDataOrigin == "gpu-readback" ), "Actual bound index readback is required." );
			var measured = FoxGpuChecks.Measure( reference, clip, time, instance.Placement, rows );
			return new( label, renderer.Sequence.Name, time, renderer.Sequence.Time, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
				Hash( rows.Single().Bytes ), Hash( System.Runtime.InteropServices.MemoryMarshal.AsBytes<uint>( rows.Single().Indices.AsSpan() ).ToArray() ), measured );
		}
		async Task<NativeFrame> Take( string label )
		{
			var frame = await capture.Take( "Fox_public_" + (compiled ? "compiled_" : "source_") + label ); await MainThread.Wait();
			Require( frame.Pixels.Length == frame.Width * frame.Height && frame.Sha256.Length == 64 && !string.IsNullOrEmpty( frame.CameraStateJson ), "Capture lacks pixels/provenance." );
			frame = frame with { Pixels = (Color[])frame.Pixels.Clone() };
			frames.Add( new( label, frame.Path, frame.Sha256, frame.Coverage, frame.EngineFrame, frame.ObservedWallclockSeconds, frame.CameraStateJson ) ); return frame;
		}
		void Destroy( ref SkinnedModelRenderer renderer ) { if ( renderer is null ) return; using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); renderer = null; Tick(); }
		void Clean( Action action, string label ) { try { action(); } catch ( Exception error ) { failure += "\n" + label + " cleanup: " + error; } }
	}

	private static HandleRow NativeFree( SkinnedModelRenderer renderer, string label )
	{
		Require( renderer.GltfInstance is not null && renderer.SceneModel is null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null,
			"Public skinned component must own custom drawing without native scene/model placeholders." );
		var handle = renderer.SceneObject.native.GetModelHandle(); var wrapperNull = handle.IsNull;
		try
		{
			var strong = !wrapperNull && handle.IsStrongHandleValid(); Require( !strong, "Custom object has a valid native model strong handle." );
			return new( label, wrapperNull, strong, renderer.SceneObject.Model is null );
		}
		finally { if ( !wrapperNull ) handle.DestroyStrongHandle(); }
	}
	private static void Empty( NativeFrame frame ) => Require( frame.Coverage == 0, "Empty/deleted scene contains geometry: " + frame.Path );
	private static void Positive( NativeFrame frame ) => Require( frame.Coverage > 64 && !frame.Pixels.Any( pixel => pixel.r > .8f && pixel.g < .2f && pixel.b > .8f ), "Fox capture is empty or contains the magenta target sentinel: " + frame.Path );
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }

	private sealed class Fixture : IDisposable
	{
		private readonly string directory = System.IO.Path.Combine( System.IO.Path.GetTempPath(), "fox_public_" + Guid.NewGuid().ToString( "N" ) );
		private readonly BaseFileSystem aggregate;
		private LocalFileSystem mount; private Model model;
		internal string Path { get; }
		internal string InputSha256 { get; }
		internal Fixture( byte[] source, Func<string, byte[]> resolve, bool compiled )
		{
			try
			{
				var prefix = "fox_" + Guid.NewGuid().ToString( "N" );
				var assetDirectory = System.IO.Path.Combine( directory, prefix ); Directory.CreateDirectory( assetDirectory );
				Path = prefix + "/model" + (compiled ? ".glb_c" : ".gltf");
				var bytes = compiled ? GltfCompiler.Compile( source, resolve ) : source; InputSha256 = Hash( bytes );
				File.WriteAllBytes( System.IO.Path.Combine( directory, Path ), bytes );
				foreach ( var dependency in new[] { "Fox.bin", "Texture.png" } ) File.WriteAllBytes( System.IO.Path.Combine( assetDirectory, dependency ), resolve( dependency ) );
				aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem is unavailable." );
				mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
			}
			catch { Dispose(); throw; }
		}
		internal Model Load() => model ??= Model.Load( Path );
		public void Dispose()
		{
			var errors = new List<Exception>();
			void Attempt( Action action ) { try { action(); } catch ( Exception error ) { errors.Add( error ); } }
			if ( model is not null && model.ResourcePath == Resource.FixPath( Path ) ) Attempt( model.Destroy );
			if ( mount is not null ) { Attempt( () => aggregate.UnMount( mount ) ); Attempt( mount.Dispose ); mount = null; }
			Attempt( () => { if ( Directory.Exists( directory ) ) Directory.Delete( directory, true ); } );
			if ( errors.Count != 0 ) throw new AggregateException( "Fox public fixture cleanup failed.", errors );
		}
	}
}
