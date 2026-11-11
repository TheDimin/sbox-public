using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

public partial class ModelRenderer
{
	private GltfVariantCatalog.Selection gltfVariantSelection;
	private GltfManagedModelResource gltfVariantResource;
	private int? nativeVariant;

	/// <summary>
	/// Select one authored glTF material variant by source index for this renderer.
	/// Null restores the primitive defaults. Indices identify one resource generation.
	/// </summary>
	public int? MaterialVariant
	{
		get => Model?.GltfResource is null && Model?.GltfNativeVariants is not null ? nativeVariant : gltfVariantSelection?.Index;
		set
		{
			ThreadSafe.AssertIsMainThread();
			var resource = Model?.GltfResource;
			if ( resource is null && Model?.GltfNativeVariants is { } variants )
			{
				// A compiled native model keeps each variant as a native material group.
				if ( value is int index && (uint)index >= (uint)variants.Count ) throw new ArgumentOutOfRangeException( nameof( value ) );
				nativeVariant = value;
				// A scene shows the variant through each node's material group; a single model through its own groups.
				if ( Model.GltfNativeScene is not null ) UpdateGltfScene();
				else MaterialGroup = value is int chosen ? GltfNativeCompiler.GroupName( chosen ) : "default";
				return;
			}
			if ( resource is null )
			{
				if ( value is not null ) throw new NotSupportedException( "Material variants require a managed glTF model." );
				gltfVariantSelection = null; gltfVariantResource = null; return;
			}
			var selected = resource.Variants.Select( value );
			if ( value is not null && !resource.Asset.HasProjectedVariants )
				throw new NotSupportedException( "Projected glTF material variants are not configured for this asset." );
			gltfInstance?.SetVariant( value );
			gltfVariantSelection = selected; gltfVariantResource = resource;
		}
	}

	private sealed record GltfVariantReloadPlan( GltfVariantCatalog.Selection Previous,
		GltfManagedModelResource PreviousResource, GltfVariantCatalog.Selection Candidate,
		GltfManagedModelResource Replacement );

	private void ClearGltfVariantSelection() { gltfVariantSelection = null; gltfVariantResource = null; }
	private object CaptureGltfVariantState() => (gltfVariantSelection, gltfVariantResource);
	private void RestoreGltfVariantState( object state )
	{
		(gltfVariantSelection, gltfVariantResource) = ((GltfVariantCatalog.Selection, GltfManagedModelResource))state;
	}

	// These four hooks participate in the model-wide managed reload transaction.
	// A duplicate source name may be selected by index within one generation,
	// but it cannot be transferred safely to another generation.
	private object PrepareGltfVariantReload( GltfManagedModelResource replacement )
	{
		ArgumentNullException.ThrowIfNull( replacement );
		var candidate = ReferenceEquals( gltfVariantResource, replacement )
			? gltfVariantSelection : replacement.Variants.Transfer( gltfVariantSelection );
		return new GltfVariantReloadPlan( gltfVariantSelection, gltfVariantResource, candidate, replacement );
	}

	private void ApplyGltfVariantReload( object plan, GltfGpuInstance replacement )
	{
		var variant = (GltfVariantReloadPlan)plan;
		if ( variant.Candidate is not null && !variant.Replacement.Asset.HasProjectedVariants )
			throw new NotSupportedException( "Projected glTF material variants are not configured for the replacement asset." );
		if ( replacement is not null ) replacement.SetVariant( variant.Candidate?.Index );
	}

	private void CommitGltfVariantReload( object plan )
	{
		var variant = (GltfVariantReloadPlan)plan;
		gltfVariantSelection = variant.Candidate; gltfVariantResource = variant.Replacement;
	}

	private void RollbackGltfVariantReload( object plan )
	{
		var variant = (GltfVariantReloadPlan)plan;
		gltfVariantSelection = variant.Previous; gltfVariantResource = variant.PreviousResource;
	}
}
