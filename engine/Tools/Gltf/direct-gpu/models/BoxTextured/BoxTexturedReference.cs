using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Independent model-specific source/PNG reference. Source accessors are decoded
/// without Sandbox.Gltf; PNG decoding uses palette/chunk/filter/zlib data rather
/// than the production Skia decoder. This intentionally supports only the exact
/// audited BoxTextured inputs, not arbitrary image or glTF files.
/// </summary>
public sealed class BoxTexturedReference
{
	public const string CorpusRevision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public const string PngSha256 = "9c22b05c5b136d03c5621a8765e50a8322be6c35b9de53e9fe22685840d7f469";
	public byte[] PngBytes { get; private set; }
	public byte[] Rgba { get; private set; }
	private readonly Dictionary<string, byte[]> sourceFiles = new( StringComparer.Ordinal );

	public sealed class Form
	{
		public string Name { get; init; }
		public bool Compiled { get; init; }
		public byte[] Bytes { get; init; }
		public IndependentPrimitive Independent { get; init; }
		public Func<string, byte[]> Resolve { get; init; }
		public GltfDocument Read() => GltfReader.Read( Bytes, Resolve );
	}

	public sealed record IndependentPrimitive( NVector3[] Positions, NVector3[] Normals, NVector2[] Uvs, uint[] Indices );
	public sealed record NativeTexturePayload( int Width, int Height, int MipCount, int Format, int Flags, byte[] Payload );

