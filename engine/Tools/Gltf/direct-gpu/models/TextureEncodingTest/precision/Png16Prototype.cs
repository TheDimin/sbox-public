using System;
using System.Runtime.InteropServices;
using Sandbox.Resources;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

/// <summary>Research-only linear data prototype. Does not replace any production source.</summary>
internal static class Png16Prototype
{
	internal sealed record Packed( byte[] NormalRoughness, byte[] Metalness );
	public static byte[] Compile( byte[] png, bool mipmaps = false )
	{
		var image = ManagedPng16Candidate.Decode( png, 256L * 1024 * 1024 );
		var values = new float[image.Rgba.Length];
		for ( int i = 0; i < values.Length; i++ ) values[i] = image.Rgba[i] / 65535f;
		return Serialize( values, image.Width, image.Height, mipmaps );
	}
	public static Packed CompileMetallicRoughness( byte[] png, float metallic, float roughness, bool mipmaps )
	{
		var image = ManagedPng16Candidate.Decode( png, 256L * 1024 * 1024 );
		var values = new float[image.Rgba.Length];
		for ( int i = 0; i < values.Length; i += 4 ) { values[i] = values[i + 1] = 1.00392163f * .5f; values[i + 2] = image.Rgba[i + 1] / 65535f * roughness; values[i + 3] = 1; }
		var r = Serialize( values, image.Width, image.Height, mipmaps );
		Array.Clear( values );
		for ( int i = 0; i < values.Length; i += 4 ) { values[i + 1] = image.Rgba[i + 2] / 65535f * metallic; values[i + 3] = 1; }
		return new( r, Serialize( values, image.Width, image.Height, mipmaps ) );
	}
	private static byte[] Serialize( float[] values, int width, int height, bool mipmaps )
	{
		int length = 0, count = 0;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) ) { length = checked(length + w * h * 16); count++; if ( !mipmaps || w == 1 && h == 1 ) break; }
		var payload = new byte[length]; int offset = length;
		for ( int w = width, h = height; ; w = Math.Max( 1, w / 2 ), h = Math.Max( 1, h / 2 ) )
		{
			var bytes = MemoryMarshal.AsBytes( values.AsSpan() ); offset -= bytes.Length; bytes.CopyTo( payload.AsSpan( offset ) );
			if ( !mipmaps || w == 1 && h == 1 ) break;
			int nw = Math.Max( 1, w / 2 ), nh = Math.Max( 1, h / 2 ); var next = new float[nw * nh * 4];
			for ( int y = 0; y < nh; y++ ) for ( int x = 0; x < nw; x++ ) for ( int c = 0; c < 4; c++ )
			{
				double total = 0; int n = 0;
				for ( int sy = y * h / nh; sy < (y + 1) * h / nh; sy++ ) for ( int sx = x * w / nw; sx < (x + 1) * w / nw; sx++ ) { total += values[(sy * w + sx) * 4 + c]; n++; }
				next[(y * nw + x) * 4 + c] = (float)(total / n);
			}
			values = next;
		}
		var writer = new VTexWriter(); writer.Header.Width = (ushort)width; writer.Header.Height = (ushort)height; writer.Header.Depth = 1;
		writer.Header.MipCount = (byte)count; writer.Header.Format = VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA32323232F;
		writer.Header.Flags = VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE | (mipmaps ? VTexWriter.VTEX_Flags_t.NONE : VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD);
		var resource = new ResourceWriter { ResourceVersion = 1 }; resource.SetDataBlock( writer.GetData() ); resource.SetStreamingData( payload ); return resource.ToArray();
	}
}
