namespace Editor;

/// <summary>
/// Hierarchy root for a <see cref="GltfEditorSession"/>. Scenes show their nodes as the GameObjects of the
/// preview, so selection, rename and drag work as they do in any scene. Every other element kind shows as
/// a flat group. The groups come from <see cref="GltfElementKind.All"/>.
/// </summary>
sealed class GltfDocumentNode : TreeNode<GltfFileSelection>
{
	// Selecting this row selects the file, which shows the file settings in the inspector.
	public GltfDocumentNode( GltfEditorSession session ) : base( new GltfFileSelection( session ) ) { }

	GltfEditorSession Session => Value.Session;

	public override bool HasChildren => true;

	protected override bool HasDescendant( object obj ) => obj is GltfElement or GameObject;

	protected override void BuildChildren()
	{
		var document = Session.Document;
		var children = new List<object>();
		foreach ( var kind in GltfElementKind.All )
		{
			var elements = kind == GltfElementKind.Node ? null : document.GetElements( kind );
			if ( kind == GltfElementKind.Node )
			{
				// Nodes show inside their scenes. Nodes that no scene reaches get their own group.
				var unattached = Session.GetUnattachedObjects().ToList();
				if ( unattached.Count > 0 ) children.Add( new GltfObjectGroup( "Unattached Nodes", kind.Icon, unattached ) );
			}
			else if ( elements.Count == 0 ) continue;
			// Images and samplers are details of textures; the texture inspector manages them. Variants show under their meshes.
			else if ( kind == GltfElementKind.Image || kind == GltfElementKind.Sampler || kind == GltfElementKind.Variant ) continue;
			// A single scene is shown by itself. A "Scenes" folder around one scene adds a level and no information.
			else if ( kind == GltfElementKind.Scene && elements.Count == 1 ) children.Add( elements[0] );
			else children.Add( new GltfGroup( kind.Title, kind.Icon, elements ) );
		}
		SetChildren( children, x => x switch
		{
			GltfGroup group => new GltfGroupNode( group ),
			GltfObjectGroup group => new GltfObjectGroupNode( group ),
			_ => new GltfElementNode( (GltfElement)x, Session ),
		} );
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
		Paint.DrawText( r, Session.Scene.Name, TextFlag.LeftCenter );
	}
}

sealed record GltfGroup( string Title, string Icon, IReadOnlyList<GltfElement> Elements );

sealed record GltfObjectGroup( string Title, string Icon, IReadOnlyList<GameObject> Objects );

sealed class GltfGroupNode : TreeNode<GltfGroup>
{
	public GltfGroupNode( GltfGroup group ) : base( group ) { }

	public override bool HasChildren => Value.Elements.Count > 0;

	protected override void BuildChildren() => SetChildren( Value.Elements, x => new GltfElementNode( x, SceneEditorSession.Active as GltfEditorSession ) );

	protected override bool HasDescendant( object obj ) => obj is GltfElement element && Value.Elements.Contains( element );

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

sealed class GltfObjectGroupNode : TreeNode<GltfObjectGroup>
{
	public GltfObjectGroupNode( GltfObjectGroup group ) : base( group ) { }

	public override bool HasChildren => Value.Objects.Count > 0;

	protected override void BuildChildren() => SetChildren( Value.Objects, x => new GameObjectNode( x ) );

	protected override bool HasDescendant( object obj ) => obj is GameObject go && Value.Objects.Any( x => x == go || x.IsDescendant( go ) );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.5f ) );
		Paint.DrawIcon( r, Value.Icon, 14, TextFlag.LeftCenter );
		r.Left += 20;
		Paint.SetDefaultFont( 8, 500 );
		Paint.DrawText( r, $"{Value.Title} ({Value.Objects.Count})", TextFlag.LeftCenter );
	}
}

/// <summary>A scene, material, mesh, texture or other element. A scene has the GameObjects of its root nodes as children.</summary>
sealed class GltfElementNode : TreeNode<GltfElement>
{
	readonly GltfEditorSession session;

	public GltfElementNode( GltfElement element, GltfEditorSession session ) : base( element )
	{
		this.session = session;
	}

	bool IsScene => Value.Kind == GltfElementKind.Scene;

	public override bool HasChildren => IsScene ? session.GetRootObjects( Value ).Any()
		: Value.Kind == GltfElementKind.Mesh ? Value.Document.GetMeshVariants( Value ).Count > 0
		: Value.Kind == GltfElementKind.Skin && Value.Document.GetJointNodes( Value ).Count > 0;

	protected override void BuildChildren()
	{
		if ( IsScene ) SetChildren( session.GetRootObjects( Value ), x => new GameObjectNode( x ) );
		else if ( Value.Kind == GltfElementKind.Mesh )
			SetChildren( Value.Document.GetMeshVariants( Value ).Select( x => new GltfMeshVariant( Value, x ) ), x => new GltfSelectableNode( x, "style" ) );
		else if ( Value.Kind == GltfElementKind.Skin )
			SetChildren( Value.Document.GetJointNodes( Value ).Select( session.GetObject ).Where( x => x.IsValid() ), x => new GltfJointNode( x ) );
	}

	protected override bool HasDescendant( object obj ) => obj switch
	{
		GameObject go => IsScene && session.GetRootObjects( Value ).Any( x => x == go || x.IsDescendant( go ) ),
		GltfMeshVariant variant => variant.Mesh == Value,
		_ => false,
	};

	public override bool CanEdit => true;

	public override string Name { get => Value.Name; set { } }

	public override void OnRename( VirtualWidget item, string text, List<TreeNode> selection = null ) => session.Rename( Value, text );

	// Repaint when the name changes, such as after undo.
	public override int ValueHash => HashCode.Combine( Value, Value.Name, Value.Kind == GltfElementKind.Material ? GltfMaterialThumbnails.Version : 0 );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		// Materials show a sphere render, images and textures their picture, instead of the kind icon.
		var preview = Value.Kind == GltfElementKind.Material ? GltfMaterialThumbnails.Get( Value ) : GltfImagePreview.Get( Value, 32 );
		if ( preview is not null )
		{
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

/// <summary>
/// A joint under a skin. It is the GameObject of the joint node, so selecting it gives the transform gizmo and
/// inspector of that node, and the viewport shows the joint's weights.
/// </summary>
sealed class GltfJointNode : TreeNode<GameObject>
{
	public GltfJointNode( GameObject go ) : base( go ) { }

	public override bool HasChildren => false;

	public override string Name { get => Value.Name; set { } }

	public override int ValueHash => HashCode.Combine( Value, Value.Name );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawIcon( r, "polyline", 14, TextFlag.LeftCenter );
		r.Left += 20;
		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( r, Value.Name, TextFlag.LeftCenter );
	}
}

/// <summary>A variant under a mesh. Selecting it previews the mesh in that variant.</summary>
sealed class GltfSelectableNode : TreeNode<IGltfSelectable>
{
	readonly string icon;

	public GltfSelectableNode( IGltfSelectable value, string icon ) : base( value ) => this.icon = icon;

	public override string Name { get => Value.ToString(); set { } }

	public override int ValueHash => HashCode.Combine( Value, Value.ToString() );

	public override void OnPaint( VirtualWidget item )
	{
		PaintSelection( item );
		var r = item.Rect;
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawIcon( r, icon, 14, TextFlag.LeftCenter );
		r.Left += 20;
		Paint.SetPen( Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( r, Value is GltfMeshVariant variant ? variant.Variant.Name : Value.ToString(), TextFlag.LeftCenter );
	}
}
