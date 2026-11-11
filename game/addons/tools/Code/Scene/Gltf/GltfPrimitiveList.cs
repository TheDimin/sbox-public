
namespace Editor;

/// <summary>
/// The primitives of a mesh as a list. Each row shows the primitive's size, the material it uses with a sphere preview,
/// and its compile compression overrides.
/// </summary>
sealed class GltfPrimitiveList : Widget
{
	readonly GltfEditorSession session;
	readonly GltfElement mesh;
	readonly Action<Action> commit;

	public GltfPrimitiveList( GltfEditorSession session, GltfElement mesh, Action<Action> commit )
	{
		this.session = session;
		this.mesh = mesh;
		this.commit = commit;
		Layout = Layout.Column();
		Layout.Margin = 8;
		Layout.Spacing = 4;

		var primitives = mesh.PrimitiveMaterials;
		var header = Layout.AddRow();
		header.Spacing = 6;
		header.Add( new Label( $"Primitives ({primitives.Count})" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) }, 1 );
		header.Add( new Label( "Material" ) { Color = Theme.TextControl.WithAlpha( 0.7f ) }, 2 );
		for ( var i = 0; i < primitives.Count; i++ )
			Layout.Add( new PrimitiveRow( this, i, primitives[i] ) );
	}

	sealed class PrimitiveRow : Widget
	{
		public PrimitiveRow( GltfPrimitiveList list, int primitive, int materialIndex )
		{
			var document = list.mesh.Document;
			var materials = document.GetElements( GltfElementKind.Material );
			var material = materialIndex >= 0 && materialIndex < materials.Count ? materials[materialIndex] : null;
			Layout = Layout.Column();
			Layout.Margin = 6;
			Layout.Spacing = 4;

			var row = Layout.AddRow();
			row.Spacing = 6;
			var info = row.AddColumn( 1 );
			info.Add( new Label( $"#{primitive}" ) );
			info.Add( new Label( GltfCompressionSheet.Describe( document, list.mesh.Index, primitive ) ) { Color = Theme.TextControl.WithAlpha( 0.5f ) } );

			var slot = row.Add( new GltfMaterialSlot( material ), 2 );
			slot.Clicked = () =>
			{
				var menu = new ContextMenu( this );
				menu.AddOption( "None", "block", () => list.commit( () => list.session.SetPrimitiveMaterial( list.mesh, primitive, -1 ) ) );
				foreach ( var option in materials )
				{
					var index = option.Index;
					void Choose() => list.commit( () => list.session.SetPrimitiveMaterial( list.mesh, primitive, index ) );
					// The sphere shows what the material looks like, so the choice is not by name only.
					if ( GltfMaterialThumbnails.Get( option ) is { } sphere ) menu.AddOptionWithImage( option.Name, sphere, Choose );
					else menu.AddOption( option.Name, "palette", Choose );
				}
				menu.OpenAtCursor( false );
			};
			var open = row.Add( new IconButton( "arrow_forward" ) { ToolTip = "Select the material", Enabled = material is not null, IconSize = 14 } );
			open.OnClick = () => { list.session.Selection.Clear(); list.session.Selection.Add( material ); };

			var compression = new GltfCompressionSheet( list.session.Asset, document, (list.mesh.Index, primitive) ) { Visible = false };
			var toggle = Layout.Add( new GltfFoldout( CompressionTitle( list.session.Asset, list.mesh.Index, primitive ) ) );
			toggle.Toggled = open => compression.Visible = open;
			compression.Changed = () => toggle.Title = CompressionTitle( list.session.Asset, list.mesh.Index, primitive );
			Layout.Add( compression );
		}

		static string CompressionTitle( Asset asset, int mesh, int primitive )
		{
			var overrides = GltfAssetImport.ReadCompression( asset ).Primitives.GetValueOrDefault( (mesh, primitive) )?.Count ?? 0;
			return overrides == 0 ? "Compression" : $"Compression ({overrides} override{(overrides == 1 ? "" : "s")})";
		}

		protected override void OnPaint()
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( LocalRect, Theme.ControlRadius );
		}
	}
}

/// <summary>A flat header with an arrow that opens and closes the section below it.</summary>
sealed class GltfFoldout : Widget
{
	string title;
	bool open;

	public Action<bool> Toggled { get; set; }

	public string Title { get => title; set { title = value; Update(); } }

