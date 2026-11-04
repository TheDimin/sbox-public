using System.Buffers.Binary;
using System.Text.Json;

namespace Sandbox.Gltf;

/// <summary>Native-independent container reading shared by import and rendering.</summary>
public static class GltfReader
{
	/// <summary>
	/// Reads container metadata and geometry buffers. Keep input and resolved arrays immutable
	/// until disposal; binary GLB views reference that storage without an additional copy.
	/// Capability declarations belong to the caller; declaring a codec does not implement it.
	/// </summary>
	public static GltfDocument Read( byte[] data, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null, long maxDecodedBufferBytes = long.MaxValue )
	{
		if ( data is null || data.Length == 0 ) throw new GltfException( "glTF input is empty." );
		JsonDocument json = null;
		try
		{
			ReadOnlyMemory<byte> jsonBytes = data;
			ReadOnlyMemory<byte>? binary = null;
			if ( data.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian( data ) == 0x46546c67 )
				(jsonBytes, binary) = ReadGlb( data );
			json = JsonDocument.Parse( jsonBytes );
			var document = new GltfDocument( json, binary, resolve, supportedExtensions, maxDecodedBufferBytes );
			return document;
		}
		catch ( Exception e ) when ( e is JsonException or InvalidOperationException or FormatException or OverflowException or ArgumentException )
		{
			json?.Dispose();
			if ( e is GltfException ) throw;
			throw new GltfException( "Invalid glTF container: " + e.Message, e );
		}
	}

	private static (ReadOnlyMemory<byte> Json, ReadOnlyMemory<byte>? Binary) ReadGlb( byte[] data )
	{
		if ( data.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 4 ) ) != 2 ||
			BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( 8 ) ) != (uint)data.Length )
			throw new GltfException( "Invalid GLB header, version or file length." );
		ReadOnlyMemory<byte> json = default;
		ReadOnlyMemory<byte>? binary = null;
		var offset = 12;
		var ordinal = 0;
		while ( offset < data.Length )
		{
			if ( data.Length - offset < 8 ) throw new GltfException( "Truncated GLB chunk header." );
			var length = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset ) );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( data.AsSpan( offset + 4 ) );
			offset += 8;
			if ( length % 4 != 0 || length > (uint)(data.Length - offset) )
				throw new GltfException( "GLB chunk is misaligned or exceeds the declared file length." );
			if ( ordinal == 0 && type != 0x4e4f534a ) throw new GltfException( "GLB JSON must be the first chunk." );
			if ( type == 0x4e4f534a )
			{
				if ( ordinal != 0 ) throw new GltfException( "GLB contains a duplicate or misplaced JSON chunk." );
				json = data.AsMemory( offset, (int)length );
			}
			else if ( type == 0x004e4942 )
			{
				if ( ordinal != 1 || binary.HasValue ) throw new GltfException( "GLB BIN must be the second chunk." );
				binary = data.AsMemory( offset, (int)length );
			}
			offset += (int)length;
			ordinal++;
		}
		return (json, binary);
	}
}
