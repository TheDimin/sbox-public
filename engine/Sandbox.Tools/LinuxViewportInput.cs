using NativeEngine;
using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Editor;

/// <summary>
/// Opt-in Linux editor input: XInput2 owns key/button delivery and Qt owns viewport capture.
/// SDL continues to own standalone and panel windows and controller input.
/// </summary>
[SkipHotload]
internal static class LinuxViewportInput
{
	static SceneRenderingWidget viewport, captured;
	static LinuxXInputStream stream;
	static CursorShape previousCursor;
	static Vector2 previousPosition, captureCenter, lastAbsolute;
	static bool skipMotion, focusingFromClick;
	static readonly Dictionary<int, ButtonCode> heldKeys = new();
	static readonly HashSet<ButtonCode> heldButtons = new();

	static bool Handles( SceneRenderingWidget widget ) => WindowInput.UsesQtInput && GameMode.IsPlayWidget( widget );

	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget9grabMouseEv" )]
	static extern void GrabMouse( IntPtr widget );
	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget12releaseMouseEv" )]
	static extern void ReleaseMouse( IntPtr widget );
	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZN7QWidget12mouseGrabberEv" )]
	static extern IntPtr MouseGrabber();

	internal static void Start( SceneRenderingWidget widget )
	{
		if ( !WindowInput.UsesQtInput ) return;
		Stop();
		stream = new LinuxXInputStream();
		viewport = widget;
		lastAbsolute = widget.FromScreen( Application.CursorPosition );
		widget.OnPreFrame += Poll;
	}

	internal static void Stop()
	{
		SetCapture( false );
		if ( viewport.IsValid() ) viewport.OnPreFrame -= Poll;
		stream?.Dispose();
		stream = null;
		viewport = null;
		ReleaseHeldInput();
	}

	internal static void FocusChanged( bool focused )
	{
		if ( !WindowInput.UsesQtInput ) return;
		ReleaseHeldInput();
		if ( !focused || !focusingFromClick ) stream?.Suspend();
	}

	static void ReleaseHeldInput()
	{
		var keys = heldKeys.Values.ToArray();
		var buttons = heldButtons.ToArray();
		heldKeys.Clear();
		heldButtons.Clear();
		foreach ( var code in keys ) InputRouter.OnKey( code, code, false, false );
		foreach ( var code in buttons ) InputRouter.OnMouseButton( code, false );
	}

	static void Poll()
	{
		if ( !viewport.IsValid() || stream is null ) return;
		var active = WindowInput.IsAppActive() && viewport.IsActiveWindow;
		var input = stream;
		if ( !active || !viewport.IsFocused ) ReleaseHeldInput();
		// Listen only while this editor is active. A raw click inside the viewport can restore
		// Qt focus even when the native borrowed-window event path swallowed its Qt event.
		if ( active && viewport.IsFocused ) UpdatePointer();
		input.Poll( active, Receive );
	}

	static void UpdatePointer()
	{

		if ( captured == viewport )
		{
			if ( skipMotion ) { skipMotion = false; CenterMouse(); return; }
			var delta = (Application.CursorPosition - captureCenter) * viewport.DpiScale;
			if ( delta.LengthSquared < 0.01f ) return;
			InputRouter.OnMouseMotion( delta.x, delta.y );
			CenterMouse();
		}
		else
		{
			var position = viewport.FromScreen( Application.CursorPosition );
			var delta = (position - lastAbsolute) * viewport.DpiScale;
			lastAbsolute = position;
			InputRouter.OnMousePositionChange( position.x * viewport.DpiScale, position.y * viewport.DpiScale, delta.x, delta.y );
		}
	}

