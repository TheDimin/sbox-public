namespace Editor.Inspectors;

/// <summary>Shared layout for the glTF inspectors that are not property groups.</summary>
static class GltfInspectorLayout
{
	public static GltfEditorSession SessionFor( GltfAuthoringDocument document ) =>
		SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == document );

	public static Layout Header( Widget owner, string icon, string title, string subtitle )
	{
		owner.Layout = Layout.Column();
		var header = owner.Layout.AddRow();
		header.Margin = 8;
		header.Spacing = 6;
		header.Add( new IconButton( icon ) { Background = Color.Transparent, IconSize = 16, TransparentForMouseEvents = true } );
		header.Add( new Label( title ), 1 );
		header.Add( new Label( subtitle ) { Color = Theme.TextControl.WithAlpha( 0.5f ) } );
		var body = owner.Layout.AddColumn();
		body.Margin = 8;
		body.Spacing = 6;
		return body;
	}

	public static Layout Row( Layout body, string label )
	{
		var row = body.AddRow();
		row.Spacing = 6;
		row.Add( new Label( label ) { MinimumWidth = 90 } );
		return row;
	}

	public static void Select( GltfEditorSession session, object target )
	{
		session.Selection.Clear();
		session.Selection.Add( target );
	}
}

/// <summary>The inspector part of an animation element. Movie Maker plays and edits the animation.</summary>
sealed class GltfAnimationInfo : Widget
{
	public GltfAnimationInfo()
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;
		Layout.Add( new Label( "The animation opens in Movie Maker as tracks on the preview model. Use its timeline to play and scrub it." ) { WordWrap = true } );
		Layout.Add( new Button( "Show Movie Maker", "movie_creation" ) { Clicked = () => EditorWindow.DockManager.RaiseDock( "Movie Maker" ) } );
	}
}

/// <summary>The joints of a skin. Selecting a joint shows its weights on the model.</summary>
sealed class GltfSkinJointList : Widget
{
	public GltfSkinJointList( GltfEditorSession session, GltfElement skin )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 2;
		var joints = skin.Document.GetJointNodes( skin );
		Layout.Add( new Label( $"{joints.Count} joints. The skeleton shows in the viewport; select a joint to move it and see its weights." ) { WordWrap = true } );
		for ( var i = 0; i < joints.Count; i++ )
		{
			if ( session.GetObject( joints[i] ) is not { } go ) continue;
			var button = Layout.Add( new Button( $"{i}: {joints[i].Name}", "accessibility" ) );
			button.Clicked = () => GltfInspectorLayout.Select( session, go );
		}
	}
}

/// <summary>The color ramp of the joint weight preview.</summary>
sealed class GltfHeatLegend : Widget
{
	protected override void OnPaint()
	{
		var r = LocalRect.Shrink( 0, 4 );
		const int steps = 32;
		for ( var i = 0; i < steps; i++ )
		{
			var t = (i + 0.5f) / steps;
			var color = new Color( Math.Clamp( 2 * t - 0.5f, 0, 1 ), t < 0.5f ? 2 * t : 2 - 2 * t, Math.Clamp( 1 - 2 * t, 0, 1 ) );
			Paint.SetBrushAndPen( color );
			Paint.DrawRect( new Rect( r.Left + r.Width * i / steps, r.Top, r.Width / steps + 1, r.Height ) );
		}
	}
}

/// <summary>A mesh in one material variant: which material each primitive uses in it.</summary>
[Inspector( typeof( GltfMeshVariant ) )]
public sealed class GltfMeshVariantInspector : InspectorWidget
{
	readonly GltfMeshVariant target;
	readonly GltfEditorSession session;
	int shownRevision = -1;

	public GltfMeshVariantInspector( SerializedObject so ) : base( so )
	{
		target = so.Targets.FirstOrDefault() as GltfMeshVariant;
		session = target is null ? null : GltfInspectorLayout.SessionFor( target.Document );
		Rebuild();
	}

	[EditorEvent.Frame]
	void CheckForChanges()
	{
		if ( target is not null && target.Document.Revision != shownRevision ) Rebuild();
	}

