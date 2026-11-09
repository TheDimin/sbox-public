using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Reads KHR_materials_clearcoat; null when absent or not supported by this reader.</summary>
	private GltfClearcoat MaterialClearcoat( JsonElement material )
	{
		const string name = "KHR_materials_clearcoat";
		var value = SupportedMaterialExtension( material, name );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		return new GltfClearcoat
		{
			Factor = MaterialOptionalNumber( value, "clearcoatFactor", 0, 0, 1 ),
			Texture = MaterialTexture( value, "clearcoatTexture", false ),
			RoughnessFactor = MaterialOptionalNumber( value, "clearcoatRoughnessFactor", 0, 0, 1 ),
			RoughnessTexture = MaterialTexture( value, "clearcoatRoughnessTexture", false ),
			NormalTexture = MaterialTexture( value, "clearcoatNormalTexture", false )
		};
	}

	/// <summary>Reads KHR_materials_anisotropy; null when absent or not supported by this reader.</summary>
	private GltfAnisotropy MaterialAnisotropy( JsonElement material )
	{
		const string name = "KHR_materials_anisotropy";
		var value = SupportedMaterialExtension( material, name );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		var rotation = MaterialOptionalNumber( value, "anisotropyRotation", 0 );
		if ( !float.IsFinite( rotation ) ) throw new GltfException( "anisotropyRotation must be finite." );
		return new GltfAnisotropy
		{
			Strength = MaterialOptionalNumber( value, "anisotropyStrength", 0, 0, 1 ),
			Rotation = rotation,
			Texture = MaterialTexture( value, "anisotropyTexture", false )
		};
	}

	private JsonElement SupportedMaterialExtension( JsonElement material, string name )
	{
		if ( !SupportsExtension( name ) ) return default;
		var value = MaterialExtension( material, name );
		if ( value.ValueKind == JsonValueKind.Undefined ) return default;
		if ( !ExtensionNames( "extensionsUsed" ).Contains( name, StringComparer.Ordinal ) )
			throw new GltfException( $"{name} must be listed in extensionsUsed." );
		MaterialRequireObject( value, name );
		// Both extensions extend the metal/rough model; unlit and spec/gloss replace it.
		foreach ( var excluded in new[] { "KHR_materials_unlit", "KHR_materials_pbrSpecularGlossiness" } )
			if ( MaterialExtension( material, excluded ).ValueKind != JsonValueKind.Undefined )
				return default;
		return value;
	}
}
