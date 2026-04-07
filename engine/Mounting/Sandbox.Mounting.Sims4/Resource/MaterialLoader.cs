using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;

namespace Mounting.Sims4;

public static class Sims4MaterialLoader
{
	static readonly Logger Log = new Logger( "Sims4-MaterialLoader" );

	/// <summary>
	/// Returns true if the given TS4 shader type indicates a translucent/glass material.
	/// </summary>
	internal static bool IsGlassShader( ShaderType shader )
	{
		return shader is ShaderType.GlassForObjects
			or ShaderType.GlassForObjectsTranslucent
			or ShaderType.GlassForFences
			or ShaderType.GlassForPortals
			or ShaderType.GlassForRabbitHoles
			or ShaderType.SimGlass
			or ShaderType.BuildingWindow
			or ShaderType.PhongAlpha;
	}

	/// <summary>
	/// Returns true if the given TS4 shader type inherently requires alpha testing.
	/// Only shader types that are known to use alpha-cutout geometry are whitelisted.
	/// Other materials that need alpha test will be caught by explicit MATD flags
	/// (AlphaMap, UseDiffuseForAlphaTest, AlphaMaskThreshold).
	/// </summary>
	internal static bool IsAlphaTestShader( ShaderType shader )
	{
		return shader is ShaderType.Foliage
			or ShaderType.Fence
			or ShaderType.SimHair
			or ShaderType.SimEyelashes;
	}

	/// <summary>
	/// Build a sandbox Material from an already-parsed MaterialDefinition and its texture keys.
	/// Textures are loaded directly from packages via the provided resolver function.
	/// </summary>
	internal static Material? BuildMaterial( string name, MaterialDefinition matd, Dictionary<ShaderFieldType, ResourceKey> textureKeys, Func<ResourceKey, Texture?> resolveTexture )
	{
		// --- Phase 1: Scan to determine which features are needed ---
		bool hasEmissive = false;
		bool hasAlphaMap = false;
		bool useDiffuseForAlpha = false;
		float alphaMaskThreshold = 0f;
		bool isGlass = IsGlassShader( matd.Shader );
		float transparency = 0f;

		foreach ( var (field, _) in textureKeys )
		{
			if ( field == ShaderFieldType.EmissionMap || field == ShaderFieldType.SelfIlluminationMap )
				hasEmissive = true;
			if ( field == ShaderFieldType.AlphaMap )
				hasAlphaMap = true;
		}

		foreach ( var shaderEntry in matd.ShaderEntries )
		{
			switch ( shaderEntry )
			{
				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.EmissiveBloomMultiplier
					|| shaderEntry.Field == ShaderFieldType.EmissiveLightMultiplier:
					hasEmissive = true;
					break;
				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.Transparency:
					transparency = f.Value;
					break;
				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.UseDiffuseForAlphaTest:
					useDiffuseForAlpha = f.Value > 0f;
					break;
				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.AlphaMaskThreshold:
					alphaMaskThreshold = f.Value;
					break;
			}
		}

		bool alphaMapIsSameAsDiffuse = hasAlphaMap
			&& textureKeys.TryGetValue( ShaderFieldType.DiffuseMap, out var diffKey )
			&& textureKeys.TryGetValue( ShaderFieldType.AlphaMap, out var alpKey )
			&& diffKey.Instance == alpKey.Instance
			&& diffKey.Group == alpKey.Group;

		bool useSeparateAlphaMap = hasAlphaMap && !alphaMapIsSameAsDiffuse;
		bool needsAlphaTest = useSeparateAlphaMap || hasAlphaMap || useDiffuseForAlpha || alphaMaskThreshold > 0f || IsAlphaTestShader( matd.Shader );

		// --- Phase 2: Create fresh material and set features on clean state ---
		Material material;
		try
		{
			material = Material.Create( name, "sims4" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Material.Create failed for '{name}': {ex}" );
			return null;
		}

		if ( needsAlphaTest )
			material.SetFeature( "F_ALPHA_TEST", 1 );
		if ( useSeparateAlphaMap )
			material.SetFeature( "F_SEPARATE_ALPHA_MAP", 1 );
		if ( hasEmissive )
			material.SetFeature( "F_EMISSIVE", 1 );
		if ( isGlass )
		{
			material.SetFeature( "F_TRANSLUCENT", 1 );
			material.SetFeature( "F_RENDER_BACKFACES", 1 );
		}

		// --- Phase 3: Set defaults + textures AFTER features are finalized ---
		var normalMap = Texture.Create( 1, 1 ).WithData( new byte[4] { 0, 0, 128, 128 } ).Finish();
		material.Set( "g_tDiffuse", Texture.White );
		material.Set( "g_tNormalMap", normalMap );
		material.Set( "g_tSpecular", Texture.Black );
		material.Set( "g_tEmissive", Texture.Black );

		foreach ( var (field, key) in textureKeys )
		{
			var paramName = field switch
			{
				ShaderFieldType.DiffuseMap => "g_tDiffuse",
				ShaderFieldType.NormalMap => "g_tNormalMap",
				ShaderFieldType.SpecularMap => "g_tSpecular",
				ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
				ShaderFieldType.AlphaMap => "g_tAlphaMap",
				_ => null,
			};

			if ( paramName != null )
			{
				var texture = resolveTexture( key );
				if ( texture != null && !texture.IsError )
					material.Set( paramName, texture );
			}
		}

		// --- Phase 4: Apply float/color parameters ---
		material.Set( "g_flNormalStrength", 1.0f );
		material.Set( "g_flSpecularScale", 1.0f );
		material.Set( "g_flEmissiveScale", 1.0f );
		material.Set( "g_vDiffuseTint", new Vector3( 1f, 1f, 1f ) );

		foreach ( var shaderEntry in matd.ShaderEntries )
		{
			switch ( shaderEntry )
			{
				case ShaderFloat3 f3 when shaderEntry.Field == ShaderFieldType.Diffuse:
					material.Set( "g_vDiffuseTint", new Vector3( f3.X, f3.Y, f3.Z ) );
					break;
				case ShaderFloat4 f4 when shaderEntry.Field == ShaderFieldType.Diffuse:
					material.Set( "g_vDiffuseTint", new Vector3( f4.X, f4.Y, f4.Z ) );
					break;

				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.EmissiveBloomMultiplier
					|| shaderEntry.Field == ShaderFieldType.EmissiveLightMultiplier:
					material.Set( "g_flEmissiveScale", Math.Max( f.Value, 0f ) );
					break;

				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.NormalMapScale
					|| shaderEntry.Field == ShaderFieldType.NormalBumpScale:
					material.Set( "g_flNormalStrength", f.Value );
					break;
			}
		}

		if ( needsAlphaTest )
		{
			float threshold = alphaMaskThreshold > 1f ? alphaMaskThreshold / 255f : alphaMaskThreshold;
			material.Set( "g_flAlphaTestThreshold", threshold > 0f ? threshold : 0.5f );
		}

		if ( isGlass )
		{
			float opacity = transparency > 0f ? transparency : 0.15f;
			material.Set( "g_flOpacity", opacity );
		}

		return material;
	}
}
