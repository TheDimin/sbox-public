using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using V2 = System.Numerics.Vector2;
using V4 = System.Numerics.Vector4;

/// <summary>Pinned source oracle. Geometry uses direct little-endian decoding, never the producer evaluator.</summary>
public sealed class TextureSettingsReference : IDisposable
{
	public const string Revision = "90d7ede14c7e280af263824604b427a1ca02cb66";
	public static readonly string[] Forms = { "glTF", "glTF-Binary", "glTF-Embedded" };
	public static readonly string[] ImageNames = { "CheckAndX_V.png", "CheckAndX.png", "TextureTestLabels.png" };
	public static readonly int[] WrapS = { 10497, 10497, 33071, 10497, 33648 };
	public static readonly int[] WrapT = { 10497, 33071, 10497, 33648, 10497 };
	public static readonly int[] TextureSamplers = { 3, 4, 2, 1, 1, 2, 0, 0, 0 };
	public static readonly int[] TextureImages = { 0, 1, 0, 0, 1, 1, 1, 1, 2 };
	public static readonly int[] MaterialTextures = { 0, 1, 2, 3, -1, 4, 5, 6, 7, 8 };
	private static readonly Dictionary<string, string> Pins = new()
	{
		["glTF/TextureSettingsTest.gltf"] = "dd1f85b4638ffc72f1e09e19b101b7a05d94a0353facced8f59c16a2d9c09447",
		["glTF/TextureSettingsTest0.bin"] = "1aa2e5e31ae08179eea355945b6fd5d84bd5ffd62a50b93a68c2368d9f345cbe",
		["glTF/CheckAndX.png"] = "19da9a7f987b9a64684500c1de49293c0922f1840f13ce5d38505b8b5fe1c001",
		["glTF/CheckAndX_V.png"] = "4f8ea98d173360c29275e04cac8d1d05004b1605d818e6f143aca8f31e4f302d",
		["glTF/TextureTestLabels.png"] = "14b29323950f10cacfbc4160ea8982aaac6bdea21cf4ec5e8c1589917266415a",
		["glTF-Binary/TextureSettingsTest.glb"] = "44013a0602cbbdcaf703905ed47cc6b6fb49f067d7de0e1a0a568a5bbfa9d769",
		["glTF-Embedded/TextureSettingsTest.gltf"] = "5b4ae629021a29be74ec8997ff029ee55b809ed716f21112e79894852451f94a"
	};
	private readonly Dictionary<string, byte[]> files = new();
	public JsonDocument Json { get; }
	public Png.Image[] Images { get; }
	public TextureSettingsReference( string checkoutRoot )
	{
		if ( string.IsNullOrWhiteSpace( checkoutRoot ) ) throw new ArgumentException( "Explicit checkout root required." );
		var modelRoot = Path.Combine( checkoutRoot, "Temp/gltf-reference/glTF-Sample-Assets/Models/TextureSettingsTest" );
		foreach ( var pin in Pins )
		{
			var bytes = File.ReadAllBytes( Path.Combine( modelRoot, pin.Key ) );
			Require( Convert.ToHexStringLower( SHA256.HashData( bytes ) ) == pin.Value, "Pinned source changed: " + pin.Key );
			files.Add( pin.Key, bytes );
		}
		Json = JsonDocument.Parse( files["glTF/TextureSettingsTest.gltf"] );
		Images = ImageNames.Select( x => Png.Decode( files["glTF/" + x] ) ).ToArray();
	}
	public byte[] Resolve( string uri ) => files.TryGetValue( "glTF/" + uri, out var bytes ) ? bytes : throw new InvalidOperationException( "Unexpected dependency: " + uri );
	public byte[] Source( string form ) => files[form + "/TextureSettingsTest." + (form == "glTF-Binary" ? "glb" : "gltf")];
	public GltfDocument Open( string form, bool compiled ) => GltfReader.Read( compiled ? GltfCompiler.Compile( Source( form ), Resolve ) : Source( form ), Resolve );
	public JsonElement Primitive( int mesh ) => Json.RootElement.GetProperty( "meshes" )[mesh].GetProperty( "primitives" )[0];
	public float[][] Attribute( int mesh, string semantic )
	{
		var a = Json.RootElement.GetProperty( "accessors" )[Primitive( mesh ).GetProperty( "attributes" ).GetProperty( semantic ).GetInt32()];
		var v = Json.RootElement.GetProperty( "bufferViews" )[a.GetProperty( "bufferView" ).GetInt32()];
		Require( a.GetProperty( "componentType" ).GetInt32() == 5126, "Reference only supports this sample's FLOAT attributes." );
		var components = semantic == "TEXCOORD_0" ? 2 : 3;
		var offset = v.GetProperty( "byteOffset" ).GetInt32() + a.GetProperty( "byteOffset" ).GetInt32();
		var stride = v.GetProperty( "byteStride" ).GetInt32();
		var bytes = files["glTF/TextureSettingsTest0.bin"];
		return Enumerable.Range( 0, a.GetProperty( "count" ).GetInt32() ).Select( row => Enumerable.Range( 0, components ).Select( c => BinaryPrimitives.ReadSingleLittleEndian( bytes.AsSpan( offset + row * stride + c * 4, 4 ) ) ).ToArray() ).ToArray();
	}
	public uint[] Indices( int mesh )
	{
		var a = Json.RootElement.GetProperty( "accessors" )[Primitive( mesh ).GetProperty( "indices" ).GetInt32()];
		var v = Json.RootElement.GetProperty( "bufferViews" )[a.GetProperty( "bufferView" ).GetInt32()];
		var offset = v.GetProperty( "byteOffset" ).GetInt32() + a.GetProperty( "byteOffset" ).GetInt32();
		return Enumerable.Range( 0, a.GetProperty( "count" ).GetInt32() ).Select( i => (uint)BinaryPrimitives.ReadUInt16LittleEndian( files["glTF/TextureSettingsTest0.bin"].AsSpan( offset + i * 2, 2 ) ) ).ToArray();
	}
	/// <summary>Interior points on the actual first two triangles of the test column.</summary>
	public IEnumerable<V2> TestColumnUvs( int mesh )
	{
		var uv = Attribute( mesh, "TEXCOORD_0" ); var indices = Indices( mesh );
		// The first source triangle belongs to the authored test quad. Its UVs
		// include negative T or S > 1; use strict interiors to avoid raster edges.
		for ( var a = 1; a < 12; a++ ) for ( var b = 1; b < 12 - a; b++ )
		{
			var x = uv[indices[0]]; var y = uv[indices[1]]; var z = uv[indices[2]];
			var wa = a / 12f; var wb = b / 12f; var wc = 1 - wa - wb;
			yield return new V2( x[0] * wa + y[0] * wb + z[0] * wc, x[1] * wa + y[1] * wb + z[1] * wc );
		}
	}
	/// <summary>Independent top-left, sRGB-to-linear base-mip sampling with per-axis wrap.</summary>
	public static V4 Sample( Png.Image image, double u, double v, int wrapS, int wrapT, bool linear )
	{
		var x = u * image.Width - (linear ? .5 : 0); var y = v * image.Height - (linear ? .5 : 0);
		var ix = (int)Math.Floor( x ); var iy = (int)Math.Floor( y );
		var tx = x - ix; var ty = y - iy; var result = new double[4];
		for ( var dy = 0; dy < (linear ? 2 : 1); dy++ ) for ( var dx = 0; dx < (linear ? 2 : 1); dx++ )
		{
			var offset = (Address( iy + dy, image.Height, wrapT ) * image.Width + Address( ix + dx, image.Width, wrapS )) * 4;
			var weight = linear ? (dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) : 1;
			for ( var c = 0; c < 4; c++ )
			{
				var encoded = image.Rgba[offset + c] / 255d;
				result[c] += weight * (c == 3 ? encoded : encoded <= .04045 ? encoded / 12.92 : Math.Pow( (encoded + .055) / 1.055, 2.4 ));
			}
		}
		return new( (float)result[0], (float)result[1], (float)result[2], (float)result[3] );
	}
	public static int Address( int texel, int size, int wrap )
	{
		if ( wrap == 33071 ) return Math.Clamp( texel, 0, size - 1 );
		var period = wrap == 33648 ? size * 2 : size;
		var t = ((texel % period) + period) % period;
		return wrap == 33648 && t >= size ? period - 1 - t : t;
	}
	public static void Require( bool pass, string message ) { if ( !pass ) throw new InvalidOperationException( message ); }
	public void Dispose() => Json.Dispose();
}
