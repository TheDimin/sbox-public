using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Sandbox.Mounting.Sims4;

[StructLayout( LayoutKind.Sequential, Pack = 1 )]
public struct VertexElementFormat
{
	public VertexUsage Usage;
	public VertexFormat Format;
	int size;// this could be reversed..
}

public enum VertexUsage : uint
{
	POSITION = 0,
	NORMAL = 1,
	UV = 2,
	BONE_ASSIGN = 3,
	WEIGHTS = 4,
	TANGENT = 5,
	COLOR = 6,
	BINORMAL = 10,
}

public enum VertexFormat : uint
{
	FLOAT1 = 0x01,
	FLOAT2 = 0x02,
	FLOAT3 = 0x03,
	FLOAT4 = 0x04,
	UBYTE4 = 0x20,
	SHORT2 = 0x21,
	SHORT4 = 0x22,
}

public readonly struct FaceIndexView
{
	private readonly ReadOnlyMemory<byte> _data;

	public FaceIndexView( ReadOnlyMemory<byte> data, byte indexSize, uint count )
	{
		_data = data;
		IndexSize = indexSize;
		Count = count;
	}

	public byte IndexSize { get; }
	public uint Count { get; }
	public ReadOnlySpan<byte> Raw => _data.Span;

	public uint this[int index]
	{
		get
		{
			if ( (uint)index >= Count )
				throw new ArgumentOutOfRangeException( nameof( index ) );

			var span = _data.Span;
			return IndexSize switch
			{
				1 => span[index],
				2 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( span.Slice( index * 2 ) ),
				4 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( span.Slice( index * 4 ) ),
				_ => throw new InvalidOperationException( $"Unsupported index size {IndexSize}" )
			};
		}
	}

	public Enumerator GetEnumerator() => new Enumerator( _data.Span, IndexSize, Count );

	public ref struct Enumerator
	{
		private readonly ReadOnlySpan<byte> _span;
		private readonly byte _stride;
		private readonly uint _count;
		private uint _idx;

		public Enumerator( ReadOnlySpan<byte> span, byte stride, uint count )
		{
			_span = span;
			_stride = stride;
			_count = count;
			_idx = uint.MaxValue;
		}

		public uint Current { get; private set; }

		public bool MoveNext()
		{
			uint next = _idx + 1;
			if ( next >= _count )
				return false;

			_idx = next;
			Current = _stride switch
			{
				1 => _span[(int)next],
				2 => System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( _span.Slice( (int)next * 2 ) ),
				4 => System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( _span.Slice( (int)next * 4 ) ),
				_ => throw new InvalidOperationException( $"Unsupported index size {_stride}" )
			};
			return true;
		}
	}
}

/// <summary>
/// Parsed GEOM resource with RCOL header and mesh data backed directly by the resource bytes.
/// </summary>
public sealed class GEOMResource
{
	private readonly byte[] _data;
	private readonly uint _rcolVersion;
	private readonly int _geomOffset;
	private readonly uint _geomVersion;
	private readonly uint _vertexCount;
	private readonly uint _elementCount;
	private readonly int _elementsOffset;
	private readonly int _vertexBufferOffset;
	private readonly int _vertexBufferLength;
	private readonly uint _faceGroupCount;
	private readonly byte _faceIndexSize;
	private readonly uint _faceIndexCount;
	private readonly int _faceIndexOffset;
	private readonly int _faceIndexLength;

