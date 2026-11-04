using System.Text;
using System.Text.Json;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private readonly JsonDocument json;
	private readonly HashSet<string> supported;
	private readonly string[] unsupportedOptional;
	private Func<string, byte[]> resolver;
	private ReadOnlyMemory<byte>[] buffers;
	private bool disposed;

	public JsonElement Root
	{
		get
		{
			ObjectDisposedException.ThrowIf( disposed, this );
			return json.RootElement;
		}
	}

	public IReadOnlyList<string> UnsupportedOptionalExtensions => Array.AsReadOnly( unsupportedOptional );
	internal long MaxDecodedBufferBytes { get; }

	internal GltfDocument( JsonDocument json, ReadOnlyMemory<byte>? binary, Func<string, byte[]> resolve, IReadOnlySet<string> supportedExtensions, long maxDecodedBufferBytes )
	{
		this.json = json;
		if ( maxDecodedBufferBytes < 0 ) throw new GltfException( "Decoded buffer budget cannot be negative." );
		MaxDecodedBufferBytes = maxDecodedBufferBytes;
		resolver = resolve;
		supported = supportedExtensions is null ? new( StringComparer.Ordinal ) : new( supportedExtensions, StringComparer.Ordinal );
		var asset = GltfJson.Required( Root, "asset" );
		var version = ParseVersion( GltfJson.String( asset, "version" ) );
		if ( version.Major != 2 ) throw new GltfException( "Only glTF 2.x assets are supported." );
		if ( asset.TryGetProperty( "minVersion", out _ ) )
		{
			var minimum = ParseVersion( GltfJson.String( asset, "minVersion" ) );
			if ( minimum > new Version( 2, 0 ) || minimum > version )
				throw new GltfException( $"Unsupported glTF minimum version {minimum}." );
		}
		var used = ExtensionNames( "extensionsUsed" );
		var required = ExtensionNames( "extensionsRequired" );
		foreach ( var name in required )
		{
			if ( !used.Contains( name, StringComparer.Ordinal ) ) throw new GltfException( $"Required extension '{name}' is not listed in extensionsUsed." );
			if ( !supported.Contains( name ) ) throw new GltfException( $"Unsupported required glTF extension '{name}'." );
		}
		unsupportedOptional = used.Where( x => !supported.Contains( x ) ).ToArray();
		var count = Root.TryGetProperty( "buffers", out var list ) ? RequireArray( list, "buffers" ).GetArrayLength() : 0;
		buffers = new ReadOnlyMemory<byte>[count];
		for ( var i = 0; i < count; i++ )
		{
			var item = list[i];
			var length = GltfJson.Int( item, "byteLength" );
			if ( length < 1 ) throw new GltfException( $"Buffer {i} has invalid byteLength." );
			ReadOnlyMemory<byte> bytes;
			if ( item.TryGetProperty( "uri", out _ ) ) bytes = ResolveResource( GltfJson.String( item, "uri" ) );
			else
			{
				if ( i != 0 || binary is null )
				{
					if ( CanSkipMeshoptPlaceholder( i ) ) continue;
					throw new GltfException( $"Buffer {i} has no URI or GLB BIN data." );
				}
				bytes = binary.Value;
				if ( bytes.Length - (long)length > 3 ) throw new GltfException( "GLB BIN contains more than three bytes of buffer padding." );
				if ( bytes.Length >= length && bytes.Span[length..].IndexOfAnyExcept( (byte)0 ) >= 0 )
					throw new GltfException( "GLB BIN padding must be zero." );
			}
			if ( bytes.Length < length ) throw new GltfException( $"Buffer {i} is shorter than its byteLength." );
			buffers[i] = bytes[..length];
		}
	}

	private static Version ParseVersion( string value )
	{
		var parts = value.Split( '.' );
		if ( parts.Length != 2 || parts.Any( x => x.Length == 0 || x.Any( c => c is < '0' or > '9' ) ) || !Version.TryParse( value, out var version ) )
			throw new GltfException( $"Invalid glTF version '{value}'." );
		return version;
	}

	private string[] ExtensionNames( string property )
	{
		if ( !Root.TryGetProperty( property, out var array ) ) return [];
		RequireArray( array, property );
		var names = new List<string>();
		foreach ( var item in array.EnumerateArray() )
		{
			if ( item.ValueKind != JsonValueKind.String || string.IsNullOrEmpty( item.GetString() ) || names.Contains( item.GetString(), StringComparer.Ordinal ) )
				throw new GltfException( $"'{property}' must contain unique extension names." );
			names.Add( item.GetString() );
		}
		return names.ToArray();
	}

	internal bool SupportsExtension( string name ) => supported.Contains( name );

	internal JsonElement ElementAt( string array, int index )
	{
		if ( !Root.TryGetProperty( array, out var values ) || values.ValueKind != JsonValueKind.Array || (uint)index >= (uint)values.GetArrayLength() )
			throw new GltfException( $"Invalid {array} index {index}." );
		if ( values[index].ValueKind != JsonValueKind.Object ) throw new GltfException( $"{array}[{index}] must be an object." );
		return values[index];
	}

	private static JsonElement RequireArray( JsonElement value, string context )
	{
		if ( value.ValueKind != JsonValueKind.Array ) throw new GltfException( $"'{context}' must be an array." );
		return value;
	}

	internal ReadOnlyMemory<byte> BufferView( int index )
	{
		var view = ElementAt( "bufferViews", index );
		if ( TryMeshoptBufferView( index, view, out var decoded ) ) return decoded;
		var buffer = GltfJson.Int( view, "buffer" );
		var offset = GltfJson.Int( view, "byteOffset", 0 );
		var length = GltfJson.Int( view, "byteLength" );
		if ( (uint)buffer >= (uint)buffers.Length || offset < 0 || length < 1 || (long)offset + length > buffers[buffer].Length )
			throw new GltfException( $"Buffer view {index} exceeds its declared buffer." );
		return buffers[buffer].Slice( offset, length );
	}

	private byte[] ResolveResource( string uri )
	{
		if ( uri.StartsWith( "data:", StringComparison.OrdinalIgnoreCase ) ) return DecodeDataUri( uri );
		if ( resolver is null ) throw new GltfException( $"No resource resolver is available for '{uri}'." );
		try { return resolver( uri ) ?? throw new GltfException( $"Resource '{uri}' was not found." ); }
		catch ( Exception e ) when ( e is not (GltfException or OutOfMemoryException or OperationCanceledException) )
		{
			throw new GltfException( $"Failed to resolve '{uri}': {e.Message}", e );
		}
	}

	private static byte[] DecodeDataUri( string uri )
	{
		var comma = uri.IndexOf( ',' );
		if ( comma < 5 ) throw new GltfException( "Invalid data URI." );
		try
		{
			var header = uri.AsSpan( 5, comma - 5 );
			if ( header.EndsWith( ";base64", StringComparison.OrdinalIgnoreCase ) )
				return Convert.FromBase64String( Uri.UnescapeDataString( uri[(comma + 1)..] ) );
			using var decoded = new MemoryStream();
			var start = comma + 1;
			for ( var i = start; i < uri.Length; i++ )
			{
				if ( uri[i] != '%' ) continue;
				decoded.Write( Encoding.UTF8.GetBytes( uri.AsSpan( start, i - start ).ToString() ) );
				if ( i + 2 >= uri.Length || !byte.TryParse( uri.AsSpan( i + 1, 2 ), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var value ) )
					throw new GltfException( "Invalid percent escape in data URI." );
				decoded.WriteByte( value );
				i += 2;
				start = i + 1;
			}
			decoded.Write( Encoding.UTF8.GetBytes( uri.AsSpan( start ).ToString() ) );
			return decoded.ToArray();
		}
		catch ( FormatException e ) when ( e is not GltfException ) { throw new GltfException( "Invalid base64 data URI.", e ); }
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		json.Dispose();
		ClearMeshoptCache();
		ClearDracoCache();
		buffers = [];
		resolver = null;
	}
}
