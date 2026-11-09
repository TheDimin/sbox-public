using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>Names in source index order for a supported material-variants catalog.</summary>
	public IReadOnlyList<string> ReadMaterialVariantNames()
	{
		_ = Root;
		const string name = "KHR_materials_variants";
		if ( !SupportsExtension( name ) ) return Array.AsReadOnly( Array.Empty<string>() );
		var extension = MaterialExtension( Root, name );
		if ( extension.ValueKind == JsonValueKind.Undefined ) return Array.AsReadOnly( Array.Empty<string>() );
		if ( !ExtensionNames( "extensionsUsed" ).Contains( name, StringComparer.Ordinal ) )
			throw new GltfException( $"{name} must be listed in extensionsUsed." );
		MaterialRequireObject( extension, name );
		var variants = MaterialProperty( extension, "variants" );
		if ( variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() == 0 )
			throw new GltfException( "Material variants must be a nonempty array." );
		var result = new string[variants.GetArrayLength()];
		for ( var i = 0; i < result.Length; i++ )
		{
			MaterialRequireObject( variants[i], "material variant" );
			var variantName = MaterialProperty( variants[i], "name" );
			if ( variantName.ValueKind != JsonValueKind.String ) throw new GltfException( "Material variant name must be a string." );
			result[i] = variantName.GetString();
		}
		return Array.AsReadOnly( result );
	}
}
