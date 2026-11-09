using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Ordinary visible-widget playback driver. It is separate from deterministic
/// seek/capture movies, and records clock evidence rather than asserting FPS.
/// The caller owns/validates the physical widget, scene, asset and instance.
/// </summary>
internal sealed class AnimatedMorphCubeWallClockProbe : IDisposable
{
	public sealed record Observation( long Timestamp, double ElapsedSeconds, double DeltaSeconds,
		double SourceTime, double ExpectedLoopTime, double PhaseErrorSeconds, double PlaybackRate,
		double? SceneDeltaSeconds = null, double? AccumulatedInputRoundingSeconds = null, double? PublicClockUlpSeconds = null, double? ArithmeticBoundSeconds = null );

	private readonly Editor.SceneRenderingWidget widget;
	private readonly long started;
	private readonly double playbackRate;
	private readonly SkinnedModelRenderer publicRenderer;
	private readonly GltfGpuInstance publicInstance;
	private readonly double duration;
	private double inputRounding, arithmeticBound;
	private long previous;
	private bool disposed;
	private readonly Queue<Observation> observations = new();
	public GltfGpuAnimationPlayer Player { get; }
	public long Ticks { get; private set; }
	public double MaximumDeltaSeconds { get; private set; }
	public double MaximumPhaseErrorSeconds { get; private set; }
	public string Error { get; private set; }
	public IReadOnlyList<Observation> Observations => observations.ToArray();
	public long TimestampFrequency => Stopwatch.Frequency;
	private double Duration => duration;

	/// <summary>Explicitly start one instance; the shared player itself never autoplays.</summary>
	public AnimatedMorphCubeWallClockProbe( Editor.SceneRenderingWidget widget, GltfDocument document, GltfGpuInstance instance, double playbackRate = 1 )
	{
		ArgumentNullException.ThrowIfNull( widget ); ArgumentNullException.ThrowIfNull( instance );
		if ( !widget.IsValid ) throw new InvalidOperationException( "The caller must supply its existing visible rendering widget." );
		this.widget = widget;
		Player = new GltfGpuAnimationPlayer( document, instance.SetPose );
		Player.SelectClip( 0, looping: true );
		Player.Speed = playbackRate;
		this.playbackRate = playbackRate;
		if ( Player.Duration <= 0 ) throw new InvalidOperationException( "AnimatedMorphCube needs its authored nonzero clip duration." );
		duration = Player.Duration;
		Player.Play();
		started = previous = Stopwatch.GetTimestamp();
		widget.OnPreFrame += Tick;
	}

	private AnimatedMorphCubeWallClockProbe( Editor.SceneRenderingWidget widget, SkinnedModelRenderer renderer, float rate )
	{
		ArgumentNullException.ThrowIfNull( widget ); ArgumentNullException.ThrowIfNull( renderer );
		if ( !float.IsFinite( rate ) || rate == 0 ) throw new ArgumentOutOfRangeException( nameof( rate ) );
		if ( !widget.IsValid || !widget.Visible || !renderer.IsValid() || !renderer.Enabled || !ReferenceEquals( widget.Scene, renderer.Scene ) ||
			!renderer.Scene.IsEditor || renderer.Scene.Editor is not null || renderer.Scene.IsSuspended || renderer.Scene.TimeScale != 1 )
			throw new InvalidOperationException( "Use an existing visible widget and its enabled renderer in an owned editor-preview scene without a selection session." );
		if ( renderer.Model is not { IsValid: true, IsError: false } || !renderer.Model.native.IsNull || renderer.Model.GltfResource is null ||
			renderer.SceneObject is not SceneCustomObject || renderer.GltfInstance is null || renderer.SceneModel is not null )
			throw new InvalidOperationException( "Public cadence requires the real managed Model.Load and SkinnedModelRenderer path." );
		this.widget = widget; publicRenderer = renderer; publicInstance = renderer.GltfInstance; playbackRate = rate;
		renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true;
		foreach ( var name in renderer.Model.Morphs.Names ) renderer.Morphs.Clear( name, 0 );
		renderer.Sequence.Name = "Square"; renderer.Sequence.Looping = true; renderer.Sequence.Time = 0; renderer.PlaybackRate = rate;
		duration = renderer.Sequence.Duration;
		if ( duration <= 0 ) throw new InvalidOperationException( "The public Cube sequence needs its authored duration." );
		started = previous = Stopwatch.GetTimestamp(); widget.OnPreFrame += Tick;
	}

