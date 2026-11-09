// Editor-only, bounded image resource conformance. No scene or authored asset is changed.
// Pipeline: GltfReader.ReadImage -> GltfTextureCompiler -> .vtex_c -> Texture.Load -> raw base-level readback.
// Oracles are authored samples, never Bitmap/Skia decoder output. This does not test Model.Load,
// rendered color transfer, UV orientation, material alpha, sampler behavior or generated mips.
// References: https://registry.khronos.org/glTF/specs/2.0/glTF-2.0.html#images
//             https://www.w3.org/TR/2025/REC-png-3-20250624/
var engineLocation = typeof( Model ).Assembly.Location;
var root = new System.IO.DirectoryInfo( System.IO.Path.GetDirectoryName( engineLocation ) );
while ( root is not null && !System.IO.File.Exists( System.IO.Path.Combine( root.FullName, "engine", "Sandbox-Engine.slnx" ) ) ) root = root.Parent;
if ( root is null ) throw new InvalidOperationException( "Cannot locate the checkout from " + engineLocation );
var runId = Guid.NewGuid().ToString( "N" );
var output = System.IO.Path.Combine( root.FullName, "Temp", "gltf-validation", "editor-image-conformance", runId );
System.IO.Directory.CreateDirectory( output );
var receiptPath = System.IO.Path.Combine( output, "receipt.json" );
var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static;
var cases = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
var fixtureInventory = new System.Collections.Generic.List<object>();
var cleanupErrors = new System.Collections.Generic.List<string>();
var fixtures = new System.Collections.Generic.List<(string Name, string Mime, int Width, int Height, int Depth, byte[] Bytes, ushort[] Rgba, int ByteTolerance, string Oracle)>();
var sourceForms = new[] { "external-uri", "data-uri-base64", "data-uri-percent", "glb-buffer-view" };
var requiredGates = new[] { "source_read", "source_role_metadata", "native_compile", "native_resource_metadata", "native_load", "native_pixel_oracle" };
var expectedCaseCount = 0;
var completed = false;
string fatalError = null;
object redirectObject = null;
IDisposable redirect = null;
System.Reflection.MethodInfo addRedirect = null, removeRedirect = null, invalidate = null, compile = null;
string redirectInitializationError = null;

