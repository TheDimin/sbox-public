namespace Sandbox;

public partial class ModelRenderer
{
	private GltfNativeScene gltfSceneShown;
	private SceneObject[] gltfSceneObjects = [];

	/// <summary>
	/// Draw a static compiled glTF scene as one native scene object per node, placed relative to this renderer. Each
	/// node culls and switches LOD on its own and shows its own material group. The renderer's own scene object draws
	/// nothing for such a model; its state is mirrored onto the node objects.
	/// </summary>
	private void UpdateGltfScene()
	{
		var scene = Active && _sceneObject.IsValid() ? Model?.GltfNativeScene : null;
		if ( !ReferenceEquals( scene, gltfSceneShown ) )
		{
			ClearGltfScene();
			if ( scene is null ) return;
			gltfSceneShown = scene;
			gltfSceneObjects = scene.Nodes.Select( node => new SceneObject( Scene.SceneWorld, scene.Geometries[node.Geometry], Transform.InterpolatedWorld.ToWorld( node.Transform ) ) ).ToArray();
		}
		if ( scene is null ) return;
		var castShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		for ( var i = 0; i < gltfSceneObjects.Length; i++ )
		{
			var node = scene.Nodes[i];
			var target = gltfSceneObjects[i];
			if ( !target.IsValid() ) continue;
			target.Component = this;
			target.ColorTint = Tint;
			target.Flags.CastShadows = castShadows;
			target.Flags.IsStatic = GameObject.IsStatic;
			target.Tags.SetFrom( GameObject.Tags );
			target.LodOverride = _lodOverride ?? -1;
			RenderOptions.Apply( target );
			if ( RenderType == ShadowRenderType.ShadowsOnly ) target.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, true );
			if ( MaterialOverride is not null ) target.SetMaterialOverride( MaterialOverride );
			else
			{
				target.SetMaterialOverride( null );
				target.SetMaterialGroup( nativeVariant is int variant && variant < node.VariantGroups.Length ? node.VariantGroups[variant] : node.Group );
			}
		}
		UpdateGltfSceneTransforms();
	}

	private void UpdateGltfSceneTransforms()
	{
		if ( gltfSceneShown is null ) return;
		var world = Transform.InterpolatedWorld;
		for ( var i = 0; i < gltfSceneObjects.Length; i++ )
			if ( gltfSceneObjects[i].IsValid() ) gltfSceneObjects[i].Transform = world.ToWorld( gltfSceneShown.Nodes[i].Transform );
	}

	private void ClearGltfScene()
	{
		foreach ( var target in gltfSceneObjects ) if ( target.IsValid() ) target.Delete();
		gltfSceneObjects = [];
		gltfSceneShown = null;
	}
}
