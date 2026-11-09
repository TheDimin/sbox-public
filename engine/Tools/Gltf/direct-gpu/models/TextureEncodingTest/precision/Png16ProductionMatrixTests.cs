using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

internal static class Png16ProductionMatrixTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static List<Result> Run( string checkout )
	{
		using var document = JsonDocument.Parse( File.ReadAllText( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/TextureEncodingTest/precision/managed-fixtures.json" ) ) );
		var rows = new List<Result>();
		foreach ( var fixture in document.RootElement.EnumerateArray() )
		{
			var name = fixture.GetProperty( "name" ).GetString(); var png = Convert.FromBase64String( fixture.GetProperty( "png" ).GetString() );
			var expected = fixture.GetProperty( "expected" ).EnumerateArray().Select( x => x.GetUInt16() ).ToArray();
			RunCase( name, () => { var image = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = png }, false, 10000000 ) ); Require( image.Width == 9 && image.Height == 7 && image.Format == 14 && image.Payload.Length == expected.Length * 4, "resource shape" ); for ( var i = 0; i < expected.Length; i++ ) Require( Math.Abs( System.Buffers.Binary.BinaryPrimitives.ReadSingleLittleEndian( image.Payload.AsSpan( i * 4 ) ) - expected[i] / 65535.0 ) < .0000001, "literal sample " + i ); } );
		}
		var sample = Convert.FromBase64String( document.RootElement[0].GetProperty( "png" ).GetString() );
		RunCase( "crc_rejected", () => { var corrupt = (byte[])sample.Clone(); corrupt[corrupt.Length - 13] ^= 1; Reject( () => global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = corrupt }, false, 10000000 ) ); } );
		RunCase( "truncation_rejected", () => Reject( () => global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = sample[..^4] }, false, 10000000 ) ) );
		RunCase( "working_set_budget_rejected", () => Reject( () => global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = sample }, false, 1 ) ) );
		RunCase( "trailing_bytes_rejected", () => Reject( () => global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { MimeType = "image/png", Bytes = sample.Concat( new byte[] { 0 } ).ToArray() }, false, 10000000 ) ) );
		return rows;
		void RunCase( string name, Action action ) { try { action(); rows.Add( new( name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); } }
	}
	private static void Reject( Action action ) { try { action(); } catch ( GltfException ) { return; } throw new Exception( "Invalid input unexpectedly accepted." ); }
	private static void Require( bool condition, string message ) { if ( !condition ) throw new Exception( message ); }
}