	public GltfFoldout( string title )
	{
		this.title = title;
		FixedHeight = 22;
		Cursor = CursorShape.Finger;
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( !e.LeftMouseButton ) return;
		open = !open;
		Toggled?.Invoke( open );
		Update();
	}

	protected override void OnPaint()
	{
		var r = LocalRect;
		if ( IsUnderMouse )
		{
			Paint.ClearPen();
			Paint.SetBrush( Theme.ControlBackground.WithAlpha( 0.5f ) );
			Paint.DrawRect( r, Theme.ControlRadius );
		}
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.7f ) );
		Paint.DrawIcon( new Rect( r.Left, r.Top, 18, r.Height ), open ? "expand_more" : "chevron_right", 14, TextFlag.Center );
		Paint.SetDefaultFont();
		Paint.DrawText( new Rect( r.Left + 20, r.Top, r.Width - 20, r.Height ), title, TextFlag.LeftCenter );
	}
}

/// <summary>A material slot: the sphere preview, the material name and a drop-down arrow.</summary>
sealed class GltfMaterialSlot : Widget
{
	readonly GltfElement material;
	int shownVersion = -1;

	public Action Clicked { get; set; }

	public GltfMaterialSlot( GltfElement material )
	{
		this.material = material;
		FixedHeight = 36;
		Cursor = CursorShape.Finger;
		ToolTip = "Change the material";
	}

	[EditorEvent.Frame]
	void CheckForChanges()
	{
		if ( material is not null ) GltfMaterialThumbnails.Get( material );
		if ( GltfMaterialThumbnails.Version != shownVersion ) { shownVersion = GltfMaterialThumbnails.Version; Update(); }
	}

	protected override void OnMousePress( MouseEvent e )
	{
		if ( e.LeftMouseButton ) Clicked?.Invoke();
	}

	protected override void OnPaint()
	{
		var r = LocalRect;
		Paint.ClearPen();
		Paint.SetBrush( IsUnderMouse ? Theme.ControlBackground.Lighten( 0.2f ) : Theme.ControlBackground );
		Paint.DrawRect( r, Theme.ControlRadius );
		var picture = new Rect( r.Left + 2, r.Top + 2, r.Height - 4, r.Height - 4 );
		if ( material is null )
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( picture, "block", 16, TextFlag.Center );
		}
		else if ( GltfMaterialThumbnails.Get( material ) is { } sphere ) Paint.Draw( picture, sphere );
		else
		{
			Paint.SetPen( Theme.TextControl.WithAlpha( 0.3f ) );
			Paint.DrawIcon( picture, "hourglass_empty", 16, TextFlag.Center );
		}
		var text = r;
		text.Left = picture.Right + 6;
		text.Right -= 22;
		Paint.SetPen( material is null ? Theme.TextControl.WithAlpha( 0.5f ) : Theme.TextControl );
		Paint.SetDefaultFont();
		Paint.DrawText( text, material?.Name ?? "None", TextFlag.LeftCenter | TextFlag.SingleLine );
		Paint.SetPen( Theme.TextControl.WithAlpha( 0.6f ) );
		Paint.DrawIcon( new Rect( r.Right - 22, r.Top, 20, r.Height ), "arrow_drop_down", 16, TextFlag.Center );
	}
}

/// <summary>
/// The compile compression of a glTF file, or of one primitive. A primitive shows only the groups it has, and does not
/// offer a size that is larger than its source data. The settings are saved in the asset's metadata, so the next import uses them.
/// </summary>
sealed class GltfCompressionSheet : Widget
{
	readonly Asset asset;
	readonly GltfAuthoringDocument document;
	readonly (int Mesh, int Primitive)? primitive;
	Label estimate;

	public Action Changed { get; set; }

	public GltfCompressionSheet( Asset asset, GltfAuthoringDocument document, (int Mesh, int Primitive)? primitive = null )
	{
		this.asset = asset;
		this.document = document;
		this.primitive = primitive;
		Layout = Layout.Column();
		Layout.Spacing = 2;
		Rebuild();
	}

