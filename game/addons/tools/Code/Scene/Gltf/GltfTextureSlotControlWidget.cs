namespace Editor;

/// <summary>
/// A material texture slot: a preview of the image, the texture to use (from the file, or an imported image),
/// the UV set, and the scale or strength where the slot has one.
/// </summary>
[CustomEditor( typeof( GltfTextureSlot ) )]
sealed class GltfTextureSlotControlWidget : ControlWidget
{
	const int PreviewSize = 48;

	readonly Button picker;
	readonly LineEdit texCoord;
	readonly LineEdit amount;

	GltfFacet Facet => SerializedProperty.Parent?.Targets?.FirstOrDefault() as GltfFacet;
	GltfAuthoringDocument Document => Facet?.Element?.Document;
	GltfEditorSession Session => SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == Document );
	GltfTextureSlot Slot => SerializedProperty.GetValue<GltfTextureSlot>();

	public GltfTextureSlotControlWidget( SerializedProperty property ) : base( property )
	{
		FixedHeight = PreviewSize + 4;
		Layout = Layout.Row();
		Layout.Margin = new Sandbox.UI.Margin( PreviewSize + 8, 2, 2, 2 );
		Layout.Spacing = 4;

		var column = Layout.AddColumn( 1 );
		column.Spacing = 2;
		picker = column.Add( new Button( "", "texture" ) );
		picker.Clicked = OpenMenu;

		var row = column.AddRow();
		row.Spacing = 4;
		row.Add( new Label( "UV" ) );
		texCoord = row.Add( new LineEdit { FixedWidth = 32 } );
		texCoord.EditingFinished += () => { if ( int.TryParse( texCoord.Text, out var value ) && value >= 0 ) Change( x => x with { TexCoord = value } ); };
		var amountKey = property.TryGetAttribute<GltfTextureAmountAttribute>( out var attribute ) ? attribute.Key : null;
		if ( amountKey is not null )
		{
			row.Add( new Label( amountKey == "scale" ? "Scale" : "Strength" ) );
			amount = row.Add( new LineEdit { FixedWidth = 48 } );
			amount.EditingFinished += () => { if ( float.TryParse( amount.Text, out var value ) ) Change( x => x with { Amount = value } ); };
		}
		row.AddStretchCell();
		Refresh();
	}

	protected override void OnValueChanged() => Refresh();

	void Refresh()
	{
		var slot = Slot;
		var texture = TextureElement( slot.Texture );
		picker.Text = texture?.Name ?? "None";
		texCoord.Text = slot.TexCoord.ToString();
		if ( amount is not null ) amount.Text = slot.Amount.ToString( "0.###" );
		Update();
	}

	GltfElement TextureElement( int index )
	{
		var textures = Document?.GetElements( GltfElementKind.Texture );
		return textures is not null && index >= 0 && index < textures.Count ? textures[index] : null;
	}

	protected override void OnPaint()
	{
		base.OnPaint();
		var rect = new Rect( 2, 2, PreviewSize, PreviewSize );
		Paint.SetBrushAndPen( Theme.ControlBackground, Theme.ControlBackground.Lighten( 0.2f ) );
		Paint.DrawRect( rect, 3 );
		var texture = TextureElement( Slot.Texture );
		if ( texture is not null && GltfImagePreview.Get( texture, PreviewSize * 2 ) is { } preview )
			Paint.Draw( rect.Shrink( 1 ), preview );
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( rect, "hide_image", 20, TextFlag.Center );
		}
	}

	void OpenMenu()
	{
		var menu = new ContextMenu( this );
		menu.AddOption( "None", "block", () => Change( x => x with { Texture = -1 } ) );
		foreach ( var texture in Document?.GetElements( GltfElementKind.Texture ) ?? [] )
		{
			var index = texture.Index;
			if ( GltfImagePreview.Get( texture, 32 ) is { } preview ) menu.AddOptionWithImage( texture.Name, preview, () => Change( x => x with { Texture = index } ) );
			else menu.AddOption( texture.Name, "texture", () => Change( x => x with { Texture = index } ) );
		}
		menu.AddSeparator();
		menu.AddOption( "Import Image...", "add_photo_alternate", ImportImage );
		menu.OpenAtCursor( false );
	}

	void ImportImage()
	{
		var session = Session;
		if ( session is null ) return;
		var dialog = new FileDialog( null ) { Title = "Import Image" };
		dialog.Directory = System.IO.Path.GetDirectoryName( session.Asset.GetSourceFile( true ) );
		dialog.SetFindFile();
		dialog.SetModeOpen();
		dialog.SetNameFilter( "Images (*.png *.jpg *.jpeg *.webp *.ktx2)" );
		if ( !dialog.Execute() ) return;
		var texture = session.ImportTexture( dialog.SelectedFile );
		Change( x => x with { Texture = texture.Index } );
	}

	void Change( Func<GltfTextureSlot, GltfTextureSlot> change )
	{
		PropertyStartEdit();
		SerializedProperty.SetValue( change( Slot ) );
		PropertyFinishEdit();
		Refresh();
	}
}
