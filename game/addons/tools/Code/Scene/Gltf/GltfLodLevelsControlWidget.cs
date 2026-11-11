namespace Editor;

/// <summary>
/// The levels of detail of a node (MSFT_lod): level 0 is the node itself, each lower level is another node of the
/// file with its mesh. Every level has the screen coverage down to which it stays visible.
/// Edits go through the session, so they join undo and keep ids and thresholds together.
/// </summary>
[CustomEditor( typeof( GltfLodLevels ) )]
sealed class GltfLodLevelsControlWidget : ControlWidget
{
	const int RowHeight = 28;

	GltfFacet Facet => SerializedProperty.Parent?.Targets?.FirstOrDefault() as GltfFacet;
	GltfElement Node => Facet?.Element;
	GltfEditorSession Session => SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == Node?.Document );
	GltfLodLevels Levels => Session.GetNodeLods( Node );

	public GltfLodLevelsControlWidget( SerializedProperty property ) : base( property )
	{
		Layout = Layout.Column();
		Layout.Margin = 4;
		Layout.Spacing = 2;
		Build();
	}

	protected override void OnValueChanged() => Build();

	void Build()
	{
		Layout.Clear( true );
		if ( Node is null || Session is null ) return;
		var levels = Levels;
		var rows = 2;

		AddHeader();
		AddRow( "LOD 0", Node, "This node", levels.BaseCoverage, -1, null );
		for ( var i = 0; i < levels.Levels.Length; i++ )
		{
			var index = i;
			var level = levels.Levels[i];
			var element = Node.Document.GetElements( GltfElementKind.Node ).ElementAtOrDefault( level.Node );
			AddRow( $"LOD {i + 1}", element, null, level.Coverage, i, levels );
			rows++;
		}

		var footer = Layout.AddRow();
		footer.Spacing = 4;
		var add = footer.Add( new Button( "Add Level", "add" ) );
		add.Clicked = OpenAddMenu;
		footer.AddStretchCell();
		var preview = footer.Add( new Button( Session.PreviewLodLevel is { } shown ? $"Viewport: LOD {shown}" : "Viewport: LOD 0", "visibility" ) { ToolTip = "Which level of detail the viewport shows" } );
		preview.Clicked = () => OpenPreviewMenu( levels.Levels.Length );
		rows++;
		FixedHeight = rows * (RowHeight + 2) + 8;
	}

	void AddHeader()
	{
		var row = Layout.AddRow();
		row.Spacing = 4;
		row.Add( new Label( "Level" ) { Color = Theme.TextControl.WithAlpha( 0.5f ), MinimumWidth = 44 } );
		row.Add( new Label( "Node and mesh" ) { Color = Theme.TextControl.WithAlpha( 0.5f ) }, 1 );
		row.Add( new Label( "Visible down to" ) { Color = Theme.TextControl.WithAlpha( 0.5f ), ToolTip = "Screen coverage below which the next level takes over. The last level shows down to this value, then hides. 0 never hides." } );
		row.Add( new Widget { FixedWidth = 3 * 28 + 8 } );
	}

	void AddRow( string label, GltfElement element, string fixedText, float coverage, int position, GltfLodLevels? levels )
	{
		var row = Layout.AddRow();
		row.Spacing = 4;
		row.Add( new Label( label ) { MinimumWidth = 44 } );

		var meshName = element?.MeshIndex is { } meshIndex && Node.Document.GetElements( GltfElementKind.Mesh ).ElementAtOrDefault( meshIndex ) is { } mesh ? mesh.Name : "no mesh";
		var node = row.Add( new Button( fixedText ?? $"{element?.Name ?? "Missing node"}  ·  {meshName}", "filter_none" ), 1 );
		if ( levels is null ) node.Enabled = false;
		else node.Clicked = () => OpenLevelMenu( position, element );

		var percent = row.Add( new LineEdit { FixedWidth = 72, Text = $"{coverage * 100:0.##} %" } );
		percent.EditingFinished += () =>
		{
			var text = percent.Text.Replace( "%", "" ).Trim();
			if ( !float.TryParse( text, out var value ) ) { Build(); return; }
			Apply( x => GltfLodEditing.SetCoverage( x, position, value / 100f ), "Edit Level of Detail Threshold" );
		};

		if ( levels is null ) { row.Add( new Widget { FixedWidth = 3 * 28 + 8 } ); return; }
		var count = levels.Value.Levels.Length;
		var up = row.Add( new Button( "", "arrow_upward" ) { FixedWidth = 28, ToolTip = "Move up", Enabled = position > 0 } );
		up.Clicked = () => Apply( x => GltfLodEditing.Move( x, position, -1 ), "Move Level of Detail" );
		var down = row.Add( new Button( "", "arrow_downward" ) { FixedWidth = 28, ToolTip = "Move down", Enabled = position < count - 1 } );
		down.Clicked = () => Apply( x => GltfLodEditing.Move( x, position, 1 ), "Move Level of Detail" );
		var remove = row.Add( new Button( "", "delete" ) { FixedWidth = 28, ToolTip = "Remove this level (the node stays in the file)" } );
		remove.Clicked = () => Apply( x => GltfLodEditing.RemoveAt( x, position ), "Remove Level of Detail" );
	}

	/// <summary>Pick the node of a level, or the mesh that node shows.</summary>
	void OpenLevelMenu( int position, GltfElement current )
	{
		var document = Node.Document;
		var menu = new ContextMenu( this );
		if ( current is not null )
		{
			var meshMenu = menu.AddMenu( "Mesh of this level", "view_in_ar" );
			meshMenu.AddOption( "None", "block", () => Session.SetNodeMesh( current, -1 ) );
			foreach ( var mesh in document.GetElements( GltfElementKind.Mesh ) )
			{
				var index = mesh.Index;
				meshMenu.AddOption( mesh.Name, current.MeshIndex == index ? "check" : "view_in_ar", () => Session.SetNodeMesh( current, index ) );
			}
			menu.AddSeparator();
		}
		var taken = Levels.Levels.Select( x => x.Node ).ToHashSet();
		var use = menu.AddMenu( "Use another node", "swap_horiz" );
		foreach ( var candidate in Session.GetLodCandidates( Node, includeCurrent: true ).Where( x => x.Index == current?.Index || !taken.Contains( x.Index ) ) )
		{
			var target = candidate.Index;
			use.AddOption( candidate.Name, target == current?.Index ? "check" : "filter_none", () => Apply( x => GltfLodEditing.SetNode( x, position, target ), "Change Level of Detail Node" ) );
		}
		menu.OpenAtCursor( false );
	}

	void OpenPreviewMenu( int levelCount )
	{
		var menu = new ContextMenu( this );
		for ( var level = 0; level <= levelCount; level++ )
		{
			var shown = level;
			menu.AddOption( $"LOD {level}", (Session.PreviewLodLevel ?? 0) == level ? "check" : "visibility", () => { Session.PreviewLodLevel = shown; Build(); } );
		}
		menu.OpenAtCursor( false );
	}

	void OpenAddMenu()
	{
		var menu = new ContextMenu( this );
		menu.AddOption( "New level (copy of this node)", "add_circle", () => Session.AddLodLevel( Node ) )
			.ToolTip = "Adds a node with the same mesh and transform. Then pick a lower-detail mesh for it.";
		var existing = Session.GetLodCandidates( Node );
		if ( existing.Count > 0 )
		{
			var sub = menu.AddMenu( "Existing node", "filter_none" );
			foreach ( var target in existing )
				sub.AddOption( target.Name, "filter_none", () => Session.AddLodLevel( Node, target ) );
		}
		menu.OpenAtCursor( false );
	}

	void Apply( Func<GltfLodLevels, GltfLodLevels> change, string title )
	{
		Session.SetNodeLods( Node, change( Levels ), title );
	}
}
