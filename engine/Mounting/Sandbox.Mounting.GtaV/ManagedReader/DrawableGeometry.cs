using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;

namespace GtaV.ManagedReader;

// The layout follows ragekit's interpretation of the resource. See ThirdPartyNotices.txt.
internal readonly struct DrawableGeometry
{
	// The format codes are DXGI vertex formats. Offsets and stride are measured in bytes.
	internal readonly record struct Field( byte Slot, int Offset, byte Format )
	{
		internal string Semantic => (Slot / 4) switch
		{
			0 => "position", 1 => "normal", 2 => "tangent", 3 => "binormal",
			4 => "blendweight", 5 => "blendindices", 6 => "color", _ => "texcoord",
		};
		internal int Index => Slot < 28 ? Slot % 4 : Slot - 28;
	}

	readonly RscResource resource;
	readonly ulong declaration, vertexData, indexData;
	readonly bool enhanced;
	internal ReadOnlySpan<byte> Vertices => resource.Array( vertexData, VertexCount, Stride );
	internal ReadOnlySpan<byte> Indices => resource.Array( indexData, IndexCount, sizeof(ushort) );
	internal int VertexCount { get; }
	internal int IndexCount { get; }
	internal int Stride { get; }
	internal bool IsEmpty => VertexCount == 0 || IndexCount == 0;

	internal DrawableGeometry( RscResource resource, ulong pointer, bool enhanced )
	{
		this.resource = resource;
		this.enhanced = enhanced;
		var geometry = resource.At( pointer, 0x74 );
		ulong vertexBuffer = Pointer( geometry, 0x18 ), indexBuffer = Pointer( geometry, 0x38 );
		if ( vertexBuffer != 0 )
		{
			var buffer = resource.At( vertexBuffer, enhanced ? 0x40 : 0x38 );
			if ( enhanced )
			{
				Stride = Short( buffer, 12 );
				VertexCount = checked((int)Word( buffer, 8 ));
				declaration = Pointer( buffer, 0x38 );
				vertexData = Pointer( buffer, 0x18 );
			}
			else
			{
				Stride = Short( buffer, 8 );
				if ( Stride == 0 ) Stride = Short( geometry, 0x70 );
				VertexCount = checked((int)Word( buffer, 0x18 ));
				if ( VertexCount == 0 ) VertexCount = Short( geometry, 0x60 );
				declaration = Pointer( buffer, 0x30 );
				vertexData = Pointer( buffer, 0x10 );
				if ( vertexData == 0 ) vertexData = Pointer( buffer, 0x20 );
			}
			if ( Vertices.IsEmpty && VertexCount != 0 ) throw new InvalidDataException( "The geometry has no vertex data." );
		}
		if ( indexBuffer != 0 )
		{
			var buffer = resource.At( indexBuffer, enhanced ? 0x20 : 0x18 );
			if ( enhanced && Short( buffer, 12 ) != sizeof(ushort) )
				throw new InvalidDataException( "Unknown geometry index format." );
			uint count = Word( buffer, 8 );
			if ( count == 0 ) count = Word( geometry, 0x58 );
			indexData = Pointer( buffer, enhanced ? 0x18 : 0x10 );
			IndexCount = indexData != 0 ? checked((int)count) : 0;
			_ = Indices.Length;
		}
	}

	// Called once for the buffer. The GPU fetches each attribute from these source bytes.
	internal int ReadFields( Span<Field> fields )
	{
		if ( declaration == 0 ) throw new InvalidDataException( "The geometry has no vertex declaration." );
		return enhanced ? ReadEnhancedFields( fields, resource.At( declaration, 320 ) )
			: ReadLegacyFields( fields, resource.At( declaration, 16 ) );
	}

	int ReadEnhancedFields( Span<Field> fields, ReadOnlySpan<byte> declaration )
	{
		if ( ((Word( declaration, 312 ) >> 2) & 0xFF) != Stride )
			throw new InvalidDataException( "The vertex stride does not match the Enhanced declaration." );
		int count = 0;
		for ( int slot = 0; slot < 52; slot++ )
		{
			byte format = declaration[260 + slot];
			if ( format == 0 ) continue;
			int offset = checked((int)Word( declaration, slot * sizeof(uint) ));
			if ( declaration[208 + slot] != Stride || offset > Stride - FormatSize( format ) )
				throw new InvalidDataException( "An Enhanced vertex field is outside its interleaved stream." );
			fields[count++] = new Field( (byte)slot, offset, format );
		}
		return count;
	}

	int ReadLegacyFields( Span<Field> fields, ReadOnlySpan<byte> declaration )
	{
		// A Legacy declaration stores each field's byte size implicitly and packs fields in semantic order.
		ReadOnlySpan<byte> slots = [0, 16, 20, 4, 24, 25, 28, 29, 30, 31, 32, 33, 34, 35, 8, 12];
		uint mask = Word( declaration, 0 );
		ulong types = Pointer( declaration, 8 );
		int count = 0, offset = 0;
		for ( int i = 0; i < slots.Length; i++ )
		{
			if ( (mask & (1u << i)) == 0 ) continue;
			int slot = slots[i];
			byte format = ((types >> (4 * i)) & 15) switch
			{
				1 => 34, // Half2
				2 => 41, // Float
				3 => 10, // Half4
				5 => 16, // Float2
				6 => 6, // Float3
				7 => 2, // Float4
				8 or 9 => i == 2 ? (byte)30 : (byte)28,
				10 => 31, // Normalized signed bytes
				_ => throw new InvalidDataException( "Unknown Legacy vertex field format." ),
			};
			fields[count++] = new Field( (byte)slot, offset, format );
			offset += FormatSize( format );
		}
		if ( offset != Stride ) throw new InvalidDataException( "The vertex stride does not match the Legacy declaration." );
		return count;
	}

	static int FormatSize( byte format ) => format switch
	{
		2 => 16,
		6 => 12,
		10 or 16 => 8,
		28 or 30 or 31 or 34 or 41 => 4,
		_ => throw new InvalidDataException( $"Unknown vertex format {format}." ),
	};
}