	private void Tick()
	{
		if ( disposed || Error is not null ) return;
		try
		{
			var timestamp = Stopwatch.GetTimestamp();
			var delta = (timestamp - previous) / (double)Stopwatch.Frequency;
			var elapsed = (timestamp - started) / (double)Stopwatch.Frequency;
			previous = timestamp;
			double sourceTime; double? sceneDelta = null, clockUlp = null;
			if ( publicRenderer is not null )
			{
				if ( !publicRenderer.IsValid() || !ReferenceEquals( publicRenderer.GltfInstance, publicInstance ) || publicRenderer.PlaybackRate != (float)playbackRate ||
					!publicRenderer.PlayAnimationsInEditorScene || publicRenderer.UseAnimGraph || publicRenderer.Sequence.Name != "Square" )
					throw new InvalidOperationException( "The declared public preview configuration changed during playback." );
				var entryDelta = (float)delta;
				if ( !float.IsFinite( entryDelta ) ) throw new InvalidOperationException( "Scene.EditorTick's float delta overflowed." );
				var boneFrame = publicRenderer.Scene.GetSystem<SceneAnimationSystem>().BoneFrame; var revision = publicInstance.PoseRevision;
				using ( publicRenderer.Scene.Push() ) publicRenderer.Scene.EditorTick( (float)elapsed, entryDelta );
				if ( publicRenderer.Scene.GetSystem<SceneAnimationSystem>().BoneFrame <= boneFrame || publicInstance.PoseRevision <= revision )
					throw new InvalidOperationException( "The ordinary editor animation stages did not publish the public pose." );
				if ( publicRenderer.Scene.TimeDelta != entryDelta ) throw new InvalidOperationException( "The scene dropped or replaced its declared full frame delta." );
				sceneDelta = entryDelta; inputRounding += Math.Abs( delta - entryDelta ) * Math.Abs( playbackRate );
				sourceTime = publicRenderer.Sequence.Time;
				clockUlp = FloatUlp( (float)sourceTime );
				// Up to five kernel operations and four independent phase operations
				// round in double. Sixteen operand ULPs cover those roundings and a
				// possible one-binade increase on addition, including bookkeeping.
				// Actual input/output float boundaries are accounted for separately;
				// this is not an empirical animation epsilon or a geometry tolerance.
				arithmeticBound += 16 * Math.Max( DoubleUlp( Duration ), Math.Max( DoubleUlp( elapsed * playbackRate ), DoubleUlp( delta * playbackRate ) ) );
			}
			else
			{
				if ( Player.Speed != playbackRate ) throw new InvalidOperationException( "This clock measurement requires its declared constant playback rate." );
				Player.Advance( delta ); sourceTime = Player.CurrentTime; // Entire elapsed gap; no 0.2 s cap.
			}
			// Independent nonincremental phase oracle, not a second production
			// clock: expected authored seconds come straight from monotonic time.
			var expected = (elapsed * playbackRate % Duration + Duration) % Duration;
			var difference = Math.Abs( sourceTime - expected );
			var error = Math.Min( difference, Math.Abs( Duration - difference ) );
			if ( !double.IsFinite( expected ) || !double.IsFinite( error ) ) throw new InvalidOperationException( "Wall-clock phase arithmetic overflowed." );
			if ( clockUlp.HasValue && error > inputRounding + clockUlp.Value + arithmeticBound )
				throw new InvalidOperationException( "Public animation lost wall time beyond the recorded float input/output and double arithmetic bounds." );
			MaximumDeltaSeconds = Math.Max( MaximumDeltaSeconds, delta );
			MaximumPhaseErrorSeconds = Math.Max( MaximumPhaseErrorSeconds, error );
			Ticks++;
			observations.Enqueue( new Observation( timestamp, elapsed, delta, sourceTime, expected, error, playbackRate,
				sceneDelta, sceneDelta.HasValue ? inputRounding : null, clockUlp, sceneDelta.HasValue ? arithmeticBound : null ) );
			if ( observations.Count > 1024 ) observations.Dequeue();
		}
		catch ( Exception error )
		{
			Error = error.ToString(); Player?.Pause();
			if ( publicRenderer.IsValid() ) publicRenderer.PlaybackRate = 0;
		}
	}

