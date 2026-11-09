using System;
using System.Collections.Generic;
using NativeTextureReference = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

internal static class CoreImageMatrixTests
{
	private const string Gray = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAAAAADRSSBWAAAAAnRSTlMAERwj7coAAAALSURBVHicYxA8CQAA7gDbELsBwQAAAABJRU5ErkJggg==";
	private const string Interlaced = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAYAAAEFsT2yAAAAG0lEQVR4nGPgEpHTYDCycQtgiErJq2jqmbYAACG/BVFubJ0+AAAAAElFTkSuQmCC";
	private const string Progressive = "/9j/4AAQSkZJRgABAQAAAQABAAD/2wBDAAIBAQEBAQIBAQECAgICAgQDAgICAgUEBAMEBgUGBgYFBgYGBwkIBgcJBwYGCAsICQoKCgoKBggLDAsKDAkKCgr/2wBDAQICAgICAgUDAwUKBwYHCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgr/wgARCAAEAAQDAREAAhEBAxEB/8QAFAABAAAAAAAAAAAAAAAAAAAABv/EABQBAQAAAAAAAAAAAAAAAAAAAAf/2gAMAwEAAhADEAAAASLUQ//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAQUCf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Bf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Bf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEABj8Cf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8hf//aAAwDAQACAAMAAAAQH//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQMBAT8Qf//EABQRAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQIBAT8Qf//EABQQAQAAAAAAAAAAAAAAAAAAAAD/2gAIAQEAAT8Qf//Z";
	public sealed record Result( string Name, bool Passed, string Error );
	public static List<Result> Run()
	{
		var rows = new List<Result>();
		Run( "gray8_trns_preserves_straight_color", () => CheckPng( Gray, 2, 1, [17,17,17,0, 201,201,201,255] ) );
		Run( "adam7_rgba8_top_left_and_straight_alpha", () => CheckPng( Interlaced, 2, 2, [10,20,30,40, 50,60,70,80, 90,100,110,120, 130,140,150,160] ) );
		Run( "progressive_jpeg_known_uniform_color", CheckJpeg );
		return rows;
		void Run( string name, Action body ) { try { body(); rows.Add( new( name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); } }
	}
	private static void CheckPng( string fixture, int width, int height, byte[] expected )
	{
		var actual = NativeTextureReference.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = Convert.FromBase64String( fixture ) }, false ) );
		Require( actual.Width == width && actual.Height == height && actual.Format == 4 && actual.Payload.AsSpan().SequenceEqual( expected ), "PNG literal pixels/dimensions" );
	}
	private static void CheckJpeg()
	{
		var actual = NativeTextureReference.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/jpeg", Bytes = Convert.FromBase64String( Progressive ) }, true ) );
		Require( actual.Width == 4 && actual.Height == 4 && actual.Format == 4 && actual.Payload.Length == 64, "JPEG dimensions/format" );
		for ( var i = 0; i < 64; i += 4 ) Require( Math.Abs( actual.Payload[i] - 40 ) <= 3 && Math.Abs( actual.Payload[i + 1] - 120 ) <= 3 && Math.Abs( actual.Payload[i + 2] - 200 ) <= 3 && actual.Payload[i + 3] == 255, "progressive JPEG color" );
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new Exception( message ); }
}
