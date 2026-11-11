using System.Collections.ObjectModel;
using System.Buffers.Binary;
using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>Document-independent owned upload bytes; no GPU resource is allocated.</summary>
public sealed class GltfUploadStorage
{
	private readonly byte[] bytes;
	internal GltfUploadStorage( byte[] bytes ) => this.bytes = bytes;
	internal byte[] MutableBytes => bytes;
	/// <summary>Read-only view of the owned snapshot. Keep it alive while consuming these bytes.</summary>
	public ReadOnlyMemory<byte> Bytes => bytes;
}

/// <summary>One source binding and the actual offset and stride within its owned storage.</summary>
public sealed class GltfUploadStream
{
	/// <summary>Authored accessor provenance, or null for Draco-generated topology without an accessor.</summary>
	public GltfUploadBinding Binding { get; internal init; }
	public GltfUploadStorage Storage { get; internal init; }
	public int ByteOffset { get; internal init; }
	public int ByteStride { get; internal init; }
	/// <summary>Actual output format and count, which may differ from source strip topology.</summary>
	public int Count { get; internal init; }
	public int ComponentType { get; internal init; }
	public int Components { get; internal init; }
	public bool Normalized { get; internal init; }
	/// <summary>True when zero-base, sparse, or selected codec data was materialized.</summary>
	public bool IsMaterialized { get; internal init; }
}

/// <summary>Owned primitive streams. Topology expansion and coordinate conversion remain separate.</summary>
public sealed class GltfPrimitiveUploadData
{
	public GltfPrimitiveUploadLayout Layout { get; internal init; }
	public IReadOnlyDictionary<string, GltfUploadStream> Attributes { get; internal init; }
	public GltfUploadStream Indices { get; internal init; }
	/// <summary>Total bytes in distinct returned storage objects.</summary>
	public long OwnedBytes { get; internal init; }
}