	static readonly (GltfVertexGroup Group, string Title, string Tooltip)[] Groups =
	[
		(GltfVertexGroup.Position, "Positions", "16-bit positions are stored around the centre of each mesh. A child node holds the offset and scale. Skinned, morphed, instanced and LOD meshes keep their positions."),
		(GltfVertexGroup.Normal, "Normals", "Signed normalized integers. 8-bit is enough for most surfaces."),
		(GltfVertexGroup.Tangent, "Tangents", "Signed normalized integers."),
		(GltfVertexGroup.TexCoord, "Texture Coordinates", "Normalized integers. Only coordinates in the -1 to 1 range can be stored; others stay as they are."),
		(GltfVertexGroup.Color, "Vertex Colors", "Unsigned normalized integers."),
		(GltfVertexGroup.Weights, "Skin Weights", "Unsigned normalized integers. The weights of each vertex still sum to exactly one."),
	];

	void Rebuild()
	{
		Layout.Clear( true );
		var settings = GltfAssetImport.ReadCompression( asset );
		foreach ( var (group, title, tooltip) in Groups )
		{
			int? sourceBits = null;
			if ( primitive is { } key )
			{
				sourceBits = SourceBits( document, key.Mesh, key.Primitive, group );
				if ( sourceBits is null ) continue;
			}
			var row = Layout.AddRow();
			row.Spacing = 6;
			row.Add( new Label( title ) { ToolTip = tooltip }, 1 );
			var current = primitive is { } p && settings.Primitives.GetValueOrDefault( p ) is { } overrides && overrides.TryGetValue( group, out var value ) ? (GltfVertexStorage?)value
				: primitive is null ? settings.Global.GetValueOrDefault( group ) : null;
			var button = row.Add( new Button( Title( current, settings.Global.GetValueOrDefault( group ), sourceBits ), current is null ? "subdirectory_arrow_right" : "compress" ), 2 );
			button.Clicked = () => OpenMenu( group, current, sourceBits );
		}

		if ( primitive is null )
		{
			var compact = Layout.Add( new Checkbox( "Compact Integers" ) { Value = settings.CompactIntegers } );
			compact.ToolTip = "Store indices in 16 bits and joint indices in 8 bits where every value fits. This loses no data.";
			compact.StateChanged = _ => Edit( x => x.CompactIntegers = compact.Value );
		}
		estimate = Layout.Add( new Label( Estimate( settings ) ) { Color = Theme.TextControl.WithAlpha( 0.5f ), WordWrap = true } );
	}

	void OpenMenu( GltfVertexGroup group, GltfVertexStorage? current, int? sourceBits )
	{
		var menu = new ContextMenu( this );
		if ( primitive is { } key )
		{
			var inherited = GltfAssetImport.ReadCompression( asset ).Global.GetValueOrDefault( group );
			var option = menu.AddOption( $"File Default ({Name( inherited, sourceBits )})", current is null ? "check" : "subdirectory_arrow_right", () => Set( group, null ) );
		}
		foreach ( var storage in Enum.GetValues<GltfVertexStorage>() )
		{
			if ( group == GltfVertexGroup.Position && storage == GltfVertexStorage.Int8 ) continue;
			var bits = storage == GltfVertexStorage.Int16 ? 16 : storage == GltfVertexStorage.Int8 ? 8 : 32;
			var option = menu.AddOption( Name( storage, sourceBits ), current == storage ? "check" : "compress", () => Set( group, storage ) );
			// Compression never makes data larger, so a size at or above the source is not offered.
			if ( storage != GltfVertexStorage.Source && sourceBits is { } source && bits >= source )
			{
				option.Enabled = false;
				option.ToolTip = $"The source already stores this data in {source} bits.";
			}
		}
		menu.OpenAtCursor( false );
	}

	static string Name( GltfVertexStorage storage, int? sourceBits ) => storage switch
	{
		GltfVertexStorage.Int16 => "16-bit",
		GltfVertexStorage.Int8 => "8-bit",
		_ => sourceBits is { } bits ? $"Source ({(bits == 32 ? "float" : $"{bits}-bit")})" : "Source",
	};

	static string Title( GltfVertexStorage? current, GltfVertexStorage inherited, int? sourceBits ) =>
		current is { } storage ? Name( storage, sourceBits ) : $"File Default ({Name( inherited, sourceBits )})";

	void Set( GltfVertexGroup group, GltfVertexStorage? storage ) => Edit( settings =>
	{
		if ( primitive is { } key )
		{
			if ( !settings.Primitives.TryGetValue( key, out var overrides ) ) settings.Primitives[key] = overrides = new();
			if ( storage is { } value ) overrides[group] = value;
			else overrides.Remove( group );
			if ( overrides.Count == 0 ) settings.Primitives.Remove( key );
		}
		else if ( storage is { } value && value != GltfVertexStorage.Source ) settings.Global[group] = value;
		else settings.Global.Remove( group );
	} );

