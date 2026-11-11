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
	string shownKey;

	public GltfElementInspector( SerializedObject so ) : base( so )
	{
		element = so.Targets.FirstOrDefault() as GltfElement;
		session = SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == element?.Document );
		Layout = Layout.Column();
		RebuildUI();
	}

	// Undo, redo and hierarchy renames change the document outside this widget. Property groups refresh
	// themselves, so only changes to what this widget shows (name, shader, material slots) rebuild it.
	[EditorEvent.Frame]
	void CheckForChanges()
	{
		if ( session?.Document == element?.Document && Key() != shownKey )
			RebuildUI();
	}

	string Key() => element is null ? null : string.Join( '|', element.Name,
		element.Kind == GltfElementKind.Material ? GltfMaterialShaders.Current( element ).Path : "",
		string.Join( ',', element.PrimitiveMaterials ),
		element.Kind == GltfElementKind.Texture ? $"{element.Document.GetTextureImage( element )?.Index}:{element.Document.GetTextureSampling( element )}" : "",
		element.Document.Count( GltfElementKind.Material ) );

	void RebuildUI()
	{
		Layout.Clear( true );
		if ( element is null || session is null || session.Document != element.Document )
		{
			Layout.Add( new Label( "This glTF element is no longer open." ) );
			return;
		}
		shownKey = Key();

		var header = Layout.AddRow();
		header.Margin = 8;
		header.Spacing = 6;
		header.Add( new IconButton( element.Kind.Icon ) { Background = Color.Transparent, IconSize = 16, TransparentForMouseEvents = true } );
		var name = header.Add( new LineEdit { Text = element.Name, PlaceholderText = $"{element.Kind.Singular} {element.Index}" }, 1 );
		name.EditingFinished += () => Commit( () => session.Rename( element, name.Text ) );
		header.Add( new Label( $"{element.Kind.Singular} {element.Index}" ) { Color = Theme.TextControl.WithAlpha( 0.5f ) } );

		if ( element.Kind == GltfElementKind.Material )
			Layout.Add( new GltfMaterialPreview( element ) { FixedHeight = 160 } );

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

		if ( element.Kind == GltfElementKind.Mesh )
			scroller.Canvas.Layout.Add( new GltfPrimitiveList( session, element, edit => Commit( edit ) ) );
		if ( element.Kind == GltfElementKind.Material )
		{
			scroller.Canvas.Layout.Add( CreateShaderRow() );
			var shader = GltfMaterialShaders.Current( element ).Path;
			// The shader row shows the selector facets. A selector with data (diffuse transmission) still shows its sheet.
			// Only groups the shader renders can be added; adding an extension group adds the extension to the file.
			scroller.Canvas.Layout.Add( new GltfFacetList( session, element,
				include: type => type.GetCustomAttribute<GltfShaderSelectorAttribute>() is null || HasProperties( type ),
				canAdd: type => type.GetCustomAttribute<GltfShaderSelectorAttribute>() is null && GltfMaterialShaders.Supports( shader, type ) ) );
		}
		else
		{
			if ( element.Kind == GltfElementKind.Animation ) scroller.Canvas.Layout.Add( new GltfAnimationInfo() );
			if ( element.Kind == GltfElementKind.Skin ) scroller.Canvas.Layout.Add( new GltfSkinJointList( session, element ) );
			if ( element.Kind == GltfElementKind.Texture ) scroller.Canvas.Layout.Add( new GltfTextureSettings( session, element ) );
			scroller.Canvas.Layout.Add( new GltfFacetList( session, element ) );
		}
		scroller.Canvas.Layout.AddStretchCell();
	}

	static bool HasProperties( Type type ) => type.GetProperties().Any( x => x.GetCustomAttribute<PropertyAttribute>() is not null );

	/// <summary>The shader picker. It only offers shaders that render every property group the material has.</summary>
	Widget CreateShaderRow()
	{
		var box = new Widget { Layout = Layout.Row() };
		box.Layout.Margin = 8;
		box.Layout.Spacing = 6;
		box.Layout.Add( new Label( "Shader" ), 1 );
		var current = GltfMaterialShaders.Current( element );
		var button = box.Layout.Add( new Button( current.Title, "auto_awesome" ) { ToolTip = current.Path }, 2 );
		button.Clicked = () =>
		{
			var menu = new ContextMenu( this );
			var compatible = GltfMaterialShaders.Compatible( element ).ToList();
			foreach ( var shader in GltfMaterialShaders.All )
			{
				var option = menu.AddOption( shader.Title, shader == current ? "check" : "auto_awesome", () => Commit( () => session.SetMaterialShader( element, shader.Path ) ) );
				option.ToolTip = shader.Path;
				option.Enabled = compatible.Contains( shader );
			}
			menu.AddSeparator();
			menu.AddOption( "Other Shader...", "folder_open", () =>
			{
				var dialog = new FileDialog( null ) { Title = "Pick Shader" };
				dialog.SetFindFile();
				dialog.SetModeOpen();
				dialog.SetNameFilter( "Shaders (*.shader)" );
				if ( !dialog.Execute() ) return;
				var path = AssetSystem.FindByPath( dialog.SelectedFile )?.Path ?? dialog.SelectedFile;
				Commit( () => session.SetMaterialShader( element, path ) );
			} );
			menu.OpenAtCursor( false );
		};
		return box;
	}

	void Commit( Action edit, bool rebuild = true )
	{
		edit();
		if ( rebuild ) RebuildUI();
	}
}