public sealed partial class GltfDocument
{
	/// <summary>
	/// Copy each selected source view once, or materialize sparse/zero-base accessors
	/// in their original component format with glTF matrix padding. Returned offsets
	/// are relative to the owned storage, not source buffer origins. Meshopt views are
	/// decoded by the existing view reader, under its separate decoder budget. The
	/// limit here bounds returned owned byte copies only, not source/resolver memory,
	/// native codec allocations or temporary metadata. Draco uses a primitive-scoped
	/// decoded base; sparse strip-index overlays remain unsupported.
	/// </summary>
	public GltfPrimitiveUploadData MaterializePrimitiveUpload( int mesh, int primitive, long maxOwnedBytes = 256L * 1024 * 1024 )
	{
		if ( maxOwnedBytes < 0 ) throw new GltfException( "Upload owned-byte budget cannot be negative." );
		var layout = DescribePrimitiveUpload( mesh, primitive );
		var dracoGroups = new Dictionary<uint, List<KeyValuePair<string, GltfUploadBinding>>>();
		if ( layout.RequiresDracoIndices )
		{
			var primitiveValue = DracoPrimitive( mesh, primitive );
			if ( !TryGetDracoExtension( primitiveValue, out var extension ) ) throw new GltfException( "Selected Draco extension is missing." );
			var mappings = GltfJson.Required( extension, "attributes" );
			foreach ( var attribute in layout.Attributes )
			{
				if ( (attribute.Value.Materialization & GltfUploadMaterialization.Draco) == 0 ) continue;
				var id = DracoAttributeId( GltfJson.Required( mappings, attribute.Key ) );
				if ( !dracoGroups.TryGetValue( id, out var group ) ) dracoGroups.Add( id, group = [] );
				group.Add( attribute );
			}
		}

		var bindings = layout.Attributes.Values.ToList();
		if ( layout.Indices is not null ) bindings.Add( layout.Indices );
		var viewLengths = new Dictionary<int, int>();
		var accessorLengths = new Dictionary<int, int>();
		long required = 0;
		foreach ( var binding in bindings )
		{
			if ( (binding.Materialization & GltfUploadMaterialization.Draco) != 0 ) continue;
			var needsAccessorCopy = (binding.Materialization & (GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized)) != 0;
			if ( needsAccessorCopy )
			{
				if ( accessorLengths.ContainsKey( binding.AccessorIndex ) ) continue;
				var sourceLayout = Accessor( binding.AccessorIndex );
				var length = (long)sourceLayout.Count * sourceLayout.Stride;
				if ( length > Array.MaxLength ) throw new GltfException( "Upload accessor exceeds the supported array length." );
				accessorLengths.Add( binding.AccessorIndex, (int)length );
				required += length;
			}
			else
			{
				var view = binding.SourceBufferView ?? throw new GltfException( "Upload binding has no source buffer view." );
				if ( viewLengths.ContainsKey( view ) ) continue;
				var length = binding.ViewByteLength ?? throw new GltfException( "Upload binding has no buffer view length." );
				viewLengths.Add( view, length );
				required += length;
			}
			if ( required > maxOwnedBytes ) throw new GltfException( "Upload owned-byte budget exceeded." );
		}
		// The codec shares one detached array per Draco ID. Reserve every extra
		// sparse variant before asking it to allocate its own output arrays.
		foreach ( var group in dracoGroups.Values )
		{
			var sparseAccessors = group.Where( item => (item.Value.Materialization & GltfUploadMaterialization.Sparse) != 0 )
				.Select( item => item.Value.AccessorIndex ).Distinct().Count();
			var hasPlain = group.Any( item => (item.Value.Materialization & GltfUploadMaterialization.Sparse) == 0 );
			var extraCopies = Math.Max( 0, sparseAccessors - (hasPlain ? 0 : 1) );
			if ( extraCopies == 0 ) continue;
			var source = Accessor( group[0].Value.AccessorIndex );
			var length = (long)source.Count * source.Stride;
			if ( length > Array.MaxLength || length > (maxOwnedBytes - required) / extraCopies ) throw new GltfException( "Upload owned-byte budget exceeded." );
			required += length * extraCopies;
		}
		var decoded = layout.RequiresDracoIndices ? ReadDracoPrimitiveData( mesh, primitive, maxOwnedBytes - required ) : null;
		if ( decoded is not null ) required += decoded.OwnedBytes;

		var views = new Dictionary<int, GltfUploadStorage>();
		var accessors = new Dictionary<int, GltfUploadStorage>();
		GltfUploadStream Copy( GltfUploadBinding binding )
		{
			var needsAccessorCopy = (binding.Materialization & (GltfUploadMaterialization.Sparse | GltfUploadMaterialization.ZeroInitialized)) != 0;
			if ( needsAccessorCopy )
			{
				if ( !accessors.TryGetValue( binding.AccessorIndex, out var storage ) )
				{
					storage = new GltfUploadStorage( MaterializeUploadAccessor( binding.AccessorIndex, accessorLengths[binding.AccessorIndex] ) );
					accessors.Add( binding.AccessorIndex, storage );
				}
				return new GltfUploadStream
				{
					Binding = binding,
					Storage = storage,
					ByteOffset = 0,
					ByteStride = Accessor( binding.AccessorIndex ).Stride,
					Count = binding.Count,
					ComponentType = binding.ComponentType,
					Components = binding.Components,
					Normalized = binding.Normalized,
					IsMaterialized = true
				};
			}
			var view = binding.SourceBufferView.Value;
			if ( !views.TryGetValue( view, out var viewStorage ) )
			{
				var bytes = BufferView( view );
				if ( bytes.Length != viewLengths[view] ) throw new GltfException( "Upload buffer view length changed during decoding." );
				viewStorage = new GltfUploadStorage( bytes.ToArray() );
				views.Add( view, viewStorage );
			}
			return new GltfUploadStream
			{
				Binding = binding,
				Storage = viewStorage,
				ByteOffset = binding.AccessorByteOffset ?? throw new GltfException( "Upload source offset is unavailable." ),
				ByteStride = binding.ByteStride ?? throw new GltfException( "Upload source stride is unavailable." ),
				Count = binding.Count,
				ComponentType = binding.ComponentType,
				Components = binding.Components,
				Normalized = binding.Normalized,
				IsMaterialized = (binding.Materialization & GltfUploadMaterialization.Meshopt) != 0
			};
		}

		var decodedStreams = new Dictionary<string, GltfUploadStream>( StringComparer.Ordinal );
		if ( decoded is not null )
		{
			foreach ( var group in dracoGroups.Values )
			{
				var baseStream = decoded.Attributes[group[0].Key];
				var baseStorage = new GltfUploadStorage( baseStream.Bytes );
				var sparseStores = new Dictionary<int, GltfUploadStorage>();
				var hasPlain = group.Any( item => (item.Value.Materialization & GltfUploadMaterialization.Sparse) == 0 );
				foreach ( var item in group )
				{
					if ( (item.Value.Materialization & GltfUploadMaterialization.Sparse) == 0 || sparseStores.ContainsKey( item.Value.AccessorIndex ) ) continue;
					var store = !hasPlain && sparseStores.Count == 0 ? baseStorage : new GltfUploadStorage( (byte[])baseStream.Bytes.Clone() );
					sparseStores.Add( item.Value.AccessorIndex, store );
				}
				// Clones are created before any in-place overlay so each starts from
				// the same detached decoder output.
				foreach ( var (accessor, store) in sparseStores ) ApplyUploadSparse( Accessor( accessor ), store.MutableBytes );
				foreach ( var item in group )
				{
					var data = decoded.Attributes[item.Key];
					var store = data.HasSparseOverrides ? sparseStores[item.Value.AccessorIndex] : baseStorage;
					decodedStreams.Add( item.Key, new GltfUploadStream
					{
						Binding = item.Value,
						Storage = store,
						ByteOffset = 0,
						ByteStride = data.ByteStride,
						Count = data.Count,
						ComponentType = data.ComponentType,
						Components = data.Components,
						Normalized = data.Normalized,
						IsMaterialized = true
					} );
				}
			}
		}
		var attributes = new Dictionary<string, GltfUploadStream>( StringComparer.Ordinal );
		foreach ( var attribute in layout.Attributes ) attributes.Add( attribute.Key, decodedStreams.TryGetValue( attribute.Key, out var stream ) ? stream : Copy( attribute.Value ) );
		foreach ( var (semantic, stream) in attributes ) ValidateUploadFloatComponents( semantic, stream );
		GltfUploadStream indices;
		if ( decoded is not null )
		{
			var source = decoded.Indices;
			if ( source.HasSparseOverrides )
			{
				ApplyUploadSparse( Accessor( source.AccessorIndex.Value ), source.Bytes );
				var restart = source.ComponentType == 5121 ? byte.MaxValue : source.ComponentType == 5123 ? ushort.MaxValue : uint.MaxValue;
				for ( var i = 0; i < source.Count; i++ )
				{
					var index = (uint)ReadComponent( source.Bytes, i * source.ByteStride, source.ComponentType, false );
					if ( index >= decoded.VertexCount || index >= restart ) throw new GltfException( "Sparse Draco index exceeds its vertex or component range." );
				}
			}
			indices = new GltfUploadStream
			{
				Binding = layout.Indices,
				Storage = new GltfUploadStorage( source.Bytes ),
				ByteOffset = 0,
				ByteStride = source.ByteStride,
				Count = source.Count,
				ComponentType = source.ComponentType,
				Components = source.Components,
				Normalized = source.Normalized,
				IsMaterialized = true
			};
		}
		else indices = layout.Indices is null ? null : Copy( layout.Indices );
		return new GltfPrimitiveUploadData { Layout = layout, Attributes = new ReadOnlyDictionary<string, GltfUploadStream>( attributes ), Indices = indices, OwnedBytes = required };
	}

