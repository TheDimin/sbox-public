namespace Editor;

/// <summary>
/// Hierarchy root for a <see cref="GltfEditorSession"/>. The scenes show their node trees;
/// every other element kind shows as a flat group. The groups come from <see cref="GltfElementKind.All"/>.
/// </summary>
sealed class GltfDocumentNode : TreeNode<GltfEditorSession>
{
	public GltfDocumentNode( GltfEditorSession session ) : base( session ) { }

	public override bool HasChildren => true;

	protected override bool HasDescendant( object obj ) => obj is GltfElement;

	protected override void BuildChildren()
	{
		var document = Value.Document;
		var children = new List<object>();
		// A file with one scene shows its nodes directly. The scene folder would only add a level.
		var scenes = document.GetElements( GltfElementKind.Scene );
		if ( scenes.Count == 1 ) children.AddRange( document.GetChildren( scenes[0] ) );
		foreach ( var kind in GltfElementKind.All )
		{
			// Nodes show inside their scenes. Nodes that no scene reaches get their own group.
			if ( kind == GltfElementKind.Scene && scenes.Count == 1 ) continue;
			var elements = kind == GltfElementKind.Node ? document.GetUnreachableNodes().ToList() : document.GetElements( kind );
			if ( elements.Count > 0 )
				children.Add( new GltfGroup( kind == GltfElementKind.Node ? "Unattached Nodes" : kind.Title, kind.Icon, elements ) );
		}
		SetChildren( children, x => x is GltfGroup group ? new GltfGroupNode( group ) : new GltfElementNode( (GltfElement)x ) );
	}

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		r.Left += 4;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawIcon( r, "view_in_ar", 14, TextFlag.LeftCenter );
		r.Left += 22;
		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( r, Value.Scene.Name, TextFlag.LeftCenter );
	}
}

sealed record GltfGroup( string Title, string Icon, IReadOnlyList<GltfElement> Elements );

sealed class GltfGroupNode : TreeNode<GltfGroup>
{
	public GltfGroupNode( GltfGroup group ) : base( group ) { }

	public override bool HasChildren => Value.Elements.Count > 0;

	protected override void BuildChildren() => SetChildren( Value.Elements, x => new GltfElementNode( x ) );

	protected override bool HasDescendant( object obj ) => obj is GltfElement element && Value.Elements.Any( x => x == element || x.Document.IsDescendant( x, element ) );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		Paint.DrawIcon( r, Value.Icon, 14, TextFlag.LeftCenter );
		r.Left += 20;
		Paint.SetDefaultFont( 8, 500 );
		Paint.DrawText( r, $"{Value.Title} ({Value.Elements.Count})", TextFlag.LeftCenter );
	}
}

sealed class GltfElementNode : TreeNode<GltfElement>
{
	public GltfElementNode( GltfElement element ) : base( element ) { }

	public override bool HasChildren => Value.Document.GetChildren( Value ).Any();

	protected override void BuildChildren() => SetChildren( Value.Document.GetChildren( Value ), x => new GltfElementNode( x ) );

	protected override bool HasDescendant( object obj ) => obj is GltfElement element && Value.Document.IsDescendant( Value, element );

	public override bool CanEdit => true;

	public override string Name { get => Value.Name; set { } }

	public override void OnRename( VirtualWidget item, string text, List<TreeNode> selection = null )
	{
		if ( SceneEditorSession.Active is GltfEditorSession session )
			session.Rename( Value, text );
	}

	// Repaint when the name changes, such as after undo.
	public override int ValueHash => HashCode.Combine( Value, Value.Name );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		var preview = GltfImagePreview.Get( Value, 32 );
		if ( preview is not null )
		{
			// Images and textures show their picture instead of the kind icon.
			Paint.Draw( new Rect( r.Left + 2, r.Top + 1, r.Height - 2, r.Height - 2 ), preview );
		}
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
			Paint.DrawIcon( r, Value.Kind.Icon, 14, TextFlag.LeftCenter );
		}
		r.Left += 20;
		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( r, Value.Name, TextFlag.LeftCenter );
	}
}
