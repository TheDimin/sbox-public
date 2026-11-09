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

using CaptureFrame = GltfGpuTriangleGateTests.NativeCapture.Frame;

/// <summary>
/// Captures every planned state from the actual own-buffer renderer. This is a
/// deterministic movie sampler, separate from ordinary wall-clock playback.
/// The caller owns the accepted native camera, borrowed shader and capture hook.
/// </summary>
internal static class AnimatedMorphCubeMovieProbe
{
	public sealed record NumericLimits( float PositionInches, float Normal, float TangentDirection, float TangentLength, float TangentNormalDot );
	public sealed record FrameMetadata( string Path, string Sha256, int Width, int Height, ulong EngineFrame,
		double CaptureWallclockSeconds, string CameraStateJson, int Coverage );
	public sealed record GpuBufferMetadata( int Node, int Mesh, int Primitive, int VertexCount, int VertexStride,
		JsonElement Fields, uint[] CpuSourceVertices, string VertexPath, string VertexSha256, string IndexPath, string IndexSha256, string IndexDataOrigin );
	public sealed record Sample( int Index, double RequestedTime, double ObservedSourceTime, long RequestedRevision,
		long SubmittedRevision, int DrawCalls, string IndexDataOrigin, FrameMetadata Capture,
		AnimatedMorphCubeGpuChecks.Measurement Geometry, AnimatedMorphCubeGpuChecks.TangentMeasurement Tangents,
		IReadOnlyList<GpuBufferMetadata> GpuBuffers = null, double ClockBoundSeconds = 0 );
	public sealed record Recording( string CaseId, string Form, string SourceSha256, string ShaderSourceSha256,
		double Duration, JsonElement ReferenceForm, IReadOnlyList<Sample> Samples, Sample Endpoint,
		int MaximumMotionPixels, double MaximumClockError, bool DefaultSourceMaterials,
		AnimatedMorphCubeGpuSmokeProbe.Bounds IndependentBounds, Guid EngineMvid, Guid GltfMvid, Guid HelperMvid,
		bool PublicModelLoadVerified = false, string SamplingRoute = "diagnostic shared player", double MaximumClockBound = 0 );

	/// <summary>Run only under the coordinator's serialized real-editor capture admission.</summary>
	public static async Task<Recording> Capture( GltfDocument document, JsonElement form, SceneWorld world,
		Material computeMaterial, string artifactForm, string shaderSourceSha256,
		Func<string, GltfGpuInstance, Task<CaptureFrame>> takeActualFrame, Func<int?, Material> materialResolver = null )
	{
		ArgumentNullException.ThrowIfNull( document ); ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( computeMaterial ); ArgumentNullException.ThrowIfNull( takeActualFrame );
		using var asset = GltfGpuAsset.Create( document, computeMaterial, materialResolver );
		using var instance = asset.CreateInstance( world );
		var player = new GltfGpuAnimationPlayer( document, instance.SetPose ); player.SelectClip( 0 ); player.Play();
		double previous = 0;
		return await CaptureCore( form, instance, artifactForm, shaderSourceSha256, takeActualFrame,
			player.Duration, () => player.CurrentTime, requested => { var changed = player.Advance( requested - previous ); previous = requested; return changed; },
			() => !player.IsPlaying, _ => 1e-10, materialResolver is null, false,
			"Deterministic source-time sampling through the diagnostic shared player; not public Model.Load or wall-clock playback" );
	}