	private static void ValidateUploadFloatComponents( string semantic, GltfUploadStream stream )
	{
		if ( stream.ComponentType != 5126 ) return;
		var binding = stream.Binding;
		var bytes = stream.Storage.Bytes.Span;
		for ( var element = 0; element < stream.Count; element++ )
			for ( var column = 0; column < binding.Columns; column++ )
				for ( var row = 0; row < binding.Rows; row++ )
				{
					var offset = stream.ByteOffset + element * stream.ByteStride + column * binding.ColumnStride + row * sizeof( float );
					var bits = BinaryPrimitives.ReadInt32LittleEndian( bytes.Slice( offset, sizeof( float ) ) );
					if ( (bits & 0x7f800000) == 0x7f800000 )
						throw new GltfException( $"Upload attribute '{semantic}' contains a non-finite floating-point value." );
				}
	}

	private byte[] MaterializeUploadAccessor( int index, int byteLength )
	{
		var layout = Accessor( index );
		var output = new byte[byteLength];
		if ( layout.Json.TryGetProperty( "bufferView", out _ ) )
		{
			var viewIndex = GltfJson.Int( layout.Json, "bufferView" );
			var view = ElementAt( "bufferViews", viewIndex );
			var offset = GltfJson.Int( layout.Json, "byteOffset", 0 );
			var stride = GltfJson.Int( view, "byteStride", layout.Stride );
			var source = BufferView( viewIndex );
			ValidateAccessorRange( layout, view, offset, layout.Count, stride, source.Length );
			for ( var i = 0; i < layout.Count; i++ ) source.Span.Slice( offset + i * stride, layout.TailSize ).CopyTo( output.AsSpan( i * layout.Stride ) );
		}
		ApplyUploadSparse( layout, output );
		return output;
	}

