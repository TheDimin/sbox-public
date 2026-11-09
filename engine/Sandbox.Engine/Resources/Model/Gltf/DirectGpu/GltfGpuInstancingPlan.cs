using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu;

using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// One source node or authored EXT_mesh_gpu_instancing element. The ordinary
/// node has a null instance index; selected extension rows retain accessor order.
/// </summary>
internal sealed record GltfGpuInstancingRow( int Node, int? InstanceIndex, NMatrix LocalTransform )
{
	/// <summary>
	/// Compose in glTF coordinates using row vectors. Basis/units and caller
	/// placement apply after this result; the full affine node matrix is retained.
	/// </summary>
	public NMatrix Compose( NMatrix nodeWorld ) => LocalTransform * nodeWorld;
}

/// <summary>
/// Original instance accessor provenance, separate from CPU-decoded transforms.
/// This record neither owns an upload stream nor claims raw GPU consumption.
/// Cloned JSON retains sparse/codec metadata that the summary does not interpret.
/// </summary>
internal sealed record GltfGpuInstancingAttribute
{
	public string Semantic { get; }
	public int AccessorIndex { get; }
	public int? BufferViewIndex { get; }
	public int? BufferIndex { get; }
	public int BufferViewByteOffset { get; }
	public int AccessorByteOffset { get; }
	public int ByteStride { get; }
	public int ComponentType { get; }
	public int Components { get; }
	public int Count { get; }
	public bool Normalized { get; }
	public JsonElement Accessor { get; }
	public JsonElement? BufferView { get; }

	internal GltfGpuInstancingAttribute( string semantic, int accessorIndex, JsonElement accessor, JsonElement? view )
	{
		Semantic = semantic;
		AccessorIndex = accessorIndex;
		Accessor = accessor.Clone();
		BufferView = view?.Clone();
		ComponentType = accessor.GetProperty( "componentType" ).GetInt32();
		Count = accessor.GetProperty( "count" ).GetInt32();
		Normalized = accessor.TryGetProperty( "normalized", out var normalized ) && normalized.GetBoolean();
		AccessorByteOffset = IntegerOrZero( accessor, "byteOffset" );

		// The reader validates shape/count and decodes TRS through its accessor
		// path. Custom attribute backing ranges remain unconsumed provenance.
		// This is metadata arithmetic only: no component bytes are read here.
		var (rows, columns) = accessor.GetProperty( "type" ).GetString() switch
		{
			"SCALAR" => (1, 1), "VEC2" => (2, 1), "VEC3" => (3, 1), "VEC4" => (4, 1),
			"MAT2" => (2, 2), "MAT3" => (3, 3), "MAT4" => (4, 4),
			_ => throw new GltfException( "Instance accessor has an invalid type." )
		};
		Components = rows * columns;
		var componentBytes = ComponentType switch
		{
			5120 or 5121 => 1, 5122 or 5123 => 2, 5125 or 5126 => 4,
			_ => throw new GltfException( "Instance accessor has an invalid component type." )
		};
		var columnBytes = columns == 1 ? rows * componentBytes : (rows * componentBytes + 3) & ~3;
		ByteStride = columnBytes * columns;
		if ( view.HasValue )
		{
			BufferViewIndex = accessor.GetProperty( "bufferView" ).GetInt32();
			BufferIndex = view.Value.GetProperty( "buffer" ).GetInt32();
			BufferViewByteOffset = IntegerOrZero( view.Value, "byteOffset" );
			if ( view.Value.TryGetProperty( "byteStride", out var stride ) ) ByteStride = stride.GetInt32();
		}
	}

	private static int IntegerOrZero( JsonElement value, string name ) => value.TryGetProperty( name, out var number ) ? number.GetInt32() : 0;
}

/// <summary>
/// Immutable CPU expansion plan for a mesh node. Decoding and extension validation
/// remain in GltfDocument.ReadInstances. This is shared draw-row preparation, not
/// a claim that hardware instancing or raw instance attribute uploads occurred.
/// </summary>
internal sealed class GltfGpuInstancingPlan
{
	private const string Extension = "EXT_mesh_gpu_instancing";
	public int Node { get; }
	public int Mesh { get; }
	public bool HasExtension { get; }
	public bool AppliesInstancing { get; }
	public IReadOnlyList<GltfGpuInstancingRow> Instances { get; }
	public IReadOnlyDictionary<string, GltfGpuInstancingAttribute> Attributes { get; }

	private GltfGpuInstancingPlan( int node, int mesh, bool hasExtension, bool appliesInstancing, List<GltfGpuInstancingRow> instances, Dictionary<string, GltfGpuInstancingAttribute> attributes )
	{
		Node = node;
		Mesh = mesh;
		HasExtension = hasExtension;
		AppliesInstancing = appliesInstancing;
		Instances = instances.AsReadOnly();
		Attributes = new ReadOnlyDictionary<string, GltfGpuInstancingAttribute>( attributes );
	}

	public static GltfGpuInstancingPlan Create( GltfDocument document, int node )
	{
		ArgumentNullException.ThrowIfNull( document );
		// A selected malformed extension must fail in the existing reader rather
		// than being mistaken for an unsupported ordinary-node fallback.
		var transforms = document.ReadInstances( node );
		var nodeValue = document.Root.GetProperty( "nodes" )[node];
		if ( !nodeValue.TryGetProperty( "mesh", out var meshValue ) || meshValue.ValueKind != JsonValueKind.Number || !meshValue.TryGetInt32( out var mesh ) || mesh < 0 ||
			!document.Root.TryGetProperty( "meshes", out var meshes ) || meshes.ValueKind != JsonValueKind.Array || mesh >= meshes.GetArrayLength() || meshes[mesh].ValueKind != JsonValueKind.Object )
			throw new GltfException( "An instancing plan requires a valid mesh node." );
		var hasExtension = nodeValue.TryGetProperty( "extensions", out var extensions ) && extensions.ValueKind == JsonValueKind.Object && extensions.TryGetProperty( Extension, out _ );
		var attributes = new Dictionary<string, GltfGpuInstancingAttribute>( StringComparer.Ordinal );
		if ( transforms.Length == 0 )
			return new( node, mesh, hasExtension, false, new() { new( node, null, NMatrix.Identity ) }, attributes );

		foreach ( var attribute in extensions.GetProperty( Extension ).GetProperty( "attributes" ).EnumerateObject() )
		{
			var index = attribute.Value.GetInt32();
			var accessor = document.Root.GetProperty( "accessors" )[index];
			JsonElement? view = accessor.TryGetProperty( "bufferView", out var viewIndex ) ? document.Root.GetProperty( "bufferViews" )[viewIndex.GetInt32()] : null;
			attributes.Add( attribute.Name, new( attribute.Name, index, accessor, view ) );
		}
		var instances = transforms.Select( (matrix, index) => new GltfGpuInstancingRow( node, index, matrix ) ).ToList();
		return new( node, mesh, true, true, instances, attributes );
	}
}
