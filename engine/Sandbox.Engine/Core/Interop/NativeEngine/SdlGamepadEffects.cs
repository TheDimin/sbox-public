using System.IO;
using System.Runtime.InteropServices;

namespace NativeEngine;

/// <summary>
/// Device effects use the same SDL library and handles as the engine. Loading its exports directly
/// keeps this usable with public native builds, without changing the generated engine function table.
/// </summary>
[SkipHotload]
internal static unsafe class SdlGamepadEffects
{
	internal const int PacketSize = 47;

	internal static ulong GetSteamHandle( IntPtr gamepad ) => gamepad != IntPtr.Zero && Functions.GetSteamHandle != null
		? Functions.GetSteamHandle( gamepad ) : 0;

	internal static bool IsSupported( IntPtr gamepad ) => gamepad != IntPtr.Zero
		&& Functions.GetVendor != null && Functions.GetProduct != null && Functions.SendEffect != null
		&& Functions.GetVendor( gamepad ) == 0x054c && Functions.GetProduct( gamepad ) == 0x0ce6;

	internal static bool SendResistance( IntPtr gamepad, byte left, byte right )
	{
		if ( !IsSupported( gamepad ) ) return false;

		Span<byte> packet = stackalloc byte[PacketSize];
		WriteResistance( packet, left, right );
		fixed ( byte* data = packet )
			return Functions.SendEffect( gamepad, data, packet.Length ) != 0;
	}

	internal static void WriteResistance( Span<byte> packet, byte left, byte right )
	{
		if ( packet.Length != PacketSize ) throw new ArgumentException( "Expected a 47-byte DualSense effect packet.", nameof( packet ) );
		packet.Clear();
		// DS5EffectsState_t and CyclePS5TriggerEffect in SDL's testcontroller.c.
		// https://github.com/libsdl-org/SDL/blob/release-3.4.14/test/testcontroller.c
		// SDL supplies the USB/Bluetooth framing and checksum. Only enable the two trigger fields.
		packet[0] = 0x0c;
		WriteTrigger( packet.Slice( 10, 11 ), right );
		WriteTrigger( packet.Slice( 21, 11 ), left );
	}

	static void WriteTrigger( Span<byte> effect, byte strength )
	{
		effect[0] = strength == 0 ? (byte)0x05 : (byte)0x01;
		effect[2] = strength;
	}

	// Native SDL bool is one byte, unlike the int used by the generated engine wrappers.
	internal static class Functions
	{
		internal static delegate* unmanaged[Cdecl]< IntPtr, ushort > GetVendor;
		internal static delegate* unmanaged[Cdecl]< IntPtr, ushort > GetProduct;
		internal static delegate* unmanaged[Cdecl]< IntPtr, byte*, int, byte > SendEffect;
		internal static delegate* unmanaged[Cdecl]< IntPtr, ulong > GetSteamHandle;

		static Functions()
		{
			var path = Path.Combine( NetCore.NativeDllPath, Sandbox.Interop.GetNativeLibraryName( "SDL3" ) );
			if ( !NativeLibrary.TryLoad( path, out var library ) ) return;
			if ( !NativeLibrary.TryGetExport( library, "SDL_GetGamepadVendor", out var vendor )
				|| !NativeLibrary.TryGetExport( library, "SDL_GetGamepadProduct", out var product )
				|| !NativeLibrary.TryGetExport( library, "SDL_SendGamepadEffect", out var send ) )
			{
				NativeLibrary.Free( library );
				return;
			}

			// Retain this library reference for the lifetime of the process, like the engine import resolver.
			GetVendor = (delegate* unmanaged[Cdecl]< IntPtr, ushort >)vendor;
			GetProduct = (delegate* unmanaged[Cdecl]< IntPtr, ushort >)product;
			SendEffect = (delegate* unmanaged[Cdecl]< IntPtr, byte*, int, byte >)send;
			if ( NativeLibrary.TryGetExport( library, "SDL_GetGamepadSteamHandle", out var steamHandle ) )
				GetSteamHandle = (delegate* unmanaged[Cdecl]< IntPtr, ulong >)steamHandle;
		}
	}
}
