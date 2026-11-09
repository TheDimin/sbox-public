using System.Reflection;

namespace Editor.Inspectors;

/// <summary>
/// Inspects one glTF element. Each facet shows as a sheet, like a component on a GameObject,
/// and "Add Property Group" works like "Add Component".
/// </summary>
[Inspector( typeof( GltfElement ) )]
public sealed class GltfElementInspector : InspectorWidget
{
	readonly GltfElement element;
	readonly GltfEditorSession session;
	int shownRevision = -1;

	public GltfElementInspector( SerializedObject so ) : base( so )
	{
		element = so.Targets.FirstOrDefault() as GltfElement;
		session = SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == element?.Document );
		Layout = Layout.Column();
		RebuildUI();
	}

	// Undo, redo and hierarchy renames change the document outside this widget.
	[EditorEvent.Frame]
	void CheckForChanges()
	{
		if ( session?.Document == element?.Document && element.Document.Revision != shownRevision )
			RebuildUI();
	}

	void RebuildUI()
	{
		Layout.Clear( true );
		if ( element is null || session is null || session.Document != element.Document )
		{
			Layout.Add( new Label( "This glTF element is no longer open." ) );
			return;
		}
		shownRevision = element.Document.Revision;

		var header = Layout.AddRow();
		header.Margin = 8;
		header.Spacing = 6;
		header.Add( new IconButton( element.Kind.Icon ) { Background = Color.Transparent, IconSize = 16, TransparentForMouseEvents = true } );
		var name = header.Add( new LineEdit { Text = element.Name, PlaceholderText = $"{element.Kind.Singular} {element.Index}" }, 1 );
		name.EditingFinished += () => Commit( () => session.Rename( element, name.Text ) );
		header.Add( new Label( $"{element.Kind.Singular} {element.Index}" ) { Color = Theme.TextControl.WithAlpha( 0.5f ) } );

		if ( GltfImagePreview.Get( element, 256 ) is { } picture )
		{
			var image = Layout.Add( new Widget { FixedHeight = 200 } );
			image.OnPaintOverride = () =>
			{
				var area = image.LocalRect.Shrink( 8 );
				var scale = Math.Min( area.Width / picture.Width, area.Height / picture.Height );
				var size = new Vector2( picture.Width, picture.Height ) * scale;
				Paint.Draw( new Rect( area.Center - size * 0.5f, size ), picture );
				return true;
			};
		}

		var scroller = Layout.Add( new ScrollArea( this ), 1 );
		scroller.Canvas = new Widget( scroller ) { HorizontalSizeMode = SizeMode.Flexible, Layout = Layout.Column() };

		foreach ( var facet in GltfFacets.GetPresent( element ) )
			scroller.Canvas.Layout.Add( CreateSheet( facet ) );

		var addable = GltfFacets.GetAddable( element ).ToList();
		if ( addable.Count > 0 )
		{
			var add = scroller.Canvas.Layout.Add( new Button.Primary( "Add Property Group", "add" ) );
			add.Clicked = () =>
			{
				var menu = new ContextMenu( this );
				foreach ( var type in addable )
				{
					var display = DisplayInfo.ForType( type );
					var option = menu.AddOption( display.Name, display.Icon ?? "extension", () => Commit( () => session.AddFacet( element, type ) ) );
					option.ToolTip = type.GetCustomAttribute<GltfFacetForAttribute>()?.Path;
				}
				menu.OpenAtCursor( false );
			};
		}
		scroller.Canvas.Layout.AddStretchCell();
	}

	ComponentSheet CreateSheet( GltfFacet facet )
	{
		var serialized = facet.GetSerialized();
		serialized.OnPropertyFinishEdit += property => Commit( () => session.SetFacetProperty( facet, property.Name ), rebuild: false );

		// A stable id per element and facet keeps the expanded state of the sheet.
		var id = new Guid( System.Security.Cryptography.MD5.HashData( System.Text.Encoding.UTF8.GetBytes( $"{session.SourcePath}/{element.Kind.Path}/{element.Index}/{facet.GetType().FullName}" ) ) );
		var sheet = new ComponentSheet( id, serialized );
		sheet.Header.IsDraggable = false;
		if ( facet.IsExtension ) sheet.Header.Color = Theme.Green;
		if ( facet.IsRemovable )
			sheet.Header.ContextMenu += menu => menu.AddOption( "Remove", "delete", () => Commit( () => session.RemoveFacet( facet ) ) );
		return sheet;
	}

	void Commit( Action edit, bool rebuild = true )
	{
		edit();
		if ( rebuild ) RebuildUI();
		else shownRevision = element.Document.Revision;
	}
}
