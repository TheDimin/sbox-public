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
		var mat = Material.Create( "sims4_base", "model" );
		var normalMap = Texture.Create( 1, 1 ).WithData( new byte[4] { 128, 128, 255, 255 } ).Finish();

		mat.Set( "g_tAlbedoMap", Texture.White );
		mat.Set( "g_tNormalMap", normalMap );
		mat.Set( "g_tSpecularMap", Texture.Black );
		mat.Set( "g_tEmissiveMap", Texture.Black );

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
				var texturePath = $"mount://sims4/textures/{key.Group:X8}_{key.Instance:X16}.vtex";

				var paramName = shaderEntry.Field switch
				{
					ShaderFieldType.DiffuseMap => "g_tAlbedoMap",
					ShaderFieldType.NormalMap => "g_tNormalMap",
					ShaderFieldType.SpecularMap => "g_tSpecularMap",
					ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissiveMap",
					_ => null,
				};

				if ( paramName == null )
					continue;

				var texture = Texture.Load( texturePath, false );
				if ( texture != null && !texture.IsError )
					material.Set( paramName, texture );
			}

			return material;
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load material {entry.Key}: {e.Message}" );
			return null;
		}
	}
}
