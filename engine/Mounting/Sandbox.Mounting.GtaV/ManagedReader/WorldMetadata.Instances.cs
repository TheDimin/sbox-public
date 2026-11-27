using System;
using System.IO;
using static GtaV.ManagedReader.MetaResource;
using NumericsVector3 = System.Numerics.Vector3;

namespace GtaV.ManagedReader;

internal sealed partial class WorldMetadata
{
	// Type IDs stored in retail prop-instance metadata.
	const uint PropList = 0xB13A8561;
	const uint PropInstance = 0xAA132A21;

	void WriteInstances( BinaryWriter writer, MetaResource meta, ReadOnlySpan<byte> root )
	{
		var grass = StructuredArray( meta, root[208..], 96, out int grassStride );
		var props = ArrayTypeIs( meta, root[192..], PropList ) ? StructuredArray( meta, root[192..], 64, out _ ) : [];
		int propStride = meta.StructSize( PropList, 64 );
		if ( !grass.IsEmpty && grassStride < 88 )
			throw new InvalidDataException( "Invalid YMAP instance list stride." );
		if ( propStride < 56 ) props = [];
		writer.Write( grass.Length / grassStride + (props.IsEmpty ? 0 : props.Length / propStride) );

		for ( int at = 0; at < grass.Length; at += grassStride )
		{
			var batch = grass.Slice( at, grassStride );
			var min = Vector( batch, 0 );
			var max = Vector( batch, 16 );
			var extent = max - min;
			float minScale = Float( batch, 32 ), maxScale = Float( batch, 36 );
			WriteBatchHeader( writer, Word( batch, 48 ), min, max, Word( batch, 52 ) );
			var instances = StructuredArray( meta, batch[72..], 16, out int stride );
			if ( !instances.IsEmpty && stride < 12 ) throw new InvalidDataException( "Truncated grass instance." );
			writer.Write( instances.Length / stride );
			for ( int i = 0; i < instances.Length; i += stride )
			{
				var row = instances.Slice( i, stride );
				WriteVector( writer, new NumericsVector3(
					min.X + extent.X * Short( row, 0 ) / 65535f,
					min.Y + extent.Y * Short( row, 2 ) / 65535f,
					min.Z + extent.Z * Short( row, 4 ) / 65535f ) );
				float scale = minScale + (maxScale - minScale) * (row[11] / 255f);
				WriteVector( writer, new( scale, 0, 0 ) );
				WriteVector( writer, new( 0, scale, 0 ) );
				WriteVector( writer, new( 0, 0, scale ) );
			}
		}

		for ( int at = 0; at < props.Length; at += propStride )
		{
			var batch = props.Slice( at, propStride );
			WriteBatchHeader( writer, Word( batch, 48 ), Vector( batch, 16 ), Vector( batch, 32 ), Word( batch, 52 ) );
			var instances = ArrayTypeIs( meta, batch, PropInstance ) ? StructuredArray( meta, batch, 48, out _ ) : [];
			int stride = meta.StructSize( PropInstance, 48 );
			if ( stride < 48 ) instances = [];
			writer.Write( instances.IsEmpty ? 0 : instances.Length / stride );
			for ( int i = 0; i < instances.Length; i += stride )
			{
				var row = instances.Slice( i, stride );
				WriteVector( writer, new NumericsVector3( Float( row, 12 ), Float( row, 28 ), Float( row, 44 ) ) );
				WriteVector( writer, Vector( row, 0 ) );
				WriteVector( writer, Vector( row, 16 ) );
				WriteVector( writer, Vector( row, 32 ) );
			}
		}
	}

	void WriteBatchHeader( BinaryWriter writer, uint hash, NumericsVector3 min, NumericsVector3 max, uint distance )
	{
		string path = index.FindArchetype( hash ) is { } archetype ? index.DrawablePath( archetype.Data ) : string.Empty;
		WriteText( writer, path );
		WriteVector( writer, min ); WriteVector( writer, max );
		writer.Write( (float)distance );
	}

	static bool ArrayTypeIs( MetaResource meta, ReadOnlySpan<byte> descriptor, uint type ) => meta.ArrayType( descriptor ) == type;

	static ReadOnlySpan<byte> StructuredArray( MetaResource meta, ReadOnlySpan<byte> descriptor, int expected, out int stride )
	{
		uint type = meta.ArrayType( descriptor );
		stride = type == 0 ? expected : meta.StructSize( type, expected );
		if ( stride <= 0 ) throw new InvalidDataException( "Invalid metadata array stride." );
		return meta.Array( descriptor, stride );
	}
}
