using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private const string ExtMeshopt = "EXT_meshopt_compression";
	private const string KhrMeshopt = "KHR_meshopt_compression";
	private readonly Dictionary<int, byte[]> meshoptViews = [];
	private long meshoptDecodedBytes;

	// A URI-less placeholder has no storage. Admit it only when every ordinary
	// view using it has selected, required compression and no compressed source
	// points back to it. The caller additionally checks whether it is a GLB BIN.
	private bool CanSkipMeshoptPlaceholder( int bufferIndex )
	{
		if ( !Root.TryGetProperty( "bufferViews", out var views ) || views.ValueKind != JsonValueKind.Array ) return false;
		var required = ExtensionNames( "extensionsRequired" );
		var referenced = false;
		foreach ( var view in views.EnumerateArray() )
		{
			if ( view.ValueKind != JsonValueKind.Object ) return false;
			var hasExtensions = view.TryGetProperty( "extensions", out var extensions );
			if ( hasExtensions && extensions.ValueKind != JsonValueKind.Object ) return false;
			var hasExt = hasExtensions && extensions.TryGetProperty( ExtMeshopt, out var ext );
			var hasKhr = hasExtensions && extensions.TryGetProperty( KhrMeshopt, out var khr );
			if ( hasExt && hasKhr ) return false;
			if ( hasExt )
			{
				ext = extensions.GetProperty( ExtMeshopt );
				if ( ext.ValueKind != JsonValueKind.Object || GltfJson.Int( ext, "buffer" ) == bufferIndex ) return false;
			}
			if ( hasKhr )
			{
				khr = extensions.GetProperty( KhrMeshopt );
				if ( khr.ValueKind != JsonValueKind.Object || GltfJson.Int( khr, "buffer" ) == bufferIndex ) return false;
			}
			if ( GltfJson.Int( view, "buffer" ) != bufferIndex ) continue;
			referenced = true;
			var name = hasExt ? ExtMeshopt : hasKhr ? KhrMeshopt : null;
			if ( name is null || !supported.Contains( name ) || !required.Contains( name, StringComparer.Ordinal ) ) return false;
		}
		return referenced;
	}

	private bool TryMeshoptBufferView( int index, JsonElement view, out ReadOnlyMemory<byte> result )
	{
		result = default;
		if ( !view.TryGetProperty( "extensions", out var extensions ) ) return false;
		if ( extensions.ValueKind != JsonValueKind.Object ) throw new GltfException( "Buffer view extensions must be an object." );
		var hasExt = extensions.TryGetProperty( ExtMeshopt, out var ext );
		var hasKhr = extensions.TryGetProperty( KhrMeshopt, out var khr );
		if ( hasExt && hasKhr ) throw new GltfException( "A buffer view cannot use both meshopt extensions." );
		var name = hasExt && supported.Contains( ExtMeshopt ) ? ExtMeshopt :
			hasKhr && supported.Contains( KhrMeshopt ) ? KhrMeshopt : null;
		if ( name is null ) return false;

		lock ( meshoptViews )
		{
			if ( meshoptViews.TryGetValue( index, out var cached ) )
			{
				result = cached;
				return true;
			}

			var compressed = name == ExtMeshopt ? ext : khr;
			if ( compressed.ValueKind != JsonValueKind.Object ) throw new GltfException( $"{name} must be an object." );
			var bufferIndex = GltfJson.Int( compressed, "buffer" );
			var offset = GltfJson.Int( compressed, "byteOffset", 0 );
			var length = GltfJson.Int( compressed, "byteLength" );
			var stride = GltfJson.Int( compressed, "byteStride" );
			var count = GltfJson.Int( compressed, "count" );
			var mode = GltfJson.String( compressed, "mode" );
			var filter = GltfJson.String( compressed, "filter", "NONE" );
			var fallbackIndex = GltfJson.Int( view, "buffer" );
			var fallbackOffset = GltfJson.Int( view, "byteOffset", 0 );
			var fallbackLength = GltfJson.Int( view, "byteLength" );
			if ( (uint)fallbackIndex >= (uint)buffers.Length || fallbackOffset < 0 || fallbackLength < 1 )
				throw new GltfException( "Meshopt fallback buffer view has invalid bounds." );
			var fallbackBuffer = ElementAt( "buffers", fallbackIndex );
			if ( (long)fallbackOffset + fallbackLength > GltfJson.Int( fallbackBuffer, "byteLength" ) )
				throw new GltfException( "Meshopt fallback buffer view exceeds its declared buffer." );
			if ( count <= 0 || stride <= 0 || (long)count * stride != fallbackLength )
				throw new GltfException( "Meshopt decoded length must match the parent buffer view." );
			if ( name == ExtMeshopt && view.TryGetProperty( "byteStride", out _ ) && GltfJson.Int( view, "byteStride" ) != stride )
				throw new GltfException( "EXT meshopt stride must match its parent buffer view." );
			if ( name == ExtMeshopt && filter == "COLOR" ) throw new GltfException( "EXT meshopt does not support the COLOR filter." );
			if ( (uint)bufferIndex >= (uint)buffers.Length || offset < 0 || length < 1 || (long)offset + length > buffers[bufferIndex].Length )
				throw new GltfException( "Meshopt compressed source exceeds its loaded buffer." );
			var sourceBuffer = ElementAt( "buffers", bufferIndex );
			if ( sourceBuffer.TryGetProperty( "extensions", out var bufferExtensions ) )
			{
				if ( bufferExtensions.ValueKind != JsonValueKind.Object ) throw new GltfException( "Buffer extensions must be an object." );
				if ( bufferExtensions.TryGetProperty( name, out var bufferExtension ) && GltfJson.Bool( bufferExtension, "fallback" ) )
					throw new GltfException( "Meshopt compressed data cannot reference a fallback buffer." );
				var other = name == ExtMeshopt ? KhrMeshopt : ExtMeshopt;
				if ( bufferExtensions.TryGetProperty( other, out var otherExtension ) && GltfJson.Bool( otherExtension, "fallback" ) )
					throw new GltfException( "Meshopt compressed data cannot reference a fallback buffer." );
			}
			var source = buffers[bufferIndex].Slice( offset, length );
			var header = source.Span[0];
			var validHeader = mode switch
			{
				"ATTRIBUTES" => name == ExtMeshopt ? header == 0xa0 : header is 0xa0 or 0xa1,
				"TRIANGLES" => header == 0xe1,
				"INDICES" => header == 0xd1,
				_ => false
			};
			if ( !validHeader ) throw new GltfException( $"Invalid {name} stream header for {mode}." );
			if ( fallbackLength > MaxDecodedBufferBytes - meshoptDecodedBytes - dracoDecodedBytes )
				throw new GltfException( "Meshopt decoded buffer budget exceeded." );
			var decoded = GltfMeshopt.Decode( source.ToArray(), count, stride, mode, filter );
			meshoptViews.Add( index, decoded );
			meshoptDecodedBytes += decoded.Length;
			result = decoded;
			return true;
		}
	}

	private void ClearMeshoptCache()
	{
		lock ( meshoptViews )
		{
			meshoptViews.Clear();
			meshoptDecodedBytes = 0;
		}
	}
}