string Hash( byte[] bytes ) => Convert.ToHexStringLower( System.Security.Cryptography.SHA256.HashData( bytes ) );
void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
string Error( Exception exception )
{
	while ( exception is System.Reflection.TargetInvocationException && exception.InnerException is not null ) exception = exception.InnerException;
	return exception.ToString();
}
void Cleanup( string name, Action action ) { try { action(); } catch ( Exception e ) { cleanupErrors.Add( name + ": " + Error( e ) ); } }
bool Gate( System.Collections.Generic.Dictionary<string, object> result, string name, Action action )
{
	try { cancellationToken.ThrowIfCancellationRequested(); action(); result[name] = "passed"; return true; }
	catch ( OperationCanceledException ) { throw; }
	catch ( Exception e ) { result[name] = "failed"; result[name + "_error"] = Error( e ); return false; }
}
void U32( System.IO.Stream stream, uint value, bool littleEndian = false )
{
	for ( var i = 0; i < 4; i++ ) stream.WriteByte( (byte)(value >> ((littleEndian ? i : 3 - i) * 8)) );
}
byte[] U16Array( ushort[] values )
{
	var bytes = new byte[values.Length * 2];
	for ( var i = 0; i < values.Length; i++ ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( i * 2, 2 ), values[i] );
	return bytes;
}
uint Crc32( byte[] data )
{
	var crc = uint.MaxValue;
	foreach ( var value in data )
	{
		crc ^= value;
		for ( var bit = 0; bit < 8; bit++ ) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0u : 0xedb88320u);
	}
	return ~crc;
}
void Chunk( System.IO.Stream stream, string name, byte[] data )
{
	var type = System.Text.Encoding.ASCII.GetBytes( name );
	U32( stream, (uint)data.Length ); stream.Write( type ); stream.Write( data );
	var crcInput = new byte[type.Length + data.Length];
	type.CopyTo( crcInput, 0 ); data.CopyTo( crcInput, type.Length );
	U32( stream, Crc32( crcInput ) );
}
byte[] ZlibStored( byte[] bytes )
{
	// Small, deterministic, uncompressed DEFLATE blocks avoid using an image encoder
	// or decoder in the PNG oracle. The zlib header and Adler-32 cover the raw rows.
	using var stream = new System.IO.MemoryStream();
	stream.WriteByte( 0x78 ); stream.WriteByte( 0x01 );
	for ( var start = 0; start < bytes.Length; )
	{
		var count = Math.Min( 65535, bytes.Length - start );
		stream.WriteByte( start + count == bytes.Length ? (byte)1 : (byte)0 );
		stream.WriteByte( (byte)count ); stream.WriteByte( (byte)(count >> 8) );
		stream.WriteByte( (byte)~count ); stream.WriteByte( (byte)(~count >> 8) );
		stream.Write( bytes, start, count ); start += count;
	}
	uint a = 1, b = 0;
	foreach ( var value in bytes ) { a = (a + value) % 65521; b = (b + a) % 65521; }
	U32( stream, (b << 16) | a ); return stream.ToArray();
}
int Paeth( int a, int b, int c )
{
	var p = a + b - c; var pa = Math.Abs( p - a ); var pb = Math.Abs( p - b ); var pc = Math.Abs( p - c );
	return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
}
byte[] Png( int width, int height, int depth, int colorType, ushort[] samples, byte[] palette, byte[] transparency, bool allFilters = false, bool adam7 = false, bool gamma = false )
{
	var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => throw new InvalidOperationException( "Fixture color type" ) };
	Require( samples.Length == width * height * channels, "Fixture sample count differs." );
	var passes = adam7
		? new[] { (X: 0, Y: 0, Dx: 8, Dy: 8), (X: 4, Y: 0, Dx: 8, Dy: 8), (X: 0, Y: 4, Dx: 4, Dy: 8), (X: 2, Y: 0, Dx: 4, Dy: 4), (X: 0, Y: 2, Dx: 2, Dy: 4), (X: 1, Y: 0, Dx: 2, Dy: 2), (X: 0, Y: 1, Dx: 1, Dy: 2) }
		: new[] { (X: 0, Y: 0, Dx: 1, Dy: 1) };
	using var raw = new System.IO.MemoryStream();
	foreach ( var pass in passes )
	{
		var passWidth = pass.X >= width ? 0 : (width - pass.X + pass.Dx - 1) / pass.Dx;
		if ( passWidth == 0 || pass.Y >= height ) continue;
		byte[] previous = null;
		for ( var y = pass.Y; y < height; y += pass.Dy )
		{
			var row = new byte[(passWidth * channels * depth + 7) / 8];
			var sampleIndex = 0;
			for ( var x = pass.X; x < width; x += pass.Dx )
			for ( var channel = 0; channel < channels; channel++ )
			{
				var sample = samples[(y * width + x) * channels + channel];
				if ( depth == 16 ) System.Buffers.Binary.BinaryPrimitives.WriteUInt16BigEndian( row.AsSpan( sampleIndex * 2, 2 ), sample );
				else if ( depth == 8 ) row[sampleIndex] = (byte)sample;
				else row[sampleIndex * depth / 8] |= (byte)(sample << (8 - depth - sampleIndex * depth % 8));
				sampleIndex++;
			}
			var filter = allFilters ? y % 5 : 0;
			raw.WriteByte( (byte)filter );
			var bytesPerPixel = Math.Max( 1, (channels * depth + 7) / 8 );
			for ( var i = 0; i < row.Length; i++ )
			{
				var left = i >= bytesPerPixel ? row[i - bytesPerPixel] : 0;
				var up = previous is null ? 0 : previous[i];
				var upperLeft = previous is null || i < bytesPerPixel ? 0 : previous[i - bytesPerPixel];
				var predictor = filter switch { 0 => 0, 1 => left, 2 => up, 3 => (left + up) / 2, 4 => Paeth( left, up, upperLeft ), _ => throw new InvalidOperationException() };
				raw.WriteByte( unchecked((byte)(row[i] - predictor)) );
			}
			previous = row;
		}
	}
	using var png = new System.IO.MemoryStream();
	png.Write( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } );
	using var ihdr = new System.IO.MemoryStream(); U32( ihdr, (uint)width ); U32( ihdr, (uint)height );
	ihdr.Write( new byte[] { (byte)depth, (byte)colorType, 0, 0, adam7 ? (byte)1 : (byte)0 } );
	Chunk( png, "IHDR", ihdr.ToArray() );
	if ( gamma ) { using var payload = new System.IO.MemoryStream(); U32( payload, 100000 ); Chunk( png, "gAMA", payload.ToArray() ); }
	if ( palette is not null ) Chunk( png, "PLTE", palette );
	if ( transparency is not null ) Chunk( png, "tRNS", transparency );
	Chunk( png, "IDAT", ZlibStored( raw.ToArray() ) ); Chunk( png, "IEND", Array.Empty<byte>() ); return png.ToArray();
}

