using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>Work needed before an accessor can provide ordinary decoded bytes.</summary>
[Flags]
public enum GltfUploadMaterialization
{
	None = 0,
	/// <summary>Apply the accessor's sparse replacements to its base data.</summary>
	Sparse = 1,
	/// <summary>Supply an implicit zero-initialized base; no buffer view exists.</summary>
	ZeroInitialized = 2,
	/// <summary>Decode the selected Draco primitive before using this accessor.</summary>
	Draco = 4,
	/// <summary>Decode the selected meshopt buffer view before using this accessor.</summary>
	Meshopt = 8
}

/// <summary>
/// An authored accessor binding. View and buffer offsets describe source storage,
/// not a GPU allocation; when a codec is selected they describe the fallback view.
/// </summary>
public sealed class GltfUploadBinding
{
	public int AccessorIndex { get; init; }
	public int? SourceBufferView { get; init; }
	public int? SourceBuffer { get; init; }
	public int? ViewByteOffset { get; init; }
	public int? ViewByteLength { get; init; }
	/// <summary>Offset in the source view, or null when Draco ignores fallback location fields.</summary>
	public int? AccessorByteOffset { get; init; }
	/// <summary>Source stride, or null when a selected Draco fallback is not inspected.</summary>
	public int? ByteStride { get; init; }
	public int ComponentType { get; init; }
	public int Components { get; init; }
	public int Rows { get; init; }
	public int Columns { get; init; }
	public int Count { get; init; }
	public bool Normalized { get; init; }
	/// <summary>Byte spacing of matrix columns, including glTF's required padding.</summary>
	public int ColumnStride { get; init; }
	public GltfUploadMaterialization Materialization { get; init; }
}

/// <summary>
/// Source-ordered semantic bindings for one primitive, before coordinate conversion,
/// node transforms, generated attributes, codec decoding, or GPU format selection.
/// </summary>
public sealed class GltfPrimitiveUploadLayout
{
	public IReadOnlyDictionary<string, GltfUploadBinding> Attributes { get; init; }
	public GltfUploadBinding Indices { get; init; }
	public int VertexCount { get; init; }
	public int SourceMode { get; init; }
	/// <summary>Topology after selected codec decoding, before any list-only conversion.</summary>
	public int DecodedMode { get; init; }
	/// <summary>The selected Draco primitive provides topology, including without an index accessor.</summary>
	public bool RequiresDracoIndices { get; init; }
	public bool RequiresGeneratedIndices { get; init; }
	/// <summary>Line loop/strip and triangle strip/fan need expansion for list-only consumers.</summary>
	public bool RequiresTopologyExpansion { get; init; }
}

public sealed partial class GltfDocument
{
	/// <summary>
	/// Describe source accessor storage without decoding buffers or allocating GPU
	/// resources. This is not full asset validation or a direct-upload guarantee;
	/// sparse/codec data, renderer formats and coordinate conversion need separate handling.
	/// </summary>
	public GltfPrimitiveUploadLayout DescribePrimitiveUpload( int mesh, int primitive )
	{
		var value = SceneArrayElement( ElementAt( "meshes", mesh ), "primitives", primitive );
		if ( value.ValueKind != JsonValueKind.Object ) throw new GltfException( "A mesh primitive must be an object." );
		var attributes = GltfJson.Required( value, "attributes" );
		if ( attributes.ValueKind != JsonValueKind.Object ) throw new GltfException( "Primitive attributes must be an object." );
		JsonElement dracoExtension = default;
		var dracoTopology = SupportsExtension( DracoExtension ) && TryGetDracoExtension( value, out dracoExtension );
		var dracoAttributes = dracoTopology ? GltfJson.Required( dracoExtension, "attributes" ) : default;
		if ( dracoTopology && dracoAttributes.ValueKind != JsonValueKind.Object ) throw new GltfException( "Draco attributes must be an object." );
		var result = new Dictionary<string, GltfUploadBinding>( StringComparer.Ordinal );
		int? count = null;
		foreach ( var attribute in attributes.EnumerateObject() )
		{
			var index = GltfJson.Integer( attribute.Value, attribute.Name );
			var layout = Accessor( index );
			if ( count.HasValue && count != layout.Count ) throw new GltfException( "Primitive attribute counts must agree." );
			count = layout.Count;
			var dracoAttribute = dracoTopology && dracoAttributes.TryGetProperty( attribute.Name, out _ );
			ValidateAttribute( attribute.Name, layout, index, dracoAttribute );
			if ( !result.TryAdd( attribute.Name, UploadBinding( index, layout, false, dracoAttribute ) ) )
				throw new GltfException( $"Duplicate primitive attribute '{attribute.Name}'." );
		}
		if ( !count.HasValue ) throw new GltfException( "A primitive must define at least one attribute." );
		ValidateAttributeSets( result.Keys );
		GltfUploadBinding indices = null;
		if ( value.TryGetProperty( "indices", out var indexValue ) )
		{
			var index = GltfJson.Integer( indexValue, "indices" );
			var layout = Accessor( index );
			if ( layout.Components != 1 || layout.ComponentType is not (5121 or 5123 or 5125) || layout.Normalized )
				throw new GltfException( "Index accessor must be an unsigned non-normalized scalar." );
			indices = UploadBinding( index, layout, true, dracoTopology );
		}
		var mode = GltfJson.Int( value, "mode", 4 );
		if ( mode is < 0 or > 6 ) throw new GltfException( "Invalid primitive mode." );
		var decodedMode = dracoTopology ? 4 : mode;
		return new GltfPrimitiveUploadLayout
		{
			Attributes = new ReadOnlyDictionary<string, GltfUploadBinding>( result ),
			Indices = indices,
			VertexCount = count.Value,
			SourceMode = mode,
			DecodedMode = decodedMode,
			RequiresGeneratedIndices = indices is null && !dracoTopology,
			RequiresDracoIndices = dracoTopology,
			RequiresTopologyExpansion = decodedMode is 2 or 3 or 5 or 6
		};
	}

