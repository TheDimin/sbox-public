using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

internal static class StreamMeasurements
{
	static readonly Meter Meter = new( "GtaV.AssetStreaming" );
	internal static readonly Histogram<double> Archive = Meter.CreateHistogram<double>( "archive.read", "ms" );
	static readonly Histogram<double> Upload = Meter.CreateHistogram<double>( "texture.fromdds", "ms" );

	internal static readonly Histogram<double> Work = Meter.CreateHistogram<double>( "placement.work", "ms" );

	internal static void TextureStage( string name, double ms ) => Work.Record( ms, new System.Collections.Generic.KeyValuePair<string, object>( "stage", name ) );

	internal static void RecordArchiveRead( int kind, uint file, uint member, string path, long start, long acquired = 0 )
	{
		var tags = new TagList { { "kind", ((Kind)kind).ToString() }, { "path", path ?? $"file/{file}/member/{member}" },
			{ "frame", (long)Application.FrameCount }, { "waitMs", acquired == 0 ? 0d : Stopwatch.GetElapsedTime( start, acquired ).TotalMilliseconds } };
		Archive.Record( Stopwatch.GetElapsedTime( start ).TotalMilliseconds, tags );
	}

	internal readonly struct Stage( string name ) : System.IDisposable
	{
		readonly long start = Work.Enabled ? Stopwatch.GetTimestamp() : 0;
		public void Dispose()
		{
			if ( start != 0 ) Work.Record( Stopwatch.GetElapsedTime( start ).TotalMilliseconds,
				new System.Collections.Generic.KeyValuePair<string, object>( "stage", name ) );
		}
	}

	internal static Texture FromDds( byte[] bytes, string path )
	{
		bytes = ExpandAlphaDds( bytes );
		if ( !Upload.Enabled ) return TextureLoader.FromDds( bytes );
		long start = Stopwatch.GetTimestamp();
		try { return TextureLoader.FromDds( bytes ); }
		finally
		{
			var tags = new TagList { { "path", path }, { "frame", (long)Application.FrameCount } };
			Upload.Record( Stopwatch.GetElapsedTime( start ).TotalMilliseconds, tags );
		}
	}

	// Preserve the A8 mask as (0, 0, 0, alpha) through upload,
	// including the authored mip chain, instead of exposing the mask as red.
	static byte[] ExpandAlphaDds( byte[] bytes )
	{
		const int headerSize = 128;
		if ( bytes.Length <= headerSize || ReadWord( bytes, 0 ) != 0x20534444 ||
			ReadWord( bytes, 4 ) != 124 || ReadWord( bytes, 76 ) != 32 ||
			ReadWord( bytes, 80 ) != 2 || ReadWord( bytes, 88 ) != 8 || ReadWord( bytes, 104 ) != 255 ||
			ReadWord( bytes, 112 ) != 0 ) return bytes;

		int width = ReadWord( bytes, 16 ), height = ReadWord( bytes, 12 );
		int levels = Math.Max( 1, ReadWord( bytes, 28 ) );
		if ( width is <= 0 or > 32768 || height is <= 0 or > 32768 || levels > 16 ) return bytes;
		long pixels = 0;
		for ( int level = 0; level < levels; level++ )
			pixels += (long)Math.Max( 1, width >> level ) * Math.Max( 1, height >> level );
		if ( pixels != bytes.Length - headerSize || pixels > (int.MaxValue - headerSize) / 4 ) return bytes;

		var rgba = new byte[headerSize + (int)pixels * 4];
		bytes.AsSpan( 0, headerSize ).CopyTo( rgba );
		WriteWord( rgba, 20, width * 4 );
		WriteWord( rgba, 80, 0x41 ); // RGB and alpha.
		WriteWord( rgba, 88, 32 );
		WriteWord( rgba, 92, 0x000000ff );
		WriteWord( rgba, 96, 0x0000ff00 );
		WriteWord( rgba, 100, 0x00ff0000 );
		WriteWord( rgba, 104, unchecked((int)0xff000000) );
		for ( int i = headerSize, dst = headerSize + 3; i < bytes.Length; i++, dst += 4 )
			rgba[dst] = bytes[i];
		return rgba;
	}

	static int ReadWord( byte[] bytes, int offset ) => BinaryPrimitives.ReadInt32LittleEndian( bytes.AsSpan( offset ) );
	static void WriteWord( byte[] bytes, int offset, int value ) => BinaryPrimitives.WriteInt32LittleEndian( bytes.AsSpan( offset ), value );
}