void AddPng( int colorType, int depth, string suffix = "", bool allFilters = false, bool adam7 = false, bool gamma = false )
{
	var width = 5; var height = allFilters ? 5 : 3;
	var channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, 6 => 4, _ => 0 };
	var samples = new ushort[width * height * channels];
	var rgba = new ushort[width * height * 4];
	var max = (1 << depth) - 1;
	var palette = colorType == 3 ? new byte[] { 23,197,61, 241,19,83, 75,129,233, 181,99,7 }.Take( depth == 1 ? 6 : 12 ).ToArray() : null;
	byte[] transparency = colorType switch
	{
		0 => new byte[] { 0, 0 },
		2 => depth == 16 ? new byte[] { 0xab,0xcd, 0x32,0x10, 0x01,0x01 } : new byte[] { 0,181, 0,99, 0,7 },
		3 => new byte[] { 0, 97, 255, 173 }.Take( depth == 1 ? 2 : 4 ).ToArray(),
		_ => null
	};
	var gray16 = new ushort[] { 0x0100, 0x0101, 0x20ff, 0xabcd, 0xffff, 0 };
	var alpha16 = new ushort[] { 0x8000, 0x8001, 0xffff, 0, 0x4000, 0xfffe };
	var colors16 = new ushort[][]
	{
		new ushort[] { 0x0100,0x0200,0x4001,0x8000 }, new ushort[] { 0x0101,0x0201,0x4002,0x8001 },
		new ushort[] { 0x2000,0x6000,0xfffe,0xffff }, new ushort[] { 0xabcd,0x3210,0x0101,0 },
		new ushort[] { 0,0xffff,0x1234,0x4000 }, new ushort[] { 0xffff,0,0xfedc,0xfffe }
	};
	var colors8 = new byte[][]
	{
		new byte[] { 23,197,61,0 }, new byte[] { 241,19,83,97 }, new byte[] { 75,129,233,255 },
		new byte[] { 181,99,7,173 }, new byte[] { 0,255,127,1 }, new byte[] { 255,0,249,254 }
	};
	for ( var y = 0; y < height; y++ )
	for ( var x = 0; x < width; x++ )
	{
		// The non-square, asymmetric layout detects row/column swaps and either flip.
		var pixel = y * width + x; var index = (x + 3 * y) % 6;
		if ( colorType == 0 || colorType == 4 )
		{
			var gray = depth == 16 ? gray16[index] : (ushort)((x + 3 * y) * 7 % (max + 1));
			var alpha = depth == 16 ? alpha16[index] : (ushort)colors8[index][3];
			samples[pixel * channels] = gray;
			if ( colorType == 4 ) samples[pixel * channels + 1] = alpha;
			for ( var c = 0; c < 3; c++ ) rgba[pixel * 4 + c] = (ushort)((long)gray * 65535 / max);
			rgba[pixel * 4 + 3] = colorType == 4 ? (ushort)((long)alpha * 65535 / max) : gray == 0 ? (ushort)0 : ushort.MaxValue;
		}
		else if ( colorType == 3 )
		{
			var paletteIndex = (x + y * 3) % (palette.Length / 3); samples[pixel] = (ushort)paletteIndex;
			for ( var c = 0; c < 3; c++ ) rgba[pixel * 4 + c] = (ushort)(palette[paletteIndex * 3 + c] * 257);
			rgba[pixel * 4 + 3] = (ushort)(transparency[paletteIndex] * 257);
		}
		else
		{
			for ( var c = 0; c < channels; c++ ) samples[pixel * channels + c] = depth == 16 ? colors16[index][c] : colors8[index][c];
			for ( var c = 0; c < 3; c++ ) rgba[pixel * 4 + c] = depth == 16 ? colors16[index][c] : (ushort)(colors8[index][c] * 257);
			var alpha = colorType == 6 ? (depth == 16 ? colors16[index][3] : colors8[index][3] * 257) : index == 3 ? 0 : 65535;
			rgba[pixel * 4 + 3] = (ushort)alpha;
		}
	}
	var name = "png-type" + colorType + "-depth" + depth + suffix;
	fixtures.Add( (name, "image/png", width, height, depth, Png( width, height, depth, colorType, samples, palette, transparency, allFilters, adam7, gamma ), rgba, 0,
		"Authored PNG integer samples; exact normalized RGBA expansion, straight alpha/tRNS, top-to-bottom rows; 16-bit values require exact native UNORM16 readback.") );
}

