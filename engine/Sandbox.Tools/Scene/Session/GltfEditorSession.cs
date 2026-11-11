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

				// Without light the materials draw black. A prefab view gets its light from the editor; this scene has no other source.
				var sun = scene.CreateObject();
				sun.Name = "Sun";
				sun.LocalTransform = new Transform( Vector3.Up * 200, Rotation.From( 50, 45, 0 ) );
				sun.Components.Create<DirectionalLight>();
				// The sky is studio lighting, not part of the glTF, so the hierarchy does not show it.
				var sky = scene.CreateObject();
				sky.Name = "Sky";
				sky.Flags |= GameObjectFlags.Hidden | GameObjectFlags.NotSaved;
				sky.Components.Create<SkyBox2D>();
				sky.Components.Create<AmbientLight>().Color = new Color( 0.6f, 0.6f, 0.6f );
				// Metals and smooth surfaces need something to reflect, like in the model asset preview.
				var probe = sky.Components.Create<EnvmapProbe>();
				probe.Mode = EnvmapProbe.EnvmapProbeMode.CustomTexture;
				probe.Texture = Texture.Load( "textures/cubemaps/default2.vtex" );
				probe.Bounds = BBox.FromPositionAndSize( Vector3.Zero, 100000 );
			}
		}
		catch
		{
			scene.Destroy();
			throw;
		}

		var session = new GltfEditorSession( scene, asset, document );
		try
		{
			session.BuildScene();
		}
		catch
		{
			// Destroy the session, not only its scene: the session already added a tab, and a tab that is
			// "open" with nothing loaded is worse than the error.
			session.Destroy();
			throw;
		}
		session.MakeActive();
		return session;
	}

	// ---- Preview scene ----
	// Every glTF node is a GameObject, so the default scene tools (selection box, gizmos, hierarchy,
	// transform inspector) work on it. The JSON stays the source of truth: a transform or name change
	// on a GameObject is written back in Tick. Each mesh is one Model that shows only that mesh.

	GameObject sceneRoot;
	GameObject preview;
	object previewKey;
	readonly Dictionary<GltfElement, GameObject> objects = new();
	readonly Dictionary<GameObject, GltfElement> elements = new();
	readonly Dictionary<int, Model> meshModels = new();

	/// <summary>
	/// Compiled units of this asset's previews. The previews use the compiled native data, as the game does; an edit
	/// to a material or a texture recompiles only what depends on it.
	/// </summary>
	readonly GltfNativeCompileCache previewCache = new();

	/// <summary>Material sphere thumbnails compile on the main thread while the preview job may use <see cref="previewCache"/>.</summary>
	readonly GltfNativeCompileCache sphereCache = new();
	readonly HashSet<GameObject> jointObjects = new();
	readonly HashSet<GltfElement> shownNodes = new();
	int builtRevision = -1;
	int builtNodeCount = -1;

	/// <summary>The GameObjects of nodes that are skin joints. They carry the bone flags, so the default bone gizmo and icons show them.</summary>
	public IReadOnlyCollection<GameObject> JointObjects => jointObjects;

	/// <summary>The GameObject of a node element, or null.</summary>
	public GameObject GetObject( GltfElement node ) => node is not null && objects.TryGetValue( node, out var go ) && go.IsValid() ? go : null;

	/// <summary>The node element of a GameObject in this preview, or null.</summary>
	public GltfElement GetElement( GameObject go ) => go.IsValid() && elements.TryGetValue( go, out var node ) ? node : null;

	/// <summary>The GameObjects of the root nodes of a scene.</summary>
	public IEnumerable<GameObject> GetRootObjects( GltfElement scene ) => Document.GetChildren( scene ).Select( GetObject ).Where( x => x.IsValid() );

	/// <summary>The GameObjects of nodes that no scene reaches.</summary>
	public IEnumerable<GameObject> GetUnattachedObjects() => Document.GetUnreachableNodes().Select( GetObject ).Where( x => x.IsValid() );

	void BuildScene()
	{
		using var _ = Scene.Push();
		objects.Clear();
		elements.Clear();
		meshModels.Clear();
		jointObjects.Clear();
		shownNodes.Clear();
		sceneRoot = Scene.CreateObject();
		sceneRoot.Name = "glTF";

		var scenes = Document.GetElements( GltfElementKind.Scene );
		var shown = Document.DefaultSceneIndex;
		// Nodes of the shown scene are visible. Every other node exists for the hierarchy but is disabled.
		for ( var i = 0; i < scenes.Count; i++ )
			foreach ( var root in Document.GetChildren( scenes[i] ) )
				CreateObject( root, sceneRoot, i == shown );
		foreach ( var node in Document.GetUnreachableNodes() )
			CreateObject( node, sceneRoot, false );

		foreach ( var skin in Document.GetElements( GltfElementKind.Skin ) )
			foreach ( var joint in Document.GetJointNodes( skin ) )
				if ( GetObject( joint ) is { } go && jointObjects.Add( go ) )
					go.Flags |= GameObjectFlags.Bone | GameObjectFlags.ProceduralBone;

		// A node without mesh or children under a joint is a socket: something attaches there.
		foreach ( var go in jointObjects )
			foreach ( var child in go.Children )
				if ( !jointObjects.Contains( child ) && GetElement( child ) is { MeshIndex: null } node && !Document.GetChildren( node ).Any() )
					child.Flags |= GameObjectFlags.Attachment;

		builtRevision = Document.Revision;
		builtNodeCount = Document.Count( GltfElementKind.Node );
		RebuildModels();
	}

	/// <summary>Make every node GameObject again, after nodes were added or removed (sockets, undo).</summary>
	void RebuildScene()
	{
		using var _ = Scene.Push();
		Selection.Clear();
		previewKey = null;
		RefreshPreview();
		if ( sceneRoot.IsValid() ) sceneRoot.Destroy();
		BuildScene();
	}

	void CreateObject( GltfElement node, GameObject parent, bool shown )
	{
		if ( objects.ContainsKey( node ) ) return; // a node that two parents list, or a cycle
		if ( shown ) shownNodes.Add( node );
		// KHR_node_visibility is the enabled state of the GameObject. Children of a hidden node are hidden by their parent.
		var go = Scene.CreateObject( shown && IsVisible( node ) );
		go.Name = node.Name;
		go.Parent = parent;
		var (position, rotation, scale) = GltfSpatial.ReadPose( node );
		go.LocalPosition = position;
		go.LocalRotation = rotation;
		go.LocalScale = scale;
		objects[node] = go;
		elements[go] = node;
		foreach ( var child in Document.GetChildren( node ) )
			CreateObject( child, go, shown );
	}

	const string VisibilityExtension = "KHR_node_visibility";

	static bool IsVisible( GltfElement node ) =>
		node.Json["extensions"]?[VisibilityExtension]?["visible"] is not JsonValue value || !value.TryGetValue<bool>( out var visible ) || visible;

	static void WriteVisibility( JsonObject json, bool visible )
	{
		var extensions = json["extensions"] as JsonObject;
		if ( visible )
		{
			extensions?.Remove( VisibilityExtension );
			if ( extensions is { Count: 0 } ) json.Remove( "extensions" );
			return;
		}
		if ( extensions is null ) json["extensions"] = extensions = new JsonObject();
		extensions[VisibilityExtension] = new JsonObject { ["visible"] = false };
	}

	/// <summary>Add an empty child node under a node, such as a socket under a joint. Returns its GameObject.</summary>
	public GameObject AddSocket( GameObject parent )
	{
		if ( GetElement( parent ) is not { } parentNode ) return null;
		WriteSceneToDocument();
		var name = $"{parentNode.Name}_socket";
		EditDocument( "Add Socket", () =>
		{
			var node = Document.AddElement( GltfElementKind.Node, new JsonObject { ["name"] = name } );
			Document.Mutate( parentNode, json =>
			{
				if ( json["children"] is not JsonArray children ) json["children"] = children = new JsonArray();
				children.Add( node.Index );
			} );
		} );
		RebuildScene();
		var socket = GetObject( Document.GetElements( GltfElementKind.Node )[^1] );
		if ( socket is not null ) Selection.Set( socket );
		return socket;
	}

	/// <summary>The compile of the mesh previews that runs off the main thread, or null.</summary>
	System.Threading.Tasks.Task<Sandbox.Gltf.GltfDocument> previewJob;

	/// <summary>The document changed while <see cref="previewJob"/> ran; compile again when it is done.</summary>
	bool previewPending;

	/// <summary>Generated level-of-detail nodes of the running preview job, in the order of their preview nodes.</summary>
	List<GltfElement> generatedPreviewNodes = [];

	/// <summary>Preview models of generated level-of-detail nodes, by node.</summary>
	readonly Dictionary<GltfElement, Model> generatedModels = new();

	/// <summary>
	/// Make every mesh model again from the current document, so edited materials show. The expensive compile runs off
	/// the main thread: the hierarchy works at once, and the models replace the previous ones when they are ready.
	/// </summary>
	void RebuildModels()
	{
		if ( previewJob is not null ) { previewPending = true; return; }
		// Generated levels of detail show their reduced mesh: one more preview node per such level, after the meshes.
		generatedPreviewNodes = Document.GetElements( GltfElementKind.Node ).Where( x => x.MeshIndex is not null && x.Json["extras"]?["sbox_lod"] is JsonObject ).ToList();
		var source = Document.CreateMeshesPreviewSource( generatedPreviewNodes.Select( x => (x.MeshIndex.Value, (JsonObject)x.Json["extras"]["sbox_lod"]) ).ToList() );
		var path = Asset.Path;
		var cache = previewCache;
		previewJob = System.Threading.Tasks.Task.Run( () =>
		{
			var document = GltfModelLoader.ReadPreviewDocument( path, source );
			try { GltfNativeCompiler.Prepare( document, cache ); return document; }
			catch { document.Dispose(); throw; }
		} );
	}

	/// <summary>Build the models of a finished preview compile and show them. Runs on the main thread, from <see cref="Tick"/>.</summary>
	void FinishPreviewJob()
	{
		if ( previewJob is not { IsCompleted: true } job ) return;
		previewJob = null;
		Dictionary<int, Model> models = null;
		Sandbox.Gltf.GltfDocument document = null;
		try
		{
			document = job.GetAwaiter().GetResult();
			var (header, blobs) = GltfNativeCompiler.Compile( document, cache: previewCache, compress: false, scene: false );
			// Node i of the preview source shows mesh i.
			models = GltfNativeModelBuilder.BuildPerNode( GltfNativePack.InMemory( header, blobs ), node => $"gltf-preview/{Guid.NewGuid():N}/mesh_{node}" );
		}
		catch ( GltfNativeUnsupportedException e )
		{
			Log.Info( $"'{Asset.Path}': {e.Message} The mesh previews use the source path." );
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"'{Asset.Path}': the compiled mesh previews could not be built, so they use the source path. {e.Message}" );
		}
		finally { document?.Dispose(); }
		// Preview nodes after the meshes are the generated levels of detail, in job order.
		var meshCount = Document.Count( GltfElementKind.Mesh );
		var generated = new Dictionary<GltfElement, Model>();
		if ( models is not null )
			for ( var i = 0; i < generatedPreviewNodes.Count; i++ )
				if ( models.Remove( meshCount + i, out var model ) ) generated[generatedPreviewNodes[i]] = model;
		AssignModels( models, generated );
		if ( previewPending ) { previewPending = false; RebuildModels(); }
	}

	/// <summary>Give every node its mesh model: from the compiled previews, or else one source-path model per mesh.</summary>
	void AssignModels( Dictionary<int, Model> compiled, Dictionary<GltfElement, Model> generated )
	{
		using var _ = Scene.Push();
		var previous = meshModels.Values.Concat( generatedModels.Values ).Where( x => x is not null ).ToList();
		meshModels.Clear();
		generatedModels.Clear();
		if ( compiled is not null ) foreach ( var (mesh, model) in compiled ) meshModels[mesh] = model;
		if ( generated is not null ) foreach ( var (node, model) in generated ) generatedModels[node] = model;
		var failedMeshes = 0;
		string firstFailure = null;
		foreach ( var (node, go) in objects )
		{
			if ( !go.IsValid() ) continue;
			var renderer = go.Components.Get<ModelRenderer>();
			if ( node.MeshIndex is not { } meshIndex )
			{
				renderer?.Destroy();
				continue;
			}
			// A ModelRenderer without a model draws the engine's dev box, which looked like a white cube in the
			// viewport. Start disabled and enable it only once the mesh model is really assigned.
			renderer ??= go.Components.Create<ModelRenderer>( false );
			// The Model is preview data, not something to assign by hand.
			renderer.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved;
			try
			{
				var model = (generatedModels.TryGetValue( node, out var reduced ) ? reduced : GetMeshModel( meshIndex )) ?? throw new InvalidOperationException( $"mesh {meshIndex} has no preview model" );
				renderer.Model = model;
				renderer.Enabled = true;
			}
			catch ( Exception e )
			{
				// The hierarchy and inspectors do not need the preview. A shader that does not compile or a bad
				// material stops the preview of this mesh only; say so once, below, not once per mesh.
				renderer.Destroy();
				failedMeshes++;
				firstFailure ??= e.InnerException?.Message ?? e.Message;
			}
		}
		// Release the previous previews now, with their materials and textures; nothing else holds them.
		foreach ( var model in previous.Where( x => !meshModels.ContainsValue( x ) && !generatedModels.ContainsValue( x ) && x.IsGltfNative ) ) model.Destroy();
		if ( failedMeshes > 0 )
			Log.Warning( $"'{Asset.Path}': {failedMeshes} mesh preview(s) could not be built, so those meshes are not drawn. The hierarchy and the properties still work. First error: {firstFailure}" );
		ApplyLodPreview();
		// The preview shows the document as it was when it was made; make it again.
		RefreshPreview();
	}

	int? previewLodLevel;

	/// <summary>
	/// The level of detail the viewport shows for every node with MSFT_lod: 0 is the node itself, 1 the first lower level.
	/// Null shows the node itself. The viewport has no screen-coverage switching; this chooses the level to look at.
	/// </summary>
	public int? PreviewLodLevel
	{
		get => previewLodLevel;
		set { previewLodLevel = value; ApplyLodPreview(); }
	}

	/// <summary>Show one level of each LOD chain in the viewport, and hide the others so they do not draw on top of each other.</summary>
	void ApplyLodPreview()
	{
		if ( Document is null ) return;
		var nodes = Document.GetElements( GltfElementKind.Node );
		foreach ( var node in nodes )
		{
			var levels = GltfLodEditing.Read( node.Json ).Levels;
			if ( levels.Length == 0 ) continue;
			var shown = Math.Clamp( previewLodLevel ?? 0, 0, levels.Length );
			var chain = levels.Select( x => x.Node ).Prepend( node.Index ).ToArray();
			for ( var level = 0; level < chain.Length; level++ )
			{
				if ( chain[level] < 0 || chain[level] >= nodes.Count ) continue;
				if ( GetObject( nodes[chain[level]] )?.Components.Get<ModelRenderer>() is { } renderer ) renderer.Enabled = level == shown;
			}
		}
	}

	Model GetMeshModel( int meshIndex )
	{
		if ( meshModels.TryGetValue( meshIndex, out var model ) ) return model;
		try
		{
			// The compiled previews had no model for this mesh: use the source path for it.
			model = GltfModelLoader.LoadPreview( Asset.Path, Document.CreateMeshPreviewSource( meshIndex ) );
		}
		catch ( Exception e )
		{
			// Other meshes and the hierarchy still work. Only this mesh has no preview.
			Log.Warning( e, $"Could not show a preview of mesh {meshIndex} in '{Asset.Path}': {e.InnerException?.Message ?? e.Message}" );
		}
		return meshModels[meshIndex] = model;
	}

	/// <summary>A model of one sphere with the current state of a material, for thumbnails. The caller owns it.</summary>
	public Model CreateMaterialPreviewModel( GltfElement material ) =>
		GltfModelLoader.LoadPreview( Asset.Path, Document.CreateMaterialSphereSource( material.Index ), sphereCache );

	/// <summary>
	/// The renderer of the animation preview while an animation is selected. Its pose comes from the node
	/// GameObjects, which Movie Maker animates.
	/// </summary>
	public SkinnedModelRenderer AnimationPreview { get; private set; }

	/// <summary>The selected animation while its preview shows, or null.</summary>
	public GltfElement PreviewAnimation { get; private set; }

	/// <summary>The sample rate of animation data that the editor writes back into the file.</summary>
	public const int AnimationSampleRate = 30;

	/// <summary>Node transforms before an animation preview moved them. They come back when the preview ends.</summary>
	readonly Dictionary<GameObject, Transform> restTransforms = new();

	/// <summary>The movie clip as it was when the animation was last read or written, to find edits.</summary>
	object writtenClip;

	/// <summary>
	/// The keys of the translation, rotation and scale channels of an animation as scene values per node
	/// GameObject: Vector3 positions and scales, Rotation rotations. Movie Maker imports them as keyframes.
	/// </summary>
	public IReadOnlyList<(GameObject Target, string Path, float[] Times, object[] Values, string Interpolation)> GetAnimationKeys( GltfElement animation )
	{
		var keys = new List<(GameObject, string, float[], object[], string)>();
		foreach ( var curve in Document.GetAnimationCurves( animation ) )
		{
			if ( GetObject( curve.Node ) is not { } go ) continue;
			object[] values = curve.Path switch
			{
				"translation" => curve.Values.Select( x => (object)GltfSpatial.PositionToScene( x ) ).ToArray(),
				"rotation" => curve.Values.Select( x => (object)GltfSpatial.RotationToScene( x ) ).ToArray(),
				_ => curve.Values.Select( x => (object)GltfSpatial.ScaleToScene( x ) ).ToArray(),
			};
			keys.Add( (go, curve.Path, curve.Times, values, curve.Interpolation) );
		}
		return keys;
	}

	/// <summary>
	/// Write the node tracks of the movie on the animation preview into the animation, if they changed since
	/// they were read. Movie Maker owns the undo of these edits; the file gets them on save.
	/// </summary>
	void WriteAnimationFromMovie()
	{
		if ( PreviewAnimation is not { } animation || preview?.Components.Get<Sandbox.MovieMaker.MoviePlayer>( true ) is not { } player ) return;
		if ( player.Resource?.Compiled is not { } clip || clip.Tracks.Length == 0 ) return;
		if ( writtenClip is null || ReferenceEquals( writtenClip, clip ) ) return;
		writtenClip = clip;

		var duration = (float)clip.Duration.TotalSeconds;
		var count = GltfAuthoringDocument.SampleCount( duration, AnimationSampleRate );
		var channels = new List<GltfAnimationChannelSamples>();
		foreach ( var track in clip.Tracks.OfType<Sandbox.MovieMaker.Compiled.ICompiledPropertyTrack>() )
		{
			if ( track.Parent is not Sandbox.MovieMaker.IReferenceTrack<GameObject> parent ) continue;
			if ( player.Binder.Get( parent ).Value is not GameObject go || GetElement( go ) is not { } node ) continue;
			var rest = restTransforms.TryGetValue( go, out var r ) ? r : go.LocalTransform;
			var times = Enumerable.Range( 0, count ).Select( i => Sandbox.MovieMaker.MovieTime.FromSeconds( Math.Min( (double)i / AnimationSampleRate, duration ) ) ).ToArray();
			switch ( track )
			{
				case Sandbox.MovieMaker.Compiled.CompiledPropertyTrack<Vector3> position when track.Name == nameof( GameObject.LocalPosition ):
					channels.Add( new( node, "translation", times.Select( t => GltfSpatial.PositionToGltf( position.TryGetValue( t, out var v ) ? v : rest.Position ) ).ToArray() ) );
					break;
				case Sandbox.MovieMaker.Compiled.CompiledPropertyTrack<Rotation> rotation when track.Name == nameof( GameObject.LocalRotation ):
					channels.Add( new( node, "rotation", times.Select( t => GltfSpatial.RotationToGltf( rotation.TryGetValue( t, out var v ) ? v : rest.Rotation ) ).ToArray() ) );
					break;
				case Sandbox.MovieMaker.Compiled.CompiledPropertyTrack<Vector3> scale when track.Name == nameof( GameObject.LocalScale ):
					channels.Add( new( node, "scale", times.Select( t => GltfSpatial.ScaleToGltf( scale.TryGetValue( t, out var v ) ? v : rest.Scale ) ).ToArray() ) );
					break;
			}
		}
		// A track whose keys were all deleted is left out of the clip. Its channel then holds the rest pose.
		foreach ( var (node, path) in Document.GetAnimatedProperties( animation ) )
		{
			if ( channels.Any( x => x.Node == node && x.Path == path ) || GetObject( node ) is not { } go ) continue;
			var rest = restTransforms.TryGetValue( go, out var r ) ? r : go.LocalTransform;
			var value = path switch { "translation" => GltfSpatial.PositionToGltf( rest.Position ), "rotation" => GltfSpatial.RotationToGltf( rest.Rotation ), _ => GltfSpatial.ScaleToGltf( rest.Scale ) };
			channels.Add( new( node, path, Enumerable.Repeat( value, count ).ToArray() ) );
		}
		if ( channels.Count == 0 ) return;
		Document.WriteAnimationSamples( animation, AnimationSampleRate, channels );
		HasUnsavedChanges = true;
	}

	/// <summary>Node renderers that the joint weight preview replaces. They show again when the preview changes.</summary>
	readonly List<ModelRenderer> hiddenRenderers = new();

	/// <summary>The skin joint that a node GameObject is, or null. A node that is a joint of more than one skin gives the first.</summary>
	public GltfJoint JointOf( GameObject go )
	{
		if ( GetElement( go ) is not { } node ) return null;
		foreach ( var skin in Document.GetElements( GltfElementKind.Skin ) )
		{
			var joints = Document.GetJointNodes( skin );
			for ( var i = 0; i < joints.Count; i++ )
				if ( joints[i] == node ) return new GltfJoint( skin, i );
		}
		return null;
	}

	/// <summary>
	/// What the viewport shows for a selection. The scene of GameObjects (null) is the default; some selections
	/// show something else in place of the scene, built from the current document.
	/// </summary>
	object PreviewKeyFor( IReadOnlyList<object> selection )
	{
		// While an animation is open, selecting its nodes and joints (here or in Movie Maker) keeps it open.
		if ( previewKey is GltfElement { Kind.Path: "animations" } && selection.Count > 0 && selection.All( x => x is GameObject go && GetElement( go ) is not null ) )
			return previewKey;
		if ( selection.Count != 1 ) return null;
		return selection[0] switch
		{
			GltfElement { Kind: var kind } element when kind == GltfElementKind.Mesh || kind == GltfElementKind.Animation || kind == GltfElementKind.Skin => element,
			GltfMeshVariant variant => variant,
			GltfJoint joint => joint,
			// A joint node keeps the scene, so the transform gizmo works, and shows its weights on the skinned meshes.
			GameObject go when JointOf( go ) is { } joint => joint,
			_ => null,
		};
	}

	void UpdatePreview()
	{
		var key = PreviewKeyFor( Selection.ToList() );
		if ( Equals( key, previewKey ) ) return;
		previewKey = key;
		RefreshPreview();
	}

	void RefreshPreview()
	{
		using var _ = Scene.Push();
		WriteAnimationFromMovie();
		preview?.Destroy();
		preview = null;
		AnimationPreview = null;
		PreviewAnimation = null;
		writtenClip = null;
		foreach ( var renderer in hiddenRenderers )
			if ( renderer.IsValid() ) renderer.Enabled = true;
		hiddenRenderers.Clear();
		foreach ( var (go, transform) in restTransforms )
			if ( go.IsValid() ) go.LocalTransform = transform;
		restTransforms.Clear();
		// Joint weights and animations show over the scene; every other preview shows in place of it.
		sceneRoot.Enabled = previewKey is null or GltfJoint or GltfElement { Kind.Path: "animations" };
		if ( previewKey is null ) return;

		preview = Scene.CreateObject();
		preview.Name = "Preview";
		try
		{
			switch ( previewKey )
			{
				case GltfElement { Kind: var kind } mesh when kind == GltfElementKind.Mesh:
					Show<ModelRenderer>().Model = GetMeshModel( mesh.Index );
					break;
				case GltfMeshVariant variant:
					var variantRenderer = Show<ModelRenderer>();
					variantRenderer.Model = GetMeshModel( variant.Mesh.Index );
					if ( variantRenderer.Model is not null ) variantRenderer.MaterialVariant = variant.Variant.Index;
					break;
				case GltfElement { Kind: var kind } animation when kind == GltfElementKind.Animation:
					// The node GameObjects are the animation: Movie Maker moves them, the skeleton gizmo follows,
					// and one renderer of the whole model takes its pose from them.
					WriteSceneToDocument();
					foreach ( var (_, go) in objects )
					{
						if ( !go.IsValid() ) continue;
						restTransforms[go] = go.LocalTransform;
						if ( go.Components.Get<ModelRenderer>() is { Enabled: true } renderer )
						{
							renderer.Enabled = false;
							hiddenRenderers.Add( renderer );
						}
					}
					PreviewAnimation = animation;
					AnimationPreview = Show<SkinnedModelRenderer>();
					AnimationPreview.UseAnimGraph = false;
					AnimationPreview.PlayAnimationsInEditorScene = true;
					AnimationPreview.Model = GltfModelLoader.LoadPreview( Asset.Path, Document.CreateSourceBytes() );
					var nodes = Document.GetElements( GltfElementKind.Node );
					AnimationPreview.GltfNodeLocalOverride = index =>
						index < nodes.Count && GetObject( nodes[index] ) is { } node ? GltfSpatial.LocalMatrix( node.LocalPosition, node.LocalRotation, node.LocalScale ) : null;
					break;
				case GltfElement:
					// A skin: the whole model in its rest pose. The scene view draws the skeleton over it.
					WriteSceneToDocument();
					Show<SkinnedModelRenderer>().Model = GltfModelLoader.LoadPreview( Asset.Path, Document.CreateSourceBytes() );
					break;
				case GltfJoint joint:
					foreach ( var node in Document.GetSkinnedNodes( joint.Skin ) )
					{
						if ( GetObject( node )?.Components.Get<ModelRenderer>() is not { Enabled: true } renderer ) continue;
						renderer.Enabled = false;
						hiddenRenderers.Add( renderer );
					}
					Show<ModelRenderer>().Model = GltfModelLoader.LoadPreview( Asset.Path, Document.CreateJointWeightSource( joint.Skin, joint.Joint ) );
					break;
			}
		}
		catch ( Exception e )
		{
			Log.Warning( e, $"Could not preview {previewKey}: {e.InnerException?.Message ?? e.Message}" );
		}
	}

	T Show<T>() where T : Component, new()
	{
		var renderer = preview.Components.Create<T>();
		renderer.Flags |= ComponentFlags.Hidden | ComponentFlags.NotSaved;
		return renderer;
	}

	/// <summary>
	/// Write GameObject transforms and names into the node JSON. Like a prefab, the scene is the truth for
	/// these while editing (the scene undo system records them), so this runs on save, not every frame.
	/// </summary>
	void WriteSceneToDocument()
	{
		foreach ( var (node, go) in objects )
		{
			if ( !go.IsValid() ) continue;
			// While an animation shows, the GameObjects hold the animated pose; the node keeps its rest pose.
			var transform = restTransforms.TryGetValue( go, out var rest ) ? rest : go.LocalTransform;
			var position = transform.Position;
			var rotation = transform.Rotation;
			var scale = transform.Scale;
			if ( shownNodes.Contains( node ) && go.Enabled != IsVisible( node ) )
			{
				var visible = go.Enabled;
				Document.Mutate( node, json => WriteVisibility( json, visible ), bumpRevision: false );
			}
			if ( !GltfSpatial.IsPose( node, position, rotation, scale ) )
				Document.Mutate( node, json => GltfSpatial.WritePose( json, position, rotation, scale ), bumpRevision: false );
			if ( go.Name != node.Name && !string.IsNullOrWhiteSpace( go.Name ) )
			{
				var name = go.Name;
				Document.Mutate( node, json => json["name"] = name, bumpRevision: false );
			}
		}
	}

	/// <summary>
	/// Keep the preview and the document in step. The scene view calls it every frame:
	/// it rebuilds models after document edits and shows what the selection previews.
	/// </summary>
	// A new session shows the model: the editor camera starts at the origin, inside most models, and sees only sky.
	// The viewport hooks OnFrameTo after the session exists, so frame on the first ticks that have bounds.
	int framingTicks = 120;

	void FrameModelOnOpen()
	{
		if ( framingTicks <= 0 || OnFrameTo is null ) return;
		framingTicks--;
		var boxes = Scene.GetAllComponents<ModelRenderer>().Where( x => x.Enabled && x.Model is not null ).Select( x => x.Bounds ).ToArray();
		if ( boxes.Length == 0 ) return;
		var bounds = BBox.FromBoxes( boxes );
		if ( bounds.Size.Length < 0.001f ) return;
		framingTicks = 0;
		FrameTo( bounds );
	}

	public override void Tick()
	{
		base.Tick();
		FinishPreviewJob();
		FrameModelOnOpen();
		if ( Document is null || !sceneRoot.IsValid() ) return;

		if ( Document.Revision != builtRevision )
		{
			builtRevision = Document.Revision;
			if ( Document.Count( GltfElementKind.Node ) != builtNodeCount ) RebuildScene();
			else RebuildModels();
		}

		UpdatePreview();

		// The first clip after the import is the baseline; later clips are edits to write back.
		if ( PreviewAnimation is not null && writtenClip is null && preview?.Components.Get<Sandbox.MovieMaker.MoviePlayer>( true )?.Resource?.Compiled is { Tracks.Length: > 0 } clip )
			writtenClip = clip;
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
		// An MSFT_lod without a level is not valid glTF, so adding the group adds a first level.
		if ( facetType == typeof( GltfFacetTypes.NodeLodFacet ) ) { AddLodLevel( element ); return; }
		var facet = GltfFacets.Create( facetType, element );
		Edit( element, $"Add {facetType.Name}", json => facet.Write( facet.Resolve( json, true ), null ) );
	}

	/// <summary>The MSFT_lod levels of a node. Empty if it has none.</summary>
	public GltfLodLevels GetNodeLods( GltfElement node ) => GltfLodEditing.Read( node.Json );

	/// <summary>
	/// Nodes that can be a level of <paramref name="node"/>. With <paramref name="includeCurrent"/>, the levels it already
	/// has are included, for replacing one of them.
	/// </summary>
	public IReadOnlyList<GltfElement> GetLodCandidates( GltfElement node, bool includeCurrent = false )
	{
		var nodes = Document.GetElements( GltfElementKind.Node );
		return GltfLodEditing.EligibleNodes( nodes.Select( x => x.Json ).ToList(), node.Index, includeCurrent ).Select( x => nodes[x] ).ToList();
	}

	/// <summary>Replace the MSFT_lod levels of a node, with their thresholds. No levels removes the extension.</summary>
	public void SetNodeLods( GltfElement node, GltfLodLevels levels, string title = "Edit Level of Detail" )
	{
		Edit( node, title, json => GltfLodEditing.Write( json, levels ) );
		ApplyLodPreview();
	}

	/// <summary>
	/// Add a lower level to a node. Without <paramref name="existing"/> this makes a new node that copies the mesh, skin and
	/// transform of <paramref name="node"/>; point it at a lower-detail mesh afterwards. A LOD node is not part of the
	/// scene hierarchy, so it only shows in the "unattached" part of the tree.
	/// </summary>
	public GltfElement AddLodLevel( GltfElement node, GltfElement existing = null ) => AddLodLevel( node, existing, null );

	/// <summary>
	/// Add a level of detail that the import makes from this node's own mesh by removing triangles. The level node has
	/// the node's mesh and transform and an <c>extras.sbox_lod</c> object with the reduction settings, which its
	/// inspector shows. Each new level keeps half the triangles of the one before.
	/// </summary>
	public GltfElement CreateGeneratedLod( GltfElement node )
	{
		var count = GltfLodEditing.Read( node.Json ).Levels.Length;
		var settings = new JsonObject { ["ratio"] = MathF.Pow( 0.5f, count + 1 ), ["error"] = 0.01f, ["lockBorder"] = false };
		var level = AddLodLevel( node, null, settings );
		if ( GetObject( level ) is { } go ) Selection.Set( go );
		return level;
	}

	GltfElement AddLodLevel( GltfElement node, GltfElement existing, JsonObject generated )
	{
		if ( node.Kind != GltfElementKind.Node ) throw new ArgumentException( "Only nodes have levels of detail.", nameof( node ) );
		WriteSceneToDocument();
		GltfElement level = existing;
		EditDocument( "Add Level of Detail", () =>
		{
			var current = GltfLodEditing.Read( node.Json );
			if ( level is null )
			{
				var json = GltfLodEditing.CreateLevelNode( node.Json, node.Name, current.Levels.Length + 1 );
				if ( generated is not null ) json["extras"] = new JsonObject { ["sbox_lod"] = generated };
				level = Document.AddElement( GltfElementKind.Node, json );
			}
			Document.Mutate( node, json => GltfLodEditing.Write( json, GltfLodEditing.Add( current, level.Index ) ) );
		} );
		RebuildScene();
		return level;
	}

	/// <summary>Make a node use another mesh. A LOD node uses this to point at its lower-detail mesh.</summary>
	public void SetNodeMesh( GltfElement node, int mesh )
	{
		Edit( node, "Set Mesh", json => { if ( mesh < 0 ) json.Remove( "mesh" ); else json["mesh"] = mesh; } );
		RebuildScene();
	}

	/// <summary>Remove a facet and its data.</summary>
	public void RemoveFacet( GltfFacet facet )
	{
		if ( !facet.IsRemovable ) throw new InvalidOperationException( "This facet is the element itself and cannot be removed." );
		Edit( facet.Element, $"Remove {facet.GetType().Name}", facet.RemoveFrom );
	}

	/// <summary>Point a mesh primitive at a material, or at none with -1.</summary>
	public void SetPrimitiveMaterial( GltfElement mesh, int primitive, int material ) => Edit( mesh, "Set Material", json =>
	{
		if ( json["primitives"] is not JsonArray primitives || primitive < 0 || primitive >= primitives.Count || primitives[primitive] is not JsonObject target ) return;
		if ( material < 0 ) target.Remove( "material" );
		else target["material"] = material;
	} );

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

	/// <summary>Write the edited document into the .gltf or .glb and reimport. Save As is not supported.</summary>
	public override void Save( bool saveAs )
	{
		if ( !GltfAssetImport.CanImport( Asset ) )
		{
			Log.Warning( $"'{Asset.Path}' is not an editable project asset. It was not saved." );
			return;
		}

		EditorEvent.Run( "scene.beforesave", Scene );
		WriteAnimationFromMovie();
		WriteSceneToDocument();
		var bytes = Document.CreateSourceBytes();
		File.WriteAllBytes( Asset.AbsoluteSourcePath, bytes );
		Document.MarkSaved( bytes );
		// Older versions of this editor kept edits as patches in the .meta file. The file now holds them.
		if ( GltfOverrides.Read( Asset ) is not null ) GltfOverrides.Write( Asset, null );
		try
		{
			GltfAssetImport.Reimport( Asset );
		}
		catch ( Exception e )
		{
			// The file is saved; the next import retries. Keep the unsaved mark off so the user is not told to save again.
			Log.Warning( e, $"'{Asset.Path}' was saved, but the import failed: {e.Message}" );
		}
		HasUnsavedChanges = false;
		EditorEvent.Run( "scene.saved", Scene );
	}

	/// <summary>Use a shader that the glTF file can express. See <see cref="GltfMaterialShaders"/>.</summary>
	public void SetMaterialShader( GltfElement material, string shaderPath )
	{
		var shader = GltfMaterialShaders.All.FirstOrDefault( x => string.Equals( x.Path, shaderPath, StringComparison.OrdinalIgnoreCase ) )
			?? throw new NotImplementedException( $"'{shaderPath}' cannot be stored in a glTF material yet. glTF has no field for a shader; this needs an s&box material extension." );
		Edit( material, $"Set Shader {shader.Title}", json =>
		{
			foreach ( var other in GltfMaterialShaders.All.Where( x => x.Selector is not null && x != shader ) )
				GltfFacets.Create( other.Selector, material ).RemoveFrom( json );
			if ( shader.Selector is not null )
			{
				var facet = GltfFacets.Create( shader.Selector, material );
				var target = facet.Resolve( json, true );
				facet.Read( target );
				facet.Write( target, null );
			}
		} );
	}

	/// <summary>Make a change that can add elements. Undo restores the whole document.</summary>
	void EditDocument( string title, Action change )
	{
		var before = Document.SnapshotRoot();
		change();
		var after = Document.SnapshotRoot();
		if ( JsonNode.DeepEquals( before, after ) ) return;
		UndoSystem.Insert( title, () => { Document.RestoreRoot( before ); HasUnsavedChanges = true; }, () => { Document.RestoreRoot( after ); HasUnsavedChanges = true; } );
		HasUnsavedChanges = true;
	}

	/// <summary>
	/// Set how a texture is sampled. glTF keeps these settings in shared samplers: the texture uses an existing
	/// sampler with the same settings, or a new one. Default settings need no sampler.
	/// </summary>
	public void SetTextureSampling( GltfElement texture, GltfTextureSampling sampling ) => EditDocument( "Edit Texture Sampling", () =>
	{
		var index = sampling.IsDefault ? -1 : Document.FindOrAddSampler( sampling );
		Document.Mutate( texture, json => { if ( index < 0 ) json.Remove( "sampler" ); else json["sampler"] = index; } );
	} );

	/// <summary>Make a texture show another image of the file.</summary>
	public void SetTextureImage( GltfElement texture, GltfElement image ) =>
		Edit( texture, "Set Texture Image", json => Document.SetTextureSource( json, image.Index ) );

	/// <summary>Make a texture show an image file. The file is added as an image once; textures that use the same file share it.</summary>
	public void SetTextureImageFile( GltfElement texture, string imagePath ) => EditDocument( "Set Texture Image", () =>
	{
		var (uri, name) = ProjectImageUri( imagePath );
		var image = Document.FindOrAddImage( uri, name );
		Document.Mutate( texture, json => Document.SetTextureSource( json, image ) );
	} );

	/// <summary>Use a material for one primitive of a mesh in one variant, or the primitive's default material with -1.</summary>
	public void SetVariantMaterial( GltfElement mesh, int primitive, GltfElement variant, int material ) => Edit( mesh, "Set Variant Material", json =>
	{
		if ( json["primitives"]?[primitive] is not JsonObject target ) return;
		var extensions = target["extensions"] as JsonObject ?? (JsonObject)(target["extensions"] = new JsonObject());
		var variants = extensions["KHR_materials_variants"] as JsonObject ?? (JsonObject)(extensions["KHR_materials_variants"] = new JsonObject());
		var mappings = variants["mappings"] as JsonArray ?? (JsonArray)(variants["mappings"] = new JsonArray());
		// A variant appears in at most one mapping of a primitive.
		foreach ( var mapping in mappings.OfType<JsonObject>() )
			if ( mapping["variants"] is JsonArray list )
				foreach ( var item in list.Where( x => x is JsonValue v && v.TryGetValue<int>( out var i ) && i == variant.Index ).ToList() ) list.Remove( item );
		if ( material >= 0 )
		{
			var existing = mappings.OfType<JsonObject>().FirstOrDefault( x => x["material"] is JsonValue m && m.TryGetValue<int>( out var i ) && i == material );
			if ( existing is null ) mappings.Add( existing = new JsonObject { ["material"] = material, ["variants"] = new JsonArray() } );
			((JsonArray)existing["variants"]).Add( variant.Index );
		}
		foreach ( var empty in mappings.OfType<JsonObject>().Where( x => x["variants"] is not JsonArray { Count: > 0 } ).ToList() ) mappings.Remove( empty );
		if ( mappings.Count == 0 ) extensions.Remove( "KHR_materials_variants" );
		if ( extensions.Count == 0 ) target.Remove( "extensions" );
	} );

	/// <summary>
	/// The relative URI of an image file for this glTF. A file outside the folder of the glTF is copied next to it,
	/// so the URI works in the project.
	/// </summary>
	(string Uri, string Name) ProjectImageUri( string imagePath )
	{
		var folder = Path.GetDirectoryName( Asset.AbsoluteSourcePath );
		var full = Path.GetFullPath( imagePath );
		if ( !full.StartsWith( folder + Path.DirectorySeparatorChar, StringComparison.Ordinal ) )
		{
			var target = Path.Combine( folder, Path.GetFileName( full ) );
			for ( var i = 1; File.Exists( target ) && !File.ReadAllBytes( target ).AsSpan().SequenceEqual( File.ReadAllBytes( full ) ); i++ )
				target = Path.Combine( folder, $"{Path.GetFileNameWithoutExtension( full )}_{i}{Path.GetExtension( full )}" );
			if ( !File.Exists( target ) ) File.Copy( full, target );
			full = target;
		}
		var relative = Path.GetRelativePath( folder, full ).Replace( '\\', '/' );
		return (string.Join( '/', relative.Split( '/' ).Select( Uri.EscapeDataString ) ), Path.GetFileNameWithoutExtension( full ));
	}

	/// <summary>
	/// Add an image file to the glTF as a new image and texture, and return the texture.
	/// A file outside the folder of the glTF is copied next to it, so the relative URI works in the project.
	/// </summary>
	public GltfElement ImportTexture( string imagePath )
	{
		EditDocument( "Import Texture", () =>
		{
			var (uri, name) = ProjectImageUri( imagePath );
			var image = Document.FindOrAddImage( uri, name );
			Document.AddElement( GltfElementKind.Texture, new JsonObject { ["name"] = name, ["source"] = image } );
		} );
		return Document.GetElements( GltfElementKind.Texture )[^1];
	}

	/// <summary>Discard unsaved edits and read the source and saved overrides again.</summary>
	public override void Reload()
	{
		Document = LoadDocument( Asset );
		Selection.Clear();
		previewKey = null;
		preview = null;
		AnimationPreview = null;
		PreviewAnimation = null;
		writtenClip = null;
		hiddenRenderers.Clear();
		restTransforms.Clear();
		if ( sceneRoot.IsValid() ) sceneRoot.Destroy();
		BuildScene();
		InitUndo();
		Selection.Clear();
		HasUnsavedChanges = false;
	}

	/// <summary>Read the source of a glTF asset with its overrides applied.</summary>
	public static GltfAuthoringDocument LoadDocument( Asset asset )
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