	/// <summary>
	/// Reuses the same recording/capture/checker implementation while normal
	/// Scene.EditorTick advances the real public component. Its prescribed deltas
	/// make a deterministic movie; actual monotonic playback is a separate probe.
	/// </summary>
	public static async Task<Recording> CapturePublic( SkinnedModelRenderer renderer, JsonElement form,
		string artifactForm, string shaderSourceSha256, Func<string, GltfGpuInstance, Task<CaptureFrame>> takeActualFrame )
	{
		ArgumentNullException.ThrowIfNull( renderer ); ArgumentNullException.ThrowIfNull( takeActualFrame );
		Require( ThreadSafe.IsMainThread && !Graphics.IsActive && renderer.IsValid() && renderer.Enabled && renderer.Scene.IsEditor &&
			renderer.Scene.Editor is null && !renderer.Scene.IsSuspended && renderer.Scene.TimeScale == 1 &&
			renderer.Model is { IsValid: true, IsError: false } && renderer.Model.native.IsNull && renderer.Model.GltfResource is not null &&
			renderer.SceneObject is SceneCustomObject && renderer.GltfInstance is not null && renderer.SceneModel is null,
			"Use the real public Model.Load renderer in its owned editor preview scene." );
		renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true;
		foreach ( var name in renderer.Model.Morphs.Names ) renderer.Morphs.Clear( name, 0 );
		renderer.Sequence.Name = "Square"; renderer.Sequence.Looping = false; renderer.Sequence.Time = 0; renderer.PlaybackRate = 1;
		var instance = renderer.GltfInstance;
		var overridesField = typeof( GltfGpuInstance ).GetField( "materialOverrides", BindingFlags.Instance | BindingFlags.NonPublic );
		Require( overridesField?.GetValue( instance ) is Material[], "Actual per-instance material diagnostics are unavailable." );
		var defaultMaterials = ((Material[])overridesField.GetValue( instance )).All( material => material is null );
		double previous = 0, inputRounding = 0, arithmeticBound = 0;
		try
		{
			return await CaptureCore( form, instance, artifactForm, shaderSourceSha256, takeActualFrame,
				renderer.Sequence.Duration, () => renderer.Sequence.Time, AdvancePublic, () => renderer.Sequence.IsFinished,
				_ => inputRounding + FloatUlp( renderer.Sequence.Time ) + arithmeticBound, defaultMaterials, true,
				"Deterministic prescribed frame deltas through public SkinnedModelRenderer and normal Scene.EditorTick; no per-frame seek, helper player or private pose evaluator, and not wall-clock playback" );
		}
		finally { if ( renderer.IsValid() ) renderer.PlaybackRate = 0; }

		bool AdvancePublic( double requested )
		{
			Require( ReferenceEquals( instance, renderer.GltfInstance ) && renderer.PlaybackRate == 1 && renderer.Sequence.Name == "Square", "The declared public movie configuration changed." );
			var gap = requested - previous; var entry = (float)gap;
			Require( double.IsFinite( gap ) && float.IsFinite( entry ) && entry > 0, "Every next public movie frame needs its actual positive full prescribed delta." );
			var revision = instance.PoseRevision; var boneFrame = renderer.Scene.GetSystem<SceneAnimationSystem>().BoneFrame;
			using ( renderer.Scene.Push() ) renderer.Scene.EditorTick( (float)requested, entry );
			Require( renderer.Scene.TimeDelta == entry && renderer.Scene.GetSystem<SceneAnimationSystem>().BoneFrame > boneFrame,
				"The ordinary scene animation stages did not consume the prescribed frame." );
			inputRounding += Math.Abs( gap - entry );
			arithmeticBound += 16 * Math.Max( DoubleUlp( renderer.Sequence.Duration ), Math.Max( DoubleUlp( requested ), DoubleUlp( gap ) ) );
			previous = requested;
			return instance.PoseRevision > revision;
		}
	}

