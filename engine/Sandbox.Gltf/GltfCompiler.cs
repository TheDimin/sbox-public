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
	public static byte[] Compile( byte[] source, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null, long maxBufferBytes = DefaultMaxBufferBytes ) =>
		Compile( source, resolve, supportedExtensions, maxBufferBytes, true );

	/// <summary>Explicitly embeds encoded images into standard buffer views.</summary>
	public static byte[] CompileWithEmbeddedImages( byte[] source, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null, long maxBufferBytes = DefaultMaxBufferBytes, bool consolidateBuffers = true )
	{
		try
		{
			var packaged = CompilePackage( source, resolve, supportedExtensions, maxBufferBytes, consolidateBuffers, out var charge );
			return EmbedImages( packaged, resolve, supportedExtensions, maxBufferBytes, charge );
		}
		catch ( Exception e ) when ( e is JsonException or InvalidOperationException or ArgumentException or OverflowException )
		{
			throw new GltfException( "Cannot package glTF: " + e.Message, e );
		}
	}

	/// <summary>
	/// Optionally materializes sparse accessors into ordinary glTF buffer views so
	/// consumers need not apply sparse overlays at load time. This may increase
	/// the file size substantially. Opaque metadata and extensions prevent the
	/// rewrite because they may refer to the original storage layout.
	/// </summary>
	public static byte[] CompileWithDenseAccessors( byte[] source, Func<string, byte[]> resolve = null, IReadOnlySet<string> supportedExtensions = null, long maxBufferBytes = DefaultMaxBufferBytes )
	{
		var packaged = Compile( source, resolve, supportedExtensions, maxBufferBytes );
		using var document = GltfReader.Read( packaged, supportedExtensions: supportedExtensions );
		if ( ContainsOpaqueData( document.Root ) || document.CompilerBufferCount != 1 ||
			!document.Root.TryGetProperty( "accessors", out var accessors ) || accessors.ValueKind != JsonValueKind.Array ) return packaged;
		var indices = new List<int>();
		for ( var i = 0; i < accessors.GetArrayLength(); i++ )
			if ( accessors[i].TryGetProperty( "sparse", out _ ) ) indices.Add( i );
		if ( indices.Count == 0 ) return packaged;
		var original = document.CompilerBuffer( 0 );
		var chunks = new List<(int Index, byte[] Bytes)>();
		long length = original.Length;
		foreach ( var index in indices )
		{
			length = (length + 3) & ~3L;
			var remaining = maxBufferBytes / 3 - length;
			if ( remaining < 0 ) throw new GltfException( "Compiler accessor budget exceeded." );
			var chunk = document.CompilerMaterializeAccessor( index, remaining, out _ );
			if ( chunk is null ) continue;
			chunks.Add( (index, chunk) );
			length = checked(length + chunk.Length);
		}
		if ( chunks.Count == 0 ) return packaged;
		if ( length > Array.MaxLength || length > maxBufferBytes / 3 ) throw new GltfException( "Compiler accessor budget exceeded." );
		var binary = new byte[(int)length];
		original.Span.CopyTo( binary );
		var root = JsonNode.Parse( document.Root.GetRawText() )!.AsObject();
		var views = root["bufferViews"]!.AsArray();
		var outputAccessors = root["accessors"]!.AsArray();
		var offset = original.Length;
		for ( var i = 0; i < chunks.Count; i++ )
		{
			offset = checked((offset + 3) & ~3);
			var (index, chunk) = chunks[i];
			chunk.CopyTo( binary, offset );
			var viewIndex = views.Count;
			views.Add( new JsonObject { ["buffer"] = 0, ["byteOffset"] = offset, ["byteLength"] = chunk.Length } );
			var accessor = outputAccessors[index]!.AsObject();
			accessor["bufferView"] = viewIndex;
			accessor.Remove( "byteOffset" );
			accessor.Remove( "sparse" );
			offset += chunk.Length;
		}
		root["buffers"]![0]!["byteLength"] = binary.Length;
		return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), binary, true );
	}

	/// <summary>Packages a GLB with optional safe consolidation of geometry buffers.</summary>
	/// <param name="orderForLodStreaming">
	/// Lay out buffer views so the lowest MSFT_lod levels come first and the most detailed level last, after the
	/// data every level shares. Needs <paramref name="consolidateBuffers"/> and a file whose extensions are all
	/// known to be safe to move; otherwise the layout is unchanged.
	/// </param>
	public static byte[] Compile( byte[] source, Func<string, byte[]> resolve, IReadOnlySet<string> supportedExtensions, long maxBufferBytes, bool consolidateBuffers, bool orderForLodStreaming = false )
	{
		try
		{
			return CompilePackage( source, resolve, supportedExtensions, maxBufferBytes, consolidateBuffers, out _, orderForLodStreaming );
		}
		catch ( Exception e ) when ( e is JsonException or InvalidOperationException or ArgumentException or OverflowException )
		{
			throw new GltfException( "Cannot package glTF: " + e.Message, e );
		}
	}

	private static byte[] CompilePackage( byte[] source, Func<string, byte[]> resolve, IReadOnlySet<string> supportedExtensions, long maxBufferBytes, bool consolidateBuffers, out long packagingCharge, bool orderForLodStreaming = false )
	{
		packagingCharge = 0;
		if ( maxBufferBytes < 0 ) throw new GltfException( "Buffer packaging budget cannot be negative." );
		using var document = GltfReader.Read( source, resolve, supportedExtensions );
		RejectUnknownChunks( source );

		try
		{
			var root = JsonNode.Parse( document.Root.GetRawText() )?.AsObject()
				?? throw new GltfException( "glTF root must be an object." );
			var bufferCount = document.CompilerBufferCount;
			var orphanBinary = FindOrphanBinary( source, document );
			var merge = consolidateBuffers && !orphanBinary.HasValue && bufferCount > 1 && CanMergeBuffers( document.Root );
			for ( var i = 0; i < bufferCount && merge; i++ )
				if ( !document.CompilerBufferOrPlaceholder( i ).HasValue ) merge = false;
			ValidateBufferViews( document );
			// Ordering by level rewrites every view offset, so it needs the same guarantees as merging, for one buffer too.
			var streamKeys = orderForLodStreaming && consolidateBuffers && !orphanBinary.HasValue && bufferCount > 0 && CanMergeBuffers( document.Root )
				&& Enumerable.Range( 0, bufferCount ).All( i => document.CompilerBufferOrPlaceholder( i ).HasValue )
				? GltfStreamOrder.ViewKeys( document ) : null;
			var relayout = streamKeys is not null && streamKeys.Any( x => x != GltfStreamOrder.Shared );
			packagingCharge = CheckBufferBudget( document, merge || relayout, maxBufferBytes, orphanBinary?.Length ?? -1 );
			if ( orphanBinary.HasValue )
			{
				// Keep the pre-existing unreferenced chunk, and embed every core
				// buffer by URI so no buffer begins referring to that chunk.
				_ = PreserveBuffers( root, document, embedFirstBufferAsUri: true );
				return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), orphanBinary.Value.ToArray(), true );
			}
			if ( relayout ) return RelayoutByKey( root, document, streamKeys, bufferCount );
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

	private static byte[] EmbedImages( byte[] packaged, Func<string, byte[]> resolve, IReadOnlySet<string> supportedExtensions, long budget, long packagingCharge )
	{
		using var document = GltfReader.Read( packaged, resolve, supportedExtensions );
		if ( !document.Root.TryGetProperty( "images", out var images ) ) return packaged;
		if ( images.ValueKind != JsonValueKind.Array ) throw new GltfException( "'images' must be an array." );
		var embedded = new List<(int Index, GltfImage Image)>();
		long imageBytes = 0;
		long validationCharge = 0;
		for ( var i = 0; i < images.GetArrayLength(); i++ )
		{
			var image = images[i];
			if ( image.ValueKind != JsonValueKind.Object ) throw new GltfException( $"Image {i} must be an object." );
			if ( !image.TryGetProperty( "uri", out _ ) )
			{
				validationCharge = checked(validationCharge + document.ReadImage( i ).Bytes.LongLength);
				if ( validationCharge > budget - packagingCharge ) throw new GltfException( "Image packaging budget exceeded." );
				continue;
			}
			// Image-level extensions may attach semantics to URI storage. Do not
			// rewrite one that the compiler cannot inspect safely.
			if ( image.TryGetProperty( "extensions", out var extensions ) )
			{
				if ( extensions.ValueKind != JsonValueKind.Object ) throw new GltfException( $"Image {i} extensions must be an object." );
				if ( extensions.EnumerateObject().Any() ) throw new GltfException( $"Cannot safely embed image {i} with image-level extensions." );
			}
			var data = document.ReadImage( i );
			if ( GltfDocument.ImageMimeType( data.Bytes ) is null )
				throw new GltfException( $"Image {i} has an unrecognized or truncated encoded image signature." );
			imageBytes = checked(imageBytes + ((data.Bytes.LongLength + 3) & ~3L));
			if ( imageBytes > (budget - packagingCharge - validationCharge) / 12 ) throw new GltfException( "Image packaging budget exceeded." );
			embedded.Add( (i, data) );
		}
		if ( embedded.Count == 0 ) return packaged;

		var root = JsonNode.Parse( document.Root.GetRawText() )!.AsObject();
		var buffers = root["buffers"] as JsonArray ?? new JsonArray();
		if ( root["buffers"] is null ) root["buffers"] = buffers;
		var views = root["bufferViews"] as JsonArray ?? new JsonArray();
		if ( root["bufferViews"] is null ) root["bufferViews"] = views;
		var orphan = FindOrphanBinary( packaged, document );
		// Repackaging copies the existing BIN or orphan and reserializes any
		// retained data-URI buffers as well as adding the new image payloads.
		var repackCharge = CheckBufferBudget( document, false, budget, orphan?.Length ?? -1 );
		var appendBin = !orphan.HasValue && (document.CompilerBufferCount == 0 || !document.ElementAt( "buffers", 0 ).TryGetProperty( "uri", out _ ));
		var original = appendBin && document.CompilerBufferCount > 0 ? document.CompilerBuffer( 0 ) : ReadOnlyMemory<byte>.Empty;
		var total = checked(((long)original.Length + 3 & ~3L) + imageBytes);
		if ( total > Array.MaxLength || checked(packagingCharge + repackCharge + validationCharge + 12L * imageBytes) > budget )
			throw new GltfException( "Image packaging budget exceeded." );
		var binary = new byte[(int)total];
		original.Span.CopyTo( binary );
		var offset = (original.Length + 3) & ~3;
		var bufferIndex = appendBin ? 0 : buffers.Count;
		foreach ( var (index, image) in embedded )
		{
			image.Bytes.CopyTo( binary, offset );
			var viewIndex = views.Count;
			views.Add( new JsonObject { ["buffer"] = bufferIndex, ["byteOffset"] = offset, ["byteLength"] = image.Bytes.Length } );
			var target = root["images"]![index]!.AsObject();
			target.Remove( "uri" ); target["bufferView"] = viewIndex; target["mimeType"] = image.MimeType;
			offset = checked((offset + image.Bytes.Length + 3) & ~3);
		}
		if ( appendBin )
		{
			if ( buffers.Count == 0 ) buffers.Add( new JsonObject { ["byteLength"] = binary.Length } );
			else buffers[0]!["byteLength"] = binary.Length;
			return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), binary, true );
		}
		buffers.Add( new JsonObject { ["uri"] = "data:application/gltf-buffer;base64," + Convert.ToBase64String( binary ), ["byteLength"] = binary.Length } );
		return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), orphan?.ToArray() ?? [], orphan.HasValue );
	}

	/// <summary>Writes one buffer with the views sorted by key (stable), each aligned to four bytes.</summary>
	private static byte[] RelayoutByKey( JsonObject root, GltfDocument document, int[] keys, int bufferCount )
	{
		var views = root["bufferViews"] as JsonArray ?? throw new GltfException( "'bufferViews' must be an array." );
		var order = Enumerable.Range( 0, keys.Length ).OrderBy( i => keys[i] ).ThenBy( i => i ).ToArray();
		var offsets = new int[keys.Length];
		long length = 0;
		foreach ( var i in order )
		{
			length = (length + 3) & ~3L;
			var original = document.ElementAt( "bufferViews", i );
			if ( length > Array.MaxLength ) throw new GltfException( "Ordered buffers exceed the supported length." );
			offsets[i] = (int)length;
			length += GltfJson.Int( original, "byteLength" );
		}
		if ( length > Array.MaxLength ) throw new GltfException( "Ordered buffers exceed the supported length." );
		var binary = new byte[(int)length];
		foreach ( var i in order )
		{
			var original = document.ElementAt( "bufferViews", i );
			var buffer = GltfJson.Int( original, "buffer" );
			var offset = GltfJson.Int( original, "byteOffset", 0 );
			var size = GltfJson.Int( original, "byteLength" );
			document.CompilerBuffer( buffer ).Span.Slice( offset, size ).CopyTo( binary.AsSpan( offsets[i] ) );
			var view = views[i]!.AsObject();
			view["buffer"] = 0;
			if ( offsets[i] == 0 ) view.Remove( "byteOffset" ); else view["byteOffset"] = offsets[i];
		}
		var buffers = root["buffers"]!.AsArray();
		var first = buffers[0]!.AsObject();
		first.Remove( "uri" );
		first["byteLength"] = binary.Length;
		while ( buffers.Count > 1 ) buffers.RemoveAt( buffers.Count - 1 );
		return WriteGlb( JsonSerializer.SerializeToUtf8Bytes( root ), binary, true );
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

	private static long CheckBufferBudget( GltfDocument document, bool merge, long budget, int orphanBinaryLength )
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
		return merge ? checked(charge + 2L * mergedLength) : charge;
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

	// Extension payloads that hold indices of other elements, never a buffer index or a byte offset.
	// Moving buffers cannot invalidate them, so they do not stop merging or ordering.
	internal static readonly HashSet<string> MovableExtensions = new( StringComparer.Ordinal )
	{
		"MSFT_lod", "KHR_materials_variants", "KHR_materials_unlit", "KHR_materials_clearcoat", "KHR_materials_anisotropy",
		"KHR_materials_diffuse_transmission", "KHR_materials_emissive_strength", "KHR_texture_transform", "KHR_texture_basisu",
		"EXT_texture_webp", "KHR_node_visibility", "KHR_lights_punctual", "KHR_mesh_quantization",
		"KHR_draco_mesh_compression", "EXT_mesh_gpu_instancing"
	};

	// Keys of extras that the engine writes or reads and that carry only numbers.
	private static readonly HashSet<string> MovableExtras = new( StringComparer.Ordinal ) { "MSFT_screencoverage" };

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
				if ( property.Name is "extensions" )
				{
					// Unknown payloads may name a buffer or hold an offset; known ones cannot.
					if ( property.Value.ValueKind != JsonValueKind.Object ) return true;
					foreach ( var extension in property.Value.EnumerateObject() )
						if ( !MovableExtensions.Contains( extension.Name ) ) return true;
					continue;
				}
				if ( property.Name is "extras" )
				{
					if ( property.Value.ValueKind != JsonValueKind.Object ) return true;
					foreach ( var extra in property.Value.EnumerateObject() )
						if ( !MovableExtras.Contains( extra.Name ) ) return true;
					continue;
				}
				if ( property.Name is "extensionsUsed" or "extensionsRequired" && property.Value.ValueKind == JsonValueKind.Array )
				{
					foreach ( var name in property.Value.EnumerateArray() )
						if ( name.ValueKind != JsonValueKind.String || !MovableExtensions.Contains( name.GetString() ) ) return true;
					continue;
				}
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

	internal static byte[] WriteGlb( byte[] json, byte[] binary, bool hasBinary )
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