	public GEOMResource( byte[] data )
	{
		_data = data;

		var reader = new SpanReader( data );
		_rcolVersion = reader.ReadUInt32();
		var unknown1 = reader.ReadUInt32();
		var unknown2 = reader.ReadUInt32();
		var externalResourceCount = reader.ReadUInt32();
		var internalChunkCount = reader.ReadUInt32();

		reader.Skip( checked((int)(internalChunkCount * StructUtil.SizeOf<ResourceKey>())) );
		reader.Skip( checked((int)(externalResourceCount * StructUtil.SizeOf<ResourceKey>())) );

		ReadOnlySpan<byte> objectDataSpan = reader.ReadSpan( checked((int)(internalChunkCount * StructUtil.SizeOf<ObjectData>())) );
		var objectData = StructUtil.CastSpan<ObjectData>( objectDataSpan );
		if ( objectData.Length == 0 )
			throw new InvalidDataException( "RCOL header missing object data entries" );

		int geomOffset = checked((int)objectData[0].Position);
		reader.Seek( geomOffset );

		var magic = reader.ReadSpan( 4 );
		if ( !magic.SequenceEqual( "GEOM"u8 ) )
		{
			// Some RCOL chunk locations point at a u32 chunk-size prefix,
			// with the chunk magic immediately after it.
			var prefixedMagic = reader.ReadSpan( 4 );
			if ( !prefixedMagic.SequenceEqual( "GEOM"u8 ) )
				throw new InvalidDataException( "Expected GEOM chunk in first RCOL object" );

			geomOffset = checked(geomOffset + sizeof( uint ));
		}

		_geomOffset = geomOffset;

		_geomVersion = reader.ReadUInt32();
		var tgiOffset = reader.ReadUInt32();
		var tgiCount = reader.ReadUInt32();
		var shaderHash = reader.ReadUInt32();

		if ( shaderHash != 0 )
		{
			var mtnfSize = reader.ReadUInt32();
			reader.Skip( checked((int)mtnfSize) );
		}

		reader.Skip( sizeof( uint ) * 2 ); // mergeGroup + sortOrder
		_vertexCount = reader.ReadUInt32();
		_elementCount = reader.ReadUInt32();

		_elementsOffset = reader.Offset;
		reader.Skip( checked((int)(_elementCount * StructUtil.SizeOf<VertexElementFormat>())) );

		int stride = CalculateStride( VertexElements );
		_vertexBufferOffset = reader.Offset;
		_vertexBufferLength = checked((int)(_vertexCount * (uint)stride));
		reader.Skip( _vertexBufferLength );

		_faceGroupCount = reader.ReadUInt32();

		if ( _faceGroupCount > 0 )
		{
			_faceIndexSize = reader.ReadByte();
			_faceIndexCount = reader.ReadUInt32();
			_faceIndexLength = checked((int)(_faceIndexCount * _faceIndexSize));
			_faceIndexOffset = reader.Offset;
			reader.Skip( _faceIndexLength );

			// Skip remaining groups if any
			for ( uint g = 1; g < _faceGroupCount; g++ )
			{
				byte groupIndexSize = reader.ReadByte();
				uint c = reader.ReadUInt32();
				reader.Skip( checked((int)(c * groupIndexSize)) );
			}
		}
		else
		{
			_faceIndexOffset = _faceIndexLength = 0;
			_faceIndexCount = 0;
			_faceIndexSize = 0;
		}
	}

	public uint RCOLVersion => _rcolVersion;
	public uint GEOMVersion => _geomVersion;
	public uint VertexCount => _vertexCount;
	public uint FaceGroupCount => _faceGroupCount;

	public ReadOnlySpan<VertexElementFormat> VertexElements => MemoryMarshal.Cast<byte, VertexElementFormat>( _data.AsSpan( _elementsOffset, checked((int)(_elementCount * StructUtil.SizeOf<VertexElementFormat>())) ) );
	public ReadOnlySpan<byte> VertexBuffer => _data.AsSpan( _vertexBufferOffset, _vertexBufferLength );

	public FaceIndexView FaceIndices => _faceIndexLength == 0
		? new FaceIndexView( ReadOnlyMemory<byte>.Empty, 0, 0 )
		: new FaceIndexView( _data.AsMemory( _faceIndexOffset, _faceIndexLength ), _faceIndexSize, _faceIndexCount );

	private static int CalculateStride( ReadOnlySpan<VertexElementFormat> elements )
	{
		int stride = 0;
		foreach ( ref readonly var element in elements )
		{
			stride += element.Format switch
			{
				VertexFormat.FLOAT1 => 4,
				VertexFormat.FLOAT2 => 8,
				VertexFormat.FLOAT3 => 12,
				VertexFormat.FLOAT4 => 16,
				VertexFormat.UBYTE4 => 4,
				VertexFormat.SHORT2 => 4,
				VertexFormat.SHORT4 => 8,
				_ => throw new InvalidDataException( $"Unsupported vertex format {element.Format}" )
			};
		}

		return stride;
	}
}
