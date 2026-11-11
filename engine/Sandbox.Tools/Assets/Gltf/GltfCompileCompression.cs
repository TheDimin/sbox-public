using Sandbox.Gltf;

namespace Editor;

/// <summary>A group of mesh vertex attributes that compile compression can store in fewer bits.</summary>
public enum GltfVertexGroup { Position, Normal, Tangent, TexCoord, Color, Weights }

/// <summary>How the compiled file stores a vertex group. Data that the source already stores in the same or fewer bits stays as it is.</summary>
public enum GltfVertexStorage { Source, Int16, Int8 }

/// <summary>
/// The compile compression of a glTF asset, for editor code. Addons cannot reference Sandbox.Gltf, so this mirrors
/// <see cref="GltfCompressionSettings"/> and converts to it for the compiler.
/// </summary>
public sealed class GltfCompileCompression
{
	public Dictionary<GltfVertexGroup, GltfVertexStorage> Global { get; } = new();
	public Dictionary<(int Mesh, int Primitive), Dictionary<GltfVertexGroup, GltfVertexStorage>> Primitives { get; } = new();
	public bool CompactIntegers { get; set; }

	public GltfVertexStorage Get( int mesh, int primitive, GltfVertexGroup group ) =>
		Primitives.TryGetValue( (mesh, primitive), out var overrides ) && overrides.TryGetValue( group, out var storage ) ? storage : Global.GetValueOrDefault( group );

	internal GltfCompressionSettings ToCompiler()
	{
		var settings = new GltfCompressionSettings { CompactIntegers = CompactIntegers };
		foreach ( var (group, storage) in Global ) settings.Global[(GltfAttributeGroup)group] = (GltfAttributeStorage)storage;
		foreach ( var (key, groups) in Primitives )
			settings.Primitives[key] = groups.ToDictionary( x => (GltfAttributeGroup)x.Key, x => (GltfAttributeStorage)x.Value );
		return settings;
	}

	internal static GltfCompileCompression FromCompiler( GltfCompressionSettings settings )
	{
		var result = new GltfCompileCompression { CompactIntegers = settings.CompactIntegers };
		foreach ( var (group, storage) in settings.Global ) result.Global[(GltfVertexGroup)group] = (GltfVertexStorage)storage;
		foreach ( var (key, groups) in settings.Primitives )
			result.Primitives[key] = groups.ToDictionary( x => (GltfVertexGroup)x.Key, x => (GltfVertexStorage)x.Value );
		return result;
	}
}
