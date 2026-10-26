using System;
using System.Runtime.InteropServices;
using Sandbox.UI;

namespace Editor;

internal class TitleBar : Widget
{
	private Widget Window { get; init; }
	private Button IconWidget { get; set; }
	private Label TitleLabel { get; set; }
	private Widget Grabber { get; set; }
	private LinuxWindowResizeHandle[] ResizeHandles;
	private Margin ResizeBaseMargins;
	private WindowControlButton MinimizeButton { get; set; }
	private WindowControlButton MaximizeButton { get; set; }
	private WindowControlButton CloseButton { get; set; }
	public TitleBarButtons TitleBarButtons { get; init; }

	public MenuBar MenuBar { get; private init; }

	private const int IconSize = 18;

	public Pixmap IconPixmap
	{
		set
		{
			IconWidget.SetIcon( value.Resize( IconSize ) );
			Update();
		}
	}

	public string Title
	{
		get => TitleLabel.Text;
		set
		{
			TitleLabel.Text = value;
			Update();
		}
	}

	public TitleBar( Widget window, bool isMainWindow = false ) : base( window )
	{
		Window = window;

		Name = "TitleBar";
		MenuBar = new MenuBar( this );
		TitleBarButtons = new TitleBarButtons();

		if ( Window is Window w )
		{
			w.MenuBar = MenuBar;
		}

		Layout = Layout.Row();
		Layout.Alignment = TextFlag.LeftCenter;

		IconWidget = new Button( this );
		IconWidget.Cursor = CursorShape.Arrow;
		IconWidget.OnPaintOverride = PaintIcon;
		IconWidget.FixedSize = new Vector2( 32, 32 );

		//
		// Left
		//

		// macOS draws its traffic lights here, leave room for them
		if ( OperatingSystem.IsMacOS() )
		{
			Layout.AddSpacingCell( 72 );
			IconWidget.Visible = false;
		}

		Layout.Add( IconWidget, 0 );
		Layout.Add( MenuBar, 0 );

		// Invisible label - just used as a middleman for manual painting
		TitleLabel = new Label();
		TitleLabel.FixedSize = new Vector2( 0 );

		//
		// Center
		//
		var center = Layout.AddRow( 1 );
		center.Alignment = TextFlag.Center;

		Grabber = center.Add( OperatingSystem.IsLinux() && window is EditorMainWindow
			? new LinuxTitleBarGrabber( window, ToggleMaximized ) : new Widget(), 1 );
		Grabber.Layout = Layout.Row();
		Grabber.Layout.Alignment = TextFlag.Center;
		Grabber.FixedHeight = 35;

		//
		// Right
		//
		var right = Layout.AddRow( 0 );
		right.Alignment = TextFlag.RightCenter;
		right.Add( TitleBarButtons );

		MinimizeButton = new WindowControlButton( WindowControlIcon.Minimize, Window.MakeMinimized );
		right.Add( MinimizeButton, 0 );

		MaximizeButton = new WindowControlButton( WindowControlIcon.Maximize, ToggleMaximized );
		right.Add( MaximizeButton, 0 );

		CloseButton = new WindowControlButton( WindowControlIcon.Close, Window.Close );
		CloseButton.HighlightColor = Color.Parse( "#c42b1c" ).Value;
		right.Add( CloseButton, 0 );

		// macOS draws its own traffic lights, hide ours
		if ( OperatingSystem.IsMacOS() )
			MinimizeButton.Visible = MaximizeButton.Visible = CloseButton.Visible = false;
	}

	private bool PaintIcon()
	{
		Paint.ClearPen();
		Paint.ClearBrush();

		var icon = IconWidget.GetIcon();

		if ( icon == null )
		{
			IconWidget.Visible = icon != null;
			IconWidget.FixedSize = new Vector2( 0 );
		}
		else
		{
			var rect = IconWidget.LocalRect.Contain( IconSize );
			Paint.Draw( rect, IconWidget.GetIcon() );
		}

		return true;
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();
		Paint.ClearBrush();

		if ( string.IsNullOrEmpty( Title ) )
			return;

		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( LocalRect, Title, TextFlag.Center );
	}

	private void UpdateMaximizeIcon()
	{
		MaximizeButton.Icon = Window.IsMaximized ? WindowControlIcon.Restore : WindowControlIcon.Maximize;
	}

	private void ToggleMaximized()
	{
		if ( Window.IsMaximized )
			Window.MakeWindowed();
		else
			Window.MakeMaximized();
	}

	public override void Update()
	{
		base.Update();
		UpdateMaximizeIcon();
		UpdateResizeHandles();
	}

	[Event( "refresh" )]
	[Event( "project.settings.saved" )]
	public void OnHotload()
	{
		if ( Window is EditorMainWindow mw )
		{
			TitleBarButtons.Layout.Clear( true );
			EditorEvent.Run( "editor.titlebar.buttons.build", TitleBarButtons );
			EditorEvent.Run( "tools.titlebar.build", TitleBarButtons.Layout );

			SetTitleBarWidgets( mw._nativeWindow );
		}
	}

