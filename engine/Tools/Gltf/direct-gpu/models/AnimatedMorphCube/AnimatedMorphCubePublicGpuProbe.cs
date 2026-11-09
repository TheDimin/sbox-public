using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Public component commands followed by completed own-buffer GPU diagnostics.
/// The caller creates the model using Model.Load and owns the existing capture
/// scene, camera, frame waits and visibility/deletion controls. No helper asset
/// or pose evaluator is used to load, seek or override the model.
/// </summary>
internal static class AnimatedMorphCubePublicGpuProbe
{
	private const string First = "gltf_node_0_target_0";
	private const string Second = "gltf_node_0_target_1";
	public sealed record Fingerprint( int Node, int Mesh, int Primitive, int VertexCount, int VertexStride,
		string VertexSha256, string IndexSha256, string IndexDataOrigin );
	public sealed record Observation( string Label, string ResourcePath, double RequestedTime, double ObservedTime,
		long PoseRevision, long SubmittedRevision, int DrawCalls, float[] PublicWeights,
		AnimatedMorphCubeGpuChecks.Measurement Geometry, AnimatedMorphCubeGpuChecks.TangentMeasurement Tangents,
		IReadOnlyList<Fingerprint> Fingerprints, string Scope );

	/// <summary>Three authored keys using the public component and the existing oracle.</summary>
	public static async Task<IReadOnlyList<Observation>> RunAuthored( SkinnedModelRenderer renderer, JsonElement form,
		Func<Task> waitForActualFrames, Func<string, SkinnedModelRenderer, Task> afterSample = null )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames ); Prepare( renderer );
		renderer.Morphs.Clear( First, 0 ); renderer.Morphs.Clear( Second, 0 );
		var keys = form.GetProperty( "keyTimes" ); Require( keys.GetArrayLength() == 127, "Audit the pinned clip inventory before changing the three-key probe." );
		var rows = new List<Observation>();
		foreach ( var selected in new[] { (Label: "public_authored_start", Index: 0), (Label: "public_authored_middle_key", Index: 63), (Label: "public_authored_last_key", Index: 126) } )
		{
			var time = keys[selected.Index].GetSingle(); renderer.Sequence.Time = time;
			rows.Add( await Observe( renderer, form, Authored( form, time ), selected.Label, waitForActualFrames, afterSample ) );
		}
		return rows.AsReadOnly();
	}

	/// <summary>
	/// Fully callable short coordinator entry: public Model.Load, native capture
	/// controls and three actual GPU states. Reuses the frozen public-test fixture
	/// for validated source/compiled mounting instead of duplicating its loader.
	/// </summary>
	public static Task<object> RunCapturedAuthored( string checkout, string referencePath, bool compiled = false,
		Action<GltfGpuTriangleGateTests.NativeCapture> configureCapture = null )
		=> RunCapturedCore( checkout, referencePath, compiled, cadence: false, 1, 2, configureCapture: configureCapture );

	/// <summary>Reuse the same public fixture/camera/controls for actual monotonic preview playback.</summary>
	public static Task<object> RunCapturedCadence( string checkout, string referencePath, bool compiled = false, float playbackRate = 1, int minimumLoops = 2 )
		=> RunCapturedCore( checkout, referencePath, compiled, cadence: true, playbackRate, minimumLoops );

	/// <summary>Actual252 public-component frames using the shared recording implementation.</summary>
	public static Task<object> RunCapturedMovie( string checkout, string referencePath, string shaderSourceSha256, bool compiled = false,
		Action<GltfGpuTriangleGateTests.NativeCapture> configureCapture = null )
	{
		if ( string.IsNullOrWhiteSpace( shaderSourceSha256 ) ) throw new ArgumentException( "Supply the actual frozen named shader source hash.", nameof( shaderSourceSha256 ) );
		return RunCapturedCore( checkout, referencePath, compiled, cadence: false, 1, 2, shaderSourceSha256, configureCapture );
	}

	private static async Task<object> RunCapturedCore( string checkout, string referencePath, bool compiled, bool cadence, float playbackRate, int minimumLoops,
		string movieShaderSourceSha256 = null, Action<GltfGpuTriangleGateTests.NativeCapture> configureCapture = null )
	{
		Require( Application.IsEditor && !Game.IsPlaying && ThreadSafe.IsMainThread && !Graphics.IsActive, "Requires an idle physical editor main thread outside graphics." );
		var referenceBytes = File.ReadAllBytes( referencePath );
		using var oracle = JsonDocument.Parse( referenceBytes );
		var form = oracle.RootElement.GetProperty( "forms" ).EnumerateArray().Single( value => value.GetProperty( "source" ).GetString().EndsWith( "/glTF/AnimatedMorphCube.gltf", StringComparison.Ordinal ) );
		var bounds = AnimatedMorphCubeGpuSmokeProbe.ReferenceBounds( form );
		global::Vector3 Vector( float[] value ) => new( value[0], value[1], value[2] );
		var position = Vector( bounds.SuggestedCameraPositionInches ); var target = Vector( bounds.CenterInches );
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/public-model-smokes/AnimatedMorphCube", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, position, target, bounds.SuggestedOrthographicHeightInches );
		capture.Camera.ZFar = Math.Max( 1000, (position - target).Length * 3 );
		// Coordinator may configure only this owned capture's light, before the
		// empty control/model acquisition. Every capture records the native state;
		// the callback does not replace the shader, material or geometry path.
		configureCapture?.Invoke( capture );
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
		var fixtureType = typeof( AnimatedMorphCubePublicMorphTests ).GetNestedType( "Fixture", BindingFlags.NonPublic ) ?? throw new MissingMemberException( "The frozen public Cube fixture is unavailable." );
		IDisposable fixture = null; SkinnedModelRenderer renderer = null;
		try
		{
			var emptyBefore = await capture.Take( "public_empty_before" ); Require( emptyBefore.Coverage == 0, "Fresh public capture scene is not empty." );
			fixture = (IDisposable)Activator.CreateInstance( fixtureType, flags, null, new object[] { checkout, form, compiled }, null );
			var model = (Model)(fixtureType.GetMethod( "Load", flags ) ?? throw new MissingMethodException( "The frozen public fixture's Model.Load entry is unavailable." )).Invoke( fixture, null );
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull, "Public Model.Load did not return the actual managed resource." );
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.PlaybackRate = 0; renderer.Model = model; renderer.Enabled = true;
			}
			Prepare( renderer );
			var handle = renderer.SceneObject.native.GetModelHandle();
			var wrapperIsNull = handle.IsNull; var strongHandleValid = false;
			try
			{
				strongHandleValid = !handle.IsNull && handle.IsStrongHandleValid();
				Require( !strongHandleValid && renderer.SceneObject.Model is null, "The public custom object contains a valid native model placeholder." );
			}
			finally { if ( !handle.IsNull ) handle.DestroyStrongHandle(); }
			var asset = model.GltfResource.Asset;
			var namedShader = Shader.Load( GltfManagedModelResource.ComputeShaderPath );
			Require( namedShader is { IsValid: true, IsError: false, IsAnonymous: false } && asset.ComputeMaterial is { IsValid: true, IsError: false } &&
				asset.ComputeMaterial.ShaderName.Contains( "gltf_deform_cs", StringComparison.OrdinalIgnoreCase ), "Public loading must use the actual named compiled deformation shader." );
			var sourceShaders = model.GltfResource.GetMaterials().Select( material => material.ShaderName ).ToArray();
			var frames = new List<GltfGpuTriangleGateTests.NativeCapture.Frame>();
			IReadOnlyList<Observation> rows; object cadenceObservation = null, movieArtifact = null;
			var movie = movieShaderSourceSha256 is not null;
			if ( movie )
			{
				var recording = await AnimatedMorphCubeMovieProbe.CapturePublic( renderer, form, compiled ? "compiled_glb" : "source", movieShaderSourceSha256,
					async (label, _) =>
					{
						var frame = await capture.Take( label );
						if ( label is "cube_movie_000" or "cube_movie_126" ) frames.Add( frame );
						return frame;
					} );
				Require( recording.PublicModelLoadVerified && recording.Samples.Count == 252 && frames.Count == 2, "Public movie recording inventory or route is incorrect." );
				var recordingPath = Path.Combine( output, "public-movie-recording.json" );
				using ( var stream = new FileStream( recordingPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read ) ) JsonSerializer.Serialize( stream, recording );
				movieArtifact = new
				{
					path = recordingPath, sha256 = Hash( File.ReadAllBytes( recordingPath ) ), sampled_frames = recording.Samples.Count,
					recording.Duration, recording.MaximumMotionPixels, recording.MaximumClockError, recording.MaximumClockBound, recording.PublicModelLoadVerified,
					recording.SamplingRoute, recording.DefaultSourceMaterials,
					maximum_position_error_inches = recording.Samples.Append( recording.Endpoint ).Max( row => row.Geometry.MaximumPositionErrorInches ),
					maximum_normal_error = recording.Samples.Append( recording.Endpoint ).Max( row => row.Geometry.MaximumNormalError ),
					scope = "All252 actual captures and GPU buffer files are retained in recording. Explicit numerical limits and lossless APNG packaging remain separate; no wall-clock or material-appearance pass is inferred."
				};
				rows = Array.Empty<Observation>();
			}
			else if ( cadence )
			{
				frames.Add( await capture.Take( "public_clock_initial" ) );
				cadenceObservation = await AnimatedMorphCubeWallClockProbe.RunPublicVisible( capture.Widget, renderer, capture.WaitFrames, playbackRate, minimumLoops );
				frames.Add( await capture.Take( "public_clock_final_frozen" ) );
				Require( frames.All( frame => frame.Coverage > 64 ), "Actual public playback produced an empty endpoint capture." );
				rows = Array.Empty<Observation>();
			}
			else
			{
				rows = await RunAuthored( renderer, form, capture.WaitFrames, async (label, _) =>
				{
					var frame = await capture.Take( label ); Require( frame.Coverage > 64, "Public Cube capture is empty: " + label ); frames.Add( frame );
				} );
				Require( rows.Count == 3 && frames.Count == 3, "Public three-key inventory changed." );
			}
			var changed = frames[0].Pixels.Zip( frames[1].Pixels, (a, b) => a.r != b.r || a.g != b.g || a.b != b.b ).Count( value => value );
			if ( !cadence ) Require( changed > 0, "Distinct authored morph states did not change the public component's actual pixels." );
			using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.ProcessDeletes(); }
			renderer = null;
			var emptyAfter = await capture.Take( "public_empty_after_instance" ); Require( emptyAfter.Coverage == 0, "Deleting the public component left rendered pixels." );
			return new
			{
				model = "AnimatedMorphCube", form = compiled ? "glTF/compiled_glb" : "glTF/source", output, rows, cadence = cadenceObservation, movie = movieArtifact,
				changed_pixels_start_to_middle = cadence ? (int?)null : changed, changed_pixels_start_to_final = cadence ? changed : (int?)null,
				independent_bounds = bounds, reference_sha256 = Hash( referenceBytes ), source_sha256 = form.GetProperty( "sha256" ).GetString(),
				engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId, gltf_mvid = typeof( GltfDocument ).Assembly.ManifestModule.ModuleVersionId,
				helper_mvid = typeof( AnimatedMorphCubePublicGpuProbe ).Assembly.ManifestModule.ModuleVersionId,
				native_model_wrapper_is_null = wrapperIsNull, native_strong_model_valid = strongHandleValid,
				namedShader.ResourcePath, namedShader.IsAnonymous, compute_material_shader = asset.ComputeMaterial.ShaderName, source_material_shaders = sourceShaders,
				captures = frames.Select( frame => new { frame.Path, frame.Sha256, frame.Coverage, frame.Width, frame.Height, frame.EngineFrame, frame.CameraStateJson } ).ToArray(),
				empty_before = new { emptyBefore.Path, emptyBefore.Sha256, emptyBefore.Coverage }, empty_after = new { emptyAfter.Path, emptyAfter.Sha256, emptyAfter.Coverage },
				native_captures = GltfGpuTriangleGateTests.Measurements.ToArray(),
				scope = movie
					? "Actual public Model.Load and SkinnedModelRenderer,252 deterministic normal Scene.EditorTick updates and actual captures/GPU buffer artifacts, named shader and deletion controls. Geometry errors measured without relaxing the independent source oracle. Clock input/output float boundaries recorded; actual monotonic playback, final material appearance and numeric approval remain separate."
					: cadence
					? "Actual public Model.Load plus SkinnedModelRenderer and ordinary Scene.EditorTick driven by full monotonic widget callback gaps, named shader and final actual GPU outputs, positive endpoints and deletion controls. Whole-loop endpoint pixels may repeat. No deterministic movie, independent posed-geometry comparison, visual FPS or matched-material appearance claim."
					: "Actual public Model.Load plus SkinnedModelRenderer, existing native presentation and named compute shader, three authored GPU/morph states and deletion controls. Default source materials through stock adapter; numeric limits, matched reference images, final material fidelity, manual states, wall-clock cadence and dense movie remain separate."
			};
		}
		finally
		{
			await MainThread.Wait();
			if ( renderer.IsValid() ) { using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.ProcessDeletes(); }
			fixture?.Dispose();
		}
	}

	/// <summary>Camera input derived only from independent authored/manual positions.</summary>
	public static AnimatedMorphCubeGpuSmokeProbe.Bounds ReferenceBounds( JsonElement form ) =>
		AnimatedMorphCubeGpuSmokeProbe.ReferenceBounds( JsonSerializer.SerializeToElement( new
		{
			samples = form.GetProperty( "samples" ).EnumerateArray().Concat( form.GetProperty( "manualSamples" ).EnumerateArray() ).ToArray()
		} ) );

	/// <summary>
	/// Nine signed/partial manual samples plus a clear-to-current sample. Results
	/// measure error; explicit RequireWithin approval and rendered comparisons are
	/// separate acceptance. Each command uses exact zero fade.
	/// </summary>
	public static async Task<IReadOnlyList<Observation>> Run( SkinnedModelRenderer renderer, JsonElement form,
		Func<Task> waitForActualFrames, Func<string, SkinnedModelRenderer, Task> afterSample = null )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames );
		Prepare( renderer );
		var rows = new List<Observation>();
		var manual = form.GetProperty( "manualSamples" );
		Require( manual.GetArrayLength() == 9, "The source oracle must supply all nine explicit signed/partial cases." );
		foreach ( var sample in manual.EnumerateArray() )
		{
			renderer.Morphs.Clear( First, 0 ); renderer.Morphs.Clear( Second, 0 );
			renderer.Sequence.Time = sample.GetProperty( "time" ).GetSingle();
			foreach ( var property in sample.GetProperty( "overrides" ).EnumerateObject() )
			{
				var target = property.Name switch { "0" => First, "1" => Second, _ => throw new InvalidDataException( "Unexpected independent target index." ) };
				renderer.Morphs.Set( target, property.Value.GetSingle(), 0 );
			}
			rows.Add( await Observe( renderer, form, sample, sample.GetProperty( "label" ).GetString(), waitForActualFrames, afterSample ) );
		}
		// The preceding case keeps target one overridden. Clearing both must
		// recover this current key's animation, without a helper EvaluatePose.
		renderer.Morphs.Clear( First, 0 ); renderer.Morphs.Clear( Second, 0 );
		var time = form.GetProperty( "keyTimes" )[32].GetSingle(); renderer.Sequence.Time = time;
		var authored = Authored( form, time );
		rows.Add( await Observe( renderer, form, authored, "clear_to_current_authored_key", waitForActualFrames, afterSample ) );
		Require( !renderer.Morphs.ContainsOverride( First ) && !renderer.Morphs.ContainsOverride( Second ), "Cleared GPU sample still has manual dictionary overrides." );
		return rows.AsReadOnly();
	}

	/// <summary>
	/// Mutating A must change its real GPU output without changing B's already
	/// submitted output. Caller positions/camera may show the independent objects;
	/// numeric expectations include each actual full-affine instance placement.
	/// </summary>
	public static async Task<IReadOnlyList<Observation>> RunIndependentInstances( SkinnedModelRenderer first,
		SkinnedModelRenderer second, JsonElement form, Func<Task> waitForActualFrames,
		Func<string, SkinnedModelRenderer, Task> afterSample = null )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames ); Prepare( first ); Prepare( second );
		Require( ReferenceEquals( first.Model, second.Model ) && !ReferenceEquals( first.GltfInstance, second.GltfInstance ), "Independent instance test needs one shared public model and two actual instances." );
		first.Morphs.Clear( First, 0 ); first.Morphs.Clear( Second, 0 );
		second.Morphs.Clear( First, 0 ); second.Morphs.Clear( Second, 0 );
		first.Sequence.Time = 0;
		var secondTime = form.GetProperty( "keyTimes" )[10].GetSingle(); second.Sequence.Time = secondTime;
		var before = await Observe( second, form, Authored( form, secondTime ), "independent_B_before_A_override", waitForActualFrames, afterSample );
		var secondRevision = second.GltfInstance.PoseRevision;
		first.Morphs.Set( First, -2, 0 ); first.Morphs.Set( Second, 0, 0 );
		var manual = form.GetProperty( "manualSamples" ).EnumerateArray().Single( sample => sample.GetProperty( "label" ).GetString() == "signed_first_minus2" );
		var a = await Observe( first, form, manual, "independent_A_signed_override", waitForActualFrames, afterSample );
		var b = await Observe( second, form, Authored( form, secondTime ), "independent_B_after_A_override", waitForActualFrames, afterSample );
		Require( second.GltfInstance.PoseRevision == secondRevision && before.Fingerprints.SequenceEqual( b.Fingerprints ),
			"Changing A's manual weights mutated B's actual pose revision or GPU vertex/index bytes." );
		Require( !second.Morphs.ContainsOverride( First ) && !second.Morphs.ContainsOverride( Second ), "A leaked its public dictionary into B." );
		return new[] { before, a, b };
	}

	/// <summary>Apply explicitly reviewed numerical limits; no tolerance is invented here.</summary>
	public static void RequireWithin( IEnumerable<Observation> rows, AnimatedMorphCubeMovieProbe.NumericLimits limits )
	{
		ArgumentNullException.ThrowIfNull( rows ); ArgumentNullException.ThrowIfNull( limits );
		foreach ( var row in rows )
		{
			AnimatedMorphCubeGpuChecks.RequireWithin( row.Geometry, limits.PositionInches, limits.Normal );
			AnimatedMorphCubeGpuChecks.RequireTangentsWithin( row.Tangents, limits.TangentDirection, limits.TangentLength, limits.TangentNormalDot );
		}
	}

	private static void Prepare( SkinnedModelRenderer renderer )
	{
		ArgumentNullException.ThrowIfNull( renderer );
		Require( ThreadSafe.IsMainThread && !Graphics.IsActive && renderer.IsValid() && renderer.Model is { IsValid: true, IsError: false } && renderer.Model.native.IsNull,
			"Use an existing main-thread public Model.Load managed model, outside graphics." );
		Require( renderer.SceneObject is SceneCustomObject && renderer.GltfInstance is not null && renderer.SceneModel is null,
			"The ordinary public component must own a direct GPU instance without native model placeholders." );
		Require( renderer.Model.MorphCount == 2 && renderer.Model.Morphs.Names.SequenceEqual( new[] { First, Second } ), "This probe expects the pinned Cube's two logical targets." );
		renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.PlaybackRate = 0; renderer.Sequence.Name = "Square";
	}

	private static async Task<Observation> Observe( SkinnedModelRenderer renderer, JsonElement form, JsonElement expected,
		string label, Func<Task> waitForActualFrames, Func<string, SkinnedModelRenderer, Task> afterSample )
	{
		var expectedTime = expected.GetProperty( "time" ).GetSingle();
		Require( renderer.Sequence.Time == expectedTime, "The public sequence did not retain its exact source-key seek." );
		var instance = renderer.GltfInstance; var revision = instance.PoseRevision; var beforeDraws = instance.DrawCalls;
		await waitForActualFrames(); Submitted();
		var snapshots = instance.Readback();
		Require( snapshots.Count == 1 && snapshots.All( row => row.IndexDataOrigin == "gpu-readback" ), "Public morph topology must come from completed GPU index-buffer readback." );
		var geometry = AnimatedMorphCubeGpuChecks.Measure( snapshots, form, expected, instance.Placement );
		var tangents = AnimatedMorphCubeGpuChecks.MeasureTangents( snapshots, expected, instance.Placement );
		Require( geometry.Finite && geometry.SourceTopologyMatches && tangents.Finite, "Public manual morph produced nonfinite output or changed source topology." );
		var weights = new[] { renderer.Morphs.Get( First ), renderer.Morphs.Get( Second ) };
		var sourceWeights = expected.GetProperty( "weights" );
		for ( var target = 0; target < weights.Length; target++ )
			Require( float.IsFinite( weights[target] ) && MathF.Abs( weights[target] - sourceWeights[target].GetSingle() ) <= 2e-6f,
				"Public manual weight differs from the independent current authored/override expectation." );
		var fingerprints = Fingerprints( snapshots );
		if ( afterSample is not null )
		{
			await afterSample( label, renderer ); Submitted();
			Require( fingerprints.SequenceEqual( Fingerprints( instance.Readback() ) ), "Capturing a paused public morph pose mutated GPU output." );
		}
		return new Observation( label, renderer.Model.ResourcePath, expected.GetProperty( "time" ).GetDouble(), renderer.Sequence.Time,
			revision, instance.SubmittedRevision, instance.DrawCalls, weights, geometry, tangents, fingerprints,
			"Public component commands plus actual own-buffer vertex/index readback. Numerical limits, matched images, source-material fidelity and wall-clock cadence remain separate gates." );

		void Submitted() => Require( renderer.Sequence.Time == expectedTime && ReferenceEquals( instance, renderer.GltfInstance ) && instance.PoseRevision == revision && instance.SubmittedRevision == revision &&
			instance.DrawCalls > beforeDraws && instance.CallbackErrors.Length == 0, "The exact public component pose was not successfully submitted: " + string.Join( "\n", instance.CallbackErrors ) );
	}

	private static Fingerprint[] Fingerprints( IReadOnlyList<GltfGpuPrimitiveSnapshot> rows ) => rows.Select( row => new Fingerprint(
		row.Node, row.Mesh, row.Primitive, row.VertexCount, row.VertexStride, Hash( row.Bytes ),
		Convert.ToHexStringLower( SHA256.HashData( MemoryMarshal.AsBytes( row.Indices.AsSpan() ) ) ), row.IndexDataOrigin ) ).ToArray();
	private static JsonElement Authored( JsonElement form, float time ) => form.GetProperty( "samples" ).EnumerateArray().Single( sample => sample.GetProperty( "time" ).GetSingle() == time );
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidDataException( message ); }
}