byte[] Glb( byte[] json, byte[] binary )
{
	var jsonLength = (json.Length + 3) & ~3; var binaryLength = (binary.Length + 3) & ~3;
	using var stream = new System.IO.MemoryStream();
	U32( stream, 0x46546c67, true ); U32( stream, 2, true ); U32( stream, (uint)(12 + 8 + jsonLength + 8 + binaryLength), true );
	U32( stream, (uint)jsonLength, true ); U32( stream, 0x4e4f534a, true ); stream.Write( json );
	for ( var i = json.Length; i < jsonLength; i++ ) stream.WriteByte( 32 );
	U32( stream, (uint)binaryLength, true ); U32( stream, 0x004e4942, true ); stream.Write( binary );
	for ( var i = binary.Length; i < binaryLength; i++ ) stream.WriteByte( 0 );
	return stream.ToArray();
}
byte[] Source( string form, string mime, byte[] image, string externalUri )
{
	var rootNode = new System.Collections.Generic.Dictionary<string, object>
	{
		["asset"] = new { version = "2.0" }, ["textures"] = new[] { new { source = 0 } },
		["materials"] = new[] { new { pbrMetallicRoughness = new { baseColorTexture = new { index = 0 }, metallicRoughnessTexture = new { index = 0 } }, normalTexture = new { index = 0 }, occlusionTexture = new { index = 0 }, emissiveTexture = new { index = 0 } } }
	};
	byte[] binary = null;
	if ( form == "external-uri" ) rootNode["images"] = new[] { new { uri = externalUri } }; // MIME sniffing, not filename inference.
	else if ( form == "data-uri-base64" ) rootNode["images"] = new[] { new { uri = "data:" + mime + ";base64," + Convert.ToBase64String( image ) } };
	else if ( form == "data-uri-percent" ) rootNode["images"] = new[] { new { uri = "data:" + mime + "," + string.Concat( image.Select( b => "%" + b.ToString( "X2", System.Globalization.CultureInfo.InvariantCulture ) ) ), mimeType = mime } };
	else
	{
		Require( form == "glb-buffer-view", "Unknown fixture source form." );
		binary = new byte[image.Length + 9]; Array.Fill( binary, (byte)0xa7 ); image.CopyTo( binary, 4 );
		rootNode["buffers"] = new[] { new { byteLength = binary.Length } };
		rootNode["bufferViews"] = new[] { new { buffer = 0, byteOffset = 4, byteLength = image.Length } };
		rootNode["images"] = new[] { new { bufferView = 0, mimeType = mime } };
	}
	var json = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( rootNode ); return binary is null ? json : Glb( json, binary );
}
int NativeDataOffset( byte[] bytes )
{
	Require( bytes.Length >= 28, "Compiled texture resource header is truncated." );
	var table = checked(8 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( 8 ) ));
	var count = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( 12 ) );
	Require( table >= 0 && count <= 64 && (long)table + count * 12 <= bytes.Length, "Invalid compiled texture block table." );
	for ( var i = 0; i < count; i++ )
	{
		var entry = table + i * 12;
		if ( bytes[entry] != 68 || bytes[entry + 1] != 65 || bytes[entry + 2] != 84 || bytes[entry + 3] != 65 ) continue;
		var data = checked(entry + 4 + System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( entry + 4 ) ));
		var size = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian( bytes.AsSpan( entry + 8 ) );
		Require( data >= 0 && size >= 32 && (long)data + size <= bytes.Length, "Invalid compiled texture DATA range." ); return data;
	}
	throw new InvalidOperationException( "Compiled texture has no DATA block." );
}