	internal void SetTitleBarWidgets( Native.CFramelessMainWindow nativeWindow )
	{
		nativeWindow.SetTitleBarWidgets(
			IconWidget._button,
			TitleLabel._label,
			MenuBar._menubar,
			Grabber._widget,
			MinimizeButton._widget,
			MaximizeButton._widget,
			CloseButton._widget
		);

		if ( OperatingSystem.IsLinux() && Window is EditorMainWindow )
		{
			// Linux otherwise shows both the compositor decoration and this title bar.
			// Preserve visibility when Qt recreates the native window for new flags.
			if ( (Window.WindowFlags & WindowFlags.FramelessWindowHint) == 0 )
			{
				var visible = Window.Visible;
				Window.WindowFlags |= WindowFlags.FramelessWindowHint;
				Window.Visible = visible;
			}
			MinimizeButton.FixedSize = MaximizeButton.FixedSize = CloseButton.FixedSize = new Vector2( 40, 32 );
			MinimizeButton.Visible = MaximizeButton.Visible = CloseButton.Visible = true;
			if ( ResizeHandles is null )
			{
				ResizeBaseMargins = Window.ContentMargins;
				ResizeHandles = new[]
				{
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Top ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Bottom ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Left ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Right ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Top | LinuxWindowEdge.Left ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Top | LinuxWindowEdge.Right ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Bottom | LinuxWindowEdge.Left ),
					new LinuxWindowResizeHandle( Window, LinuxWindowEdge.Bottom | LinuxWindowEdge.Right )
				};
			}
			UpdateResizeHandles();
		}
	}
	void UpdateResizeHandles()
	{
		if ( ResizeHandles is null ) return;
		var enabled = !Window.IsMaximized && !Window._widget.isFullScreen();
		var border = enabled ? LinuxWindowResizeHandle.Border : 0;
		var margins = new Margin( ResizeBaseMargins.Left + border, ResizeBaseMargins.Top + border,
			ResizeBaseMargins.Right + border, ResizeBaseMargins.Bottom + border );
		// Reserve the hit area rather than covering menus, docks or the game viewport.
		if ( !Window.ContentMargins.Equals( margins ) ) Window.ContentMargins = margins;
		foreach ( var handle in ResizeHandles ) handle.UpdateBounds( enabled );
	}
}

/// <summary>
/// A list of title bar buttons, at the top right of a window.
/// </summary>
public class TitleBarButtons : Widget
{
	public TitleBarButtons()
	{
		Layout = Layout.Row();
	}

	/// <summary>
	/// Adds a button to the title bar.
	/// </summary>
	/// <param name="icon"></param>
	/// <param name="onClick"></param>
	/// <returns></returns>
	public Widget AddButton( string icon, Action onClick )
	{
		return Layout.Add( new TitleBarButton( icon, onClick ) );
	}

	/// <summary>
	/// Adds a toggle button to the title bar.
	/// </summary>
	/// <param name="icon"></param>
	/// <param name="onSet"></param>
	/// <param name="initialValue"></param>
	/// <returns></returns>
	public Widget AddToggleButton( string icon, Action<bool> onSet, bool initialValue = false )
	{
		var b = Layout.Add( new TitleBarToggle( icon, onSet ) );
		b.Value = initialValue;

		return b;
	}

	/// <inheritdoc cref="AddToggleButton(string, Action{bool}, bool)"/>
	public Widget AddToggleButton( Pixmap icon, Action<bool> onSet, bool initialValue = false )
	{
		var b = Layout.Add( new TitleBarToggle( icon, onSet ) );
		b.Value = initialValue;

		return b;
	}

	/// <summary>
	/// Adds a custom widget to the title bar.
	/// </summary>
	/// <param name="widget"></param>
	/// <returns></returns>
	public Widget Add( Widget widget )
	{
		return Layout.Add( widget );
	}
}

internal class TitleBarButton : Widget
{
	private Action _onClick;

	public string Icon { get; set; }
	public Color HighlightColor { get; set; } = Theme.Text.WithAlpha( 0.1f );

	public TitleBarButton( string icon, Action onClick = null )
	{
		_onClick = onClick;

		Icon = icon;
		FixedSize = new Vector2( 40, 32 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		_onClick?.Invoke();
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver && Enabled )
		{
			Paint.SetBrush( HighlightColor );
			Paint.DrawRect( LocalRect );
		}

		Paint.ClearBrush();
		Paint.SetPen( Theme.Text );

		if ( !Enabled )
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );

		Paint.DrawIcon( LocalRect, Icon, 13.0f );
	}
}


internal class TitleBarToggle : Widget
{
	private Action<bool> _onSet;

	public string Icon { get; set; }
	public Pixmap PixmapIcon { get; set; }
	public Color HighlightColor { get; set; } = Theme.Text.WithAlpha( 0.1f );

	public bool Value { get; set; }

	public TitleBarToggle( string icon, Action<bool> onSet )
	{
		Icon = icon;
		Init( onSet );
	}

	public TitleBarToggle( Pixmap icon, Action<bool> onSet )
	{
		PixmapIcon = icon;
		Init( onSet );
	}

