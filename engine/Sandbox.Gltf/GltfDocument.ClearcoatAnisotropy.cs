using System.Numerics;
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

	/// <summary>KHR_materials_ior; null when absent or not supported.</summary>
	private float? MaterialIor( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_ior" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		var ior = MaterialOptionalNumber( value, "ior", 1.5f, 0 );
		if ( ior is > 0 and < 1 ) throw new GltfException( "ior must be 0 or at least 1." );
		return ior == 0 ? 1000 : ior; // the spec allows 0 for an infinite index
	}

	private GltfSpecular MaterialSpecular( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_specular" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		var color = MaterialVector( value, "specularColorFactor", [1, 1, 1], 0 );
		return new GltfSpecular
		{
			Factor = MaterialOptionalNumber( value, "specularFactor", 1, 0 ),
			Texture = MaterialTexture( value, "specularTexture", false ),
			ColorFactor = new Vector3( color[0], color[1], color[2] ),
			ColorTexture = MaterialTexture( value, "specularColorTexture", true )
		};
	}

	private GltfSheen MaterialSheen( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_sheen" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		var color = MaterialVector( value, "sheenColorFactor", [0, 0, 0], 0, 1 );
		return new GltfSheen
		{
			ColorFactor = new Vector3( color[0], color[1], color[2] ),
			ColorTexture = MaterialTexture( value, "sheenColorTexture", true ),
			RoughnessFactor = MaterialOptionalNumber( value, "sheenRoughnessFactor", 0, 0, 1 ),
			RoughnessTexture = MaterialTexture( value, "sheenRoughnessTexture", false )
		};
	}

	private GltfIridescence MaterialIridescence( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_iridescence" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		return new GltfIridescence
		{
			Factor = MaterialOptionalNumber( value, "iridescenceFactor", 0, 0, 1 ),
			Texture = MaterialTexture( value, "iridescenceTexture", false ),
			Ior = MaterialOptionalNumber( value, "iridescenceIor", 1.3f, 1 ),
			ThicknessMinimum = MaterialOptionalNumber( value, "iridescenceThicknessMinimum", 100, 0 ),
			ThicknessMaximum = MaterialOptionalNumber( value, "iridescenceThicknessMaximum", 400, 0 ),
			ThicknessTexture = MaterialTexture( value, "iridescenceThicknessTexture", false )
		};
	}

	private GltfTransmission MaterialTransmission( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_transmission" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		return new GltfTransmission { Factor = MaterialOptionalNumber( value, "transmissionFactor", 0, 0, 1 ), Texture = MaterialTexture( value, "transmissionTexture", false ) };
	}

	private GltfVolume MaterialVolume( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_volume" );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		var color = MaterialVector( value, "attenuationColor", [1, 1, 1], 0, 1 );
		var distance = MaterialOptionalNumber( value, "attenuationDistance", float.PositiveInfinity, 0 );
		if ( distance == 0 ) throw new GltfException( "attenuationDistance must be greater than 0." );
		return new GltfVolume
		{
			ThicknessFactor = MaterialOptionalNumber( value, "thicknessFactor", 0, 0 ),
			ThicknessTexture = MaterialTexture( value, "thicknessTexture", false ),
			AttenuationDistance = distance,
			AttenuationColor = new Vector3( color[0], color[1], color[2] )
		};
	}

	private float MaterialDispersion( JsonElement material )
	{
		var value = SupportedMaterialExtension( material, "KHR_materials_dispersion" );
		return value.ValueKind == JsonValueKind.Undefined ? 0 : MaterialOptionalNumber( value, "dispersion", 0, 0 );
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
