using System;
using System.IO;
using System.Text;

namespace GtaV.ManagedReader;

internal sealed class MountPacket : IDisposable
{
	readonly MemoryStream data;
	internal BinaryWriter Writer { get; }

	internal MountPacket( int capacity = 0 )
	{
		data = new MemoryStream( capacity );
		Writer = new BinaryWriter( data );
	}

	internal static void WriteText( BinaryWriter writer, string value )
	{
		int size = Encoding.UTF8.GetByteCount( value );
		writer.Write( size );
		if ( size == 0 ) return;
		Span<byte> bytes = size <= 512 ? stackalloc byte[size] : new byte[size];
		Encoding.UTF8.GetBytes( value, bytes );
		writer.Write( bytes );
	}

	internal BinaryReader Finish() => Open( new ArraySegment<byte>( data.GetBuffer(), 0, (int)data.Length ) );
	internal static BinaryReader Open( ArraySegment<byte> bytes ) => new( new MemoryStream( bytes.Array, bytes.Offset, bytes.Count, writable: false ) );
	public void Dispose() => Writer.Dispose();
}
