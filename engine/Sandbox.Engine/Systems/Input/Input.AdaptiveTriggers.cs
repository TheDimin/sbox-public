using Sandbox.Engine;

namespace Sandbox;

public static partial class Input
{
	/// <summary>
	/// Apply resistance across the full travel of each adaptive trigger on the current player's controller.
	/// </summary>
	/// <param name="leftStrength">Left trigger resistance, from 0 (release) to 1 (maximum).</param>
	/// <param name="rightStrength">Right trigger resistance, from 0 (release) to 1 (maximum).</param>
	/// <param name="duration">Duration in milliseconds. Must be positive. Repeat the call to extend the effect.</param>
	/// <returns>True if the command was accepted; false if unavailable, disabled, unfocused, or the device write failed.</returns>
	/// <remarks>
	/// Supports the standard Sony DualSense through SDL, or a PS5 controller identified by Steam Input.
	/// Both strengths must be finite and between 0 and 1. Values are rounded to the backend's supported strength levels.
	/// Zero releases a trigger. Ordinary vibration is independent. Effects stop on expiry, focus loss,
	/// game shutdown, or <see cref="StopAllHaptics"/>. Call on the main thread.
	/// Steam Input has no delivery acknowledgement. A successful result does not confirm physical movement.
	/// Unsupported controllers keep their normal rumble behaviour.
	/// </remarks>
	public static bool TrySetAdaptiveTriggerResistance( float leftStrength, float rightStrength, int duration = 500 )
	{
		if ( !float.IsFinite( leftStrength ) || leftStrength < 0 || leftStrength > 1 )
			throw new ArgumentOutOfRangeException( nameof( leftStrength ) );
		if ( !float.IsFinite( rightStrength ) || rightStrength < 0 || rightStrength > 1 )
			throw new ArgumentOutOfRangeException( nameof( rightStrength ) );
		if ( duration <= 0 ) throw new ArgumentOutOfRangeException( nameof( duration ) );
		ThreadSafe.AssertIsMainThread();

		var left = (byte)MathF.Round( leftStrength * byte.MaxValue );
		var right = (byte)MathF.Round( rightStrength * byte.MaxValue );
		if ( Application.IsHeadless ) return false;
		if ( (left != 0 || right != 0) && (!Preferences.AdaptiveTriggers || Suppressed || !WindowInput.HasMouseFocus()) )
			return false;

		var lifetime = GlobalContext.Current.CancellationTokenSource;
		if ( (left != 0 || right != 0) && (lifetime is null || lifetime.IsCancellationRequested) ) return false;
		return CurrentController?.TrySetAdaptiveTriggerResistance( left, right, duration, lifetime?.Token ?? default ) ?? false;
	}
}