	void Edit( Action<GltfCompileCompression> edit )
	{
		var settings = GltfAssetImport.ReadCompression( asset );
		edit( settings );
		GltfAssetImport.WriteCompression( asset, settings );
		Rebuild();
		Changed?.Invoke();
	}

	static GltfPrimitiveInfo Info( GltfAuthoringDocument document, int mesh, int primitive )
	{
		var meshes = document.GetElements( GltfElementKind.Mesh );
		return mesh >= 0 && mesh < meshes.Count ? document.GetPrimitives( meshes[mesh] ).ElementAtOrDefault( primitive ) : null;
	}

	/// <summary>The bits per component of the widest attribute of a group in one primitive, or null if it has none.</summary>
	internal static int? SourceBits( GltfAuthoringDocument document, int mesh, int primitive, GltfVertexGroup group )
	{
		int? bits = null;
		foreach ( var (semantic, layout) in Info( document, mesh, primitive )?.Attributes ?? new Dictionary<string, GltfAccessorLayout>() )
			if ( GroupOf( semantic ) == group )
				bits = Math.Max( bits ?? 0, layout.Bits );
		return bits;
	}

	static GltfVertexGroup? GroupOf( string semantic ) => semantic switch
	{
		"POSITION" => GltfVertexGroup.Position,
		"NORMAL" => GltfVertexGroup.Normal,
		"TANGENT" => GltfVertexGroup.Tangent,
		_ when semantic.StartsWith( "TEXCOORD_" ) => GltfVertexGroup.TexCoord,
		_ when semantic.StartsWith( "COLOR_" ) => GltfVertexGroup.Color,
		_ when semantic.StartsWith( "WEIGHTS_" ) => GltfVertexGroup.Weights,
		_ => null,
	};

	/// <summary>Vertex and triangle counts of a primitive, for the primitive list.</summary>
	internal static string Describe( GltfAuthoringDocument document, int mesh, int primitive )
	{
		if ( Info( document, mesh, primitive ) is not { } info ) return "";
		var indices = info.Indices?.Count ?? info.VertexCount;
		var shape = info.Mode == 4 ? $"{indices / 3:N0} triangles" : $"{indices:N0} indices";
		return $"{info.VertexCount:N0} vertices · {shape}";
	}

	/// <summary>
	/// The vertex data size before and after compression. It is an estimate: it does not know which data a range check
	/// or a mesh rule keeps, and counts shared accessors once per primitive.
	/// </summary>
	string Estimate( GltfCompileCompression settings )
	{
		long before = 0, after = 0;
		foreach ( var mesh in document.GetElements( GltfElementKind.Mesh ) )
			foreach ( var info in document.GetPrimitives( mesh ) )
			{
				if ( primitive is { } key && key != (info.Mesh, info.Primitive) ) continue;
				foreach ( var (semantic, layout) in info.Attributes )
				{
					var size = (long)layout.Count * Stride( layout.Bits, layout.Components );
					before += size;
					var group = GroupOf( semantic );
					var target = group is { } g ? settings.Get( info.Mesh, info.Primitive, g ) switch
					{
						GltfVertexStorage.Int16 => 16,
						GltfVertexStorage.Int8 => g == GltfVertexGroup.Position ? 16 : 8,
						_ => 32,
					} : 32;
					if ( settings.CompactIntegers && semantic.StartsWith( "JOINTS_" ) ) target = 8;
					after += target < layout.Bits ? (long)layout.Count * Stride( target, layout.Components ) : size;
				}
				if ( info.Indices is { } indices )
				{
					before += (long)indices.Count * indices.Bits / 8;
					after += (long)indices.Count * (settings.CompactIntegers && indices.Bits == 32 && info.VertexCount < 65535 ? 16 : indices.Bits) / 8;
				}
			}
		return after < before ? $"Vertex data: {Bytes( before )} → about {Bytes( after )}" : $"Vertex data: {Bytes( before )}";
	}

	static int Stride( int bits, int components ) => (bits / 8 * components + 3) & ~3;

	static string Bytes( long bytes ) => bytes >= 1 << 20 ? $"{bytes / 1048576f:0.0} MB" : bytes >= 1024 ? $"{bytes / 1024f:0.0} KB" : $"{bytes} B";
}