	/// <summary>
	/// Fully callable normal-widget observation. Requires borrowed existing visible
	/// presentation and real-frame waits; it neither seeks/captures a movie nor
	/// constructs a camera, shader, asset or public Model.Load replacement.
	/// </summary>
	public static async Task<object> RunVisible( Editor.SceneRenderingWidget widget, GltfDocument document,
		GltfGpuInstance instance, Func<Task> waitForActualFrames, double playbackRate = 1, int minimumLoops = 2 )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames );
		if ( !double.IsFinite( playbackRate ) || playbackRate == 0 )
			throw new ArgumentOutOfRangeException( nameof( playbackRate ), "Use a finite nonzero declared rate." );
		if ( minimumLoops is < 1 or > 4 ) throw new ArgumentOutOfRangeException( nameof( minimumLoops ), "Observe one to four loops per bounded request." );
		using var driver = new AnimatedMorphCubeWallClockProbe( widget, document, instance, playbackRate );
		return await ObserveUntil( driver, instance, waitForActualFrames, minimumLoops );
	}

	/// <summary>
	/// Actual public component cadence through the shipped Scene.EditorTick path.
	/// After its initial reset it never sets Sequence.Time or calls a helper player,
	/// pose evaluator, private clock or SetPose. Every real callback gap is retained.
	/// </summary>
	public static async Task<object> RunPublicVisible( Editor.SceneRenderingWidget widget, SkinnedModelRenderer renderer,
		Func<Task> waitForActualFrames, float playbackRate = 1, int minimumLoops = 2 )
	{
		ArgumentNullException.ThrowIfNull( waitForActualFrames );
		if ( minimumLoops is < 1 or > 4 ) throw new ArgumentOutOfRangeException( nameof( minimumLoops ) );
		using var driver = new AnimatedMorphCubeWallClockProbe( widget, renderer, playbackRate );
		return await ObserveUntil( driver, renderer.GltfInstance, waitForActualFrames, minimumLoops );
	}

	private static async Task<object> ObserveUntil( AnimatedMorphCubeWallClockProbe driver, GltfGpuInstance instance,
		Func<Task> waitForActualFrames, int minimumLoops )
	{
		var beforeDraws = instance.DrawCalls;
		var playbackRate = driver.playbackRate;
		var requiredSeconds = driver.Duration * minimumLoops / Math.Abs( playbackRate );
		if ( !double.IsFinite( requiredSeconds ) || requiredSeconds > 30 )
			throw new ArgumentOutOfRangeException( nameof( playbackRate ), "This bounded observation allows at most 30 seconds; split longer observations into separate requests." );
		var timer = Stopwatch.StartNew();
		while ( driver.Ticks < 6 || driver.Observations.Count == 0 || driver.Observations[^1].ElapsedSeconds < requiredSeconds )
		{
			if ( timer.Elapsed.TotalSeconds > Math.Min( 50, Math.Max( 15, requiredSeconds * 2 + 5 ) ) ) throw new TimeoutException( "Visible cube widget did not complete its declared wall-clock observation." );
			await waitForActualFrames();
			if ( driver.Error is not null ) throw new InvalidOperationException( driver.Error );
			if ( instance.CallbackErrors.Length != 0 ) throw new InvalidOperationException( string.Join( "\n", instance.CallbackErrors ) );
		}
		driver.Dispose(); // Freeze the last normally advanced pose before readback.
		await waitForActualFrames();
		if ( instance.DrawCalls <= beforeDraws || instance.SubmittedRevision != instance.PoseRevision || instance.CallbackErrors.Length != 0 )
			throw new InvalidOperationException( "The visible wall-clock instance did not submit its last ordinary update." );
		var readback = instance.Readback();
		if ( readback.Count == 0 || readback.Any( row => row.IndexDataOrigin != "gpu-readback" ) )
			throw new InvalidOperationException( "Wall-clock playback needs actual completed GPU outputs/index provenance." );
		return new
		{
			playback_rate = playbackRate, minimum_loops = minimumLoops, clip_duration = driver.Duration, driver.Ticks,
			driver.TimestampFrequency, driver.MaximumDeltaSeconds, driver.MaximumPhaseErrorSeconds, observations = driver.Observations,
			instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls,
			public_model_load = driver.publicRenderer is not null, public_resource_path = driver.publicRenderer?.Model?.ResourcePath,
			input_rounding_budget_seconds = driver.publicRenderer is null ? (double?)null : driver.inputRounding,
			double_arithmetic_budget_seconds = driver.publicRenderer is null ? (double?)null : driver.arithmeticBound,
			gpu_rows = readback.Select( row => new { row.Node, row.Mesh, row.Primitive, row.VertexCount, row.VertexStride, row.IndexDataOrigin } ).ToArray(),
			scope = driver.publicRenderer is null
				? "Diagnostic player on an existing visible widget; no public Model.Load claim. Clock error is measured, separate from visual cadence and deterministic movie evidence."
				: "Actual public SkinnedModelRenderer through normal Scene.EditorTick and full monotonic callback gaps. Initial reset only; no per-frame seek or helper clock. Phase bounds derive from recorded float entry/output and double operations. Actual visual FPS, independent current-time geometry and dense movie remain separate acceptance."
		};
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		widget.OnPreFrame -= Tick;
		Player?.Pause();
		if ( publicRenderer.IsValid() ) publicRenderer.PlaybackRate = 0;
	}

	private static double DoubleUlp( double value ) { value = Math.Abs( value ); return Math.BitIncrement( value ) - value; }
	private static double FloatUlp( float value ) => Math.Max( (double)MathF.BitIncrement( value ) - value, (double)value - MathF.BitDecrement( value ) );
}
