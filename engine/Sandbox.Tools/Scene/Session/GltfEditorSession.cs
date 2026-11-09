using System;
using System.IO;
using System.Text.Json.Nodes;

namespace Editor;

/// <summary>
/// Opens a glTF asset like a prefab: the viewport shows the model, the hierarchy shows the
/// glTF elements, and the inspector shows their facets. Edits are kept as overrides in the
/// asset metadata. Save writes them, and the import applies them (compile on save, like ModelDoc).
/// Every change to the document goes through this class, so every change has undo.
/// </summary>
public sealed class GltfEditorSession( Scene scene, Asset asset, GltfAuthoringDocument document ) : SceneEditorSession( scene )
{
	// Initialized before the base constructor runs, so SourcePath is valid when the dock is made.
	public Asset Asset { get; } = asset;

	public GltfAuthoringDocument Document { get; private set; } = document;

	public override string SourcePath => Asset?.Path;

	/// <summary>Double-click on a .gltf or .glb asset opens it here, like a prefab.</summary>
	[EditorForAssetType( "gltf" )]
	[EditorForAssetType( "glb" )]
	public static void OpenFromAssetBrowser( Asset asset ) => Open( asset );

	/// <summary>Open a session for a .gltf or .glb asset, or activate the session that has it open.</summary>
	public static GltfEditorSession Open( Asset asset )
	{
		ArgumentNullException.ThrowIfNull( asset );
		if ( All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Asset == asset ) is { } existing )
		{
			existing.MakeActive();
			return existing;
		}

		var document = LoadDocument( asset );
		var scene = Scene.CreateEditorScene();
		try
		{
			using ( scene.Push() )
			{
				scene.Name = Path.GetFileName( asset.Path );
				scene.SceneWorld.AmbientLightColor = new Color( 0.7f, 0.7f, 0.7f );
				var root = scene.CreateObject();
				root.Name = scene.Name;
				var renderer = root.Components.Create<ModelRenderer>();
				try
				{
					var model = Model.Load( asset.Path );
					// A file without a default scene loads as an unselected asset that draws nothing.
					if ( model is { GltfSelectedScene: null, GltfSceneCount: > 0 } )
						model = Model.Load( asset.Path, 0 );
					renderer.Model = model;
				}
				catch ( Exception e )
				{
					// The hierarchy and inspector work on the JSON, so they stay usable; the user
					// can still fix the asset here. Only the viewport preview is missing.
					Log.Warning( e, $"Could not show a preview of '{asset.Path}': {e.InnerException?.Message ?? e.Message}" );
				}
			}
		}
		catch
		{
			scene.Destroy();
			throw;
		}

		var session = new GltfEditorSession( scene, asset, document );
		session.MakeActive();
		return session;
	}

	/// <summary>Set the name of an element.</summary>
	public void Rename( GltfElement element, string name ) => Edit( element, $"Rename {element.Kind.Singular}", json =>
	{
		if ( string.IsNullOrWhiteSpace( name ) ) json.Remove( "name" );
		else json["name"] = name;
	} );

	/// <summary>Write one property of a facet after the inspector changed it.</summary>
	public void SetFacetProperty( GltfFacet facet, string propertyName ) =>
		Edit( facet.Element, $"Edit {propertyName}", json => facet.Write( facet.Resolve( json, true ), propertyName ) );

	/// <summary>Add a facet with its default values.</summary>
	public void AddFacet( GltfElement element, Type facetType )
	{
		var facet = GltfFacets.Create( facetType, element );
		Edit( element, $"Add {facetType.Name}", json => facet.Write( facet.Resolve( json, true ), null ) );
	}

	/// <summary>Remove a facet and its data.</summary>
	public void RemoveFacet( GltfFacet facet )
	{
		if ( !facet.IsRemovable ) throw new InvalidOperationException( "This facet is the element itself and cannot be removed." );
		Edit( facet.Element, $"Remove {facet.GetType().Name}", facet.RemoveFrom );
	}

	void Edit( GltfElement element, string title, Action<JsonObject> mutate )
	{
		if ( element.Document != Document ) throw new InvalidOperationException( "The element belongs to a different document." );
		var (before, after) = Document.Mutate( element, mutate );
		if ( JsonNode.DeepEquals( before, after ) ) return;
		UndoSystem.Insert( title, () => Restore( element, before ), () => Restore( element, after ) );
		HasUnsavedChanges = true;
	}

	void Restore( GltfElement element, JsonObject snapshot )
	{
		Document.Restore( element, snapshot );
		HasUnsavedChanges = true;
	}

	/// <summary>Write the overrides to the asset metadata and reimport. Save As is not supported.</summary>
	public override void Save( bool saveAs )
	{
		if ( !GltfAssetImport.CanImport( Asset ) )
		{
			Log.Warning( $"'{Asset.Path}' is not an editable project asset. Its glTF overrides were not saved." );
			return;
		}

		EditorEvent.Run( "scene.beforesave", Scene );
		GltfOverrides.Write( Asset, Document.CreateOverrides() );
		try
		{
			GltfAssetImport.Reimport( Asset );
		}
		catch ( Exception e )
		{
			// The overrides are saved; the next import retries. Keep the unsaved mark off so the user is not told to save again.
			Log.Warning( e, $"glTF overrides for '{Asset.Path}' were saved, but the import failed: {e.Message}" );
		}
		HasUnsavedChanges = false;
		EditorEvent.Run( "scene.saved", Scene );
	}

	/// <summary>Discard unsaved edits and read the source and saved overrides again.</summary>
	public override void Reload()
	{
		Document = LoadDocument( Asset );
		InitUndo();
		Selection.Clear();
		HasUnsavedChanges = false;
	}

	static GltfAuthoringDocument LoadDocument( Asset asset )
	{
		var path = asset.AbsoluteSourcePath;
		if ( string.IsNullOrEmpty( path ) || !File.Exists( path ) )
			throw new FileNotFoundException( "The glTF source file is unavailable.", path );
		var warnings = new List<string>();
		var document = GltfAuthoringDocument.Load( File.ReadAllBytes( path ), GltfOverrides.Read( asset ), warnings, Path.GetDirectoryName( path ) );
		foreach ( var warning in warnings ) Log.Warning( $"{asset.Path}: {warning}" );
		return document;
	}
}
