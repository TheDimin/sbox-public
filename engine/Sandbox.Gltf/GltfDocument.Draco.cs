using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private const string DracoExtension = "KHR_draco_mesh_compression";
	private readonly record struct DracoBinding( int Mesh, int Primitive, int Mode, bool IsIndex, uint AttributeId );
	private readonly Dictionary<(int Mesh, int Primitive), GltfDracoMesh> dracoCache = [];
	private Dictionary<int, List<DracoBinding>> dracoBindings;
	private long dracoDecodedBytes;

	/// <summary>
	/// Read only this primitive's selected Draco mapping, preserving original component
	/// bytes and normalization metadata. Returned arrays are caller-owned snapshots and
	/// survive document disposal. Accessor fallback buffer views/offsets are ignored.
	/// Sparse replacements are not applied: flagged streams retain accessor provenance
	/// so the caller can apply overlays to the decoded base. Sparse source-strip indices
	/// cannot be mapped faithfully to decoded triangle-list positions and are rejected.
	/// The output limit bounds distinct returned arrays; the document's separate shared
	/// decoder budget bounds cached decoded bytes, not initial native decoder allocations.
	/// </summary>
	public GltfDracoPrimitiveData ReadDracoPrimitiveData( int mesh, int primitive, long maxOwnedBytes = 256L * 1024 * 1024 )
	{
		if ( maxOwnedBytes < 0 ) throw new GltfException( "Draco owned-byte budget cannot be negative." );
		if ( !SupportsExtension( DracoExtension ) ) throw new GltfException( "Draco is not selected for this document." );
		lock ( meshoptViews )
		{
			var value = DracoPrimitive( mesh, primitive );
			if ( !TryGetDracoExtension( value, out var extension ) ) throw new GltfException( "This primitive has no selected Draco extension." );
			var mode = GltfJson.Int( value, "mode", 4 );
			if ( mode is not (4 or 5) ) throw new GltfException( "Draco primitives require TRIANGLES or TRIANGLE_STRIP mode." );
			var attributes = GltfJson.Required( value, "attributes" );
			var compressedAttributes = GltfJson.Required( extension, "attributes" );
			if ( attributes.ValueKind != JsonValueKind.Object || compressedAttributes.ValueKind != JsonValueKind.Object || !compressedAttributes.EnumerateObject().Any() )
				throw new GltfException( "Draco primitive attributes must be non-empty objects." );
			var mappings = new Dictionary<string, (int Accessor, uint Id, AccessorLayout Layout)>( StringComparer.Ordinal );
			foreach ( var attribute in compressedAttributes.EnumerateObject() )
			{
				if ( !attributes.TryGetProperty( attribute.Name, out var accessor ) ) throw new GltfException( $"Draco attribute '{attribute.Name}' is not present in the primitive." );
				var index = GltfJson.Integer( accessor, attribute.Name );
				if ( !mappings.TryAdd( attribute.Name, (index, DracoAttributeId( attribute.Value ), Accessor( index )) ) )
					throw new GltfException( $"Duplicate Draco attribute '{attribute.Name}'." );
			}
			int? indexAccessor = value.TryGetProperty( "indices", out var indexValue ) ? GltfJson.Integer( indexValue, "indices" ) : null;
			AccessorLayout? indexLayout = indexAccessor.HasValue ? Accessor( indexAccessor.Value ) : null;
			var sparseIndices = indexLayout.HasValue && indexLayout.Value.Json.TryGetProperty( "sparse", out _ );
			if ( mode == 5 && sparseIndices ) throw new GltfException( "Draco sparse strip indices cannot be mapped to decoded triangle-list positions." );
			var decoded = ReadDracoMesh( mesh, primitive );
			ValidateDracoIndices( decoded, indexLayout, mode );
			var indexType = indexLayout?.ComponentType ?? 5125;
			var indexWidth = indexLayout?.Size ?? sizeof( uint );
			var indexBytes = (long)decoded.Indices.Length * indexWidth;
			if ( indexBytes > Array.MaxLength ) throw new GltfException( "Draco packed topology exceeds the supported array length." );
			long ownedBytes = indexBytes;
			var distinctAttributes = new Dictionary<uint, GltfDracoAttribute>();
			foreach ( var mapping in mappings.Values )
			{
				var attribute = ValidateDracoAttribute( decoded, mapping.Id, mapping.Layout );
				if ( !distinctAttributes.TryAdd( mapping.Id, attribute ) ) continue;
				ownedBytes += attribute.Bytes.LongLength;
				if ( attribute.ComponentType == 5126 )
				{
					// Test the float exponent directly so validation never changes
					// component bits, including signed zero, on the raw-byte path.
					for ( var offset = 0; offset < attribute.Bytes.Length; offset += sizeof( float ) )
						if ( (BinaryPrimitives.ReadUInt32LittleEndian( attribute.Bytes.AsSpan( offset, sizeof( float ) ) ) & 0x7f800000u) == 0x7f800000u )
							throw new GltfException( "Draco accessor contains a non-finite floating-point value." );
				}
			}
			if ( ownedBytes > maxOwnedBytes ) throw new GltfException( "Draco owned-byte budget exceeded." );

			// Every layout, decoded range, index width and owned length has been
			// checked before any returned array is allocated or copied.
			var copies = distinctAttributes.ToDictionary( pair => pair.Key, pair => (byte[])pair.Value.Bytes.Clone() );
			var streams = new Dictionary<string, GltfDracoStream>( StringComparer.Ordinal );
			foreach ( var (semantic, mapping) in mappings )
			{
				var layout = mapping.Layout;
				streams.Add( semantic, new GltfDracoStream
				{
					AccessorIndex = mapping.Accessor,
					Count = layout.Count,
					ComponentType = layout.ComponentType,
					Components = layout.Components,
					Normalized = layout.Normalized,
					ByteStride = layout.Size * layout.Components,
					HasSparseOverrides = layout.Json.TryGetProperty( "sparse", out _ ),
					Bytes = copies[mapping.Id]
				} );
			}
			var packedIndices = new byte[(int)indexBytes];
			for ( var i = 0; i < decoded.Indices.Length; i++ )
			{
				var target = packedIndices.AsSpan( i * indexWidth, indexWidth );
				var index = decoded.Indices[i];
				switch ( indexWidth )
				{
					case 1: target[0] = (byte)index; break;
					case 2: BinaryPrimitives.WriteUInt16LittleEndian( target, (ushort)index ); break;
					case 4: BinaryPrimitives.WriteUInt32LittleEndian( target, index ); break;
				}
			}
			return new GltfDracoPrimitiveData
			{
				VertexCount = decoded.VertexCount,
				SourceMode = mode,
				OwnedBytes = ownedBytes,
				Attributes = new ReadOnlyDictionary<string, GltfDracoStream>( streams ),
				Indices = new GltfDracoStream
				{
					AccessorIndex = indexAccessor,
					Count = decoded.Indices.Length,
					ComponentType = indexType,
					Components = 1,
					Normalized = false,
					ByteStride = indexWidth,
					HasSparseOverrides = sparseIndices,
					Bytes = packedIndices
				}
			};
		}
	}

	// Draco changes the source of only the attributes listed in its extension,
	// and of the primitive's index accessor; other attributes remain ordinary glTF.
	private bool TryReadDracoAccessor( int accessorIndex, bool indices, out float[] floats, out uint[] integers )
	{
		floats = null;
		integers = null;
		if ( !SupportsExtension( DracoExtension ) ) return false;
		lock ( meshoptViews )
		{
			EnsureDracoBindings();
			if ( !dracoBindings.TryGetValue( accessorIndex, out var bindings ) ) return false;
			var layout = Accessor( accessorIndex );
			if ( layout.Json.TryGetProperty( "sparse", out _ ) )
				throw new GltfException( "Sparse overrides on Draco-backed accessors are not supported." );
			uint[] exactValues = null;
			foreach ( var binding in bindings )
			{
				var mesh = ReadDracoMesh( binding.Mesh, binding.Primitive );
				if ( binding.IsIndex )
				{
					if ( binding.Mode == 5 )
						throw new GltfException( "A Draco triangle-strip index accessor cannot be reconstructed from decoded triangles; read the primitive instead." );
					ValidateDracoIndices( mesh, layout, binding.Mode );
					if ( exactValues is not null && !exactValues.AsSpan().SequenceEqual( mesh.Indices ) )
						throw new GltfException( "A shared Draco accessor resolves to different data in multiple primitives." );
					exactValues ??= mesh.Indices;
					if ( indices )
					{
						if ( integers is not null && !integers.AsSpan().SequenceEqual( mesh.Indices ) )
							throw new GltfException( "A shared Draco accessor resolves to different data in multiple primitives." );
						integers ??= (uint[])mesh.Indices.Clone();
					}
					else
					{
						var values = Array.ConvertAll( mesh.Indices, value => (float)value );
						if ( floats is not null && !floats.AsSpan().SequenceEqual( values ) )
							throw new GltfException( "A shared Draco accessor resolves to different data in multiple primitives." );
						floats ??= values;
					}
				}
				else
				{
					uint[] unsigned = null;
					if ( layout.Components == 1 && !layout.Normalized && layout.ComponentType is 5121 or 5123 or 5125 )
					{
						var attribute = ValidateDracoAttribute( mesh, binding.AttributeId, layout );
						unsigned = new uint[layout.Count];
						for ( var i = 0; i < unsigned.Length; i++ )
							unsigned[i] = (uint)ReadComponent( attribute.Bytes, i * layout.Size, layout.ComponentType, false );
						if ( exactValues is not null && !exactValues.AsSpan().SequenceEqual( unsigned ) )
							throw new GltfException( "A shared Draco accessor resolves to different data in multiple primitives." );
						exactValues ??= unsigned;
					}
					if ( indices )
					{
						if ( unsigned is null ) throw new GltfException( "Index accessor must be an unsigned, non-normalized scalar." );
						integers ??= unsigned;
					}
					else
					{
						var values = ReadDracoAttribute( mesh, binding.AttributeId, layout );
						if ( floats is not null && !floats.AsSpan().SequenceEqual( values ) )
							throw new GltfException( "A shared Draco accessor resolves to different data in multiple primitives." );
						floats ??= values;
					}
				}
			}
			return true;
		}
	}

	private bool IsDracoAttributeAccessor( int accessorIndex )
	{
		if ( !SupportsExtension( DracoExtension ) ) return false;
		lock ( meshoptViews )
		{
			EnsureDracoBindings();
			return dracoBindings.TryGetValue( accessorIndex, out var bindings ) && bindings.Any( x => !x.IsIndex );
		}
	}

	private bool TryReadDracoPrimitive( int meshIndex, int primitiveIndex, out uint[] indices )
	{
		indices = null;
		if ( !SupportsExtension( DracoExtension ) ) return false;
		lock ( meshoptViews )
		{
			var primitive = DracoPrimitive( meshIndex, primitiveIndex );
			if ( !TryGetDracoExtension( primitive, out var extension ) ) return false;
			EnsureDracoBindings();
			var mesh = ReadDracoMesh( meshIndex, primitiveIndex );
			var primitiveAttributes = GltfJson.Required( primitive, "attributes" );
			foreach ( var property in GltfJson.Required( extension, "attributes" ).EnumerateObject() )
			{
				var accessorIndex = GltfJson.Integer( primitiveAttributes.GetProperty( property.Name ), property.Name );
				ValidateDracoAttribute( mesh, DracoAttributeId( property.Value ), Accessor( accessorIndex ) );
			}
			var mode = GltfJson.Int( primitive, "mode", 4 );
			AccessorLayout? layout = primitive.TryGetProperty( "indices", out _ ) ? Accessor( GltfJson.Int( primitive, "indices" ) ) : null;
			ValidateDracoIndices( mesh, layout, mode );
			indices = (uint[])mesh.Indices.Clone();
			return true;
		}
	}

	private void EnsureDracoBindings()
	{
		if ( dracoBindings is not null ) return;
		var all = new Dictionary<int, List<DracoBinding>>();
		if ( Root.TryGetProperty( "meshes", out var meshes ) )
		{
			if ( meshes.ValueKind != JsonValueKind.Array ) throw new GltfException( "Meshes must be an array." );
			for ( var meshIndex = 0; meshIndex < meshes.GetArrayLength(); meshIndex++ )
			{
				var mesh = ElementAt( "meshes", meshIndex );
				var primitives = GltfJson.Required( mesh, "primitives" );
				if ( primitives.ValueKind != JsonValueKind.Array ) throw new GltfException( "Mesh primitives must be an array." );
				for ( var primitiveIndex = 0; primitiveIndex < primitives.GetArrayLength(); primitiveIndex++ )
				{
					var primitive = primitives[primitiveIndex];
					if ( primitive.ValueKind != JsonValueKind.Object ) throw new GltfException( "A mesh primitive must be an object." );
					if ( !TryGetDracoExtension( primitive, out var extension ) ) continue;
					var mode = GltfJson.Int( primitive, "mode", 4 );
					if ( mode is not (4 or 5) ) throw new GltfException( "Draco primitives require TRIANGLES or TRIANGLE_STRIP mode." );
					var attributes = GltfJson.Required( primitive, "attributes" );
					var dracoAttributes = GltfJson.Required( extension, "attributes" );
					if ( attributes.ValueKind != JsonValueKind.Object || dracoAttributes.ValueKind != JsonValueKind.Object || !dracoAttributes.EnumerateObject().Any() )
						throw new GltfException( "Draco primitive attributes must be non-empty objects." );
					foreach ( var attribute in dracoAttributes.EnumerateObject() )
					{
						if ( !attributes.TryGetProperty( attribute.Name, out var accessor ) )
							throw new GltfException( $"Draco attribute '{attribute.Name}' is not present in the primitive." );
						var accessorIndex = GltfJson.Integer( accessor, attribute.Name );
						_ = Accessor( accessorIndex );
						AddDracoBinding( all, accessorIndex, new( meshIndex, primitiveIndex, mode, false, DracoAttributeId( attribute.Value ) ) );
					}
					if ( primitive.TryGetProperty( "indices", out _ ) )
					{
						var accessorIndex = GltfJson.Int( primitive, "indices" );
						_ = Accessor( accessorIndex );
						AddDracoBinding( all, accessorIndex, new( meshIndex, primitiveIndex, mode, true, 0 ) );
					}
				}
			}
		}
		dracoBindings = all;
	}

	private static void AddDracoBinding( Dictionary<int, List<DracoBinding>> all, int index, DracoBinding binding )
	{
		if ( !all.TryGetValue( index, out var bindings ) ) all.Add( index, bindings = [] );
		bindings.Add( binding );
	}

	private static uint DracoAttributeId( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Number ) throw new GltfException( "Draco attribute ID must be an unsigned integer." );
		if ( value.TryGetUInt32( out var id ) ) return id;
		// Analyze decimal digits exactly; double or decimal rounding could turn a
		// fractional JSON number into a valid-looking attribute ID.
		var token = value.GetRawText().AsSpan();
		var exponentAt = token.IndexOfAny( 'e', 'E' );
		var mantissa = exponentAt < 0 ? token : token[..exponentAt];
		long exponent = 0;
		if ( exponentAt >= 0 && !long.TryParse( token[(exponentAt + 1)..], System.Globalization.NumberStyles.AllowLeadingSign,
			System.Globalization.CultureInfo.InvariantCulture, out exponent ) )
			exponent = token[exponentAt + 1] == '-' ? long.MinValue / 4 : long.MaxValue / 4;
		exponent = Math.Clamp( exponent, long.MinValue / 4, long.MaxValue / 4 );
		var dot = mantissa.IndexOf( '.' );
		var fractionDigits = dot < 0 ? 0 : mantissa.Length - dot - 1;
		var first = -1;
		var significant = 0;
		var trailingZeros = 0;
		for ( var i = mantissa[0] == '-' ? 1 : 0; i < mantissa.Length; i++ )
		{
			if ( mantissa[i] == '.' ) continue;
			if ( first < 0 && mantissa[i] != '0' ) first = i;
			if ( first < 0 ) continue;
			significant++;
			trailingZeros = mantissa[i] == '0' ? trailingZeros + 1 : 0;
		}
		if ( first < 0 ) return 0;
		var digits = significant - trailingZeros;
		var scale = exponent - fractionDigits + trailingZeros;
		if ( mantissa[0] != '-' && scale >= 0 && scale <= 10 && digits + scale <= 10 )
		{
			ulong number = 0;
			for ( var i = first; digits > 0; i++ )
			{
				if ( mantissa[i] == '.' ) continue;
				number = number * 10 + (uint)(mantissa[i] - '0');
				digits--;
			}
			while ( scale-- > 0 ) number *= 10;
			if ( number <= uint.MaxValue ) return (uint)number;
		}
		throw new GltfException( "Draco attribute ID must be an unsigned 32-bit integer." );
	}

	private JsonElement DracoPrimitive( int mesh, int primitive )
		=> SceneArrayElement( ElementAt( "meshes", mesh ), "primitives", primitive );

	private static bool TryGetDracoExtension( JsonElement primitive, out JsonElement extension )
	{
		extension = default;
		if ( !primitive.TryGetProperty( "extensions", out var extensions ) ) return false;
		if ( extensions.ValueKind != JsonValueKind.Object ) throw new GltfException( "Primitive extensions must be an object." );
		if ( !extensions.TryGetProperty( DracoExtension, out extension ) ) return false;
		if ( extension.ValueKind != JsonValueKind.Object ) throw new GltfException( "Draco extension must be an object." );
		return true;
	}

	private GltfDracoMesh ReadDracoMesh( int meshIndex, int primitiveIndex )
	{
		if ( dracoCache.TryGetValue( (meshIndex, primitiveIndex), out var cached ) ) return cached;
		var primitive = DracoPrimitive( meshIndex, primitiveIndex );
		if ( !TryGetDracoExtension( primitive, out var extension ) ) throw new GltfException( "Primitive has no Draco extension." );
		var source = BufferView( GltfJson.Int( extension, "bufferView" ) );
		var remaining = MaxDecodedBufferBytes - dracoDecodedBytes - meshoptDecodedBytes;
		if ( remaining < 0 ) throw new GltfException( "Decoded buffer budget exceeded." );
		var decoded = GltfDraco.Decode( source.ToArray(), remaining );
		if ( decoded.VertexCount <= 0 || decoded.Indices is null || decoded.Attributes is null ) throw new GltfException( "Draco decoder returned an invalid mesh." );
		long size = (long)decoded.Indices.Length * sizeof( uint );
		foreach ( var attribute in decoded.Attributes.Values )
		{
			if ( attribute?.Bytes is null ) throw new GltfException( "Draco decoder returned invalid attribute data." );
			size = checked(size + attribute.Bytes.LongLength);
		}
		if ( size > remaining ) throw new GltfException( "Decoded buffer budget exceeded." );
		dracoCache.Add( (meshIndex, primitiveIndex), decoded );
		dracoDecodedBytes += size;
		return decoded;
	}

	private static float[] ReadDracoAttribute( GltfDracoMesh mesh, uint id, AccessorLayout layout )
	{
		var attribute = ValidateDracoAttribute( mesh, id, layout );
		var values = new float[checked(layout.Count * layout.Components)];
		for ( var i = 0; i < values.Length; i++ )
			values[i] = (float)ReadComponent( attribute.Bytes, i * layout.Size, layout.ComponentType, layout.Normalized );
		return values;
	}

	private static GltfDracoAttribute ValidateDracoAttribute( GltfDracoMesh mesh, uint id, AccessorLayout layout )
	{
		if ( !mesh.Attributes.TryGetValue( id, out var attribute ) ) throw new GltfException( $"Draco attribute ID {id} is missing from the decoded mesh." );
		if ( layout.Columns != 1 || layout.Count != mesh.VertexCount || attribute.ComponentType != layout.ComponentType || attribute.Components != layout.Components ||
			(long)layout.Count * layout.Components * layout.Size != attribute.Bytes.LongLength )
			throw new GltfException( "Draco attribute data does not match its accessor layout." );
		return attribute;
	}

	private static void ValidateDracoIndices( GltfDracoMesh mesh, AccessorLayout? layout, int mode )
	{
		if ( mode is not (4 or 5) || mesh.Indices.Length == 0 || mesh.Indices.Length % 3 != 0 )
			throw new GltfException( "Draco mesh must decode to a non-empty triangle list." );
		uint restart = uint.MaxValue;
		if ( layout.HasValue )
		{
			var accessor = layout.Value;
			if ( accessor.Components != 1 || accessor.Normalized || accessor.ComponentType is not (5121 or 5123 or 5125) )
				throw new GltfException( "Draco index accessor must be an unsigned scalar." );
			if ( mode == 4 && accessor.Count != mesh.Indices.Length || mode == 5 && accessor.Count < 3 )
				throw new GltfException( "Draco index count does not match its accessor." );
			restart = accessor.ComponentType == 5121 ? byte.MaxValue : accessor.ComponentType == 5123 ? ushort.MaxValue : uint.MaxValue;
		}
		if ( mesh.Indices.Any( index => index >= mesh.VertexCount || index >= restart ) )
			throw new GltfException( "Draco index exceeds the vertex count or accessor component range." );
	}

	private void ClearDracoCache()
	{
		lock ( meshoptViews )
		{
			dracoCache.Clear();
			dracoBindings = null;
			dracoDecodedBytes = 0;
		}
	}
}