	private static async Task<Recording> CaptureCore( JsonElement form, GltfGpuInstance instance, string artifactForm, string shaderSourceSha256,
		Func<string, GltfGpuInstance, Task<CaptureFrame>> takeActualFrame, double duration, Func<double> sourceTime,
		Func<double, bool> advanceTo, Func<bool> finished, Func<double, double> clockBound,
		bool defaultSourceMaterials, bool publicModelLoad, string samplingRoute )
	{
		if ( string.IsNullOrWhiteSpace( artifactForm ) || string.IsNullOrWhiteSpace( shaderSourceSha256 ) )
			throw new ArgumentException( "Record the actual artifact form and frozen compute-shader identity." );
		var media = form.GetProperty( "media" ); var times = media.GetProperty( "times" );
		var expected = media.GetProperty( "samples" );
		Require( times.GetArrayLength() == 252 && expected.GetArrayLength() == times.GetArrayLength(), "Audit the pinned cube's 252-frame movie inventory before changing it." );
		var samples = new List<Sample>(); var paths = new HashSet<string>( StringComparer.Ordinal );
		global::Color[] previousPixels = null; ulong? previousEngineFrame = null;
		var maximumMotion = 0; double maximumClock = 0, maximumClockBound = 0;
		Require( duration == form.GetProperty( "duration" ).GetDouble(), "The selected clip duration differs from the independent source oracle." );
		for ( var frame = 0; frame < times.GetArrayLength(); frame++ )
		{
			var requested = times[frame].GetDouble();
			if ( frame == 0 ) Require( requested == 0 && sourceTime() == 0, "The first movie state must be the actual selected zero pose." );
			else Require( advanceTo( requested ), "Nonzero source time did not produce one pose update." );
			var error = Math.Abs( sourceTime() - requested ); var bound = clockBound( requested );
			Require( double.IsFinite( error ) && double.IsFinite( bound ) && bound >= 0 && error <= bound, "The deterministic sampler lost source time beyond its declared transport arithmetic bounds." );
			maximumClock = Math.Max( maximumClock, error ); maximumClockBound = Math.Max( maximumClockBound, bound );
			var sample = await CaptureOne( frame, "cube_movie_" + frame.ToString( "D3" ), requested, expected[frame] );
			samples.Add( sample );
		}
		Require( advanceTo( duration ) && finished() && sourceTime() == duration,
			"Nonlooping playback must apply the authored endpoint before finishing." );
		var endpoint = await CaptureOne( samples.Count, "cube_movie_endpoint_not_encoded", duration, media.GetProperty( "endpoint" ) );
		maximumClockBound = Math.Max( maximumClockBound, endpoint.ClockBoundSeconds );
		Require( maximumMotion > 0, "No actual captured state changed across the complete cube movie." );
		var source = form.GetProperty( "source" ).GetString().Replace( '\\', '/' );
		var variant = source.Split( '/' )[^2];
		return new Recording( "AnimatedMorphCube/" + variant, artifactForm, form.GetProperty( "sha256" ).GetString(), shaderSourceSha256,
			duration, form.Clone(), samples.AsReadOnly(), endpoint, maximumMotion, maximumClock, defaultSourceMaterials,
			AnimatedMorphCubeGpuSmokeProbe.ReferenceBounds( form ), typeof( Model ).Assembly.ManifestModule.ModuleVersionId,
			typeof( GltfDocument ).Assembly.ManifestModule.ModuleVersionId, typeof( AnimatedMorphCubeMovieProbe ).Assembly.ManifestModule.ModuleVersionId,
			publicModelLoad, samplingRoute, maximumClockBound );

		async Task<Sample> CaptureOne( int index, string label, double requested, JsonElement reference )
		{
			var revision = instance.PoseRevision;
			var capture = await takeActualFrame( label, instance );
			Require( instance.PoseRevision == revision && instance.SubmittedRevision == revision && instance.DrawCalls > 0 && instance.CallbackErrors.Length == 0,
				"Captured frame did not submit its exact current GPU pose." );
			Require( capture.Width == 192 && capture.Height == 192 && capture.Pixels.Length == 192 * 192 && capture.Coverage > 0,
				"An actual movie capture is empty or has incorrect dimensions." );
			Require( previousEngineFrame is null || capture.EngineFrame > previousEngineFrame.Value, "Reused engine frame identity cannot prove a newly captured state." );
			Require( paths.Add( capture.Path ) && File.Exists( capture.Path ) && Hash( File.ReadAllBytes( capture.Path ) ) == capture.Sha256,
				"Movie frame path/hash does not identify a unique actual captured file." );
			var readback = instance.Readback();
			Require( readback.All( row => row.IndexDataOrigin == "gpu-readback" ), "Movie topology must be read from actual GPU index buffers." );
			var geometry = AnimatedMorphCubeGpuChecks.Measure( readback, form, reference, instance.Placement );
			var tangents = AnimatedMorphCubeGpuChecks.MeasureTangents( readback, reference, instance.Placement );
			Require( geometry.Finite && geometry.SourceTopologyMatches && tangents.Finite, "Captured GPU geometry is nonfinite or changed source topology." );
			if ( index < times.GetArrayLength() && previousPixels is not null )
				maximumMotion = Math.Max( maximumMotion, Changed( previousPixels, capture.Pixels ) );
			previousPixels = capture.Pixels; previousEngineFrame = capture.EngineFrame;
			var buffers = readback.Select( (row, rowIndex) =>
			{
				var vertexPath = capture.Path + $".row{rowIndex}.vertices.bin"; var indexPath = capture.Path + $".row{rowIndex}.indices.bin";
				WriteNew( vertexPath, row.Bytes ); WriteNew( indexPath, MemoryMarshal.AsBytes( row.Indices.AsSpan() ) );
				return new GpuBufferMetadata( row.Node, row.Mesh, row.Primitive, row.VertexCount, row.VertexStride,
					JsonSerializer.SerializeToElement( row.Fields ), (uint[])row.SourceVertices.Clone(), vertexPath, Hash( row.Bytes ), indexPath,
					Convert.ToHexStringLower( SHA256.HashData( MemoryMarshal.AsBytes( row.Indices.AsSpan() ) ) ), row.IndexDataOrigin );
			} ).ToArray();
			return new Sample( index, requested, sourceTime(), revision, instance.SubmittedRevision, instance.DrawCalls, "gpu-readback",
				new FrameMetadata( capture.Path, capture.Sha256, capture.Width, capture.Height, capture.EngineFrame, capture.ObservedWallclockSeconds, capture.CameraStateJson, capture.Coverage ),
				geometry, tangents, buffers, clockBound( requested ) );
		}
	}

