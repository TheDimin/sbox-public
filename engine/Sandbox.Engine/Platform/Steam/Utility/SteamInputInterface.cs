using System.Runtime.InteropServices;

namespace Steamworks;

/// <summary>
/// The Steam Input calls needed for controller effects, using the shipped Steamworks flat API.
/// </summary>
internal class SteamInputInterface : SteamInterface
{
	[DllImport( Platform.LibraryName, CallingConvention = Platform.CC )]
	static extern IntPtr SteamAPI_SteamInput_v006();
	internal override IntPtr GetUserInterfacePointer() => SteamAPI_SteamInput_v006();

	[DllImport( Platform.LibraryName, CallingConvention = Platform.CC )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool SteamAPI_ISteamInput_Init( IntPtr self, [MarshalAs( UnmanagedType.I1 )] bool explicitlyCallRunFrame );
	internal virtual bool Init( bool explicitlyCallRunFrame ) => SteamAPI_ISteamInput_Init( Self, explicitlyCallRunFrame );

	[DllImport( Platform.LibraryName, CallingConvention = Platform.CC )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool SteamAPI_ISteamInput_Shutdown( IntPtr self );
	internal virtual bool Shutdown() => SteamAPI_ISteamInput_Shutdown( Self );

	[DllImport( Platform.LibraryName, CallingConvention = Platform.CC )]
	static extern InputType SteamAPI_ISteamInput_GetInputTypeForHandle( IntPtr self, ulong handle );
	internal virtual InputType GetInputType( ulong handle ) => SteamAPI_ISteamInput_GetInputTypeForHandle( Self, handle );

	[DllImport( Platform.LibraryName, CallingConvention = Platform.CC )]
	static extern void SteamAPI_ISteamInput_SetDualSenseTriggerEffect( IntPtr self, ulong handle, in DualSenseTriggerEffect effect );
	internal virtual void SetTriggerEffect( ulong handle, in DualSenseTriggerEffect effect ) =>
		SteamAPI_ISteamInput_SetDualSenseTriggerEffect( Self, handle, in effect );
}

/// <summary>
/// ScePadTriggerEffectParam from the Steamworks SDK's isteamdualsense.h.
/// Both 56-byte commands use feedback at position zero, or off. Unused fields stay zero.
/// https://partner.steamgames.com/doc/api/isteaminput#SetDualSenseTriggerEffect
/// </summary>
[StructLayout( LayoutKind.Explicit, Size = 120 )]
internal struct DualSenseTriggerEffect
{
	[FieldOffset( 0 )] internal byte Mask;
	[FieldOffset( 8 )] internal int LeftMode;
	[FieldOffset( 17 )] internal byte LeftStrength;
	[FieldOffset( 64 )] internal int RightMode;
	[FieldOffset( 73 )] internal byte RightStrength;

	internal static DualSenseTriggerEffect Resistance( byte left, byte right ) => new()
	{
		Mask = 3,
		LeftMode = left == 0 ? 0 : 1,
		RightMode = right == 0 ? 0 : 1,
		LeftStrength = Strength( left ),
		RightStrength = Strength( right )
	};

	// Steam offers eight nonzero feedback strengths. Preserve a nonzero request at its minimum.
	static byte Strength( byte value ) => value == 0 ? (byte)0 : (byte)Math.Clamp( (value * 8 + 127) / 255, 1, 8 );
}
