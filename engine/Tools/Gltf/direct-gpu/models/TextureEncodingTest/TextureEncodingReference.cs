using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;

/// <summary>Literal texel and bounded PNG reference; never calls the production decoder.</summary>
public sealed class TextureEncodingReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly string[] ImageNames = ["0_136_0.png", "0_136_0_gamma.png", "0_136_0_icc.png", "0_136_255.png", "0_136_255_gamma.png", "0_136_255_icc.png", "TestLabels.png", "SlotLabels.png"];
	private static readonly string[] ImageHashes = ["7310c0157395b5732c24711abbbc644ae273587de38077045470c9c7ea129bdb", "ec86d9a12c304702f5f6fc21a03d425bdf8608d8a70a8b0eca5f4ff5521afdff", "c50df4c3a6c536e27bec21132dcafa2eb0b3c6a5570fe88e7f60aebaa296e8dd", "0fa99fc5d9a46991a583302bb176867463bc03f13d40ca72ca4ffdbf5f6aa3f9", "f581f51829831a1435e2b1c4e8e1e060c2c91a17fb0403c1c1ae98ebedd9fc5d", "00681909c34b627cc7ef05da0112f0f384318813ce0726e148d3647a160f619e", "8d963c30e101bee83df3db342b80d2905b52b076220b2aed76ce9908a15500b6", "4a4f3a1ecc2bf5085962db72f1b81d7523aa364484fe7049b3c196955cee9cf6"];
	private readonly Dictionary<string, byte[]> files = new( StringComparer.Ordinal );
	public byte[][] Images { get; private set; }
	public sealed record Form( string Name, byte[] Bytes, Func<string, byte[]> Resolve, bool Portable, bool Embedded );

	public static TextureEncodingReference Open( string checkoutRoot )
	{
		var root = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureEncodingTest" );
		var result = new TextureEncodingReference();
		for ( var i = 0; i < ImageNames.Length; i++ ) Read( "glTF/" + ImageNames[i], ImageHashes[i] );
		Read( "glTF/TextureEncodingTest.gltf", "7985fb99097900fc8df7ef5e90d750e329db51ecd4174a2b01dc5d727489570c" );
		Read( "glTF-Binary/TextureEncodingTest.glb", "c2f654bdf918e3cce93db3f8d8c1b008b90d3089e756c9b38149e9306b1338f4" );
		Read( "glTF/Plane.bin", "68eb0aaba58264604648e828ba7536b3c52333659445200f56d90b23b05a2f36" );
		Read( "glTF/Sphere.bin", "92bfff23a1cff625d3c012b4a2db0cfb2c30c92775195ee261247fec602f26f4" );
		Require( File.ReadAllText( Path.Combine( root, "metadata.json" ) ).Contains( "CC0-1.0", StringComparison.Ordinal ), "Preserve source CC0-1.0 attribution." );
		result.Images = ImageNames.Select( name => result.files["glTF/" + name] ).ToArray();
		return result;

		void Read( string path, string hash )
		{
			var file = new FileInfo( Path.Combine( root, path ) ); Require( file.Exists && file.Length is > 0 and < 64 * 1024, "Missing/oversized pinned input: " + path );
			var bytes = File.ReadAllBytes( file.FullName ); Require( Hash( bytes ) == hash, "Pinned source changed: " + path ); result.files.Add( path, bytes );
		}
	}

	public Form[] Forms()
	{
		var rows = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Binary" } )
		{
			var embedded = variant == "glTF-Binary"; var source = files[variant + "/TextureEncodingTest." + (embedded ? "glb" : "gltf")];
			byte[] Resolve( string uri ) => files.TryGetValue( "glTF/" + uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected source dependency: " + uri );
			rows.Add( new( variant + "/source", source, Resolve, false, embedded ) );
			var bundle = GltfArtifactDependencies.Prepare( GltfCompiler.Compile( source, Resolve ), Resolve );
			byte[] PortableResolve( string uri ) => bundle.Dependencies.TryGetValue( uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Portable artifact used a source-tree dependency: " + uri );
			Require( bundle.Dependencies.Count == (embedded ? 0 : 8), "Portable external image count changed." );
			rows.Add( new( variant + "/portable_glb_c", bundle.Artifact, PortableResolve, true, embedded ) );
		}
		return rows.ToArray();
	}

	public static byte[] LiteralTexel( int image ) => [0, 136, image < 3 ? (byte)0 : (byte)255, 255];
	public static double Linear136 => Math.Pow( (136.0 / 255 + .055) / 1.055, 2.4 );
	public static byte[] DecodeImage( byte[] image, int index ) => index < 6 ? Png.Decode( image ).Rgba : DecodeLabelPalette4( image );

	/// <summary>Exact pinned label format: noninterlaced 4-bit indexed PNG, top row first.</summary>
	private static byte[] DecodeLabelPalette4( byte[] png )
	{
		var chunks = Chunks( png ); var header = chunks.Single( x => x.Type == "IHDR" ).Bytes;
		Require( header.Length == 13 && BinaryPrimitives.ReadInt32BigEndian( header ) == 1024 && BinaryPrimitives.ReadInt32BigEndian( header.AsSpan( 4 ) ) == 256 && header.AsSpan( 8 ).SequenceEqual( new byte[] { 4, 3, 0, 0, 0 } ), "Pinned label format changed." );
		var palette = chunks.Single( x => x.Type == "PLTE" ).Bytes; var alpha = chunks.Single( x => x.Type == "tRNS" ).Bytes;
		Require( palette.Length == 24 && alpha.Length == 1 && alpha[0] == 0, "Pinned labels need eight palette colors and transparent index zero." );
		using var compressed = new MemoryStream( chunks.Where( x => x.Type == "IDAT" ).SelectMany( x => x.Bytes ).ToArray() );
		var filtered = new byte[513 * 256]; using ( var zlib = new ZLibStream( compressed, CompressionMode.Decompress ) ) { zlib.ReadExactly( filtered ); Require( zlib.ReadByte() == -1, "Extra PNG decompressed data." ); }
		var decoded = new byte[512 * 256]; var rgba = new byte[1024 * 256 * 4];
		for ( var y = 0; y < 256; y++ )
		{
			var filter = filtered[y * 513]; Require( filter <= 4, "Invalid PNG filter." );
			for ( var x = 0; x < 512; x++ )
			{
				var a = x == 0 ? 0 : decoded[y * 512 + x - 1]; var b = y == 0 ? 0 : decoded[(y - 1) * 512 + x]; var c = x == 0 || y == 0 ? 0 : decoded[(y - 1) * 512 + x - 1];
				var prediction = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) / 2, _ => Paeth( a, b, c ) }; decoded[y * 512 + x] = unchecked((byte)(filtered[y * 513 + x + 1] + prediction));
				for ( var nibble = 0; nibble < 2; nibble++ )
				{
					var index = (decoded[y * 512 + x] >> (nibble == 0 ? 4 : 0)) & 15; Require( index < 8, "PNG palette index exceeds pinned palette." ); var pixel = (y * 1024 + x * 2 + nibble) * 4;
					palette.AsSpan( index * 3, 3 ).CopyTo( rgba.AsSpan( pixel ) ); rgba[pixel + 3] = index == 0 ? (byte)0 : (byte)255;
				}
			}
		}
		return rgba;
	}

	public sealed record Chunk( string Type, byte[] Bytes );
	public static Chunk[] Chunks( byte[] png )
	{
		Require( png is { Length: >= 33 } && png.AsSpan( 0, 8 ).SequenceEqual( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } ), "Invalid PNG signature." );
		var rows = new List<Chunk>(); var offset = 8;
		while ( offset < png.Length )
		{
			Require( png.Length - offset >= 12, "Truncated PNG chunk." ); var count = checked((int)BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset ) )); Require( count <= png.Length - offset - 12, "Invalid PNG chunk extent." );
			var type = Encoding.ASCII.GetString( png, offset + 4, 4 ); var bytes = png.AsSpan( offset + 8, count ).ToArray();
			Require( Crc( png.AsSpan( offset + 4, count + 4 ) ) == BinaryPrimitives.ReadUInt32BigEndian( png.AsSpan( offset + count + 8 ) ), "PNG CRC mismatch." ); rows.Add( new( type, bytes ) ); offset += count + 12;
			if ( type == "IEND" ) { Require( count == 0 && offset == png.Length, "Invalid PNG end." ); return rows.ToArray(); }
		}
		throw new InvalidOperationException( "Missing PNG IEND." );
	}

	/// <summary>Asymmetric literal 2x2 fixture with straight, semitransparent and zero-alpha colors.</summary>
	public static byte[] StraightAlphaFixture( int colorType, out byte[] expected )
	{
		Require( colorType is 2 or 6, "Fixture supports RGB/RGBA." );
		expected = [32, 64, 128, 17, 255, 5, 90, 128, 0, 136, 255, 0, 71, 3, 201, 255];
		if ( colorType == 2 ) for ( var i = 3; i < expected.Length; i += 4 ) expected[i] = 255;
		var channels = colorType == 2 ? 3 : 4; var filtered = new byte[(1 + 2 * channels) * 2];
		for ( var y = 0; y < 2; y++ ) for ( var x = 0; x < 2; x++ ) expected.AsSpan( (y * 2 + x) * 4, channels ).CopyTo( filtered.AsSpan( y * (1 + 2 * channels) + 1 + x * channels ) );
		using var output = new MemoryStream(); output.Write( new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 } );
		var header = new byte[13]; BinaryPrimitives.WriteInt32BigEndian( header, 2 ); BinaryPrimitives.WriteInt32BigEndian( header.AsSpan( 4 ), 2 ); header[8] = 8; header[9] = (byte)colorType; WriteChunk( output, "IHDR", header );
		WriteChunk( output, "gAMA", new byte[] { 0, 0, 0x4e, 0x20 } );
		using var compressed = new MemoryStream(); using ( var zlib = new ZLibStream( compressed, CompressionLevel.Optimal, true ) ) zlib.Write( filtered );
		WriteChunk( output, "IDAT", compressed.ToArray() ); WriteChunk( output, "IEND", [] ); return output.ToArray();
	}

	private static void WriteChunk( Stream output, string type, byte[] bytes ) { Span<byte> integer = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian( integer, bytes.Length ); output.Write( integer ); var block = Encoding.ASCII.GetBytes( type ).Concat( bytes ).ToArray(); output.Write( block ); BinaryPrimitives.WriteUInt32BigEndian( integer, Crc( block ) ); output.Write( integer ); }
	private static int Paeth( int a, int b, int c ) { var p = a + b - c; var da = Math.Abs( p - a ); var db = Math.Abs( p - b ); var dc = Math.Abs( p - c ); return da <= db && da <= dc ? a : db <= dc ? b : c; }
	public static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static uint Crc( ReadOnlySpan<byte> bytes ) { var crc = uint.MaxValue; foreach ( var value in bytes ) { crc ^= value; for ( var bit = 0; bit < 8; bit++ ) crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u); } return ~crc; }
	public static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
