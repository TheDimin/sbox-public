using System.Reflection;

namespace Editor;

/// <summary>
/// The facets of one glTF element as sheets, like the components of a GameObject, and an
/// "Add Property Group" button that works like "Add Component".
/// </summary>
sealed class GltfFacetList : Widget
{
	readonly GltfElement element;
	readonly GltfEditorSession session;
	readonly Func<Type, bool> include;
	readonly Func<Type, bool> canAdd;
	int shownRevision = -1;

	/// <param name="include">Chooses which facets show. A node shown as a GameObject leaves out its transform facet type.</param>
	/// <param name="canAdd">Chooses which facets "Add Property Group" offers. Defaults to <paramref name="include"/>.</param>
	public GltfFacetList( GltfEditorSession session, GltfElement element, Func<Type, bool> include = null, Func<Type, bool> canAdd = null )
	{
		this.session = session;
		this.element = element;
		this.include = include ?? (_ => true);
		this.canAdd = canAdd ?? this.include;
		Layout = Layout.Column();
		RebuildUI();
	}

	// Undo, redo and renames change the document outside this widget.
	[EditorEvent.Frame]
	void CheckForChanges()
	{
		if ( element.Document.Revision != shownRevision ) RebuildUI();
	}

	void RebuildUI()
	{
		Layout.Clear( true );
		shownRevision = element.Document.Revision;

		foreach ( var facet in GltfFacets.GetPresent( element ).Where( x => include( x.GetType() ) ) )
			Layout.Add( CreateSheet( facet ) );

		var addable = GltfFacets.GetAddable( element ).Where( canAdd ).ToList();
		if ( addable.Count > 0 )
		{
			var row = Layout.AddRow();
			row.Margin = 16;
			row.AddStretchCell();
			var add = row.Add( new Button.Primary( "Add Property Group", "add" ) );
			add.MinimumWidth = 300;
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
			row.AddStretchCell();
		}
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
