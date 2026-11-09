using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.TextureLinearValidation;

/// <summary>Independent pinned GLB accessor reader; no producer evaluator supplies expected data.</summary>
internal sealed class TextureLinearReference : IDisposable
{
	internal const string SourceHash = "5a7d79bb138f9f732002a988298b5ba44f614064330e94f6ea8c17e38b998440";
	internal readonly byte[] Source, Binary;
	private readonly JsonDocument json;
	internal JsonElement Root => json.RootElement;
	internal TextureLinearReference( string checkout )
	{
		Source = File.ReadAllBytes( Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureLinearInterpolationTest/glTF-Binary/TextureLinearInterpolationTest.glb" ) );
		Require( Hash( Source ) == SourceHash, "Pinned source changed." );
		Require( BitConverter.ToUInt32( Source, 0 ) == 0x46546c67 && BitConverter.ToUInt32( Source, 4 ) == 2 && BitConverter.ToUInt32( Source, 8 ) == Source.Length, "Invalid independent GLB header." );
		int length = BitConverter.ToInt32( Source, 12 ); Require( BitConverter.ToUInt32( Source, 16 ) == 0x4e4f534a, "Expected JSON chunk." );
		json = JsonDocument.Parse( Source.AsMemory( 20, length ) );
		int binary = 20 + length; Require( BitConverter.ToUInt32( Source, binary + 4 ) == 0x004e4942, "Expected BIN chunk." );
		Binary = Source.AsSpan( binary + 8, BitConverter.ToInt32( Source, binary ) ).ToArray();
	}
	internal float[] Float( int accessor, int vertex )
	{
		var a = Root.GetProperty( "accessors" )[accessor]; Require( a.GetProperty( "componentType" ).GetInt32() == 5126, "Oracle expects this source's float accessors." );
		int components = a.GetProperty( "type" ).GetString() switch { "VEC2" => 2, "VEC3" => 3, _ => throw new InvalidOperationException( "Unexpected source type." ) };
		int offset = Offset( a, vertex, components * 4 ); return Enumerable.Range( 0, components ).Select( c => BitConverter.ToSingle( Binary, offset + c * 4 ) ).ToArray();
	}
	internal uint Index( int accessor, int index )
	{
		var a = Root.GetProperty( "accessors" )[accessor]; Require( a.GetProperty( "componentType" ).GetInt32() == 5123, "Oracle expects this source's U16 indices." );
		return BitConverter.ToUInt16( Binary, Offset( a, index, 2 ) );
	}
	private int Offset( JsonElement a, int index, int packed )
	{
		Require( index >= 0 && index < a.GetProperty( "count" ).GetInt32(), "Independent accessor index out of range." );
		var view = Root.GetProperty( "bufferViews" )[a.GetProperty( "bufferView" ).GetInt32()];
		Require( view.GetProperty( "buffer" ).GetInt32() == 0, "GLB oracle expects its single BIN buffer." );
		return Optional( view, "byteOffset", 0 ) + Optional( a, "byteOffset", 0 ) + index * Optional( view, "byteStride", packed );
	}
	internal byte[] Image( int image )
	{
		var im = Root.GetProperty( "images" )[image]; var v = Root.GetProperty( "bufferViews" )[im.GetProperty( "bufferView" ).GetInt32()];
		return Binary.AsSpan( Optional( v, "byteOffset", 0 ), v.GetProperty( "byteLength" ).GetInt32() ).ToArray();
	}
	internal static int Optional( JsonElement e, string name, int fallback ) => e.TryGetProperty( name, out var p ) ? p.GetInt32() : fallback;
	internal static double Decode( double value ) => value <= .04045 ? value / 12.92 : Math.Pow( (value + .055) / 1.055, 2.4 );
	internal static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	internal static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
	public void Dispose() => json.Dispose();
}
