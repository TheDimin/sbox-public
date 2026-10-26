using System;
using System.Runtime.InteropServices;

namespace Editor;

[Flags]
internal enum LinuxWindowEdge
{
	None = 0, Top = 1, Left = 2, Right = 4, Bottom = 8 // Qt::Edge values.
}

/// <summary>
/// A caption or border grip. The compositor owns the move/resize operation after mouse-down.
/// </summary>
internal sealed class LinuxWindowGrip : Widget
{
	internal const int Border = 6;
	readonly Widget window;
	readonly LinuxWindowEdge edges;
	readonly Action doubleClicked;

	internal LinuxWindowGrip( Widget window, LinuxWindowEdge edges, Action doubleClicked = null ) : base( window )
	{
		this.window = window;
		this.edges = edges;
		this.doubleClicked = doubleClicked;
		FocusMode = FocusMode.None;
		Cursor = edges switch
		{
			LinuxWindowEdge.None => CursorShape.Arrow,
			LinuxWindowEdge.Top or LinuxWindowEdge.Bottom => CursorShape.SizeV,
			LinuxWindowEdge.Left or LinuxWindowEdge.Right => CursorShape.SizeH,
			(LinuxWindowEdge.Top | LinuxWindowEdge.Left) or (LinuxWindowEdge.Bottom | LinuxWindowEdge.Right) => CursorShape.SizeFDiag,
			_ => CursorShape.SizeBDiag
		};
	}

	internal void UpdateBounds( Vector2 size, bool enabled )
	{
		Visible = enabled;
		if ( !enabled ) return;
		var horizontal = (edges & (LinuxWindowEdge.Left | LinuxWindowEdge.Right)) != 0;
		var vertical = (edges & (LinuxWindowEdge.Top | LinuxWindowEdge.Bottom)) != 0;
		Position = new Vector2( (edges & LinuxWindowEdge.Right) != 0 ? size.x - Border : horizontal ? 0 : Border,
			(edges & LinuxWindowEdge.Bottom) != 0 ? size.y - Border : vertical ? 0 : Border );
		Size = new Vector2( horizontal ? Border : Math.Max( 0, size.x - 2 * Border ),
			vertical ? Border : Math.Max( 0, size.y - 2 * Border ) );
	}

	bool TryStartOperation()
	{
		if ( edges != LinuxWindowEdge.None && (window.IsMaximized || window._widget.isFullScreen()) ) return false;
		var handle = WindowHandle( window._widget );
		if ( handle == IntPtr.Zero ) return false;
		return edges == LinuxWindowEdge.None ? StartSystemMove( handle ) : StartSystemResize( handle, edges );
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton && TryStartOperation() )
		{
			e.Accepted = true;
			return;
		}
		base.OnMousePress( e );
	}

	protected override void OnDoubleClick( MouseEvent e )
	{
		if ( e.LeftMouseButton && doubleClicked is not null )
		{
			doubleClicked();
			e.Accepted = true;
			return;
		}
		base.OnDoubleClick( e );
	}

	[DllImport( "libQt5Widgets.so.5", EntryPoint = "_ZNK7QWidget12windowHandleEv" )]
	static extern IntPtr WindowHandle( IntPtr widget );

	[DllImport( "libQt5Gui.so.5", EntryPoint = "_ZN7QWindow15startSystemMoveEv" )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool StartSystemMove( IntPtr window );

	[DllImport( "libQt5Gui.so.5", EntryPoint = "_ZN7QWindow17startSystemResizeE6QFlagsIN2Qt4EdgeEE" )]
	[return: MarshalAs( UnmanagedType.I1 )]
	static extern bool StartSystemResize( IntPtr window, LinuxWindowEdge edges );
}
