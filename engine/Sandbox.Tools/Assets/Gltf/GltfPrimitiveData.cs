using System.Text.Json.Nodes;

namespace Editor;

/// <summary>The storage of one accessor: its component type (such as 5126 for float), components per element and element count.</summary>
public readonly record struct GltfAccessorLayout( int ComponentType, int Components, int Count )
{
	/// <summary>Bits per component: 8, 16 or 32.</summary>
	public int Bits => ComponentType switch { 5120 or 5121 => 8, 5122 or 5123 => 16, _ => 32 };
}

/// <summary>A summary of one mesh primitive: its attributes, its indices and its material.</summary>
public sealed record GltfPrimitiveInfo( int Mesh, int Primitive, int Mode, int Material, IReadOnlyDictionary<string, GltfAccessorLayout> Attributes, GltfAccessorLayout? Indices )
{
	public int VertexCount => Attributes.TryGetValue( "POSITION", out var position ) ? position.Count : 0;
}

partial class GltfAuthoringDocument
{
	/// <summary>The primitives of a mesh, in file order.</summary>
	public IReadOnlyList<GltfPrimitiveInfo> GetPrimitives( GltfElement mesh )
	{
		if ( mesh?.Kind != GltfElementKind.Mesh || mesh.Json["primitives"] is not JsonArray primitives ) return [];
		var accessors = root["accessors"] as JsonArray;
		GltfAccessorLayout? Layout( JsonNode reference )
		{
			if ( reference is not JsonValue value || !value.TryGetValue<int>( out var index ) || accessors is null || index < 0 || index >= accessors.Count ) return null;
			var accessor = accessors[index];
			var components = accessor?["type"]?.GetValue<string>() switch { "SCALAR" => 1, "VEC2" => 2, "VEC3" => 3, "VEC4" => 4, "MAT4" => 16, _ => 0 };
			return new GltfAccessorLayout( accessor?["componentType"]?.GetValue<int>() ?? 0, components, accessor?["count"]?.GetValue<int>() ?? 0 );
		}
		var result = new List<GltfPrimitiveInfo>();
		for ( var p = 0; p < primitives.Count; p++ )
		{
			var primitive = primitives[p];
			var attributes = new Dictionary<string, GltfAccessorLayout>();
			if ( primitive?["attributes"] is JsonObject json )
				foreach ( var (semantic, reference) in json )
					if ( Layout( reference ) is { } layout ) attributes[semantic] = layout;
			var material = primitive?["material"] is JsonValue m && m.TryGetValue<int>( out var materialIndex ) ? materialIndex : -1;
			var mode = primitive?["mode"] is JsonValue modeValue && modeValue.TryGetValue<int>( out var modeNumber ) ? modeNumber : 4;
			result.Add( new GltfPrimitiveInfo( mesh.Index, p, mode, material, attributes, Layout( primitive?["indices"] ) ) );
		}
		return result;
	}
}