	static void Receive( int kind, int detail, bool repeated )
	{
		if ( !viewport.IsValid() ) return;
		var down = kind is 13 or 15;
		if ( kind is 13 or 14 )
		{
			if ( !viewport.IsFocused ) return;
			var code = down ? stream.Key( detail ) : heldKeys.GetValueOrDefault( detail, stream.Key( detail ) );
			if ( code == ButtonCode.BUTTON_CODE_INVALID ) return;
			var repeat = down && (repeated || heldKeys.ContainsKey( detail ));
			if ( down ) heldKeys[detail] = code;
			else heldKeys.Remove( detail );
			InputRouter.OnKey( code, code, down, repeat );
			if ( down && stream is not null && InputRouter.KeyboardFocusPanel is not null )
			{
				var text = stream.Text( detail );
				if ( text.Length > 0 ) InputRouter.OnText( text );
			}
			return;
		}

		var inside = viewport.LocalRect.IsInside( viewport.FromScreen( Application.CursorPosition ) );
		if ( captured is null && !inside && down ) return;
		if ( down && inside && !viewport.IsFocused )
		{
			focusingFromClick = true;
			try { viewport.Focus( activateWindow: false ); }
			finally { focusingFromClick = false; }
		}
		if ( !viewport.IsFocused ) return;
		if ( detail is >= 4 and <= 7 )
		{
			if ( down ) InputRouter.OnMouseWheel( detail == 6 ? -1 : detail == 7 ? 1 : 0, detail == 4 ? 1 : detail == 5 ? -1 : 0 );
			return;
		}
		var button = detail switch
		{
			1 => ButtonCode.MouseLeft, 2 => ButtonCode.MouseMiddle, 3 => ButtonCode.MouseRight,
			8 => ButtonCode.MouseBack, 9 => ButtonCode.MouseForward, _ => ButtonCode.BUTTON_CODE_INVALID
		};
		if ( button == ButtonCode.BUTTON_CODE_INVALID ) return;
		if ( down ) heldButtons.Add( button );
		else heldButtons.Remove( button );
		InputRouter.OnMouseButton( button, down );
	}

	internal static void SetCapture( bool value )
	{
		if ( !WindowInput.UsesQtInput ) return;
		var widget = GameMode.PlayWidget;
		if ( !value && widget.IsValid() && (!WindowInput.IsAppActive() || !widget.IsActiveWindow || !widget.IsFocused) ) FocusChanged( false );
		stream?.SetCursorHidden( value && widget.IsValid() );
		if ( value && widget.IsValid() && captured == widget )
		{
			if ( widget.Cursor != CursorShape.Blank ) widget.Cursor = CursorShape.Blank;
			if ( MouseGrabber() != (IntPtr)widget._widget )
			{
				GrabMouse( widget._widget );
				skipMotion = true;
				CenterMouse();
			}
			return;
		}
		if ( captured.IsValid() )
		{
			if ( MouseGrabber() == (IntPtr)captured._widget ) ReleaseMouse( captured._widget );
			captured.Cursor = previousCursor;
			if ( captured.IsActiveWindow ) Application.CursorPosition = previousPosition;
		}
		captured = null;
		if ( !value || !widget.IsValid() ) return;
		captured = widget;
		previousCursor = widget.Cursor;
		previousPosition = Application.CursorPosition;
		widget.Cursor = CursorShape.Blank;
		GrabMouse( widget._widget );
		skipMotion = true;
		CenterMouse();
	}

	static void CenterMouse()
	{
		var center = new Vector2( (int)(captured.Size.x * 0.5f), (int)(captured.Size.y * 0.5f) );
		captureCenter = captured.ToScreen( center );
		Application.CursorPosition = captureCenter;
	}

	internal static bool SetCursorPosition( int x, int y )
	{
		if ( !viewport.IsValid() ) return false;
		if ( captured is not null ) return true; // Relative-mode cursor positions are virtual.
		lastAbsolute = new Vector2( x, y ) / viewport.DpiScale;
		Application.CursorPosition = viewport.ToScreen( lastAbsolute );
		return true;
	}

	internal static bool OnButton( SceneRenderingWidget widget, MouseEvent e, bool down )
	{
		if ( !Handles( widget ) ) return false;
		if ( down ) widget.Focus( activateWindow: false );
		e.Accepted = true;
		return true; // The dedicated XInput2 stream delivers this transition once.
	}

	internal static bool OnMotion( SceneRenderingWidget widget, MouseEvent e )
	{
		if ( !Handles( widget ) ) return false;
		e.Accepted = true;
		return true;
	}

	internal static bool OnWheel( SceneRenderingWidget widget, WheelEvent e )
	{
		if ( !Handles( widget ) ) return false;
		e.Accept();
		return true;
	}
}