	public static BoxTexturedReference Open( string checkoutRoot )
	{
		var root = Path.Combine( Path.GetFullPath( checkoutRoot ), "Temp/gltf-reference/glTF-Sample-Assets/Models/BoxTextured" );
		var reference = new BoxTexturedReference();
		foreach ( var entry in new[]
		{
			("glTF/BoxTextured.gltf", "1e9003a4a2a8822ff60da529357bd8e4dec4a59b1a479017993e7e2ad5fcebef"),
			("glTF-Embedded/BoxTextured.gltf", "879feb174dbf48e9e0cb22d2402ab35ff8519e13f0c5a100676788712dc3c8c7"),
			("glTF-Binary/BoxTextured.glb", "b510eca2e2ef33f62f9ed57d6e7ce2d10ebb2bdebc4a8e59d347719ba81abdf4"),
			("glTF/BoxTextured0.bin", "2e8c0483fa6665c686ec345f89dcbb2a694a587442584d09f8a83a59633327bc"),
			("glTF/CesiumLogoFlat.png", PngSha256)
		} )
		{
			var path = Path.Combine( root, entry.Item1 ); var info = new FileInfo( path );
			Require( info.Length > 0 && info.Length < 64 * 1024, "Missing or oversized pinned input: " + entry.Item1 );
			var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == entry.Item2, "Pinned input hash changed: " + entry.Item1 );
			reference.sourceFiles.Add( entry.Item1, bytes );
		}
		using var manifest = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkoutRoot, "research/gltf-validation/corpus-case-manifest.json" ) ) );
		Require( manifest.RootElement.GetProperty( "corpus" ).GetProperty( "commit" ).GetString() == CorpusRevision, "Pinned corpus manifest revision changed." );
		var metadata = File.ReadAllText( Path.Combine( root, "metadata.json" ) );
		Require( metadata.Contains( "CC-BY-4.0", StringComparison.Ordinal ) && metadata.Contains( "LicenseRef-LegalMark-Cesium", StringComparison.Ordinal ), "Model/license attribution metadata must be retained." );
		reference.PngBytes = reference.sourceFiles["glTF/CesiumLogoFlat.png"];
		reference.Rgba = DecodePinnedPalettePng( reference.PngBytes );
		return reference;
	}

	public Form[] Forms()
	{
		var result = new List<Form>();
		foreach ( var variant in new[] { "glTF", "glTF-Embedded", "glTF-Binary" } )
		{
			var file = variant + "/BoxTextured." + (variant == "glTF-Binary" ? "glb" : "gltf"); var bytes = sourceFiles[file];
			byte[] Resolve( string uri ) => uri switch
			{
				"BoxTextured0.bin" => sourceFiles["glTF/BoxTextured0.bin"],
				"CesiumLogoFlat.png" => sourceFiles["glTF/CesiumLogoFlat.png"],
				_ => throw new InvalidOperationException( "Unexpected model dependency: " + uri )
			};
			var independent = ReadIndependent( bytes, Resolve );
			result.Add( new Form { Name = variant + "/source", Bytes = bytes, Independent = independent, Resolve = Resolve } );
			result.Add( new Form { Name = variant + "/compiled_glb", Compiled = true, Bytes = GltfCompiler.Compile( bytes, Resolve ), Independent = independent, Resolve = Resolve } );
		}
		return result.ToArray();
	}

	private static IndependentPrimitive ReadIndependent( byte[] source, Func<string, byte[]> resolve )
	{
		byte[] json = source, binary = null;
		if ( source.Length >= 12 && BinaryPrimitives.ReadUInt32LittleEndian( source ) == 0x46546c67 )
		{
			Require( BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( 8 ) ) == source.Length, "Bad pinned GLB frame." );
			for ( var offset = 12; offset < source.Length; )
			{
				var count = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset ) )); var type = BinaryPrimitives.ReadUInt32LittleEndian( source.AsSpan( offset + 4 ) );
				Require( count >= 0 && count <= source.Length - offset - 8, "Bad pinned GLB chunk extent." );
				if ( type == 0x4e4f534a ) json = source.AsSpan( offset + 8, count ).ToArray();
				if ( type == 0x004e4942 ) binary = source.AsSpan( offset + 8, count ).ToArray();
				offset += 8 + count;
			}
		}
		using var document = JsonDocument.Parse( json ); var root = document.RootElement;
		var buffers = root.GetProperty( "buffers" ).EnumerateArray().Select( buffer =>
		{
			if ( !buffer.TryGetProperty( "uri", out var uriValue ) ) return binary ?? throw new InvalidOperationException( "Missing GLB BIN." );
			var uri = uriValue.GetString(); return uri.StartsWith( "data:", StringComparison.Ordinal ) ? Convert.FromBase64String( uri[(uri.IndexOf( ',' ) + 1)..] ) : resolve( uri );
		} ).ToArray();
		var primitive = root.GetProperty( "meshes" )[0].GetProperty( "primitives" )[0]; var attributes = primitive.GetProperty( "attributes" );
		float[] Attribute( string semantic, int components )
		{
			var accessor = root.GetProperty( "accessors" )[attributes.GetProperty( semantic ).GetInt32()]; var view = root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
			Require( accessor.GetProperty( "componentType" ).GetInt32() == 5126 && accessor.GetProperty( "count" ).GetInt32() == 24, "Pinned source attribute format changed." );
			var data = buffers[view.GetProperty( "buffer" ).GetInt32()]; var offset = OptionalInt( view, "byteOffset" ) + OptionalInt( accessor, "byteOffset" ); var stride = OptionalInt( view, "byteStride", components * 4 ); var values = new float[24 * components];
			for ( var vertex = 0; vertex < 24; vertex++ ) for ( var component = 0; component < components; component++ )
				values[vertex * components + component] = BinaryPrimitives.ReadSingleLittleEndian( data.AsSpan( offset + vertex * stride + component * 4, 4 ) );
			return values;
		}
		var positions = Attribute( "POSITION", 3 ); var normals = Attribute( "NORMAL", 3 ); var uvs = Attribute( "TEXCOORD_0", 2 );
		var indexAccessor = root.GetProperty( "accessors" )[primitive.GetProperty( "indices" ).GetInt32()]; var indexView = root.GetProperty( "bufferViews" )[indexAccessor.GetProperty( "bufferView" ).GetInt32()];
		Require( indexAccessor.GetProperty( "componentType" ).GetInt32() == 5123 && indexAccessor.GetProperty( "count" ).GetInt32() == 36, "Pinned source index format changed." );
		var indexData = buffers[indexView.GetProperty( "buffer" ).GetInt32()]; var indexOffset = OptionalInt( indexView, "byteOffset" ) + OptionalInt( indexAccessor, "byteOffset" );
		return new IndependentPrimitive( Enumerable.Range( 0, 24 ).Select( i => new NVector3( positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2] ) ).ToArray(),
			Enumerable.Range( 0, 24 ).Select( i => new NVector3( normals[i * 3], normals[i * 3 + 1], normals[i * 3 + 2] ) ).ToArray(),
			Enumerable.Range( 0, 24 ).Select( i => new NVector2( uvs[i * 2], uvs[i * 2 + 1] ) ).ToArray(),
			Enumerable.Range( 0, 36 ).Select( i => (uint)BinaryPrimitives.ReadUInt16LittleEndian( indexData.AsSpan( indexOffset + i * 2, 2 ) ) ).ToArray() );
	}

	public static NVector3 EnginePosition( NVector3 gltfWorld ) => new( gltfWorld.Z / .0254f, gltfWorld.X / .0254f, gltfWorld.Y / .0254f );
	public static NVector3 EngineDirection( NVector3 gltfWorld ) => new( gltfWorld.Z, gltfWorld.X, gltfWorld.Y );
	public static NVector3 WorldPosition( NVector3 local ) => EnginePosition( new( local.X, local.Z, -local.Y ) );
	public static NVector3 WorldNormal( NVector3 local ) => new( -local.Y, local.X, local.Z );

	/// <summary>Top-left image origin, repeat addressing and nearest base-level lookup.</summary>
	public NVector4 SampleNearestRepeat( double u, double v )
	{
		Require( double.IsFinite( u ) && double.IsFinite( v ), "Nonfinite reference UV." );
		var x = Math.Min( 255, (int)Math.Floor( (u - Math.Floor( u )) * 256 ) ); var y = Math.Min( 255, (int)Math.Floor( (v - Math.Floor( v )) * 256 ) );
		return Texel( x, y );
	}

	/// <summary>Linear-light RGB + linear alpha for magnification (no LOD assumption).</summary>
	public NVector4 SampleBilinearLinearRepeat( double u, double v )
	{
		Require( double.IsFinite( u ) && double.IsFinite( v ), "Nonfinite reference UV." );
		var x = (u - Math.Floor( u )) * 256 - .5; var y = (v - Math.Floor( v )) * 256 - .5;
		var ix = (int)Math.Floor( x ); var iy = (int)Math.Floor( y ); var tx = x - ix; var ty = y - iy;
		var output = new double[4];
		for ( var dy = 0; dy <= 1; dy++ ) for ( var dx = 0; dx <= 1; dx++ )
		{
			var color = Texel( (ix + dx + 256) % 256, (iy + dy + 256) % 256 ); var weight = (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty);
			for ( var channel = 0; channel < 4; channel++ ) output[channel] += (channel < 3 ? Linear( color[channel] ) : color[channel]) * weight;
		}
		return new( (float)output[0], (float)output[1], (float)output[2], (float)output[3] );
	}

	public NVector4 Texel( int x, int y )
	{
		Require( (uint)x < 256 && (uint)y < 256, "Reference texel out of range." ); var offset = (y * 256 + x) * 4;
		return new( Rgba[offset] / 255f, Rgba[offset + 1] / 255f, Rgba[offset + 2] / 255f, Rgba[offset + 3] / 255f );
	}

	public byte[] BuildMipChain( bool srgb )
	{
		var levels = new List<byte[]> { (byte[])Rgba.Clone() }; var size = 256;
		while ( size > 1 ) { levels.Add( ReducePowerOfTwo( levels[^1], size, srgb ) ); size /= 2; }
		return levels.AsEnumerable().Reverse().SelectMany( x => x ).ToArray();
	}

	private static byte[] ReducePowerOfTwo( byte[] source, int size, bool srgb )
	{
		var half = size / 2; var target = new byte[half * half * 4];
		for ( var y = 0; y < half; y++ ) for ( var x = 0; x < half; x++ ) for ( var channel = 0; channel < 4; channel++ )
		{
			double total = 0;
			foreach ( var sourcePixel in new[] { (y * 2 * size + x * 2), (y * 2 * size + x * 2 + 1), ((y * 2 + 1) * size + x * 2), ((y * 2 + 1) * size + x * 2 + 1) } )
			{
				var value = source[sourcePixel * 4 + channel] / 255.0; total += srgb && channel < 3 ? Linear( value ) : value;
			}
			var mean = total / 4; if ( srgb && channel < 3 ) mean = Encoded( mean );
			target[(y * half + x) * 4 + channel] = checked((byte)Math.Round( mean * 255, MidpointRounding.ToEven ));
		}
		return target;
	}
	private static double Linear( double value ) => value <= .04045 ? value / 12.92 : Math.Pow( (value + .055) / 1.055, 2.4 );
	private static double Encoded( double value ) => value <= .0031308 ? value * 12.92 : 1.055 * Math.Pow( value, 1 / 2.4 ) - .055;

	public int TestWrongOffsetSensitivity()
	{
		var bytes = sourceFiles["glTF/BoxTextured0.bin"]; var wrong = new NVector3( BinaryPrimitives.ReadSingleLittleEndian( bytes ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( 4 ) ), BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( 8 ) ) );
		var primitive = ReadIndependent( sourceFiles["glTF/BoxTextured.gltf"], uri => sourceFiles["glTF/" + uri] );
		Require( NVector3.Distance( wrong, primitive.Positions[0] ) > .5f, "Independent fixture does not detect POSITION accessor offset288 being ignored." ); return 1;
	}
	public int TestImageOrientationSensitivity()
	{
		var top = Texel( 128, 48 ); var bottom = Texel( 128, 208 );
		Require( top.Z > top.Y && top.Y > top.X && bottom.Y > bottom.X && bottom.X > bottom.Z && NVector4.Distance( top, bottom ) > .3f, "Logo is not a useful vertical-origin negative control." );
		var dot = Texel( 167, 72 ); Require( dot.X == 1 && dot.Y == 1 && dot.Z == 1 && dot.W == 1, "Independent PNG decoder lost the asymmetric white upper-right marker." ); return 2;
	}
	public int TestRepeatAddressing()
	{
		var color = SampleNearestRepeat( 128.5 / 256, 48.5 / 256 );
		Require( color == SampleNearestRepeat( 128.5 / 256 + 5, 48.5 / 256 - 3 ), "Repeat must preserve integer UV tile shifts." );
		Require( color != Texel( 255, 48 ), "Fixture must detect clamp substituted for repeated UV tiles." ); return 2;
	}
	public int TestMipColorSpaceSensitivity()
	{
		byte[] pixels = [0,0,0,255, 255,255,255,255, 0,0,0,255, 255,255,255,255];
		var linear = ReducePowerOfTwo( pixels, 2, true ); var encoded = ReducePowerOfTwo( pixels, 2, false );
		Require( linear.AsSpan().SequenceEqual( new byte[] { 188,188,188,255 } ) && encoded.AsSpan().SequenceEqual( new byte[] { 128,128,128,255 } ), "Mip reference fails the independent black/white transfer landmark." ); return 2;
	}

	public static NativeTexturePayload ParseNativeTextureResource( byte[] resource )
	{
		Require( resource.Length >= 28 && BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( 4 ) ) == 12, "Bad native texture resource header." );
		var payloadOffset = checked((int)BinaryPrimitives.ReadUInt32LittleEndian( resource )); var array = checked(8 + BinaryPrimitives.ReadInt32LittleEndian( resource.AsSpan( 8 ) )); var count = BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan( 12 ) );
		Require( payloadOffset <= resource.Length && count is > 0 and <= 32 && array >= 16 && (long)array + count * 12L <= payloadOffset, "Bad native resource blocks." );
		for ( var index = 0; index < count; index++ )
		{
			var entry = checked(array + (int)index * 12); if ( BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan( entry ) ) != 0x41544144 ) continue;
			var data = checked(entry + 4 + BinaryPrimitives.ReadInt32LittleEndian( resource.AsSpan( entry + 4 ) )); var length = BinaryPrimitives.ReadUInt32LittleEndian( resource.AsSpan( entry + 8 ) );
			Require( data >= 0 && length >= 32 && (long)data + length <= payloadOffset, "Bad texture DATA extent." );
			return new( BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 20 ) ), BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 22 ) ), resource[data + 27], resource[data + 26], BinaryPrimitives.ReadUInt16LittleEndian( resource.AsSpan( data + 2 ) ), resource[payloadOffset..] );
		}
		throw new InvalidOperationException( "Texture has no DATA block." );
	}

	private static byte[] DecodePinnedPalettePng( byte[] png )
	{
		var image = PngTexelReference.Decode( png );
		Require( image.Width == 256 && image.Height == 256 && image.ColorType == 3, "Reference accepts only the audited256x256 palette source logo." );
		return image.Rgba;
	}
	private static int OptionalInt( JsonElement value, string name, int otherwise = 0 ) => value.TryGetProperty( name, out var field ) ? field.GetInt32() : otherwise;
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
