using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

public class Sims4MaterialLoader( DbpfPackage package, ResourceEntry entry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-MaterialLoader" );

	internal static readonly Material BaseMaterial = CreateBaseMaterial();

	private static Material CreateBaseMaterial()
	{
		var mat = Material.Create( "sims4_base", "sims4" );
		// TS4 normal maps: XY packed into ZW (blue, alpha) channels.
		// Flat normal needs ZW=128 (~0 after decode). RG unused.
		var normalMap = Texture.Create( 1, 1 ).WithData( new byte[4] { 0, 0, 128, 128 } ).Finish();

		mat.Set( "g_tDiffuse", Texture.White );
		mat.Set( "g_tNormalMap", normalMap );
		mat.Set( "g_tSpecular", Texture.Black );
		mat.Set( "g_tEmissive", Texture.Black );
		mat.Set( "g_flNormalStrength", 1.0f );
		mat.Set( "g_flSpecularScale", 1.0f );
		mat.Set( "g_flEmissiveScale", 1.0f );
		mat.Set( "g_vDiffuseTint", new Vector3( 1f, 1f, 1f ) );

		return mat;
	}

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
	/// In practice, nearly all non-glass TS4 shaders can have masked geometry
	/// (foliage, plants, hair, eyelashes, fences, props, etc.).
	/// We default to true and exclude only shaders that are known to never need it.
	/// </summary>
	internal static bool IsAlphaTestShader( ShaderType shader )
	{
		// Glass/translucent shaders use blending instead of alpha test
		if ( IsGlassShader( shader ) )
			return false;

		// These shaders never have meaningful diffuse alpha
		return shader is not (
			ShaderType.None
			or ShaderType.ShadowMap
			or ShaderType.DropShadow
			or ShaderType.Plumbob
			or ShaderType.Blueprint
			or ShaderType.PreviewWallsAndFloors
			or ShaderType.ImpostorWater
			or ShaderType.StandingWater
			or ShaderType.BasinWater
			or ShaderType.Subtractive
			or ShaderType.Additive
			or ShaderType.ParticleAnim
			or ShaderType.ParticleJet
		);
	}

	/// <summary>
	/// Build a sandbox Material from an already-parsed MaterialDefinition and its texture keys.
	/// Shared by both Sims4MaterialLoader (standalone MATD) and InlineMaterialLoader (MATD from MODL RCOL).
	/// </summary>
	internal static Material? BuildMaterialFromMatd( string mountPath, MaterialDefinition matd, Dictionary<ShaderFieldType, ResourceKey> textureKeys )
	{
		var material = BaseMaterial.CreateCopy( mountPath );

		bool hasEmissive = false;
		bool hasAlphaMap = false;
		bool useDiffuseForAlpha = false;
		float alphaMaskThreshold = 0f;
		bool isGlass = IsGlassShader( matd.Shader );
		float transparency = 0f;

		// Apply textures from pre-resolved texture keys
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
				if ( field == ShaderFieldType.EmissionMap || field == ShaderFieldType.SelfIlluminationMap )
					hasEmissive = true;
				if ( field == ShaderFieldType.AlphaMap )
					hasAlphaMap = true;

				var texturePath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
				var texture = Texture.Load( texturePath, false );
				if ( texture != null && !texture.IsError )
					material.Set( paramName, texture );
			}
		}

		// Apply float/color parameters from shader entries
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
					hasEmissive = true;
					break;

				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.NormalMapScale
					|| shaderEntry.Field == ShaderFieldType.NormalBumpScale:
					material.Set( "g_flNormalStrength", f.Value );
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

		// If the AlphaMap points to the same texture as the DiffuseMap, the alpha
		// data lives in the diffuse's .a channel (DXT5/DST5). Don't use the separate
		// alpha map path (which reads .r) — just let the default diffuseSample.a work.
		bool alphaMapIsSameAsDiffuse = hasAlphaMap
			&& textureKeys.TryGetValue( ShaderFieldType.DiffuseMap, out var diffKey )
			&& textureKeys.TryGetValue( ShaderFieldType.AlphaMap, out var alpKey )
			&& diffKey.Instance == alpKey.Instance
			&& diffKey.Group == alpKey.Group;

		bool useSeparateAlphaMap = hasAlphaMap && !alphaMapIsSameAsDiffuse;

		bool needsAlphaTest = useSeparateAlphaMap || hasAlphaMap || useDiffuseForAlpha || IsAlphaTestShader( matd.Shader );

		// DEBUG: Log material alpha decisions
		Log.Info( $"Material {mountPath}: shader={matd.Shader}, hasAlphaMap={hasAlphaMap}, useSeparateAlphaMap={useSeparateAlphaMap}, useDiffuseForAlpha={useDiffuseForAlpha}, alphaMaskThreshold={alphaMaskThreshold}, needsAlphaTest={needsAlphaTest}, textures=[{string.Join( ", ", textureKeys.Select( kv => $"{kv.Key}={kv.Value}" ) )}]" );

		if ( needsAlphaTest )
		{
			material.Set( "F_ALPHA_TEST", true );
			// Always set a sensible threshold — shader Default1(0.5) is compile-time only
			// and may not apply to runtime-created materials (leaving it at 0.0, which
			// means clip(alpha - 0.0) never fires for any alpha >= 0).
			// TS4 stores the threshold in 0-255 byte range; the shader operates in 0.0-1.0.
			// Normalize any value > 1 (clearly a byte value) to float range.
			float threshold = alphaMaskThreshold > 1f ? alphaMaskThreshold / 255f : alphaMaskThreshold;
			material.Set( "g_flAlphaTestThreshold", threshold > 0f ? threshold : 0.5f );
		}
		if ( useSeparateAlphaMap )
			material.Set( "F_SEPARATE_ALPHA_MAP", true );
		if ( hasEmissive )
			material.Set( "F_EMISSIVE", true );

		// Enable translucent rendering for glass/window materials
		if ( isGlass )
		{
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "F_RENDER_BACKFACES", true );

			// Use the Transparency parameter from the MATD if available,
			// otherwise use a sensible default for glass (mostly see-through).
			float opacity = transparency > 0f ? transparency : 0.15f;
			material.Set( "g_flOpacity", opacity );
		}
		else if ( transparency > 0f )
		{
			// Non-glass materials with an explicit Transparency value
			// (e.g. curtain fabric, frosted surfaces).
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "g_flOpacity", transparency );
		}

		return material;
	}

	/// <summary>
	/// Async variant of <see cref="BuildMaterialFromMatd"/> that kicks off all texture
	/// loads concurrently via <c>Task.WhenAll</c> so the GPU can compile multiple
	/// textures in parallel instead of sequentially.
	/// </summary>
	internal static async Task<Material?> BuildMaterialFromMatdAsync( string mountPath, MaterialDefinition matd, Dictionary<ShaderFieldType, ResourceKey> textureKeys )
	{
		var material = BaseMaterial.CreateCopy( mountPath );

		bool hasEmissive = false;
		bool hasAlphaMap = false;
		bool useDiffuseForAlpha = false;
		float alphaMaskThreshold = 0f;
		bool isGlass = IsGlassShader( matd.Shader );
		float transparency = 0f;

		// Build a list of (paramName, path) pairs for all texture fields we handle
		var pending = new List<(string ParamName, string Path, ShaderFieldType Field)>();

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
				if ( field == ShaderFieldType.EmissionMap || field == ShaderFieldType.SelfIlluminationMap )
					hasEmissive = true;
				if ( field == ShaderFieldType.AlphaMap )
					hasAlphaMap = true;

				var texturePath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
				pending.Add( (paramName, texturePath, field) );
			}
		}

		// Kick off ALL texture loads concurrently, then await completion of all
		var tasks = pending.Select( p => Texture.LoadAsync( p.Path, false ) ).ToList();
		await Task.WhenAll( tasks );

		for ( int i = 0; i < pending.Count; i++ )
		{
			var texture = tasks[i].Result;
			if ( texture != null && !texture.IsError )
				material.Set( pending[i].ParamName, texture );
		}

		// Apply float/color parameters from shader entries
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
					hasEmissive = true;
					break;

				case ShaderFloat f when shaderEntry.Field == ShaderFieldType.NormalMapScale
					|| shaderEntry.Field == ShaderFieldType.NormalBumpScale:
					material.Set( "g_flNormalStrength", f.Value );
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

		bool needsAlphaTest = useSeparateAlphaMap || hasAlphaMap || useDiffuseForAlpha || IsAlphaTestShader( matd.Shader );
		if ( needsAlphaTest )
		{
			material.Set( "F_ALPHA_TEST", true );
			float threshold = alphaMaskThreshold > 1f ? alphaMaskThreshold / 255f : alphaMaskThreshold;
			material.Set( "g_flAlphaTestThreshold", threshold > 0f ? threshold : 0.5f );
		}
		if ( useSeparateAlphaMap )
			material.Set( "F_SEPARATE_ALPHA_MAP", true );
		if ( hasEmissive )
			material.Set( "F_EMISSIVE", true );

		if ( isGlass )
		{
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "F_RENDER_BACKFACES", true );
			float opacity = transparency > 0f ? transparency : 0.15f;
			material.Set( "g_flOpacity", opacity );
		}
		else if ( transparency > 0f )
		{
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "g_flOpacity", transparency );
		}

		return material;
	}

	protected override async Task<object?> LoadAsync()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var rcol = package.GetResource<RcolContainer>( entry );
			var matd = rcol.GetChunk<MaterialDefinition>();
			if ( matd == null )
			{
				Log.Warning( $"No MATD chunk found in RCOL {entry.Key}" );
				return null;
			}

			var textureKeys = ModlModelLoader.ExtractTextureKeys( matd, rcol.ExternalReferences );
			return await BuildMaterialFromMatdAsync( Path, matd, textureKeys );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load material {entry.Key}: {e.Message}" );
			return null;
		}
	}

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			// MATD resources are wrapped in an RCOL container
			var rcol = package.GetResource<RcolContainer>( entry );
			var matd = rcol.GetChunk<MaterialDefinition>();
			if ( matd == null )
			{
				Log.Warning( $"No MATD chunk found in RCOL {entry.Key}" );
				return null;
			}

			// Extract texture keys using the RCOL's external references
			var textureKeys = ModlModelLoader.ExtractTextureKeys( matd, rcol.ExternalReferences );

			return BuildMaterialFromMatd( Path, matd, textureKeys );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load material {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
