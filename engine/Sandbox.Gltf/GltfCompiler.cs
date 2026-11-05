using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf;

/// <summary>Packages glTF data losslessly into a portable GLB container.</summary>
public static class GltfCompiler
{
	private const long DefaultMaxBufferBytes = 256L * 1024 * 1024;

	/// <summary>
	/// Consolidates geometry buffers when safe, otherwise preserves their identities.
	/// Buffer contents and external image URIs are retained. The budget limits
	/// estimated cumulative packaging allocations for buffer data; it does not
	/// limit caller-owned input, resource resolution, or arbitrary JSON metadata.
	/// This is a lossless packaging step, not a GPU layout optimization.
	/// </summary>
	public static byte[] Compile( byte[] source, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null, long maxBufferBytes = DefaultMaxBufferBytes )
	{
		if ( maxBufferBytes < 0 ) throw new GltfException( "Buffer packaging budget cannot be negative." );
		using var document = GltfReader.Read( source, resolve, supportedExtensions );
		RejectUnknownChunks( source );

		try
		{
			var root = JsonNode.Parse( document.Root.GetRawText() )?.AsObject()
				?? throw new GltfException( "glTF root must be an object." );
			var bufferCount = document.CompilerBufferCount;
			var orphanBinary = FindOrphanBinary( source, document );
			var merge = !orphanBinary.HasValue && bufferCount > 1 && CanMergeBuffers( document.Root );
			for ( var i = 0; i < bufferCount && merge; i++ )
				if ( !document.CompilerBufferOrPlaceholder( i ).HasValue ) merge = false;
			ValidateBufferViews( document );
			CheckBufferBudget( document, merge, maxBufferBytes, orphanBinary?.Length ?? -1 );
			if ( orphanBinary.HasValue )
			{
				// Keep the pre-existing unreferenced chunk, and embed every core
				// buffer by URI so no buffer begins referring to that chunk.
				_ = PreserveBuffers( root, document, embedFirstBufferAsUri: true );
				return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), orphanBinary.Value.ToArray(), true );
			}
			if ( !merge )
			{
				var preservedBinary = PreserveBuffers( root, document );
				return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), preservedBinary ?? [], preservedBinary is not null );
			}

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

			var binary = new byte[(int)bufferLength];
			for ( var i = 0; i < bufferCount; i++ )
				document.CompilerBuffer( i ).Span.CopyTo( binary.AsSpan( offsets[i] ) );

			if ( root.TryGetPropertyValue( "bufferViews", out var viewsNode ) )
			{
				if ( viewsNode is not JsonArray views ) throw new GltfException( "'bufferViews' must be an array." );
				for ( var i = 0; i < views.Count; i++ )
				{
					var original = document.ElementAt( "bufferViews", i );
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

	private static bool CanMergeBuffers( JsonElement root )
	{
		if ( ContainsOpaqueData( root ) ) return false;
		foreach ( var buffer in root.GetProperty( "buffers" ).EnumerateArray() )
		{
			foreach ( var property in buffer.EnumerateObject() )
			{
				if ( property.Name is not ("uri" or "byteLength") ) return false;
			}
		}
		return true;
	}

	private static void ValidateBufferViews( GltfDocument document )
	{
		if ( !document.Root.TryGetProperty( "bufferViews", out var views ) ) return;
		if ( views.ValueKind != JsonValueKind.Array ) throw new GltfException( "'bufferViews' must be an array." );
		for ( var i = 0; i < views.GetArrayLength(); i++ )
		{
			var view = document.ElementAt( "bufferViews", i );
			var buffer = GltfJson.Int( view, "buffer" );
			var offset = GltfJson.Int( view, "byteOffset", 0 );
			var length = GltfJson.Int( view, "byteLength" );
			if ( (uint)buffer >= (uint)document.CompilerBufferCount || offset < 0 || length < 1 ||
				(long)offset + length > GltfJson.Int( document.ElementAt( "buffers", buffer ), "byteLength" ) )
				throw new GltfException( $"Buffer view {i} exceeds its declared buffer." );
		}
	}

	private static void CheckBufferBudget( GltfDocument document, bool merge, long budget, int orphanBinaryLength )
	{
		// An orphan BIN is copied once to a temporary array and once into output.
		long charge = orphanBinaryLength >= 0 ? 2L * orphanBinaryLength : 0;
		if ( charge > budget ) throw new GltfException( "Buffer packaging budget exceeded." );
		long mergedLength = 0;
		for ( var i = 0; i < document.CompilerBufferCount; i++ )
		{
			var bytes = document.CompilerBufferOrPlaceholder( i );
			if ( merge )
			{
				mergedLength = checked(((mergedLength + 3) & ~3L) + bytes!.Value.Length);
				continue;
			}
			if ( i == 0 && orphanBinaryLength < 0 )
			{
				var length = bytes.HasValue ? bytes.Value.Length : GltfJson.Int( document.ElementAt( "buffers", i ), "byteLength" );
				// One temporary BIN and its later copy into the GLB.
				charge = checked(charge + 2L * length);
			}
			else if ( bytes.HasValue )
			{
				// Base64 creation and concatenation use UTF-16, followed by JSON and
				// GLB byte copies. Use the larger existing data URI when applicable.
				var characters = 4L * ((bytes.Value.Length + 2L) / 3L) + 64;
				var original = document.ElementAt( "buffers", i );
				if ( original.TryGetProperty( "uri", out _ ) )
				{
					var uri = GltfJson.String( original, "uri" );
					if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) ) characters = Math.Max( characters, uri.Length );
				}
				charge = checked(charge + 6L * characters);
			}
			if ( charge > budget ) throw new GltfException( "Buffer packaging budget exceeded." );
		}
		if ( merge && (mergedLength > Array.MaxLength || mergedLength > budget / 2) )
			throw new GltfException( "Buffer packaging budget exceeded." );
	}

	private static byte[] PreserveBuffers( JsonObject root, GltfDocument document, bool embedFirstBufferAsUri = false )
	{
		if ( document.CompilerBufferCount == 0 ) return null;
		var buffers = root["buffers"]!.AsArray();
		byte[] binary = null;
		for ( var i = 0; i < document.CompilerBufferCount; i++ )
		{
			var data = document.CompilerBufferOrPlaceholder( i );
			var buffer = buffers[i]!.AsObject();
			if ( !data.HasValue )
			{
				// Core GLB requires a BIN chunk when buffer zero has no URI.
				// A required compressed fallback has no authored bytes, so materialize
				// zero-filled placeholder storage without changing the buffer identity.
				if ( i == 0 )
				{
					var length = GltfJson.Int( document.ElementAt( "buffers", i ), "byteLength" );
					if ( length > Array.MaxLength ) throw new GltfException( "Fallback placeholder exceeds the supported GLB size." );
					binary = new byte[length];
				}
				continue;
			}
			if ( i == 0 && !embedFirstBufferAsUri )
			{
				binary = data.Value.ToArray();
				buffer.Remove( "uri" );
			}
			else
			{
				var original = document.ElementAt( "buffers", i );
				var uri = original.TryGetProperty( "uri", out _ ) ? GltfJson.String( original, "uri" ) : null;
				if ( uri is null || !uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) )
					buffer["uri"] = "data:application/gltf-buffer;base64," + Convert.ToBase64String( data.Value.Span );
			}
		}
		return binary;
	}

	private static ReadOnlyMemory<byte>? FindOrphanBinary( byte[] source, GltfDocument document )
	{
		if ( source.Length < 20 || BinaryPrimitives.ReadUInt32LittleEndian( source ) != 0x46546c67 ) return null;
		// GltfReader and RejectUnknownChunks already validated framing and order.
		var offset = 20 + (int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 12 ) );
		if ( offset == source.Length || BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset + 4 ) ) != 0x004e4942 ) return null;
		if ( document.CompilerBufferCount > 0 && !document.ElementAt( "buffers", 0 ).TryGetProperty( "uri", out _ ) ) return null;
		var length = (int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset ) );
		return source.AsMemory( offset + 8, length );
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