	/// <summary>
	/// Explicit numeric acceptance is required before handing a recording to the
	/// shared lossless APNG writer. Visibility, source material fidelity, wall-clock
	/// playback and public Model.Load integration are separate recorded scopes.
	/// </summary>
	public static object ToWriterCase( Recording recording, NumericLimits approvedLimits, Guid nativeRendererMvid )
	{
		ArgumentNullException.ThrowIfNull( recording ); ArgumentNullException.ThrowIfNull( approvedLimits );
		if ( nativeRendererMvid == Guid.Empty ) throw new ArgumentException( "Supply the coordinator's actual loaded native-presentation assembly identity.", nameof( nativeRendererMvid ) );
		foreach ( var sample in recording.Samples.Append( recording.Endpoint ) )
		{
			AnimatedMorphCubeGpuChecks.RequireWithin( sample.Geometry, approvedLimits.PositionInches, approvedLimits.Normal );
			AnimatedMorphCubeGpuChecks.RequireTangentsWithin( sample.Tangents, approvedLimits.TangentDirection, approvedLimits.TangentLength, approvedLimits.TangentNormalDot );
			Require( sample.GpuBuffers is { Count: > 0 }, "The actual movie GPU buffers were not retained for independent artifact audit." );
			foreach ( var buffer in sample.GpuBuffers )
			{
				Require( buffer.IndexDataOrigin == "gpu-readback" && File.Exists( buffer.VertexPath ) && File.Exists( buffer.IndexPath ) &&
					new FileInfo( buffer.VertexPath ).Length == checked((long)buffer.VertexCount * buffer.VertexStride) &&
					Hash( File.ReadAllBytes( buffer.VertexPath ) ) == buffer.VertexSha256 && Hash( File.ReadAllBytes( buffer.IndexPath ) ) == buffer.IndexSha256,
					"A retained actual GPU movie buffer changed or is missing; reject stale recorded measurements." );
			}
		}
		return new
		{
			id = recording.CaseId, form = recording.Form, source_sha256 = recording.SourceSha256, passed = true, load = "passed", motion = "passed",
			framing = recording.IndependentBounds,
			provenance = new { renderer_route = recording.PublicModelLoadVerified ? "Public Model.Load and SkinnedModelRenderer through native SceneCustomObject presentation of C# owned GPU buffers" : "Diagnostic native SceneCustomObject presentation of C# owned GPU buffers", recording.EngineMvid, recording.GltfMvid, recording.HelperMvid, native_renderer_mvid = nativeRendererMvid,
				recording.ShaderSourceSha256, recording.DefaultSourceMaterials, numeric_limits = approvedLimits, public_model_load_verified = recording.PublicModelLoadVerified, wallclock_playback_verified = false,
				source_material_appearance_verified = false, capture_controls_scope = "Coordinator must attach its independent empty/positive/deletion controls", timing_scope = recording.SamplingRoute, recording.MaximumClockBound },
			clips = new[] { new
			{
				name = "Square", duration = recording.Duration, passed = true,
				frames = recording.Samples.Select( sample => new { path = sample.Capture.Path, sha256 = sample.Capture.Sha256, width = sample.Capture.Width, height = sample.Capture.Height,
					requested_time = sample.RequestedTime, source_clock_seconds = sample.ObservedSourceTime,
					evaluated_time_before = (float)sample.ObservedSourceTime, evaluated_time_after = (float)sample.ObservedSourceTime,
						sample.RequestedRevision, sample.SubmittedRevision, sample.DrawCalls, sample.IndexDataOrigin, sample.Geometry, sample.Tangents, sample.GpuBuffers, sample.ClockBoundSeconds,
					engine_frame = sample.Capture.EngineFrame, capture_wallclock_seconds = sample.Capture.CaptureWallclockSeconds, camera_state_json = sample.Capture.CameraStateJson,
					renderer_mvid = nativeRendererMvid, managed_backend_mvid = recording.EngineMvid, helper_mvid = recording.HelperMvid,
					renderer_identity_scope = "Actual native presentation assembly supplied by the coordinator's loaded-identity receipt; backend and test helper identities are recorded separately" } ).ToArray(),
				max_motion_pixels = recording.MaximumMotionPixels, max_clock_error = recording.MaximumClockError,
				seek_reset_pixel_difference = (int?)null, seek_reset_verified = false, nonlooping_end_passed = true,
				looping_wrap_verified = false, independent_instances_verified = false, wallclock_playback_verified = false,
				endpoint_not_encoded = recording.Endpoint,
				source_timelines = new[] { new { sampler = 0, interpolation = "LINEAR", key_times = recording.ReferenceForm.GetProperty( "keyTimes" ).Clone() } }
			} }
		};
	}

	private static int Changed( global::Color[] first, global::Color[] second )
	{
		Require( first.Length == second.Length, "Movie frame extent changed." );
		var count = 0;
		for ( var index = 0; index < first.Length; index++ )
			if ( first[index].r != second[index].r || first[index].g != second[index].g || first[index].b != second[index].b ) count++;
		return count;
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void WriteNew( string path, ReadOnlySpan<byte> bytes ) { using var output = new FileStream( path, FileMode.CreateNew, FileAccess.Write, FileShare.Read ); output.Write( bytes ); }
	private static double DoubleUlp( double value ) { value = Math.Abs( value ); return Math.BitIncrement( value ) - value; }
	private static double FloatUlp( float value ) => Math.Max( (double)MathF.BitIncrement( value ) - value, (double)value - MathF.BitDecrement( value ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidDataException( message ); }
}
