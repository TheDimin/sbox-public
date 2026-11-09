using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using static BoxVertexColorsReference;

/// <summary>Model and adversarial source/compiled data assertions; no GPU allocation.</summary>
public static class BoxVertexColorsCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public const int ExpectedCases = 64;
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new BoxVertexColorsReference( checkoutRoot ); var rows = new List<Result>();
		Run( "source_color_position_alias_is_explicit_and_fixture_breaks_alias", "oracle", () =>
		{
			Require( reference.Forms.Where( form => !form.Derived ).All( form => form.RawColors.Select( value => new NVector3( value.X, value.Y, value.Z ) ).SequenceEqual( reference.Positions ) ), "Pinned source correlation changed." );
			Require( reference.Forms.Where( form => form.Derived ).All( form => !form.RawColors.Select( value => new NVector3( value.X, value.Y, value.Z ) ).SequenceEqual( reference.Positions ) ), "Fixtures must detect reading POSITION as COLOR_0." ); return 2;
		} );
		Run( "linear_product_including_alpha_has_no_gamma_decode", "oracle", () =>
		{
			var color = new NVector4( .5f, .25f, .75f, .5f ); var factor = new NVector4( .5f, .75f, .25f, .5f ); var texture = new NVector4( .25f, .5f, .75f, .5f );
			Require( color * factor * texture == new NVector4( .0625f, .09375f, .140625f, .125f ), "Linear product oracle changed." );
			Require( MathF.Abs( MathF.Pow( (.5f + .055f) / 1.055f, 2.4f ) * .5f * .25f - .0625f ) > .03f, "Fractional fixture must detect sRGB-decode of linear COLOR_0." ); return 2;
		} );
		Run( "color_zero_clamps_and_color_one_preserves_extended_range", "oracle", () =>
		{
			var clamp = reference.Forms.Single( form => form.Name == "derived/float4_clamp/source" );
			Require( clamp.RawColors[0] == new NVector4( -.25f, .5f, 1.25f, .25f ) && clamp.DrawColor( 0 ) == new NVector4( 0, .5f, 1, .25f ), "COLOR_0 clamp fixture changed." );
			Require( clamp.Color1 == new NVector4( -.25f, .5f, 1.25f, .75f ), "Additional color must preserve its authored float range." ); return 2;
		} );
		Run( "rgb_fixture_requires_alpha_one", "oracle", () => { Require( reference.Forms.Where( form => form.ColorComponents == 3 ).All( form => form.RawColors.All( value => value.W == 1 ) ), "RGB alpha oracle is not one." ); return 1; } );
		foreach ( var form in reference.Forms )
		{
			Run( "source_color_representation_offsets_and_owned_bytes", form.Name, () => CheckStreams( reference, form ) );
			Run( "runtime_output_layout_preserves_all_color_sets", form.Name, () => CheckPlan( form ) );
			Run( "portable_compiled_form_and_material_defaults", form.Name, () => CheckPackaging( form ) );
		}
		Require( rows.Count == ExpectedCases, "Frozen test inventory changed." ); return rows.ToArray();
		void Run( string name, string form, Func<int> test ) { try { rows.Add( new( name, form, test(), true, null ) ); } catch ( Exception error ) { rows.Add( new( name, form, 0, false, error.ToString() ) ); } }
	}

	private static int CheckStreams( BoxVertexColorsReference reference, Form form )
	{
		GltfPrimitiveUploadData upload;
		using ( var document = form.Read() ) upload = document.MaterializePrimitiveUpload( 0, 0 );
		var p = upload.Attributes["POSITION"]; var n = upload.Attributes["NORMAL"]; var c = upload.Attributes["COLOR_0"];
		Require( upload.Layout.VertexCount == 24 && upload.Layout.SourceMode == 4 && upload.Indices.ComponentType == 5123 && upload.Indices.Count == 36, "Pinned shape/topology format changed." );
		Require( p.ByteOffset == 0 && n.ByteOffset == 288 && p.ByteStride == 12 && n.ByteStride == 12 && ReferenceEquals( p.Storage, n.Storage ), "Position/normal source view sharing or offsets were lost." );
		Require( c.ComponentType == form.ColorComponentType && c.Components == form.ColorComponents && c.ByteStride == form.ColorStride && c.Normalized == (form.ColorComponentType != 5126) && !c.IsMaterialized, "Color source representation was assumed/repacked." );
		Require( c.ByteOffset == (form.Derived ? 4 : 576), "Color accessor-relative offset changed." );
		if ( !form.Derived ) Require( ReferenceEquals( p.Storage, c.Storage ) && c.Storage.Bytes.Length == 864, "Source color must share its 864-byte view." );
		for ( var vertex = 0; vertex < 24; vertex++ )
		{
			Equal( new NVector4( reference.Positions[vertex], 0 ), Read( p, vertex ), 0, "source position" );
			Equal( new NVector4( reference.Normals[vertex], 0 ), Read( n, vertex ), 0, "source normal" );
			Equal( form.RawColors[vertex], Read( c, vertex, rgbAlphaOne: true ), 0, "owned raw linear color after document disposal" );
			if ( form.Color1 is { } extra ) Equal( extra, Read( upload.Attributes["COLOR_1"], vertex ), 0, "unclamped source COLOR_1" );
		}
		for ( var index = 0; index < 36; index++ ) Require( BinaryPrimitives.ReadUInt16LittleEndian( upload.Indices.Storage.Bytes.Span.Slice( upload.Indices.ByteOffset + index * 2, 2 ) ) == reference.Indices[index], "Literal triangle order/winding changed." );
		return 5 + 24 * (form.Derived ? 4 : 3) + 36;
	}

	private static int CheckPlan( Form form )
	{
		using var document = form.Read(); var plan = GltfGpuPrimitivePlan.Create( document, 0, 0 ); var colors = plan.Fields.Where( field => field.Semantic == "COLOR" ).OrderBy( field => field.SemanticIndex ).ToArray();
		Require( colors.Length == (form.Color1.HasValue ? 2 : 1) && colors.Select( field => field.SemanticIndex ).SequenceEqual( Enumerable.Range( 0, colors.Length ) ), "Dynamic output declaration lost/renumbered color sets." );
		Require( colors.All( field => field.Components == 4 && field.ComponentType == 5126 ), "Color conversion must explicitly target FLOAT4." );
		var ordered = plan.Fields.OrderBy( field => field.Offset ).ToArray();
		Require( ordered.All( field => field.Offset >= 0 && field.Offset % 4 == 0 && field.Offset + field.Components * 4 <= plan.VertexStride ), "Runtime output range/alignment invalid." );
		for ( var index = 1; index < ordered.Length; index++ ) Require( ordered[index - 1].Offset + ordered[index - 1].Components * 4 <= ordered[index].Offset, "Runtime output fields overlap." );
		foreach ( var pair in plan.Sources )
		{
			var stream = pair.Value.Stream; var start = pair.Value.ArenaByteOffset - stream.ByteOffset;
			Require( start >= 0 && start % 4 == 0 && plan.InputBytes.AsSpan( start, stream.Storage.Bytes.Length ).SequenceEqual( stream.Storage.Bytes.Span ), "Immutable GPU arena did not preserve exact source storage bytes." );
		}
		Require( !plan.GenerateFlatNormals && plan.SourceVertices.SequenceEqual( Enumerable.Range( 0, 24 ).Select( value => (uint)value ) ), "Authored normal/source vertex identity lost." ); return 6 + plan.Sources.Count;
	}

	private static int CheckPackaging( Form form )
	{
		using var document = form.Read(); var primitive = document.ReadPrimitive( 0, 0 );
		Require( primitive.Material == (form.Derived ? 0 : null) && document.GetDefaultScene() == 0 && primitive.Attributes.Count == (form.Derived ? 4 : 3), "Implicit source material or fixture color sets changed." );
		if ( form.Derived ) Require( document.ReadMaterial( 0 ).BaseColorFactor == new NVector4( .5f, .25f, .75f, .5f ), "Fixture linear material factor/alpha changed." );
		if ( form.Compiled )
		{
			Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Compiled result is not a valid GLB frame." );
			using var standalone = GltfReader.Read( form.Bytes, uri => throw new InvalidOperationException( "Portable compiled cube requested " + uri ) );
			Require( standalone.MaterializePrimitiveUpload( 0, 0 ).Attributes.Count == (form.Derived ? 4 : 3), "Compiled cube needs external source files or lost color attributes." );
		}
		return form.Compiled ? 4 : 2;
	}

	internal static NVector4 Read( GltfUploadStream stream, int vertex, bool rgbAlphaOne = false )
	{
		var bytes = stream.Storage.Bytes.Span; var offset = stream.ByteOffset + vertex * stream.ByteStride; var value = NVector4.Zero;
		for ( var channel = 0; channel < stream.Components; channel++ ) value[channel] = stream.ComponentType switch
		{
			5126 => BinaryPrimitives.ReadSingleLittleEndian( bytes.Slice( offset + channel * 4, 4 ) ),
			5121 => stream.Normalized ? bytes[offset + channel] / 255f : bytes[offset + channel],
			5123 => stream.Normalized ? BinaryPrimitives.ReadUInt16LittleEndian( bytes.Slice( offset + channel * 2, 2 ) ) / 65535f : BinaryPrimitives.ReadUInt16LittleEndian( bytes.Slice( offset + channel * 2, 2 ) ),
			_ => throw new InvalidOperationException( "Unexpected fixture type." )
		};
		if ( rgbAlphaOne && stream.Components == 3 ) value.W = 1; return value;
	}
	internal static void Equal( NVector4 expected, NVector4 actual, float tolerance, string label )
	{
		for ( var component = 0; component < 4; component++ ) Require( float.IsFinite( actual[component] ) && MathF.Abs( expected[component] - actual[component] ) <= tolerance, $"{label} component {component}: expected {expected[component]:R}, actual {actual[component]:R}." );
	}
}
