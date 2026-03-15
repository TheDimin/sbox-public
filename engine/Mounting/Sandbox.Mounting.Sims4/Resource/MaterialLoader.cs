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
	/// Build a sandbox Material from an already-parsed MaterialDefinition and its texture keys.
	/// Shared by both Sims4MaterialLoader (standalone MATD) and InlineMaterialLoader (MATD from MODL RCOL).
	/// </summary>
	internal static Material? BuildMaterialFromMatd( string mountPath, MaterialDefinition matd, Dictionary<ShaderFieldType, ResourceKey> textureKeys )
	{
		var material = BaseMaterial.CreateCopy( mountPath );

		bool hasEmissive = false;
		bool hasAlphaMap = false;
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
				ShaderFieldType.AlphaMap => "g_tDiffuse",
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
			}
		}

		if ( hasAlphaMap )
			material.Set( "F_ALPHA_TEST", true );
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

		return material;
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
