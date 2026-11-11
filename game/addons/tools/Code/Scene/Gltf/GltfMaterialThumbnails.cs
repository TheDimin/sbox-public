using System.Runtime.CompilerServices;

namespace Editor;

/// <summary>
/// Sphere renders of glTF materials. The sphere is built through the glTF loader with the edited material,
/// so it matches the model. Renders happen one per frame, out of paint; until then the last render (or null) shows.
/// </summary>
static class GltfMaterialThumbnails
{
	sealed class Entry
	{
		public string Key;
		public Pixmap Pixmap;
	}

	const int RenderSize = 128;

	static readonly ConditionalWeakTable<GltfElement, Entry> cache = new();
	static readonly List<GltfElement> pending = new();
	static Scene scene;
	static ModelRenderer renderer;

	/// <summary>Increments when a render finishes, so views can repaint.</summary>
	public static int Version { get; private set; }

	/// <summary>The sphere render of a material, or null while the first render is pending.</summary>
	public static Pixmap Get( GltfElement material )
	{
		if ( material?.Kind != GltfElementKind.Material ) return null;
		var entry = cache.GetOrCreateValue( material );
		if ( entry.Key != material.JsonText && !pending.Contains( material ) ) pending.Add( material );
		return entry.Pixmap;
	}

	[EditorEvent.Frame]
	static void RenderNext()
	{
		if ( pending.Count == 0 ) return;
		var material = pending[0];
		pending.RemoveAt( 0 );
		var session = SceneEditorSession.All.OfType<GltfEditorSession>().FirstOrDefault( x => x.Document == material.Document );
		if ( session is null ) return;

		var entry = cache.GetOrCreateValue( material );
		entry.Key = material.JsonText;
		try
		{
			EnsureScene();
			using ( scene.Push() )
			{
				renderer.Model = session.CreateMaterialPreviewModel( material );
				var pixmap = new Pixmap( RenderSize, RenderSize );
				if ( scene.Camera.RenderToPixmap( pixmap ) ) entry.Pixmap = pixmap;
			}
			Version++;
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not render a preview of {material}: {e.InnerException?.Message ?? e.Message}" );
		}
	}

	static void EnsureScene()
	{
		if ( scene.IsValid() ) return;
		scene = Scene.CreateEditorScene();
		scene.Name = "glTF Material Preview";
		using ( scene.Push() )
		{
			var camera = new GameObject( true, "camera" ).AddComponent<CameraComponent>();
			camera.BackgroundColor = Color.Transparent;
			camera.FieldOfView = 30;
			camera.ZNear = 1;
			camera.ZFar = 1000;
			// The sphere has a radius of 0.5 m (about 20 units). This distance fills the frame with a small margin.
			camera.WorldRotation = Rotation.From( 20, 180 + 45, 0 );
			camera.WorldPosition = -camera.WorldRotation.Forward * 85;

			var sun = new GameObject( true, "sun" ).AddComponent<DirectionalLight>();
			sun.WorldRotation = Rotation.From( 50, 45, 0 );
			sun.LightColor = Color.White * 0.8f;
			new GameObject( true, "ambient" ).AddComponent<AmbientLight>().Color = Color.White * 0.3f;
			var envmap = new GameObject( true, "envmap" ).AddComponent<EnvmapProbe>();
			envmap.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
			envmap.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
			envmap.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );

			renderer = new GameObject( true, "sphere" ).AddComponent<ModelRenderer>();
		}
	}
}
