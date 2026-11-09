using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

public partial class ModelRenderer
{
	private GltfGpuInstance gltfInstance;
	private GltfManagedModelResource gltfInstanceResource;
	private object gltfPendingCreationVariant;
	internal GltfGpuInstance GltfInstance => gltfInstance;

	private SceneObject CreateGltfSceneObject( GltfManagedModelResource resource )
	{
		var variant = PrepareGltfVariantReload( resource );
		DisposeGltfInstance();
		GltfGpuInstance instance = null;
		try
		{
			instance = resource.CreateInstance( Scene.SceneWorld, initiallyVisible: false );
			instance.Placement = Matrix.FromTransform( Transform.InterpolatedWorld );
			ApplyGltfVariantReload( variant, instance );
			gltfInstance = instance;
			gltfInstanceResource = resource;
			gltfPendingCreationVariant = variant;
			return instance.SceneObject;
		}
		catch { RollbackGltfVariantReload( variant ); instance?.Dispose(); throw; }
	}

	private void CommitGltfCreationVariant()
	{
		if ( gltfPendingCreationVariant is not { } variant ) return;
		CommitGltfVariantReload( variant ); gltfPendingCreationVariant = null;
	}

	private void RollbackGltfCreationVariant()
	{
		if ( gltfPendingCreationVariant is not { } variant ) return;
		RollbackGltfVariantReload( variant ); gltfPendingCreationVariant = null;
	}

	/// <summary>Returns true when this update belongs to the managed model path.</summary>
	private bool UpdateGltfObject()
	{
		var resource = Model?.GltfResource;
		if ( resource is null )
		{
			if ( gltfInstance is null ) return false;
			var replacement = gltfReloadPlan?.CandidateObject ?? CreateSceneObject( Model ?? Model.Load( "models/dev/box.vmdl" ) );
			ReplaceGltfSceneObject( replacement, null, null );
			return true;
		}

		// Assignment to a disabled component must not realize GPU state.
		if ( !_sceneObject.IsValid() ) return true;
		if ( !ReferenceEquals( gltfInstanceResource, resource ) )
		{
			if ( gltfReloadPlan is not null )
			{
				var prepared = gltfReloadPlan.Candidate ?? throw new InvalidOperationException( "Missing prepared glTF instance." );
				ReplaceGltfSceneObject( prepared.SceneObject, prepared, resource );
				return true;
			}
			var variant = PrepareGltfVariantReload( resource );
			GltfGpuInstance replacement = null;
			try
			{
				replacement = resource.CreateInstance( Scene.SceneWorld, initiallyVisible: false );
				replacement.Placement = Matrix.FromTransform( Transform.InterpolatedWorld );
				ApplyGltfVariantReload( variant, replacement );
				ReplaceGltfSceneObject( replacement.SceneObject, replacement, resource );
				CommitGltfVariantReload( variant );
			}
			catch { RollbackGltfVariantReload( variant ); replacement?.Dispose(); throw; }
			return true;
		}

		UpdateGltfPlacement();
		_sceneObject.ColorTint = Tint;
		_sceneObject.Flags.CastShadows = RenderType == ShadowRenderType.On || RenderType == ShadowRenderType.ShadowsOnly;
		_sceneObject.Flags.IsStatic = GameObject.IsStatic;
		_sceneObject.RenderingEnabled = gltfReloadPlan?.Candidate == gltfInstance ? false : Model.HasRenderMeshes();
		RenderOptions.Apply( _sceneObject );
		if ( RenderType == ShadowRenderType.ShadowsOnly ) _sceneObject.Flags.SetFlag( Rendering.SceneObjectFlags.ExcludeGameLayer, true );
		ApplyGltfMaterialOverrides();
		return true;
	}

	private void ReplaceGltfSceneObject( SceneObject replacement, GltfGpuInstance replacementInstance, GltfManagedModelResource resource )
	{
		var previous = _sceneObject;
		var previousInstance = gltfInstance;
		var previousResource = gltfInstanceResource;
		BackupRenderAttributes( previous?.Attributes );
		gltfInstance = replacementInstance;
		gltfInstanceResource = resource;
		_sceneObject = replacement;
		try { OnSceneObjectCreated( replacement ); }
		catch
		{
			gltfInstance = previousInstance;
			gltfInstanceResource = previousResource;
			_sceneObject = previous;
			try
			{
				if ( replacementInstance is not null )
				{
					if ( gltfReloadPlan?.Candidate != replacementInstance ) replacementInstance.Dispose();
				}
				else if ( gltfReloadPlan is null || !ReferenceEquals( gltfReloadPlan.CandidateObject, replacement ) ) replacement?.Delete();
			}
			finally { if ( previous.IsValid() ) base.OnSceneObjectCreated( previous ); }
			throw;
		}
		if ( gltfReloadPlan is { } reload && ReferenceEquals( reload.Candidate, replacementInstance ) && ReferenceEquals( reload.CandidateObject, replacement ) )
		{
			reload.Applied = true;
			return;
		}
		// Keep the previous valid presentation until all replacement callbacks,
		// material overrides and component state have been applied successfully.
		if ( previousInstance is not null ) previousInstance.Dispose();
		else previous?.Delete();
	}

	private void UpdateGltfPlacement()
	{
		if ( gltfInstance is null ) return;
		System.Numerics.Matrix4x4 placement = Matrix.FromTransform( Transform.InterpolatedWorld );
		if ( gltfInstance.Placement != placement ) gltfInstance.Placement = placement;
		// GPU positions already include placement. Never apply it again in native.
		_sceneObject.Transform = global::Transform.Zero;
	}

	private bool ApplyGltfMaterialOverrides()
	{
		if ( gltfInstance is null ) return false;
		if ( taggedMaterialOverrides is { Count: > 0 } )
			throw new NotSupportedException( "Tagged material overrides for managed glTF models are not implemented yet." );
		var overrides = new Material[gltfInstanceResource.MaterialSlotSourceIndices.Count];
		for ( var i = 0; i < overrides.Length; i++ )
			overrides[i] = MaterialOverride ?? _materialAccessor?.GetOverride( i );
		gltfInstance.SetMaterialOverrides( overrides );
		return true;
	}

	private void DisposeGltfInstance()
	{
		var instance = gltfInstance;
		gltfInstance = null;
		gltfInstanceResource = null;
		instance?.Dispose();
	}
}