try
{
	await MainThread.Wait();
	Require( Application.IsEditor && !Graphics.IsActive, "Run inside the editor outside a graphics scope." );
	foreach ( var depth in new[] { 1, 2, 4, 8, 16 } ) AddPng( 0, depth );
	foreach ( var depth in new[] { 1, 2, 4, 8 } ) AddPng( 3, depth );
	foreach ( var type in new[] { 2, 4, 6 } ) foreach ( var depth in new[] { 8, 16 } ) AddPng( type, depth );
	AddPng( 6, 8, "-all-filters", allFilters: true );
	AddPng( 6, 8, "-adam7", adam7: true );
	AddPng( 6, 8, "-gamma-ignored", gamma: true );
	// JPEG fixture provenance: self-generated specifically for this probe from
	// the six constant 8x8 source blocks below; no external image/content was used.
	// These fixture bytes are part of this repository's authored test source and
	// follow its license. Pillow 12.3.0 encoded real baseline/progressive/grayscale
	// JPEG streams at quality=100, subsampling=0, optimize=false; progressive=true
	// only for jpeg-progressive-color. No format markers were rewritten.
	// Expected pixels are the pre-encode source colors, never JPEG decoder output.
	var jpegDefinitions = new (string Name, string Base64, bool Gray)[]
	{
		("jpeg-baseline-color", "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAARCAAQABgDAREAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD+c+v6oP5nPtiv4PP+rg/TCvnz/wA5c+4K/wARz/tgP5d6/wDRoPw8/wBVCv8Am3P5XP/Z", false),
		("jpeg-progressive-color", "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/2wBDAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wgARCAAQABgDAREAAhEBAxEB/8QAFwAAAwEAAAAAAAAAAAAAAAAABwgJCv/EABkBAAIDAQAAAAAAAAAAAAAAAAcJBQYKCP/aAAwDAQACEAMQAAABnOVBmbAO1xmI/OWcOI3YS70aUfVQtsV//8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABBQJf/8QAFBEBAAAAAAAAAAAAAAAAAAAAIP/aAAgBAwEBPwFf/8QAFBEBAAAAAAAAAAAAAAAAAAAAIP/aAAgBAgEBPwFf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQAGPwJf/8QAFBABAAAAAAAAAAAAAAAAAAAAIP/aAAgBAQABPyFf/9oADAMBAAIAAwAAABAAAD//xAAUEQEAAAAAAAAAAAAAAAAAAAAg/9oACAEDAQE/EF//xAAUEQEAAAAAAAAAAAAAAAAAAAAg/9oACAECAQE/EF//xAAUEAEAAAAAAAAAAAAAAAAAAAAg/9oACAEBAAE/EF//2Q==", false),
		("jpeg-grayscale", "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQH/wAALCAAQABgBAREA/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/9oACAEBAAA/AP8AP/r9AK/dCv3gr94K/Wiv/9k=", true)
	};
	var colorBlocks = new byte[][] { new byte[] { 32,64,96 }, new byte[] { 180,40,70 }, new byte[] { 20,180,100 }, new byte[] { 200,150,30 }, new byte[] { 100,30,200 }, new byte[] { 240,240,240 } };
	var grayBlocks = new byte[] { 0,32,90,150,210,255 };
	foreach ( var definition in jpegDefinitions )
	{
		var expected = new ushort[24 * 16 * 4];
		for ( var y = 0; y < 16; y++ ) for ( var x = 0; x < 24; x++ )
		{
			var block = y / 8 * 3 + x / 8; var pixel = y * 24 + x;
			for ( var c = 0; c < 3; c++ ) expected[pixel * 4 + c] = (ushort)((definition.Gray ? grayBlocks[block] : colorBlocks[block][c]) * 257);
			expected[pixel * 4 + 3] = ushort.MaxValue;
		}
		fixtures.Add( (definition.Name, "image/jpeg", 24, 16, 8, Convert.FromBase64String( definition.Base64 ), expected, 3,
			"Authored six 8x8 source-color blocks in top-to-bottom order; RGB tolerance 3/255 for JPEG quantization/color conversion; alpha must be exactly 1.") );
	}
	expectedCaseCount = fixtures.Count * sourceForms.Length * 2;
	Require( fixtures.Count == 21 && expectedCaseCount == 168, "Conformance fixture inventory changed; review case counts." );
	try
	{
		var assembly = typeof( Model ).Assembly;
		compile = assembly.GetType( "Sandbox.GltfTextureCompiler", true ).GetMethod( "Compile", flags );
		var redirectType = assembly.GetType( "Sandbox.Internal.RedirectFileSystem", true );
		redirectObject = redirectType.GetMethod( "Create", flags ).Invoke( null, new object[] { output } );
		redirect = (IDisposable)redirectObject;
		addRedirect = redirectType.GetMethod( "AddAbsFile", flags ); removeRedirect = redirectType.GetMethod( "RemoveAbsFile", flags );
		invalidate = assembly.GetType( "NativeEngine.g_pResourceSystem", true ).GetMethod( "InvalidateDatabase", flags );
	}
	catch ( Exception e ) { redirectInitializationError = Error( e ); } // Cases still record source/native results independently.
	foreach ( var fixture in fixtures )
	{
		var imageFile = fixture.Name + (fixture.Mime == "image/png" ? ".png" : ".jpg");
		var imageDirectory = System.IO.Path.Combine( output, "image payload" ); System.IO.Directory.CreateDirectory( imageDirectory );
		var imagePath = System.IO.Path.Combine( imageDirectory, imageFile ); System.IO.File.WriteAllBytes( imagePath, fixture.Bytes );
		var oraclePath = System.IO.Path.Combine( output, fixture.Name + ".expected-rgba16le" ); var oracleBytes = U16Array( fixture.Rgba ); System.IO.File.WriteAllBytes( oraclePath, oracleBytes );
		fixtureInventory.Add( new { name = fixture.Name, mime = fixture.Mime, width = fixture.Width, height = fixture.Height, source_bit_depth = fixture.Depth,
			image_path = imagePath, image_sha256 = Hash( fixture.Bytes ), oracle_path = oraclePath, oracle_sha256 = Hash( oracleBytes ),
			provenance = fixture.Mime == "image/png" ? "Self-generated by this probe's integer PNG writer; no third-party image; repository license." : "Self-generated from the six authored 8x8 source blocks, Pillow 12.3.0, JPEG quality=100/subsampling=0/optimize=false; repository license; no third-party image.",
			oracle = fixture.Oracle, rgb_byte_tolerance = fixture.ByteTolerance, alpha_tolerance = 0,
			precision_pair = fixture.Depth == 16 ? "first two texels: channel R=0x0100/0x0101; both high bytes 0x01; preserve distinct low bits" : null } );
		foreach ( var form in sourceForms )
		foreach ( var isSrgb in new[] { false, true } )
		{
			cancellationToken.ThrowIfCancellationRequested();
			var caseName = fixture.Name + "-" + form + (isSrgb ? "-srgb" : "-linear");
			var result = new System.Collections.Generic.Dictionary<string, object>
			{
				["case"] = caseName, ["fixture"] = fixture.Name, ["source_form"] = form, ["is_srgb_resource"] = isSrgb,
				["image_sha256"] = Hash( fixture.Bytes ), ["native_role_transfer_sampling"] = "not-tested", ["uv_orientation_rendering"] = "not-tested",
				["material_alpha_behavior"] = "not-tested", ["samplers"] = "not-tested", ["generated_mips"] = "not-tested", ["model_load"] = "not-tested"
			};
			foreach ( var gate in requiredGates ) result[gate] = "blocked-by-prior-gate";
			cases.Add( result );
			Sandbox.Gltf.GltfImage image = null;
			Sandbox.Gltf.GltfMaterial sourceMaterial = null;
			byte[] native = null;
			Texture texture = null;
			var virtualBase = "gltf-image-conformance/" + runId + "/" + caseName;
			var alias = "/" + virtualBase + ".vtex_c";
			var linked = false;
			try
			{
				var sourcePassed = Gate( result, "source_read", () =>
				{
					var externalUri = "image%20payload/" + imageFile;
					var source = Source( form, fixture.Mime, fixture.Bytes, externalUri );
					result["source_sha256"] = Hash( source );
					var sourcePath = System.IO.Path.Combine( output, caseName + (form == "glb-buffer-view" ? ".glb" : ".gltf") );
					System.IO.File.WriteAllBytes( sourcePath, source ); result["source_path"] = sourcePath;
					var requests = new System.Collections.Generic.List<string>();
					byte[] Resolve( string uri ) { requests.Add( uri ); Require( uri == externalUri, "Unexpected image resolver request: " + uri ); return System.IO.File.ReadAllBytes( imagePath ); }
					using var document = Sandbox.Gltf.GltfReader.Read( source, Resolve );
					image = document.ReadImage( 0 ); sourceMaterial = document.ReadMaterial( 0 );
					Require( image.MimeType == fixture.Mime && image.Bytes.AsSpan().SequenceEqual( fixture.Bytes ), "Image source form changed encoded data or MIME type." );
					Require( requests.Count == (form == "external-uri" ? 1 : 0), "Unexpected external resolver use." ); result["resolver_requests"] = requests.ToArray();
				} );
				if ( !sourcePassed ) continue;
				Gate( result, "source_role_metadata", () =>
				{
					var roles = new[] { sourceMaterial.BaseColorTexture, sourceMaterial.MetallicRoughnessTexture, sourceMaterial.NormalTexture, sourceMaterial.OcclusionTexture, sourceMaterial.EmissiveTexture };
					var expected = new[] { true, false, false, false, true };
					for ( var i = 0; i < roles.Length; i++ ) Require( roles[i]?.Index == 0 && roles[i].IsSrgb == expected[i], "Shared image role transfer metadata differs at role " + i );
				} );
				var compiled = Gate( result, "native_compile", () =>
				{
					Require( compile is not null, "Live engine GltfTextureCompiler.Compile is unavailable: " + redirectInitializationError );
					native = (byte[])compile.Invoke( null, new object[] { image, isSrgb, 16L * 1024 * 1024 } );
					Require( native is { Length: > 0 }, "Native texture compilation returned no resource." ); result["native_resource_sha256"] = Hash( native );
					var path = System.IO.Path.Combine( output, caseName + ".vtex_c" ); System.IO.File.WriteAllBytes( path, native ); result["native_resource_path"] = path;
				} );
				if ( !compiled ) continue;
				Gate( result, "native_resource_metadata", () =>
				{
					var data = NativeDataOffset( native ); var resourceFlags = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( native.AsSpan( data + 2 ) );
					var width = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( native.AsSpan( data + 20 ) ); var height = System.Buffers.Binary.BinaryPrimitives.ReadUInt16LittleEndian( native.AsSpan( data + 22 ) );
					var format = native[data + 26]; result["native_disk_format"] = format; result["native_disk_flags"] = resourceFlags; result["native_disk_mip_count"] = native[data + 27];
					Require( width == fixture.Width && height == fixture.Height, "Compiled texture dimensions differ." );
					Require( ((resourceFlags & 0x0200) != 0) == !isSrgb, "Compiled linear API creation flag differs from the requested role." );
					if ( fixture.Depth == 16 ) Require( format is 7 or 10 or 14, "16-bit PNG must use a native wide-precision format; RGBA8 silently loses required low bits." );
				} );
				var loaded = Gate( result, "native_load", () =>
				{
					Require( redirectObject is not null && addRedirect is not null && invalidate is not null, "Native redirect initialization failed: " + redirectInitializationError );
					addRedirect.Invoke( redirectObject, new object[] { alias, (string)result["native_resource_path"] } ); linked = true;
					invalidate.Invoke( null, null ); texture = Texture.Load( virtualBase + ".vtex", false );
					Require( texture is { IsValid: true } && !texture.IsError, "Texture.Load returned an invalid or error resource." );
					Require( texture.Width == fixture.Width && texture.Height == fixture.Height, "Native loaded texture dimensions differ." ); result["native_image_format"] = texture.ImageFormat.ToString();
				} );
				if ( !loaded ) continue;
				Gate( result, "native_pixel_oracle", () =>
				{
					var differences = new System.Collections.Generic.List<object>(); var mismatchCount = 0; var maxError = 0;
					ushort[] actual;
					if ( fixture.Depth == 16 )
					{
						actual = new ushort[fixture.Rgba.Length]; texture.GetPixels( (0, 0, fixture.Width, fixture.Height), 0, 0, actual.AsSpan(), ImageFormat.RGBA16161616 );
						result["readback_format"] = "RGBA16161616";
					}
					else
					{
						var bytes = new byte[fixture.Rgba.Length]; texture.GetPixels( (0, 0, fixture.Width, fixture.Height), 0, 0, bytes.AsSpan(), ImageFormat.RGBA8888 );
						actual = bytes.Select( b => (ushort)(b * 257) ).ToArray(); result["readback_format"] = "RGBA8888 expanded exactly to UNORM16 oracle units";
					}
					var actualBytes = U16Array( actual ); result["readback_sha256"] = Hash( actualBytes );
					var actualPath = System.IO.Path.Combine( output, caseName + ".actual-rgba16le" ); System.IO.File.WriteAllBytes( actualPath, actualBytes ); result["readback_path"] = actualPath;
					for ( var i = 0; i < actual.Length; i++ )
					{
						var delta = Math.Abs( actual[i] - fixture.Rgba[i] ); maxError = Math.Max( maxError, delta );
						var tolerance = i % 4 == 3 ? 0 : fixture.ByteTolerance * 257;
						if ( delta <= tolerance ) continue;
						mismatchCount++; if ( differences.Count < 16 ) differences.Add( new { x = i / 4 % fixture.Width, y = i / 4 / fixture.Width, channel = "RGBA"[i % 4].ToString(), expected = fixture.Rgba[i], observed = actual[i], error = delta, tolerance } );
					}
					result["mismatched_components"] = mismatchCount; result["max_unorm16_component_error"] = maxError; result["first_mismatches"] = differences;
					if ( fixture.Depth == 16 ) result["low_bit_pair"] = new { expected_first = fixture.Rgba[0], expected_second = fixture.Rgba[4], observed_first = actual[0], observed_second = actual[4] };
					Require( mismatchCount == 0, "Native base-level pixel oracle failed: " + mismatchCount + " components; maximum UNORM16 error " + maxError );
				} );
			}
			finally
			{
				if ( texture is not null && texture.IsValid && !texture.IsError ) Cleanup( caseName + " texture", () => texture.Dispose() );
				if ( linked ) Cleanup( caseName + " redirect", () => removeRedirect.Invoke( redirectObject, new object[] { alias } ) );
			}
			// Yield between resources; no preview windows, game scenes or user resources are owned here.
			await Task.Delay( 1, cancellationToken ); await MainThread.Wait();
		}
	}
	completed = true;
}
catch ( Exception e ) { fatalError = Error( e ); }
finally { if ( redirect is not null ) Cleanup( "redirect filesystem", () => redirect.Dispose() ); }

