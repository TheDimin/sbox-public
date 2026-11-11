using static Editor.Inspectors.AssetInspector;

namespace Editor.Inspectors;

[CanEdit( "asset:gltf" ), CanEdit( "asset:glb" )]
public class GltfImportSettings : Widget, IAssetInspector
{
	public class Settings
	{
		[Title( "Consolidate Geometry Buffers" )]
		[Description( "Combine geometry buffers when it is safe to do so. Disable to preserve their identities in the generated GLB." )]
		public bool ConsolidateBuffers { get; set; } = true;
	}

	private Asset asset;
	private Settings settings;
	private Label status;

	public GltfImportSettings( Widget parent ) : base( parent )
	{
		VerticalSizeMode = SizeMode.CanGrow;
	}

	public void SetAsset( Asset selected )
	{
		asset = selected;
		if ( !GltfAssetImport.CanImport( asset ) )
		{
			Layout = Layout.Column();
			Layout.Add( new Label( "Import settings are unavailable for this asset." ) );
			return;
		}
		settings = new Settings { ConsolidateBuffers = asset.MetaData.Get( GltfAssetImport.ConsolidateBuffersKey, true ) };
		var serialized = settings.GetSerialized();
		Layout = Layout.Column();
		Layout.Add( ControlSheet.Create( serialized ) );
		Layout.Add( new Label( "Compile Compression" ) );
		try { Layout.Add( new GltfCompressionSheet( asset, GltfEditorSession.LoadDocument( asset ) ) ); }
		catch ( Exception e ) { Layout.Add( new Label( $"Compression settings are unavailable: {e.Message}" ) ); }
		Layout.Add( new Button( "Reimport" ) { Clicked = Reimport } );
		status = Layout.Add( new Label( "External images remain separate files." ) );
		serialized.OnPropertyChanged += _ =>
		{
			try
			{
				asset.MetaData.Set( GltfAssetImport.ConsolidateBuffersKey, settings.ConsolidateBuffers );
				var saved = asset.MetaData.GetElement( GltfAssetImport.ConsolidateBuffersKey );
				if ( !saved.HasValue || saved.Value.ValueKind is not (System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False) || saved.Value.GetBoolean() != settings.ConsolidateBuffers )
					throw new InvalidOperationException( "The import setting could not be saved." );
				Reimport();
			}
			catch ( Exception e )
			{
				Log.Warning( e, $"Could not save glTF import settings for '{asset.Path}'" );
				status.Text = $"Settings were not saved: {e.Message}";
			}
		};
	}

	private void Reimport()
	{
		try
		{
			GltfAssetImport.Reimport( asset );
			status.Text = "Reimported. External images remain separate files.";
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not reimport '{asset.Path}'" );
			status.Text = $"Reimport failed: {e.Message}";
		}
	}
}
