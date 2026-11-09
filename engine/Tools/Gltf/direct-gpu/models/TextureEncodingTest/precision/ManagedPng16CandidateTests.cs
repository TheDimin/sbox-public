using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

internal static class ManagedPng16CandidateTests
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
			RunCase( name, () => { var image = ManagedPng16Candidate.Decode( png, 10000000 ); Require( image.Width == 9 && image.Height == 7 && image.Rgba.AsSpan().SequenceEqual( expected ), "literal samples differ" ); } );
		}
		var sample = Convert.FromBase64String( document.RootElement[0].GetProperty( "png" ).GetString() );
		RunCase( "crc_rejected", () => { var corrupt = (byte[])sample.Clone(); corrupt[corrupt.Length - 13] ^= 1; Reject( () => ManagedPng16Candidate.Decode( corrupt, 10000000 ) ); } );
		RunCase( "truncation_rejected", () => Reject( () => ManagedPng16Candidate.Decode( sample[..^4], 10000000 ) ) );
		RunCase( "working_set_budget_rejected", () => Reject( () => ManagedPng16Candidate.Decode( sample, 1 ) ) );
		RunCase( "trailing_bytes_rejected", () => Reject( () => ManagedPng16Candidate.Decode( sample.Concat( new byte[] { 0 } ).ToArray(), 10000000 ) ) );
		return rows;
		void RunCase( string name, Action action ) { try { action(); rows.Add( new( name, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, false, e.ToString() ) ); } }
	}
	private static void Reject( Action action ) { try { action(); } catch ( GltfException ) { return; } throw new Exception( "Invalid input unexpectedly accepted." ); }
	private static void Require( bool condition, string message ) { if ( !condition ) throw new Exception( message ); }
}
