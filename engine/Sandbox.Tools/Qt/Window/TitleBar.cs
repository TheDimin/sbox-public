using System;

namespace Editor;

internal class TitleBar : Widget
{
	private Widget Window { get; init; }
	private Button IconWidget { get; set; }
	private Label TitleLabel { get; set; }
	private Widget Grabber { get; set; }
	private LinuxWindowGrip[] ResizeHandles;
	private int ResizeBorder = -1;
	private Vector2 ResizeSize;
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
			? new LinuxWindowGrip( window, LinuxWindowEdge.None, ToggleMaximized ) : new Widget(), 1 );
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
		// A maximized restore can change state without a resize event.
		UpdateResizeHandles();
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
			MinimizeButton.Visible = MaximizeButton.Visible = CloseButton.Visible = true;
			if ( ResizeHandles is null )
			{
				ResizeHandles = new[]
				{
					new LinuxWindowGrip( Window, LinuxWindowEdge.Top ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Bottom ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Left ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Right ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Top | LinuxWindowEdge.Left ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Top | LinuxWindowEdge.Right ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Bottom | LinuxWindowEdge.Left ),
					new LinuxWindowGrip( Window, LinuxWindowEdge.Bottom | LinuxWindowEdge.Right )
				};
			}
			ResizeBorder = -1;
			UpdateResizeHandles();
		}
	}

	internal void UpdateResizeHandles()
	{
		if ( ResizeHandles is null ) return;
		var enabled = !Window.IsMaximized && !Window._widget.isFullScreen();
		var border = enabled ? LinuxWindowGrip.Border : 0;
		var size = Window.Size;
		if ( ResizeBorder == border && ResizeSize == size ) return;
		ResizeSize = size;
		// This client frame owns its border; change padding only when its state changes.
		if ( ResizeBorder != border )
		{
			ResizeBorder = border;
			Window.ContentMargins = border;
			UpdateMaximizeIcon();
		}
		foreach ( var handle in ResizeHandles ) handle.UpdateBounds( size, enabled );
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
