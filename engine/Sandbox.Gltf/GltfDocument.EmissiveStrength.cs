using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>
	/// Returns the effective KHR_materials_emissive_strength multiplier for a
	/// material. Unsupported optional extensions retain the core multiplier of
	/// one; registering reader support is not proof of rendering support.
	/// Multiply this value with the linear emissive factor and decoded texture;
	/// do not clamp the result to one. No texture or material state is modified.
	/// </summary>
	public float ReadMaterialEmissiveStrength( int index )
	{
		var material = ElementAt( "materials", index );
		const string name = "KHR_materials_emissive_strength";
		if ( !SupportsExtension( name ) ) return 1;
		var extension = MaterialExtension( material, name );
		if ( extension.ValueKind == JsonValueKind.Undefined ) return 1;
		if ( !ExtensionNames( "extensionsUsed" ).Contains( name, StringComparer.Ordinal ) )
			throw new GltfException( $"{name} must be listed in extensionsUsed." );
		MaterialRequireObject( extension, name );
		if ( MaterialExtension( material, "KHR_materials_unlit" ).ValueKind != JsonValueKind.Undefined )
			throw new GltfException( $"{name} cannot be used with KHR_materials_unlit." );
		return MaterialOptionalNumber( extension, "emissiveStrength", 1, 0 );
	}
}