var passedCases = cases.Count( item => requiredGates.All( gate => (string)item[gate] == "passed" ) );
var gateCounts = requiredGates.ToDictionary( gate => gate, gate => new
{
	passed = cases.Count( item => (string)item[gate] == "passed" ), failed = cases.Count( item => (string)item[gate] == "failed" ),
	blocked = cases.Count( item => (string)item[gate] == "blocked-by-prior-gate" )
}, StringComparer.Ordinal );
var receipt = new
{
	version = 1, completed, image_resource_gates_passed = completed && fatalError is null && cleanupErrors.Count == 0 && cases.Count == expectedCaseCount && passedCases == expectedCaseCount,
	scope = "Core PNG/JPEG encoded sources, native texture compilation/loading, resource-role flags and independent base-level pixel oracles; no Model.Load, rendered transfer/UV/alpha, sampler, generated mip, cache or lifecycle acceptance.",
	expected_case_count = expectedCaseCount, attempted_case_count = cases.Count, passed_case_count = passedCases, failed_or_blocked_case_count = cases.Count - passedCases,
	fixture_count = fixtures.Count, source_forms = sourceForms, resource_roles = new[] { "linear", "sRGB" }, gate_counts = gateCounts,
	is_editor = Application.IsEditor, engine_location = engineLocation, engine_mvid = typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString(),
	gltf_mvid = typeof( Sandbox.Gltf.GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), fixture_inventory = fixtureInventory, cases,
	fatal_error = fatalError, native_initialization_error = redirectInitializationError, cleanup_errors = cleanupErrors, output, receipt_path = receiptPath
};
System.IO.File.WriteAllText( receiptPath, System.Text.Json.JsonSerializer.Serialize( receipt, new System.Text.Json.JsonSerializerOptions { WriteIndented = true } ) );
return receipt;
