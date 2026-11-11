using System;
using System.Reflection;

namespace Editor;

/// <summary>A shader a glTF material can use, and the facet that selects it in the file (null for the default).</summary>
public sealed record GltfMaterialShader( string Path, string Title, Type Selector );

/// <summary>
/// Which shaders a glTF material can use. glTF has no field for a shader, so a shader is only
/// available when the file can say it: the default lit shader, or a facet with
/// <see cref="GltfShaderSelectorAttribute"/> (an extension such as KHR_materials_unlit).
/// Support for a facet comes from the shader schema and <see cref="GltfShaderParameterAttribute"/>.
/// </summary>
public static class GltfMaterialShaders
{
	public const string DefaultShader = "shaders/gltf_surface_five_uv.shader";

	static readonly Dictionary<string, HashSet<string>> parameters = new( StringComparer.OrdinalIgnoreCase );

	/// <summary>Every shader that a glTF file can select.</summary>
	public static IReadOnlyList<GltfMaterialShader> All
	{
		get
		{
			var list = new List<GltfMaterialShader> { new( DefaultShader, "glTF Lit", null ) };
			foreach ( var type in GltfFacets.TypesFor( GltfElementKind.Material ) )
				if ( type.GetCustomAttribute<GltfShaderSelectorAttribute>() is { } selector )
					list.Add( new( selector.Shader, "glTF " + DisplayInfo.ForType( type ).Name, type ) );
			return list;
		}
	}

	/// <summary>The shader the material uses now.</summary>
	public static GltfMaterialShader Current( GltfElement material ) =>
		All.FirstOrDefault( x => x.Selector is not null && GltfFacets.GetPresent( material ).Any( f => f.GetType() == x.Selector ) ) ?? All[0];

	/// <summary>True if the shader has every parameter the facet type needs.</summary>
	public static bool Supports( string shader, Type facetType )
	{
		var needed = facetType.GetCustomAttributes<GltfShaderParameterAttribute>()
			.Concat( facetType.GetProperties().SelectMany( x => x.GetCustomAttributes<GltfShaderParameterAttribute>() ) )
			.Select( x => x.Name ).ToList();
		if ( needed.Count == 0 ) return true;
		var available = Parameters( shader );
		return available is not null && needed.All( available.Contains );
	}

	/// <summary>
	/// Shaders the material can switch to: they support every facet the material has. A material with
	/// clearcoat data cannot pick a shader that does not render clearcoat.
	/// </summary>
	public static IEnumerable<GltfMaterialShader> Compatible( GltfElement material )
	{
		var data = GltfFacets.GetPresent( material ).Select( x => x.GetType() ).Where( x => x.GetCustomAttribute<GltfShaderSelectorAttribute>() is null ).ToList();
		return All.Where( shader => data.All( facet => Supports( shader.Path, facet ) ) );
	}

	/// <summary>Variable names of a shader, or null if it does not load.</summary>
	static HashSet<string> Parameters( string shader )
	{
		if ( parameters.TryGetValue( shader, out var names ) ) return names;
		var schema = Shader.Load( shader ) is { IsValid: true } loaded ? loaded.Schema : null;
		names = schema?.Variables?.SelectMany( x => new[] { x.Name, x.AttributeName } ).Where( x => !string.IsNullOrEmpty( x ) ).ToHashSet( StringComparer.Ordinal );
		return parameters[shader] = names;
	}
}
