using Sandbox.Engine;
using Sandbox.Gltf.DirectGpu;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>One texture input of a material recipe: the compiled texture resource and how the shader samples it.</summary>
internal sealed record GltfNativeTextureSlot( string Parameter, byte[] Resource, SamplerState Sampler, string SamplerParameter );

/// <summary>
/// Everything a glTF material sets on its shader template, without any native resource. The import compiler
/// stores recipes in the compiled file; the loader realizes them. Both use the adapters that build materials, so a
/// compiled material equals the one the adapter makes from source.
/// </summary>
internal sealed record GltfNativeMaterialRecipe(
	string Shader,
	KeyValuePair<string, int>[] TemplateFeatures,
	KeyValuePair<string, int>[] Features,
	KeyValuePair<string, Vector4>[] Vectors,
	KeyValuePair<string, float>[] Scalars,
	GltfNativeTextureSlot[] Textures )
{
	/// <summary>Acquire the textures and the shared material. The lease releases both.</summary>
	internal GltfMaterialLease<Material> Realize( Material template, Action<Material> verify = null )
	{
		ArgumentNullException.ThrowIfNull( template );
		ThreadSafe.AssertIsMainThread();
		if ( Graphics.IsActive ) throw new Gltf.GltfException( "glTF materials cannot be realized during rendering." );
		var ownerContext = GlobalContext.Current;
		var textures = new List<GltfMaterialLease<Texture>>();
		GltfMaterialLease<Material> material = null;
		try
		{
			textures.AddRange( GltfNativeTextureCache.AcquireBatch( Textures.Select( x => x.Resource ).ToArray() ) );
			var bindings = new List<GltfMaterialTextureBinding>( Textures.Length );
			for ( var i = 0; i < Textures.Length; i++ )
			{
				var texture = textures[i].Value;
				bindings.Add( new( Textures[i].Parameter, texture, texture.DirtyVersion, Textures[i].Sampler, Textures[i].SamplerParameter ) );
			}
			material = GltfMaterialCache.Shared.Acquire( new GltfMaterialKey( template, 0, Features, Vectors, bindings, Scalars ) );
			verify?.Invoke( material.Value );
			var retained = material;
			return new GltfMaterialLease<Material>( material.Value, () =>
			{
				using var scope = new GlobalContext.GlobalContextScope( ownerContext );
				GltfGpuCleanup.ReleaseAll( ReleaseSteps( retained, textures ) );
			}, () => ThreadSafe.AssertIsMainThread() );
		}
		catch ( Exception original )
		{
			GltfGpuCleanup.Rollback( original, ReleaseSteps( material, textures ) );
			throw;
		}
	}

	private static IEnumerable<Action> ReleaseSteps( GltfMaterialLease<Material> material, List<GltfMaterialLease<Texture>> textures )
	{
		if ( material is not null ) yield return material.Dispose;
		foreach ( var texture in textures ) yield return texture.Dispose;
	}
}

/// <summary>Shared shader templates for compiled recipes: one per shader and template feature set, like the source loader's templates.</summary>
internal static class GltfNativeMaterialTemplates
{
	private static readonly Dictionary<string, Material> templates = new( StringComparer.Ordinal );

	internal static Material Get( string shader, IReadOnlyList<KeyValuePair<string, int>> features )
	{
		ThreadSafe.AssertIsMainThread();
		var key = shader + "|" + string.Join( ",", features.OrderBy( x => x.Key, StringComparer.Ordinal ).Select( x => x.Key + "=" + x.Value ) );
		if ( templates.TryGetValue( key, out var existing ) && existing.IsValid ) return existing;
		var source = Material.FromShader( shader );
		if ( source is not { IsValid: true, IsError: false } ) throw new Gltf.GltfException( $"The glTF material template '{shader}' is unavailable." );
		var template = source.CreateCopy( "gltf_native_template_" + Guid.NewGuid().ToString( "N" ) );
		foreach ( var (name, value) in features ) template.SetFeature( name, value );
		templates[key] = template;
		return template;
	}

	/// <summary>
	/// The template of a source material: the same shader and template features the source loader selects.
	/// Unlit uses the five-UV unlit shader; lit uses the surface or diffuse transmission shader.
	/// </summary>
	internal static (string Shader, KeyValuePair<string, int>[] Features) Describe( Gltf.GltfMaterial material )
	{
		if ( material is null ) return ("shaders/gltf_surface_five_uv.shader", [new( "F_ALPHA_TEST", 0 ), new( "F_TRANSLUCENT", 0 ), new( "F_RENDER_BACKFACES", 0 ), new( "F_GLTF_TRANSMISSION", 0 ), new( "F_GLTF_ANISOTROPY", 0 )]);
		var mode = material.AlphaMode;
		var shader = material.Unlit ? "shaders/gltf_unlit_five_uv.shader" : material.DiffuseTransmission is not null ? "shaders/gltf_diffuse_transmission.shader" : "shaders/gltf_surface_five_uv.shader";
		var features = new List<KeyValuePair<string, int>>
		{
			new( "F_ALPHA_TEST", mode == "MASK" ? 1 : 0 ),
			new( "F_TRANSLUCENT", mode == "BLEND" || (material.Transmission is not null && material.DiffuseTransmission is null && !material.Unlit) ? 1 : 0 ),
			new( "F_RENDER_BACKFACES", material.DoubleSided ? 1 : 0 )
		};
		if ( material.DiffuseTransmission is null && !material.Unlit )
		{
			features.Add( new( "F_GLTF_TRANSMISSION", material.Transmission is null ? 0 : 1 ) );
			features.Add( new( "F_GLTF_ANISOTROPY", material.Anisotropy is null ? 0 : 1 ) );
		}
		return (shader, features.ToArray());
	}
}
