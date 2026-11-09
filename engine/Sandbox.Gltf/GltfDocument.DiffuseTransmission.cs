using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private GltfDiffuseTransmission MaterialDiffuseTransmission( JsonElement material )
	{
		const string name = "KHR_materials_diffuse_transmission";
		if ( !SupportsExtension( name ) ) return null;
		var value = MaterialExtension( material, name );
		if ( value.ValueKind == JsonValueKind.Undefined ) return null;
		if ( !ExtensionNames( "extensionsUsed" ).Contains( name, StringComparer.Ordinal ) )
			throw new GltfException( $"{name} must be listed in extensionsUsed." );
		MaterialRequireObject( value, name );
		foreach ( var excluded in new[] { "KHR_materials_unlit", "KHR_materials_pbrSpecularGlossiness" } )
			if ( MaterialExtension( material, excluded ).ValueKind != JsonValueKind.Undefined )
				throw new GltfException( $"{name} cannot be used with {excluded}." );
		var color = MaterialVector( value, "diffuseTransmissionColorFactor", [1, 1, 1], 0, 1 );
		return new GltfDiffuseTransmission
		{
			Factor = MaterialOptionalNumber( value, "diffuseTransmissionFactor", 0, 0, 1 ),
			ColorFactor = new Vector3( color[0], color[1], color[2] ),
			Texture = MaterialTexture( value, "diffuseTransmissionTexture", false ),
			ColorTexture = MaterialTexture( value, "diffuseTransmissionColorTexture", true )
		};
	}
}