	// Compiler-only snapshot in the accessor's original component representation.
	internal byte[] CompilerMaterializeAccessor( int index, long maxBytes, out int stride )
	{
		var layout = Accessor( index );
		stride = layout.Stride;
		// A tightly packed vertex attribute must retain four-byte element
		// alignment. Leave smaller strides sparse rather than guessing its use.
		if ( stride % 4 != 0 ) return null;
		var length = (long)layout.Count * layout.Stride;
		if ( length > Array.MaxLength || length > maxBytes ) throw new GltfException( "Compiler accessor budget exceeded." );
		return MaterializeUploadAccessor( index, (int)length );
	}

	private void ApplyUploadSparse( AccessorLayout layout, byte[] output )
	{
		if ( !layout.Json.TryGetProperty( "sparse", out var sparse ) ) return;
		var count = GltfJson.Int( sparse, "count" );
		if ( count < 1 || count > layout.Count ) throw new GltfException( "Upload accessor has invalid sparse count." );
		var indexInfo = GltfJson.Required( sparse, "indices" );
		var indexType = GltfJson.Int( indexInfo, "componentType" );
		var indexSize = indexType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new GltfException( "Sparse indices must be unsigned integers." ) };
		var indexViewIndex = GltfJson.Int( indexInfo, "bufferView" );
		var indexView = ElementAt( "bufferViews", indexViewIndex ); ValidateSparseView( indexView );
		var indexData = BufferView( indexViewIndex );
		var indexOffset = GltfJson.Int( indexInfo, "byteOffset", 0 );
		if ( indexOffset < 0 || indexOffset % indexSize != 0 || (GltfJson.Int( indexView, "byteOffset", 0 ) + (long)indexOffset) % indexSize != 0 ||
			(long)indexOffset + (long)count * indexSize > indexData.Length ) throw new GltfException( "Sparse upload indices exceed or are misaligned within their buffer view." );
		var valueInfo = GltfJson.Required( sparse, "values" );
		var valueViewIndex = GltfJson.Int( valueInfo, "bufferView" );
		var valueView = ElementAt( "bufferViews", valueViewIndex ); ValidateSparseView( valueView );
		var valueData = BufferView( valueViewIndex );
		var valueOffset = GltfJson.Int( valueInfo, "byteOffset", 0 );
		ValidateAccessorRange( layout, valueView, valueOffset, count, layout.Stride, valueData.Length );
		uint previous = 0;
		for ( var i = 0; i < count; i++ )
		{
			var target = (uint)ReadComponent( indexData.Span, indexOffset + i * indexSize, indexType, false );
			if ( target >= layout.Count || (i > 0 && target <= previous) ) throw new GltfException( "Sparse indices must be strictly increasing and within the accessor count." );
			valueData.Span.Slice( valueOffset + i * layout.Stride, layout.TailSize ).CopyTo( output.AsSpan( (int)target * layout.Stride ) );
			previous = target;
		}
	}
}
