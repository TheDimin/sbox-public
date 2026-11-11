using System.Buffers.Binary;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	public float[] ReadFloatAccessor( int index )
	{
		DecodeAccessor( index, false, out var values, out _ );
		return values;
	}

	public uint[] ReadIndexAccessor( int index )
	{
		DecodeAccessor( index, true, out _, out var values );
		return values;
	}

	private readonly record struct AccessorLayout( JsonElement Json, int Count, int ComponentType, int Size, int Rows, int Columns, bool Normalized )
	{
		public int Components => Rows * Columns;
		public int ColumnStride => Columns == 1 ? Rows * Size : (Rows * Size + 3) & ~3;
		public int Stride => ColumnStride * Columns;
		public int TailSize => ColumnStride * (Columns - 1) + Rows * Size;
	}

	private AccessorLayout Accessor( int index )
	{
		var value = ElementAt( "accessors", index );
		var count = GltfJson.Int( value, "count" );
		if ( count < 1 ) throw new GltfException( $"Accessor {index} must have a positive count." );
		var component = GltfJson.Int( value, "componentType" );
		var size = component switch
		{
			5120 or 5121 => 1,
			5122 or 5123 => 2,
			5125 or 5126 => 4,
			_ => throw new GltfException( $"Accessor {index} has invalid componentType {component}." )
		};
		var (rows, columns) = GltfJson.String( value, "type" ) switch
		{
			"SCALAR" => (1, 1),
			"VEC2" => (2, 1),
			"VEC3" => (3, 1),
			"VEC4" => (4, 1),
			"MAT2" => (2, 2),
			"MAT3" => (3, 3),
			"MAT4" => (4, 4),
			_ => throw new GltfException( $"Accessor {index} has invalid type." )
		};
		var normalized = GltfJson.Bool( value, "normalized" );
		if ( normalized && component is not (5120 or 5121 or 5122 or 5123) )
			throw new GltfException( $"Accessor {index} requests normalization for an unsupported component type." );
		return new( value, count, component, size, rows, columns, normalized );
	}

	private void DecodeAccessor( int index, bool indices, out float[] floats, out uint[] integers )
	{
		var layout = Accessor( index );
		if ( indices && (layout.Components != 1 || layout.ComponentType is not (5121 or 5123 or 5125) || layout.Normalized) )
			throw new GltfException( $"Index accessor {index} must be an unsigned, non-normalized SCALAR." );
		if ( TryReadDracoAccessor( index, indices, out floats, out integers ) ) return;
		int length;
		try { length = checked(layout.Count * layout.Components); }
		catch ( OverflowException e ) { throw new GltfException( $"Accessor {index} exceeds supported array length.", e ); }
		var accessor = layout.Json;
		ReadOnlyMemory<byte>? baseData = null;
		var baseOffset = 0;
		var baseStride = layout.Stride;
		if ( accessor.TryGetProperty( "bufferView", out _ ) )
		{
			var viewIndex = GltfJson.Int( accessor, "bufferView" );
			var view = ElementAt( "bufferViews", viewIndex );
			var offset = GltfJson.Int( accessor, "byteOffset", 0 );
			var stride = view.TryGetProperty( "byteStride", out _ ) ? GltfJson.Int( view, "byteStride" ) : layout.Stride;
			if ( view.TryGetProperty( "byteStride", out _ ) && (indices || stride < 4 || stride > 252 || stride % 4 != 0) )
				throw new GltfException( $"Accessor {index} has an invalid buffer view byteStride." );
			var data = BufferView( viewIndex );
			ValidateAccessorRange( layout, view, offset, layout.Count, stride, data.Length );
			baseData = data;
			baseOffset = offset;
			baseStride = stride;
		}
		else if ( accessor.TryGetProperty( "byteOffset", out _ ) )
			throw new GltfException( $"Accessor {index} cannot specify byteOffset without bufferView." );
		floats = indices ? null : new float[length];
		integers = indices ? new uint[length] : null;
		if ( baseData.HasValue )
		{
			for ( var element = 0; element < layout.Count; element++ )
				DecodeElement( baseData.Value.Span, baseOffset + element * baseStride, layout, element * layout.Components, floats, integers );
		}

		if ( !accessor.TryGetProperty( "sparse", out var sparse ) ) return;
		var count = GltfJson.Int( sparse, "count" );
		if ( count < 1 || count > layout.Count ) throw new GltfException( $"Accessor {index} has invalid sparse count." );
		var indexInfo = GltfJson.Required( sparse, "indices" );
		var indexType = GltfJson.Int( indexInfo, "componentType" );
		var indexSize = indexType switch { 5121 => 1, 5123 => 2, 5125 => 4, _ => throw new GltfException( "Sparse indices must be unsigned integers." ) };
		var indexView = GltfJson.Int( indexInfo, "bufferView" );
		var indexViewJson = ElementAt( "bufferViews", indexView );
		ValidateSparseView( indexViewJson );
		var indexData = BufferView( indexView );
		var indexOffset = GltfJson.Int( indexInfo, "byteOffset", 0 );
		if ( indexOffset < 0 || indexOffset % indexSize != 0 || (GltfJson.Int( indexViewJson, "byteOffset", 0 ) + (long)indexOffset) % indexSize != 0 ||
			(long)indexOffset + (long)count * indexSize > indexData.Length )
			throw new GltfException( "Sparse indices exceed or are misaligned within their buffer view." );
		var valueInfo = GltfJson.Required( sparse, "values" );
		var valueView = GltfJson.Int( valueInfo, "bufferView" );
		var valueViewJson = ElementAt( "bufferViews", valueView );
		ValidateSparseView( valueViewJson );
		var valueData = BufferView( valueView );
		var valueOffset = GltfJson.Int( valueInfo, "byteOffset", 0 );
		ValidateAccessorRange( layout, valueViewJson, valueOffset, count, layout.Stride, valueData.Length );
		uint previous = 0;
		for ( var element = 0; element < count; element++ )
		{
			var slot = (uint)ReadComponent( indexData.Span, indexOffset + element * indexSize, indexType, false );
			if ( slot >= layout.Count || (element != 0 && slot <= previous) )
				throw new GltfException( "Sparse accessor indices must be strictly increasing and within the accessor count." );
			DecodeElement( valueData.Span, valueOffset + element * layout.Stride, layout, (int)slot * layout.Components, floats, integers );
			previous = slot;
		}
	}

	private static void ValidateSparseView( JsonElement view )
	{
		if ( view.TryGetProperty( "target", out _ ) || view.TryGetProperty( "byteStride", out _ ) )
			throw new GltfException( "Sparse buffer views cannot specify target or byteStride." );
	}

	private static void ValidateAccessorRange( AccessorLayout layout, JsonElement view, int offset, int count, int stride, int length )
	{
		var absoluteOffset = GltfJson.Int( view, "byteOffset", 0 ) + (long)offset;
		if ( offset < 0 || offset % layout.Size != 0 || absoluteOffset % layout.Size != 0 ||
			(layout.Columns > 1 && absoluteOffset % 4 != 0) || stride < layout.Stride || stride % layout.Size != 0 ||
			(long)offset + (long)(count - 1) * stride + layout.TailSize > length )
			throw new GltfException( "Accessor data is misaligned or exceeds its buffer view." );
	}

	private static void DecodeElement( ReadOnlySpan<byte> data, int offset, AccessorLayout layout, int destination, float[] floats, uint[] integers )
	{
		for ( var column = 0; column < layout.Columns; column++ )
		{
			for ( var row = 0; row < layout.Rows; row++ )
			{
				var value = ReadComponent( data, offset + column * layout.ColumnStride + row * layout.Size, layout.ComponentType, layout.Normalized );
				var index = destination + column * layout.Rows + row;
				if ( integers is not null ) integers[index] = (uint)value;
				else floats[index] = (float)value;
			}
		}
	}

	private static double ReadComponent( ReadOnlySpan<byte> data, int offset, int type, bool normalized )
	{
		double value = type switch
		{
			5120 => (sbyte)data[offset],
			5121 => data[offset],
			5122 => BinaryPrimitives.ReadInt16LittleEndian( data[offset..] ),
			5123 => BinaryPrimitives.ReadUInt16LittleEndian( data[offset..] ),
			5125 => (double)BinaryPrimitives.ReadUInt32LittleEndian( data[offset..] ),
			5126 => (double)BinaryPrimitives.ReadSingleLittleEndian( data[offset..] ),
			_ => throw new GltfException( "Invalid component type." )
		};
		if ( !double.IsFinite( value ) ) throw new GltfException( "Accessor contains a non-finite floating-point value." );
		if ( !normalized ) return value;
		return type switch
		{
			5120 => Math.Max( value / 127, -1 ),
			5121 => value / 255,
			5122 => Math.Max( value / 32767, -1 ),
			5123 => value / 65535,
			_ => throw new GltfException( "Invalid normalized component type." )
		};
	}
}
