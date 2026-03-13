using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

public class Sims4MaterialLoader( DbpfPackage package, ResourceEntry entry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-MaterialLoader" );

	private static readonly Material BaseMaterial = CreateBaseMaterial();

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

			var material = BaseMaterial.CreateCopy( Path );

			// Map textures
			foreach ( var shaderEntry in matd.ShaderEntries )
			{
				ResourceKey? textureKey = shaderEntry switch
				{
					ShaderTextureRef texRef => texRef.Key,
					ShaderTextureKey texKey => texKey.Key,
					ShaderImageMapKey imgKey => imgKey.Key,
					_ => null,
				};

				if ( textureKey == null )
					continue;

				var key = textureKey.Value;
				var texturePath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";

				var paramName = shaderEntry.Field switch
				{
					ShaderFieldType.DiffuseMap => "g_tDiffuse",
					ShaderFieldType.NormalMap => "g_tNormalMap",
					ShaderFieldType.SpecularMap => "g_tSpecular",
					ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
					ShaderFieldType.AlphaMap => "g_tDiffuse",
					_ => null,
				};

				if ( paramName == null )
					continue;

				var texture = Texture.Load( texturePath, false );
				if ( texture != null && !texture.IsError )
					material.Set( paramName, texture );
			}

			// Map float/color shader parameters
			bool hasEmissive = false;

			foreach ( var shaderEntry in matd.ShaderEntries )
			{
				switch ( shaderEntry )
				{
					case ShaderFloat3 f3 when shaderEntry.Field == ShaderFieldType.Diffuse:
						material.Set( "g_vDiffuseTint", new Vector3( f3.X, f3.Y, f3.Z ) );
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
				}
			}

			// Check for emissive textures
			foreach ( var shaderEntry in matd.ShaderEntries )
			{
				if ( shaderEntry.Field == ShaderFieldType.EmissionMap
					|| shaderEntry.Field == ShaderFieldType.SelfIlluminationMap )
				{
					hasEmissive = true;
					break;
				}
			}

			if ( hasEmissive )
				material.Set( "F_EMISSIVE", true );

			return material;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load material {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
