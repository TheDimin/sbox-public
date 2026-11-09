using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	/// <summary>
	/// Conservatively detects whether a material may consume a core normal map.
	/// This lightweight topology preflight deliberately does not parse or realize
	/// unrelated optional material inputs. It is not material validation: normal
	/// texture and selected extension details are validated when the route is used.
	/// </summary>
	public bool MaterialMayUseNormalTexture( int? materialIndex )
	{
		if ( materialIndex is null ) return false;
		var value = ElementAt( "materials", materialIndex.Value );
		if ( value.ValueKind != JsonValueKind.Object ) return true;
		if ( !value.TryGetProperty( "normalTexture", out _ ) ) return false;
		if ( SupportsExtension( "KHR_materials_unlit" ) && value.TryGetProperty( "extensions", out var extensions ) &&
			extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty( "KHR_materials_unlit", out var unlit ) && unlit.ValueKind == JsonValueKind.Object )
			return false;
		return true;
	}
}
