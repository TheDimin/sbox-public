using Sandbox.Engine;

namespace Steamworks;

/// <summary>
/// Steam owns effects for controllers routed through Steam Input. SDL supplies their handles.
/// </summary>
internal class SteamInput : SteamClientClass<SteamInput>
{
	internal static SteamInputInterface Internal => Interface as SteamInputInterface;

	internal override void InitializeInterface( bool server )
	{
		var input = new SteamInputInterface();
		input.SetupInterface( server );
		if ( input.IsValid && input.Init( false ) )
			SetInterface( server, input );
	}

	internal override void DestroyInterface( bool server )
	{
		if ( Internal is { IsValid: true } input )
		{
			// Release while Steam Input is still available, including when the Steam client closes.
			SdlGamepads.StopAdaptiveTriggers();
			input.Shutdown();
		}
		base.DestroyInterface( server );
	}

	internal static bool SupportsAdaptiveTriggers( ulong handle ) => handle != 0
		&& Internal is { IsValid: true } input && input.GetInputType( handle ) == InputType.PS5Controller;

	internal static bool SendTriggerResistance( ulong handle, byte left, byte right )
	{
		if ( !SupportsAdaptiveTriggers( handle ) ) return false;
		var effect = DualSenseTriggerEffect.Resistance( left, right );
		Internal.SetTriggerEffect( handle, in effect );
		// Valve's API has no delivery result. True means the command was submitted.
		return true;
	}
}