/// <summary>A large sphere render of a material that updates after edits.</summary>
sealed class GltfMaterialPreview : Widget
{
	readonly GltfElement material;
	int shownVersion = -1;

	public GltfMaterialPreview( GltfElement material ) => this.material = material;

	[EditorEvent.Frame]
	void CheckForChanges()
	{
		// Get queues a new render after an edit; repaint when one finishes.
		GltfMaterialThumbnails.Get( material );
		if ( GltfMaterialThumbnails.Version != shownVersion ) { shownVersion = GltfMaterialThumbnails.Version; Update(); }
	}

	protected override void OnPaint()
	{
		var area = LocalRect.Shrink( 8 );
		var side = MathF.Min( area.Width, area.Height );
		var rect = new Rect( area.Center - new Vector2( side * 0.5f ), new Vector2( side ) );
		if ( GltfMaterialThumbnails.Get( material ) is { } sphere ) Paint.Draw( rect, sphere );
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, "hourglass_empty", 32, TextFlag.Center );
		}
	}
}

/// <summary>
/// Settings of the whole glTF file, shown when the file is selected at the top of the hierarchy. These are compile
/// settings: they are saved in the asset's metadata and change the compiled file, not the source.
/// </summary>
[Inspector( typeof( GltfFileSelection ) )]
public sealed class GltfFileInspector : InspectorWidget
{
	public GltfFileInspector( SerializedObject so ) : base( so )
	{
		Layout = Layout.Column();
		if ( so.Targets.FirstOrDefault() is not GltfFileSelection { Session: var session } || session.Asset is null ) return;

		var header = Layout.AddRow();
		header.Margin = 8;
		header.Spacing = 6;
		header.Add( new IconButton( "view_in_ar" ) { Background = Color.Transparent, IconSize = 16, TransparentForMouseEvents = true } );
		header.Add( new Label( System.IO.Path.GetFileName( session.Asset.Path ) ), 1 );

		var column = Layout.AddColumn();
		column.Margin = 8;
		column.Spacing = 6;
		column.Add( new Label( "Compile Compression" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) } );
		column.Add( new Label( "Applies to every primitive. A mesh's primitive list can change it for one primitive. The next import uses these settings; the source file does not change." ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.5f ) } );
		column.Add( new GltfCompressionSheet( session.Asset, session.Document ) );
		var consolidate = column.Add( new Checkbox( "Consolidate Geometry Buffers" ) { Value = session.Asset.MetaData.Get( GltfAssetImport.ConsolidateBuffersKey, true ) } );
		consolidate.ToolTip = "Combine geometry buffers when it is safe to do so. Compression needs one buffer.";
		consolidate.StateChanged = _ => session.Asset.MetaData.Set( GltfAssetImport.ConsolidateBuffersKey, consolidate.Value );
		Layout.AddStretchCell();
	}
}
