using NativeEngine;
using Sandbox.Engine;
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Editor;

/// <summary>
/// A separate X11 client receives raw key/button transitions without competing with Qt or SDL
/// for events on their borrowed windows. No native callbacks or background input log are used.
/// </summary>
[SkipHotload]
internal sealed class LinuxXInputStream : IDisposable
{
	IntPtr display, mask, storage, keyboardState;
	readonly ulong root;
	readonly int opcode;
	bool enabled, polling, disposeRequested, suspendRequested, cursorHidden;
	readonly byte[] text = new byte[64];

	[StructLayout( LayoutKind.Sequential )]
	struct EventMask
	{
		public int Device, Length;
		public IntPtr Mask;
	}

	[StructLayout( LayoutKind.Sequential )]
	struct KeyEvent
	{
		public int Type;
		public ulong Serial;
		public int SendEvent;
		public IntPtr Display;
		public ulong Window, Root, Subwindow, Time;
		public int X, Y, RootX, RootY;
		public uint State, Keycode;
		public int SameScreen;
	}

	[DllImport( "libX11.so.6" )] static extern IntPtr XOpenDisplay( IntPtr name );
	[DllImport( "libX11.so.6" )] static extern int XCloseDisplay( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern ulong XDefaultRootWindow( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern int XQueryExtension( IntPtr display, string name, out int opcode, out int firstEvent, out int firstError );
	[DllImport( "libXi.so.6" )] static extern int XIQueryVersion( IntPtr display, ref int major, ref int minor );
	[DllImport( "libXi.so.6" )] static extern int XISelectEvents( IntPtr display, ulong window, ref EventMask masks, int count );
	[DllImport( "libX11.so.6" )] static extern int XFlush( IntPtr display );
	[DllImport( "libXfixes.so.3" )] static extern void XFixesHideCursor( IntPtr display, ulong window );
	[DllImport( "libXfixes.so.3" )] static extern void XFixesShowCursor( IntPtr display, ulong window );
	[DllImport( "libX11.so.6" )] static extern int XPending( IntPtr display );
	[DllImport( "libX11.so.6" )] static extern int XNextEvent( IntPtr display, IntPtr result );
	[DllImport( "libX11.so.6" )] static extern int XGetEventData( IntPtr display, IntPtr cookie );
	[DllImport( "libX11.so.6" )] static extern void XFreeEventData( IntPtr display, IntPtr cookie );
	[DllImport( "libX11.so.6" )] static extern void XRefreshKeyboardMapping( IntPtr mapping );
	[DllImport( "libX11.so.6" )] static extern ulong XkbKeycodeToKeysym( IntPtr display, byte keycode, int group, int level );
	[DllImport( "libX11.so.6" )] static extern int XkbGetState( IntPtr display, uint device, IntPtr state );
	[DllImport( "libX11.so.6" )] static extern int XLookupString( ref KeyEvent key, [Out] byte[] buffer, int length, out ulong symbol, IntPtr compose );

	internal LinuxXInputStream()
	{
		try
		{
			display = XOpenDisplay( IntPtr.Zero );
			if ( display == IntPtr.Zero ) throw new InvalidOperationException( "Linux editor input requires an X11 display." );
			int major = 2, minor = 2;
			if ( XQueryExtension( display, "XInputExtension", out opcode, out _, out _ ) == 0
				|| XIQueryVersion( display, ref major, ref minor ) != 0 || major < 2 || (major == 2 && minor < 1) )
				throw new InvalidOperationException( "Linux editor input requires XInput 2.1 or newer for raw events during another client's grab." );
			root = XDefaultRootWindow( display );
			mask = Marshal.AllocHGlobal( 4 );
			storage = Marshal.AllocHGlobal( 192 ); // XEvent is 24 native longs on the supported Linux x64 build.
			keyboardState = Marshal.AllocHGlobal( 64 );
		}
		catch { Dispose(); throw; }
	}

	internal void Poll( bool active, Action<int, int, bool> receive )
	{
		if ( display == IntPtr.Zero ) return;
		polling = true;
		try
		{
			if ( enabled != active )
			{
				enabled = active;
				// XIAllMasterDevices; only raw keys/buttons, never raw motion or touch.
				Marshal.WriteInt32( mask, active ? 0x1e000 : 0 );
				var selection = new EventMask { Device = 1, Length = 4, Mask = mask };
				if ( XISelectEvents( display, root, ref selection, 1 ) != 0 )
					throw new InvalidOperationException( "Unable to select Linux editor input events." );
				XFlush( display );
			}

			for ( var count = 0; !disposeRequested && count < 256 && XPending( display ) > 0; count++ )
			{
				XNextEvent( display, storage );
				if ( Marshal.ReadInt32( storage ) == 34 ) { XRefreshKeyboardMapping( storage ); continue; }
				if ( Marshal.ReadInt32( storage ) != 35 || Marshal.ReadInt32( storage, 32 ) != opcode ) continue;
				if ( XGetEventData( display, storage ) == 0 ) continue;
				try
				{
					if ( !active ) continue;
					var kind = Marshal.ReadInt32( storage, 36 );
					if ( kind is < 13 or > 16 ) continue;
					var raw = Marshal.ReadIntPtr( storage, 48 );
					receive( kind, Marshal.ReadInt32( raw, 56 ), (Marshal.ReadInt32( raw, 60 ) & 0x10000) != 0 );
				}
				finally { XFreeEventData( display, storage ); }
			}
		}
		finally
		{
			polling = false;
			if ( disposeRequested ) Dispose();
			else if ( suspendRequested ) { suspendRequested = false; Suspend(); }
		}
	}

	internal void SetCursorHidden( bool hidden )
	{
		if ( display == IntPtr.Zero || cursorHidden == hidden ) return;
		// Qt and SDL both assign cursors to borrowed windows. XFixes visibility belongs
		// to this client, so either toolkit changing the shape cannot reveal capture.
		if ( hidden ) XFixesHideCursor( display, root );
		else XFixesShowCursor( display, root );
		cursorHidden = hidden;
		XFlush( display );
	}

	internal void Suspend()
	{
		if ( polling ) { suspendRequested = true; return; }
		// Drop queued events before a later focus gain; inactive input must never replay.
		Poll( false, static ( _, _, _ ) => { } );
	}

	internal ButtonCode Key( int keycode )
	{
		var group = XkbGetState( display, 0x100, keyboardState ) == 0 ? Marshal.ReadByte( keyboardState ) : 0;
		return TranslateKeySym( XkbKeycodeToKeysym( display, (byte)keycode, group, 0 ) );
	}

	internal string Text( int keycode )
	{
		if ( XkbGetState( display, 0x100, keyboardState ) != 0 ) return "";
		var modifiers = Marshal.ReadByte( keyboardState, 6 );
		if ( (modifiers & (4 | 8 | 64)) != 0 ) return ""; // Control, Alt and Super chords aren't text.
		var key = new KeyEvent { Type = 2, Display = display, Window = root, Root = root, State = modifiers, Keycode = (uint)keycode, SameScreen = 1 };
		var length = XLookupString( ref key, text, text.Length, out _, IntPtr.Zero );
		return length > 0 && text[0] >= 32 ? Encoding.Latin1.GetString( text, 0, length ) : "";
	}

	internal static ButtonCode TranslateKeySym( ulong symbol )
	{
		if ( symbol < 128 ) return KeyTranslation.KeyCodeToButtonCode( (uint)char.ToLowerInvariant( (char)symbol ) );
		if ( symbol is >= 0xffbe and <= 0xffd5 ) return KeyTranslation.StringToButtonCode( $"F{symbol - 0xffbe + 1}" );
		if ( symbol is >= 0xffb0 and <= 0xffb9 ) return KeyTranslation.StringToButtonCode( $"KP_{symbol - 0xffb0}" );
		return KeyTranslation.StringToButtonCode( symbol switch
		{
			0xff08 => "BACKSPACE", 0xff09 or 0xfe20 => "TAB", 0xff0d => "ENTER", 0xff1b => "ESCAPE",
			0xff50 => "HOME", 0xff51 => "LEFTARROW", 0xff52 => "UPARROW", 0xff53 => "RIGHTARROW", 0xff54 => "DOWNARROW",
			0xff55 => "PGUP", 0xff56 => "PGDN", 0xff57 => "END", 0xff63 => "INS", 0xffff => "DEL",
			0xff13 => "PAUSE", 0xff14 => "SCROLLLOCK", 0xff7f => "NUMLOCK", 0xffe5 => "CAPSLOCK",
			0xffe1 => "SHIFT", 0xffe2 => "RSHIFT", 0xffe3 => "CTRL", 0xffe4 => "RCTRL",
			0xffe9 => "ALT", 0xffea or 0xfe03 => "RALT", 0xffeb => "LWIN", 0xffec => "RWIN", 0xff67 => "APP",
			0xff95 => "KP_7", 0xff96 => "KP_4", 0xff97 => "KP_8", 0xff98 => "KP_6", 0xff99 => "KP_2",
			0xff9a => "KP_9", 0xff9b => "KP_3", 0xff9c => "KP_1", 0xff9d => "KP_5", 0xff9e => "KP_0", 0xff9f => "KP_DEL",
			0xff8d => "KP_ENTER", 0xffaa => "KP_MULTIPLY", 0xffab => "KP_PLUS", 0xffad => "KP_MINUS", 0xffae => "KP_DEL", 0xffaf => "KP_DIVIDE",
			_ => ""
		} );
	}

	public void Dispose()
	{
		if ( polling ) { disposeRequested = true; return; }
		SetCursorHidden( false );
		if ( display != IntPtr.Zero ) XCloseDisplay( display );
		if ( mask != IntPtr.Zero ) Marshal.FreeHGlobal( mask );
		if ( storage != IntPtr.Zero ) Marshal.FreeHGlobal( storage );
		if ( keyboardState != IntPtr.Zero ) Marshal.FreeHGlobal( keyboardState );
		display = mask = storage = keyboardState = IntPtr.Zero;
	}
}