	private GltfUploadBinding UploadBinding( int index, AccessorLayout layout, bool indices, bool draco )
	{
		var materialization = draco ? GltfUploadMaterialization.Draco : GltfUploadMaterialization.None;
		if ( layout.Json.TryGetProperty( "sparse", out _ ) ) materialization |= GltfUploadMaterialization.Sparse;
		if ( draco )
		{
			// Both fallback location fields are ignored by selected Draco; they may
			// not describe any source storage used by this primitive.
			return new GltfUploadBinding
			{
				AccessorIndex = index,
				ComponentType = layout.ComponentType,
				Components = layout.Components,
				Rows = layout.Rows,
				Columns = layout.Columns,
				Count = layout.Count,
				Normalized = layout.Normalized,
				ColumnStride = layout.ColumnStride,
				Materialization = materialization
			};
		}
		int? viewIndex = null, buffer = null, viewOffset = null, viewLength = null;
		var accessorOffset = GltfJson.Int( layout.Json, "byteOffset", 0 );
		int? stride = layout.Stride;
		if ( layout.Json.TryGetProperty( "bufferView", out _ ) )
		{
			viewIndex = GltfJson.Int( layout.Json, "bufferView" );
			var view = ElementAt( "bufferViews", viewIndex.Value );
			buffer = GltfJson.Int( view, "buffer" );
			viewOffset = GltfJson.Int( view, "byteOffset", 0 );
			viewLength = GltfJson.Int( view, "byteLength" );
			var bufferJson = ElementAt( "buffers", buffer.Value );
			if ( viewOffset < 0 || viewLength < 1 || (long)viewOffset + viewLength > GltfJson.Int( bufferJson, "byteLength" ) )
				throw new GltfException( "Upload layout buffer view exceeds its declared buffer." );
			if ( view.TryGetProperty( "byteStride", out _ ) )
			{
				stride = GltfJson.Int( view, "byteStride" );
				if ( indices || stride < 4 || stride > 252 || stride % 4 != 0 ) throw new GltfException( "Invalid accessor byte stride." );
			}
			// The general attribute validator may skip fallback alignment when this
			// accessor is also Draco-backed by a different primitive.
			if ( !indices && (accessorOffset % 4 != 0 || stride.Value % 4 != 0) )
				throw new GltfException( "Vertex attribute offsets and strides must be aligned to four bytes." );
			ValidateAccessorRange( layout, view, accessorOffset, layout.Count, stride.Value, viewLength.Value );
			if ( !draco && view.TryGetProperty( "extensions", out var extensions ) && extensions.ValueKind == JsonValueKind.Object &&
				((SupportsExtension( ExtMeshopt ) && extensions.TryGetProperty( ExtMeshopt, out _ )) ||
				 (SupportsExtension( KhrMeshopt ) && extensions.TryGetProperty( KhrMeshopt, out _ ))) )
				materialization |= GltfUploadMaterialization.Meshopt;
		}
		else
		{
			if ( layout.Json.TryGetProperty( "byteOffset", out _ ) ) throw new GltfException( "Accessor byteOffset requires a bufferView." );
			stride = null;
			if ( !draco ) materialization |= GltfUploadMaterialization.ZeroInitialized;
		}
		return new GltfUploadBinding
		{
			AccessorIndex = index,
			SourceBufferView = viewIndex,
			SourceBuffer = buffer,
			ViewByteOffset = viewOffset,
			ViewByteLength = viewLength,
			AccessorByteOffset = accessorOffset,
			ByteStride = stride,
			ComponentType = layout.ComponentType,
			Components = layout.Components,
			Rows = layout.Rows,
			Columns = layout.Columns,
			Count = layout.Count,
			Normalized = layout.Normalized,
			ColumnStride = layout.ColumnStride,
			Materialization = materialization
		};
	}
}
