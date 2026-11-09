using System;

namespace Sandbox;

/// <summary>
/// Shared glTF authored-seconds clock. It owns no clip, document, pose, model or
/// rendering state. Consumers decide when one changed clock requires evaluation.
/// </summary>
internal sealed class GltfPlaybackClock
{
	internal double Time { get; private set; }
	internal double Duration { get; private set; }
	internal bool Looping { get; set; }
	internal bool Reverse { get; private set; }
	internal bool IsFinished => !Looping && (Reverse ? Time <= 0 : Time >= Duration);

	internal void Reset( double duration )
	{
		RequireFinite( duration, nameof( duration ) );
		if ( duration < 0 ) throw new ArgumentOutOfRangeException( nameof( duration ), "Animation duration cannot be negative." );
		Duration = duration;
		Time = 0;
		Reverse = false;
	}

	/// <summary>Explicit sequence seeks clamp, including while playback is looping.</summary>
	internal void Seek( double time )
	{
		RequireFinite( time, nameof( time ) );
		Time = Math.Clamp( time, 0, Duration );
	}

	/// <summary>
	/// Consume the entire finite signed delta times finite signed rate. Only
	/// advancement wraps; zero movement preserves time and the previous direction.
	/// Arithmetic is checked before publishing any state.
	/// </summary>
	internal bool Advance( double delta, double playbackRate )
	{
		RequireFinite( delta, nameof( delta ) );
		RequireFinite( playbackRate, nameof( playbackRate ) );
		if ( Duration <= 0 || delta == 0 || playbackRate == 0 ) return false;
		var step = delta * playbackRate;
		var next = Time + step;
		if ( !double.IsFinite( step ) || !double.IsFinite( next ) )
			throw new ArgumentOutOfRangeException( nameof( delta ), "Animation time overflowed." );
		if ( step == 0 ) return false;
		double time;
		if ( Looping )
		{
			time = next % Duration;
			// Adding a large period to an already positive small phase can erase
			// that phase. Correct only negative remainders; this is the same
			// positive-modulo rule without that unnecessary precision loss.
			// Preserve the existing zero-endpoint policy if the negative
			// correction rounds to exactly Duration; no epsilon is introduced.
			if ( time < 0 ) time = (time + Duration) % Duration;
		}
		else time = Math.Clamp( next, 0, Duration );
		Time = time;
		Reverse = step < 0;
		return true;
	}

	private static void RequireFinite( double value, string name )
	{
		if ( !double.IsFinite( value ) ) throw new ArgumentOutOfRangeException( name, "Animation clock values must be finite." );
	}
}
