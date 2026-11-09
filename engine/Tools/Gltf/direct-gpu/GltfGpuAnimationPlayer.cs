using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu;

/// <summary>
/// Instance-owned authored-seconds clock. A pose is evaluated once per explicit
/// seek/selection or playing update and passed to both skin and morph consumers.
/// Rendering and capture callbacks never advance this clock.
/// </summary>
public sealed class GltfGpuAnimationPlayer
{
	private readonly GltfDocument document;
	private readonly Action<GltfPose> applyPose;
	private readonly GltfPlaybackClock clock = new();
	private double speed = 1;

	public int? Animation { get; private set; }
	public bool Looping => clock.Looping;
	public bool IsPlaying { get; private set; }
	public double CurrentTime => clock.Time;
	/// <summary>Latest authored input key among the selected animation's used samplers.</summary>
	public double Duration => clock.Duration;
	public GltfPose CurrentPose { get; private set; }

	public double Speed
	{
		get => speed;
		set { ValidateFinite( value, nameof( value ) ); speed = value; }
	}

	public GltfGpuAnimationPlayer( GltfDocument document, Action<GltfPose> applyPose = null )
	{
		ArgumentNullException.ThrowIfNull( document );
		this.document = document;
		this.applyPose = applyPose;
		CurrentPose = document.EvaluatePose();
	}

	/// <summary>Select a clip at zero, paused, or explicitly return to authored rest.</summary>
	public void SelectClip( int? animation, bool looping = false )
	{
		// Validate/evaluate before replacing the previous usable state.
		var pose = document.EvaluatePose( animation, 0 );
		var duration = animation.HasValue ? ReadDuration( animation.Value ) : 0;
		Animation = animation;
		clock.Reset( duration );
		clock.Looping = animation.HasValue && looping;
		IsPlaying = false;
		CurrentPose = pose;
		applyPose?.Invoke( pose );
	}

	public void Play()
	{
		RequireClip();
		IsPlaying = true;
	}

	public void Pause() => IsPlaying = false;

	public void Seek( double time )
	{
		ValidateFinite( time, nameof( time ) );
		RequireClip();
		clock.Seek( time );
		ApplyCurrent();
	}

	/// <summary>
	/// Consume every supplied signed second, including a long frame. A paused or
	/// zero-rate/delta player does nothing; nonlooping playback applies its directional endpoint. No frame
	/// rate resampling, elapsed cap, global state or rendering callback is involved.
	/// </summary>
	public bool Advance( double elapsed )
	{
		ValidateFinite( elapsed, nameof( elapsed ) );
		if ( !IsPlaying ) return false;
		if ( !clock.Advance( elapsed, speed ) ) return false;
		ApplyCurrent();
		if ( clock.IsFinished ) IsPlaying = false;
		return true;
	}

	private void ApplyCurrent()
	{
		// glTF authored keys and this evaluator use FLOAT. Keep accumulated time
		// double and round only this individual sample, never the instance clock.
		var pose = document.EvaluatePose( Animation, (float)clock.Time );
		CurrentPose = pose;
		applyPose?.Invoke( pose );
	}

	private double ReadDuration( int animationIndex )
	{
		var animation = document.Root.GetProperty( "animations" )[animationIndex];
		var samplers = animation.GetProperty( "samplers" );
		var used = new HashSet<int>();
		foreach ( var channel in animation.GetProperty( "channels" ).EnumerateArray() )
			used.Add( channel.GetProperty( "sampler" ).GetInt32() );
		double duration = 0;
		foreach ( var index in used )
		{
			var times = document.ReadFloatAccessor( samplers[index].GetProperty( "input" ).GetInt32() );
			foreach ( var time in times ) duration = Math.Max( duration, time );
		}
		return duration;
	}

	private void RequireClip()
	{
		if ( !Animation.HasValue ) throw new InvalidOperationException( "Select an animation before playing or seeking." );
	}

	private static void ValidateFinite( double value, string name )
	{
		if ( !double.IsFinite( value ) ) throw new ArgumentOutOfRangeException( name, "Time and speed must be finite." );
	}
}
