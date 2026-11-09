namespace Sandbox.SceneRenderer.Features;

internal sealed partial class MeshRenderFeature
{
	internal const string SkinWeightFormatFeature = "F_SKIN_WEIGHT_FORMAT";
	private readonly Dictionary<GltfSkinWeightFormat, Material> skinningVariants = new();
	private Material skinningVariantTemplate;
	private Shader skinningVariantShader;
	private readonly record struct TemplateKey( IntPtr MaterialBinding, long MaterialRevision, IntPtr ShaderBinding, long ShaderRevision );
	private TemplateKey skinningVariantKey;

	/// <summary>
	/// Material/shader binding identities, reload revisions and scalar format form the cache key. Variants belong
	/// to this render feature; the shared default material is never changed or freed.
	/// </summary>
	private Material SkinningShaderFor( GltfSkinWeightFormat format )
	{
		if ( skinningShader is not { IsValid: true, IsError: false } ) skinningShader = Material.Load( SkinningShader );
		if ( skinningShader is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "The stock compute skinning material is unavailable." );
		var shader = skinningShader.Shader;
		if ( shader is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "The stock compute skinning shader is unavailable." );
		var key = new TemplateKey( skinningShader.native.GetBindingPtr(), skinningShader.ReloadRevision, shader.native.GetBindingPtr(), shader.ReloadRevision );
		if ( !ReferenceEquals( skinningVariantTemplate, skinningShader ) || !ReferenceEquals( skinningVariantShader, shader ) || skinningVariantKey != key )
		{
			DisposeSkinningVariants();
			skinningVariantTemplate = skinningShader;
			// Retain the canonical shader wrapper so native in-place reload callbacks
			// can update its revision after the resource cache's retention expires.
			skinningVariantShader = shader;
			skinningVariantKey = key;
		}
		if ( format == GltfSkinWeightFormat.Unorm8 ) return skinningShader;
		if ( format is not (GltfSkinWeightFormat.Float32 or GltfSkinWeightFormat.Unorm16) ) throw new ArgumentOutOfRangeException( nameof( format ) );
		if ( skinningVariants.TryGetValue( format, out var existing ) )
		{
			if ( existing is { IsValid: true, IsError: false } && existing.GetFeature( SkinWeightFormatFeature ) == (int)format ) return existing;
			existing?.Destroy();
			skinningVariants.Remove( format );
		}
		var material = skinningShader.CreateCopy( $"scene_skinning_{format}_{Guid.NewGuid():N}" );
		if ( material is not { IsValid: true, IsError: false } ) throw new InvalidOperationException( "Could not create the stock compute skinning format variant." );
		try
		{
			material.SetFeature( SkinWeightFormatFeature, (int)format );
			if ( material.GetFeature( SkinWeightFormatFeature ) != (int)format ) throw new InvalidOperationException( "The stock skinning shader did not accept its declared weight format." );
			skinningVariants.Add( format, material );
			return material;
		}
		catch
		{
			material.Destroy();
			throw;
		}
	}

	private void DisposeSkinningVariants()
	{
		foreach ( var material in skinningVariants.Values ) material?.Destroy();
		skinningVariants.Clear();
	}

	private void DisposeSkinningMaterials()
	{
		DisposeSkinningVariants();
		skinningVariantTemplate = null;
		skinningVariantShader = null;
		skinningVariantKey = default;
		skinningShader = null;
	}
}
