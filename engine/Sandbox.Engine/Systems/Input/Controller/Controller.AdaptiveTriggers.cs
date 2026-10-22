using NativeEngine;
using System.Diagnostics;
using System.Threading;

namespace Sandbox;

partial class Controller
{
	bool adaptiveTriggersActive, adaptiveTriggersApplied;
	byte adaptiveLeft, adaptiveRight;
	long adaptiveExpires, adaptiveRetry;
	ulong adaptiveSteamHandle;
	CancellationToken adaptiveLifetime;

	internal bool TrySetAdaptiveTriggerResistance( byte left, byte right, int duration, CancellationToken lifetime )
	{
		var gamepad = Gamepad;
		if ( gamepad == IntPtr.Zero )
		{
			StopAdaptiveTriggerResistance();
			ForgetAdaptiveTriggers();
			return false;
		}

		var steamHandle = SdlGamepadEffects.GetSteamHandle( gamepad );
		if ( steamHandle != 0 ? !Steamworks.SteamInput.SupportsAdaptiveTriggers( steamHandle ) : !SdlGamepadEffects.IsSupported( gamepad ) )
			return false;

		// Release through the original owner before routing a new effect to a different owner.
		if ( adaptiveTriggersActive && adaptiveSteamHandle != steamHandle && !StopAdaptiveTriggerResistance() ) return false;
		adaptiveSteamHandle = steamHandle;
		if ( left == 0 && right == 0 )
		{
			// An explicit release also clears effects left by another session.
			adaptiveTriggersActive = true;
			return StopAdaptiveTriggerResistance();
		}

		var now = Stopwatch.GetTimestamp();
		var unchanged = adaptiveTriggersApplied && now < adaptiveExpires && adaptiveLeft == left && adaptiveRight == right;
		adaptiveTriggersActive = true;
		adaptiveLifetime = lifetime;
		adaptiveExpires = now + (long)(duration * (double)Stopwatch.Frequency / 1000);
		adaptiveRetry = 0;
		if ( unchanged ) return true;

		adaptiveTriggersApplied = SendAdaptiveTriggerResistance( gamepad, left, right );
		if ( !adaptiveTriggersApplied )
		{
			// A failed write must not leave an older effect running or count as applied state.
			adaptiveExpires = 0;
			return false;
		}

		adaptiveLeft = left;
		adaptiveRight = right;
		return true;
	}

	internal void UpdateAdaptiveTriggerResistance( long now )
	{
		if ( !adaptiveTriggersActive || now < adaptiveRetry ) return;
		if ( now >= adaptiveExpires || adaptiveLifetime.IsCancellationRequested || !Preferences.AdaptiveTriggers )
			StopAdaptiveTriggerResistance();
	}

	internal bool StopAdaptiveTriggerResistance()
	{
		if ( !adaptiveTriggersActive ) return true;
		adaptiveTriggersApplied = false;
		adaptiveExpires = 0;
		adaptiveLifetime = default;
		var gamepad = Gamepad;
		if ( gamepad == IntPtr.Zero && adaptiveSteamHandle == 0 )
		{
			ForgetAdaptiveTriggers();
			return false;
		}

		if ( SendAdaptiveTriggerResistance( gamepad, 0, 0 ) )
		{
			ForgetAdaptiveTriggers();
			return true;
		}

		// Retry a failed release without sending a packet on every frame.
		adaptiveRetry = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 4;
		return false;
	}

	internal void ForgetAdaptiveTriggers()
	{
		adaptiveTriggersActive = adaptiveTriggersApplied = false;
		adaptiveLifetime = default;
		adaptiveExpires = adaptiveRetry = 0;
		adaptiveSteamHandle = 0;
	}

	bool SendAdaptiveTriggerResistance( IntPtr gamepad, byte left, byte right ) => adaptiveSteamHandle != 0
		? Steamworks.SteamInput.SendTriggerResistance( adaptiveSteamHandle, left, right )
		: SdlGamepadEffects.SendResistance( gamepad, left, right );
}