	private void Init( Action<bool> onSet )
	{
		_onSet = onSet;
		FixedSize = new Vector2( 40, 32 );
	}

	protected override void OnMouseClick( MouseEvent e )
	{
		Value = !Value;

		_onSet?.Invoke( Value );
	}

	protected override void OnPaint()
	{
		Paint.ClearPen();

		if ( Paint.HasMouseOver && Enabled )
		{
			Paint.SetBrush( HighlightColor );
			Paint.DrawRect( LocalRect );
		}

		Paint.ClearBrush();
		Paint.SetPen( Theme.Text );

		if ( !Enabled )
			Paint.SetPen( Theme.Text.WithAlpha( 0.5f ) );

		if ( PixmapIcon is not null )
		{
			Paint.Draw( LocalRect, PixmapIcon );
		}
		else
		{
			Paint.DrawIcon( LocalRect, Icon, 13.0f );
		}

		if ( Value )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.Primary );

			var borderHeight = 2.0f;
			var r = LocalRect;
			r.Top = r.Bottom - borderHeight;

			Paint.DrawRect( r );
		}
	}
}

/// <summary>
/// Delegate Linux title-bar dragging to the compositor, preserving snapping and restore-on-drag.
/// </summary>
internal sealed class LinuxTitleBarGrabber : Widget
{
	readonly Widget window;
	readonly Action toggleMaximized;


	internal LinuxTitleBarGrabber( Widget window, Action toggleMaximized )
	{
		this.window = window;
		this.toggleMaximized = toggleMaximized;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			if ( LinuxWindowOperations.Move( window ) )
			{
				e.Accepted = true;
				return;
			}
		}
		base.OnMousePress( e );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( e.LeftMouseButton )
		{
			toggleMaximized();
			e.Accepted = true;
			return;
		}
		base.OnDoubleClick( e );
	}
}

[Flags]
internal enum LinuxWindowEdge
{
	Top = 1, Left = 2, Right = 4, Bottom = 8 // Qt::Edge values, combined for corners.
}

/// <summary>
/// A logical-pixel border hit area that delegates resizing to the compositor.
/// </summary>
internal sealed class LinuxWindowResizeHandle : Widget
{
	internal const int Border = 6;
	readonly Widget window;
	readonly LinuxWindowEdge edges;

	internal LinuxWindowResizeHandle( Widget window, LinuxWindowEdge edges ) : base( window )
	{
		this.window = window;
		this.edges = edges;
		FocusMode = FocusMode.None;
		Cursor = edges switch
		{
			LinuxWindowEdge.Top or LinuxWindowEdge.Bottom => CursorShape.SizeV,
			LinuxWindowEdge.Left or LinuxWindowEdge.Right => CursorShape.SizeH,
			(LinuxWindowEdge.Top | LinuxWindowEdge.Left) or (LinuxWindowEdge.Bottom | LinuxWindowEdge.Right) => CursorShape.SizeFDiag,
			_ => CursorShape.SizeBDiag
		};
	}

	internal void UpdateBounds( bool enabled )
	{
		Visible = enabled;
		if ( !enabled ) return;
		var size = window.Size;
		var horizontal = (edges & (LinuxWindowEdge.Left | LinuxWindowEdge.Right)) != 0;
		var vertical = (edges & (LinuxWindowEdge.Top | LinuxWindowEdge.Bottom)) != 0;
		Position = new Vector2( edges.HasFlag( LinuxWindowEdge.Right ) ? size.x - Border : horizontal ? 0 : Border,
			edges.HasFlag( LinuxWindowEdge.Bottom ) ? size.y - Border : vertical ? 0 : Border );
		Size = new Vector2( horizontal ? Border : Math.Max( 0, size.x - 2 * Border ),
			vertical ? Border : Math.Max( 0, size.y - 2 * Border ) );
		Raise();
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && !window.IsMaximized && !window._widget.isFullScreen()
			&& LinuxWindowOperations.Resize( window, edges ) )
		{
			e.Accepted = true;
			return;
		}
		base.OnMousePress( e );
	}
}

internal static class LinuxWindowOperations
{
	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZNK7QWidget12windowHandleEv" )]
	static extern IntPtr WindowHandle( IntPtr widget );

	[DllImport( "libQt5Gui.so.5", EntryPoint = "_ZN7QWindow15startSystemMoveEv" )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool StartSystemMove( IntPtr window );

	[DllImport( "libQt5Gui.so.5", EntryPoint = "_ZN7QWindow17startSystemResizeE6QFlagsIN2Qt4EdgeEE" )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool StartSystemResize( IntPtr window, LinuxWindowEdge edges );

	internal static bool Move( Widget window )
	{
		var handle = WindowHandle( window._widget );
		return handle != IntPtr.Zero && StartSystemMove( handle );
	}

	internal static bool Resize( Widget window, LinuxWindowEdge edges )
	{
		var handle = WindowHandle( window._widget );
		return handle != IntPtr.Zero && StartSystemResize( handle, edges );
	}
}