	void Rebuild()
	{
		Layout?.Clear( true );
		if ( target is null || session is null ) return;
		shownRevision = target.Document.Revision;
		var body = GltfInspectorLayout.Header( this, "style", target.Variant.Name, $"Variant of {target.Mesh.Name}" );
		body.Add( new Label( "The viewport shows the mesh in this variant. A primitive set to Default keeps its own material." ) { WordWrap = true, Color = Theme.TextControl.WithAlpha( 0.6f ) } );

		var materials = target.Document.GetElements( GltfElementKind.Material );
		var defaults = target.Mesh.PrimitiveMaterials;
		for ( var p = 0; p < defaults.Count; p++ )
		{
			var primitive = p;
			var current = target.Document.GetVariantMaterial( target.Mesh, primitive, target.Variant );
			var row = GltfInspectorLayout.Row( body, $"Primitive {primitive}" );
			var shown = current >= 0 && current < materials.Count ? materials[current] : null;
			var button = row.Add( new Button( shown?.Name ?? "Default", "palette" ), 1 );
			button.Clicked = () =>
			{
				var menu = new ContextMenu( this );
				menu.AddOption( "Default", "undo", () => session.SetVariantMaterial( target.Mesh, primitive, target.Variant, -1 ) );
				foreach ( var material in materials )
				{
					var index = material.Index;
					if ( GltfMaterialThumbnails.Get( material ) is { } sphere )
						menu.AddOptionWithImage( material.Name, sphere, () => session.SetVariantMaterial( target.Mesh, primitive, target.Variant, index ) );
					else
						menu.AddOption( material.Name, "palette", () => session.SetVariantMaterial( target.Mesh, primitive, target.Variant, index ) );
				}
				menu.OpenAtCursor( false );
			};
		}
		body.AddStretchCell();
	}
}

/// <summary>
/// The image and sampling of a texture. glTF splits these into image and sampler elements; the editor hides
/// those and keeps them shared and free of duplicates.
/// </summary>
sealed class GltfTextureSettings : Widget
{
	public GltfTextureSettings( GltfEditorSession session, GltfElement texture )
	{
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 6;
		var document = texture.Document;

		var imageRow = GltfInspectorLayout.Row( Layout, "Image" );
		var image = document.GetTextureImage( texture );
		var picker = imageRow.Add( new Button( image?.Name ?? "None", "image" ), 1 );
		picker.Clicked = () =>
		{
			var menu = new ContextMenu( this );
			foreach ( var candidate in document.GetElements( GltfElementKind.Image ) )
			{
				var choice = candidate;
				if ( GltfImagePreview.Get( candidate, 32 ) is { } preview ) menu.AddOptionWithImage( candidate.Name, preview, () => session.SetTextureImage( texture, choice ) );
				else menu.AddOption( candidate.Name, "image", () => session.SetTextureImage( texture, choice ) );
			}
			menu.AddSeparator();
			menu.AddOption( "Image File...", "add_photo_alternate", () =>
			{
				var dialog = new FileDialog( null ) { Title = "Pick Image" };
				dialog.Directory = System.IO.Path.GetDirectoryName( session.Asset.GetSourceFile( true ) );
				dialog.SetFindFile();
				dialog.SetModeOpen();
				dialog.SetNameFilter( "Images (*.png *.jpg *.jpeg *.webp *.ktx2)" );
				if ( dialog.Execute() ) session.SetTextureImageFile( texture, dialog.SelectedFile );
			} );
			menu.OpenAtCursor( false );
		};

		var sampling = document.GetTextureSampling( texture );
		AddEnum( "Mag Filter", sampling.MagFilter, value => session.SetTextureSampling( texture, document.GetTextureSampling( texture ) with { MagFilter = value } ) );
		AddEnum( "Min Filter", sampling.MinFilter, value => session.SetTextureSampling( texture, document.GetTextureSampling( texture ) with { MinFilter = value } ) );
		AddEnum( "Wrap U", sampling.WrapS, value => session.SetTextureSampling( texture, document.GetTextureSampling( texture ) with { WrapS = value } ) );
		AddEnum( "Wrap V", sampling.WrapT, value => session.SetTextureSampling( texture, document.GetTextureSampling( texture ) with { WrapT = value } ) );
	}

	void AddEnum<T>( string label, T current, Action<T> set ) where T : struct, Enum
	{
		var row = GltfInspectorLayout.Row( Layout, label );
		var combo = row.Add( new ComboBox(), 1 );
		foreach ( var value in Enum.GetValues<T>() ) combo.AddItem( value.ToString(), selected: value.Equals( current ) );
		combo.ItemChanged += () =>
		{
			var value = Enum.GetValues<T>()[combo.CurrentIndex];
			if ( !value.Equals( current ) ) set( value );
		};
	}
}
