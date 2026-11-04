using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf;

/// <summary>Packages glTF data losslessly into a portable GLB container.</summary>
public static class GltfCompiler
{
	/// <summary>
	/// Consolidates geometry buffers while retaining original bytes and external image URIs.
	/// This is a lossless packaging step, not a GPU layout optimization.
	/// </summary>
	public static byte[] Compile( byte[] source, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null )
	{
		using var document = GltfReader.Read( source, resolve, supportedExtensions );
		RejectUnknownChunks( source );

		try
		{
			var root = JsonNode.Parse( document.Root.GetRawText() )?.AsObject()
				?? throw new GltfException( "glTF root must be an object." );
			var bufferCount = document.CompilerBufferCount;
			var offsets = new int[bufferCount];
			long bufferLength = 0;
			for ( var i = 0; i < bufferCount; i++ )
			{
				bufferLength = (bufferLength + 3) & ~3L;
				if ( bufferLength > Array.MaxLength ) throw new GltfException( "Merged buffers exceed the supported length." );
				offsets[i] = (int)bufferLength;
				bufferLength += document.CompilerBuffer( i ).Length;
			}
			if ( bufferLength > Array.MaxLength ) throw new GltfException( "Merged buffers exceed the supported length." );

			if ( bufferCount > 1 ) ValidateMergeSafety( document.Root );
			var binary = new byte[(int)bufferLength];
			for ( var i = 0; i < bufferCount; i++ )
				document.CompilerBuffer( i ).Span.CopyTo( binary.AsSpan( offsets[i] ) );

			if ( root.TryGetPropertyValue( "bufferViews", out var viewsNode ) )
			{
				if ( viewsNode is not JsonArray views ) throw new GltfException( "'bufferViews' must be an array." );
				for ( var i = 0; i < views.Count; i++ )
				{
					var original = document.ElementAt( "bufferViews", i );
					_ = document.BufferView( i );
					var buffer = GltfJson.Int( original, "buffer" );
					var offset = GltfJson.Int( original, "byteOffset", 0 );
					var newOffset = checked(offsets[buffer] + offset);
					var view = views[i]!.AsObject();
					if ( buffer != 0 ) view["buffer"] = 0;
					if ( newOffset != offset ) view["byteOffset"] = newOffset;
				}
			}

			if ( bufferCount > 0 )
			{
				var buffers = root["buffers"]!.AsArray();
				var first = buffers[0]!.AsObject();
				first.Remove( "uri" );
				first["byteLength"] = binary.Length;
				while ( buffers.Count > 1 ) buffers.RemoveAt( buffers.Count - 1 );
			}
			var json = JsonSerializer.SerializeToUtf8Bytes( root );
			return WriteGlb( json, binary, bufferCount > 0 );
		}
		catch ( Exception e ) when ( e is JsonException or InvalidOperationException or ArgumentException or OverflowException )
		{
			throw new GltfException( "Cannot package glTF: " + e.Message, e );
		}
	}

	private static void ValidateMergeSafety( JsonElement root )
	{
		if ( ContainsOpaqueData( root ) )
			throw new GltfException( "Cannot safely merge buffers with extension or extras data that may contain buffer references." );
		foreach ( var buffer in root.GetProperty( "buffers" ).EnumerateArray() )
		{
			foreach ( var property in buffer.EnumerateObject() )
			{
				if ( property.Name is not ("uri" or "byteLength") )
					throw new GltfException( "Cannot merge buffers without losing their metadata." );
			}
		}
	}

	private static bool ContainsOpaqueData( JsonElement element )
	{
		if ( element.ValueKind == JsonValueKind.Array )
		{
			foreach ( var value in element.EnumerateArray() )
				if ( ContainsOpaqueData( value ) ) return true;
		}
		else if ( element.ValueKind == JsonValueKind.Object )
		{
			foreach ( var property in element.EnumerateObject() )
			{
				if ( property.Name is "extensions" or "extras" ) return true;
				if ( property.Name is "extensionsUsed" or "extensionsRequired" && property.Value.ValueKind == JsonValueKind.Array && property.Value.GetArrayLength() > 0 ) return true;
				if ( ContainsOpaqueData( property.Value ) ) return true;
			}
		}
		return false;
	}

	private static void RejectUnknownChunks( byte[] source )
	{
		if ( source.Length < 4 || BinaryPrimitives.ReadUInt32LittleEndian( source ) != 0x46546c67 ) return;
		var offset = 12;
		while ( offset < source.Length )
		{
			var length = BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset ) );
			var type = BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset + 4 ) );
			if ( type is not (0x4e4f534a or 0x004e4942) )
				throw new GltfException( "Cannot safely package a GLB containing an unknown chunk." );
			offset += 8 + (int)length;
		}
	}

	private static byte[] WriteGlb( byte[] json, byte[] binary, bool hasBinary )
	{
		var jsonLength = (json.LongLength + 3) & ~3L;
		var binaryLength = (binary.LongLength + 3) & ~3L;
		var total = 12 + 8 + jsonLength + (hasBinary ? 8 + binaryLength : 0);
		if ( total > Array.MaxLength ) throw new GltfException( "Compiled GLB exceeds the supported length." );
		var output = new byte[(int)total];
		BinaryPrimitives.WriteUInt32LittleEndian( output, 0x46546c67 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 4 ), 2 );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 8 ), (uint)total );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 12 ), (uint)jsonLength );
		BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( 16 ), 0x4e4f534a );
		output.AsSpan( 20, (int)jsonLength ).Fill( 0x20 );
		json.CopyTo( output, 20 );
		if ( hasBinary )
		{
			var offset = 20 + (int)jsonLength;
			BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( offset ), (uint)binaryLength );
			BinaryPrimitives.WriteUInt32LittleEndian( output.AsSpan( offset + 4 ), 0x004e4942 );
			binary.CopyTo( output, offset + 8 );
		}
		return output;
	}
}
