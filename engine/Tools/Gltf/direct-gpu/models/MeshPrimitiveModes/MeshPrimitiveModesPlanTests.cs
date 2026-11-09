using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using static MeshPrimitiveModesReference;

/// <summary>84 staging-plan cases. CPU metadata is never described as executed GPU output.</summary>
public static class MeshPrimitiveModesPlanTests
{
	public static MeshPrimitiveModesCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<MeshPrimitiveModesCoreTests.Result>(); var inputs = new Inputs( checkoutRoot );
		foreach ( var form in inputs.Forms ) for ( var mode = 0; mode < 7; mode++ )
		{
			try { Check( form, mode ); rows.Add( new( ModeNames[mode], form.Name, true, null ) ); }
			catch ( Exception error ) { rows.Add( new( ModeNames[mode], form.Name, false, error.ToString() ) ); }
		}
		return rows.ToArray();
	}
	private static void Check( Form form, int mode )
	{
		using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, mode, 0 );
		var upload = document.MaterializePrimitiveUpload( mode, 0 ); var position = upload.Attributes["POSITION"];
		Require( plan.Mode == DrawMode( mode ) && plan.GenerateFlatNormals == (mode >= 4) && plan.Material is null, "Staging draw type, flat-normal applicability or material changed." );
		Require( plan.Sources.Count == 1 && plan.Sources.ContainsKey( "POSITION" ), "Unexpected authored input attributes." );
		var binding = plan.Sources["POSITION"];
		Require( binding.Stream.ComponentType == 5126 && binding.Stream.Components == 3 && !binding.Stream.Normalized &&
			binding.Stream.ByteStride == position.ByteStride && binding.Stream.ByteOffset == position.ByteOffset && binding.ArenaByteOffset == position.ByteOffset,
			"The arena descriptor must retain actual source width, accessor offset and stride." );
		Require( plan.InputBytes.AsSpan( 0, position.Storage.Bytes.Length ).SequenceEqual( position.Storage.Bytes.Span ) && plan.InputBytes.Length == (position.Storage.Bytes.Length + 3) / 4 * 4,
			"Arena packing changed source bytes/poisoned padding or omitted aligned load tail." );
		Require( plan.Fields.Count == 5 && plan.Fields.Select( x => (x.Semantic, x.SemanticIndex) ).Distinct().Count() == 5, "Output declaration must be complete and unambiguous." );
		foreach ( var (semantic, count) in new[] { ("POSITION", 3), ("NORMAL", 3), ("TANGENT", 4), ("COLOR", 4), ("TEXCOORD", 2) } )
		{
			var field = plan.Fields.Single( x => x.Semantic == semantic && x.SemanticIndex == 0 );
			Require( field.Components == count && field.ComponentType == 5126 && field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + count * 4 <= plan.VertexStride,
				"Runtime output declaration has an invalid field: " + semantic );
		}
		var occupied = plan.Fields.SelectMany( x => Enumerable.Range( x.Offset, x.Components * 4 ) ).ToArray();
		Require( occupied.Distinct().Count() == occupied.Length && plan.VertexStride >= occupied.Length, "Output fields overlap." );
		var expectedCount = mode >= 4 ? DrawHexagonIndices[mode].Length : form.LocalVertexCount( mode );
		Require( plan.SourceVertices.Length == expectedCount && plan.Indices.Length == DrawHexagonIndices[mode].Length, "Flat corner or list index count changed." );
		if ( mode < 4 ) Require( plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, expectedCount ).Select( x => (uint)x ) ), "Points/lines must keep source vertices; no synthetic triangles/quads." );
		for ( var index = 0; index < plan.Indices.Length; index++ )
		{
			var output = plan.Indices[index]; Require( output < plan.SourceVertices.Length, "Index exceeds output buffer." );
			var source = plan.SourceVertices[output]; Require( source < form.LocalVertexCount( mode ), "Source mapping exceeds source accessor." );
			Require( form.HexagonVertex( mode, source ) == DrawHexagonIndices[mode][index], "Expanded oriented topology differs from literal geometry." );
		}
	}
}
