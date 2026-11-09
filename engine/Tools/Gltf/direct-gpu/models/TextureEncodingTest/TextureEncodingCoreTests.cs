using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Native = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.BoxTexturedReference;
using Png = Sandbox.Gltf.DirectGpu.BoxTexturedValidation.PngTexelReference;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using static TextureEncodingReference;

/// <summary>Independent CPU/serialized-resource assertions, not rendered color fidelity.</summary>
public static class TextureEncodingCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>(); var reference = Open( checkoutRoot );
		Run( "literal_texels_and_metadata_are_independent", "reference", () => CheckOracle( reference ) );
		Run( "rgb_component_alpha_and_top_left_fixture", "reference", () => CheckFixture( 2 ) );
		Run( "rgba_straight_alpha_and_top_left_fixture", "reference", () => CheckFixture( 6 ) );
		Run( "transfer_and_alpha_mip_negative_control", "reference", CheckMipControl );
		foreach ( var form in reference.Forms() )
		{
			Run( "authored_material_roles_and_linear_factors", form.Name, () => CheckRoles( form ) );
			Run( "all_image_bytes_and_portable_dependency_closure", form.Name, () => CheckDependencies( form, reference ) );
			Run( "six_pngs_ignore_gamma_and_icc_in_both_transfer_roles", form.Name, () => CheckColorResources( form ) );
			Run( "base_color_factor_matches_literal_srgb_sample", form.Name, () => CheckBaseColor( form ) );
			Run( "metallic_roughness_remains_linear_and_ignores_metadata", form.Name, () => CheckMetallicRoughness( form ) );
			Run( "palette4_label_origin_alpha_and_pixels", form.Name, () => CheckLabels( form ) );
		}
		return rows.ToArray();

		void Run( string name, string form, Func<int> body )
		{
			try { rows.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}

	private static int CheckOracle( TextureEncodingReference reference )
	{
		for ( var image = 0; image < 6; image++ )
		{
			var decoded = Png.Decode( reference.Images[image] ); Require( decoded.Width == 1 && decoded.Height == 1 && decoded.ColorType == 2 && decoded.Rgba.AsSpan().SequenceEqual( LiteralTexel( image ) ), "Pinned source is not the literal RGB texel." );
			var chunks = Chunks( reference.Images[image] );
			Require( chunks.Any( x => x.Type == "gAMA" ) == (image % 3 == 1) && chunks.Any( x => x.Type == "iCCP" ) == (image % 3 == 2), "Metadata variant lost its sensitivity." );
			if ( image % 3 == 1 ) Require( BinaryPrimitives.ReadUInt32BigEndian( chunks.Single( x => x.Type == "gAMA" ).Bytes ) == 20000, "Gamma control is not 0.2." );
		}
		Require( Math.Abs( Linear136 - .24620132670783548 ) < 1e-15 && Math.Abs( Linear136 - 136.0 / 255 ) > .28, "Independent literal equation must distinguish sRGB and linear interpretation." );
		return 15;
	}

	private static int CheckFixture( int colorType )
	{
		var png = StraightAlphaFixture( colorType, out var expected ); var decoded = Png.Decode( png ); Require( decoded.Rgba.AsSpan().SequenceEqual( expected ), "Independent fixture writer/decoder disagrees with literal top-left rows." );
		foreach ( var srgb in new[] { false, true } )
		{
			var actual = Native.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( new GltfImage { Bytes = png, MimeType = "image/png" }, srgb ) );
			Require( actual.Width == 2 && actual.Height == 2 && actual.Format == 4 && actual.MipCount == 1, "Literal fixture native header changed." );
			Require( actual.Payload.AsSpan().SequenceEqual( expected ), "Decode changed stored RGB, component expansion, straight alpha or top-left row order." );
			Require( ((actual.Flags & 0x0200) == 0) == srgb, "Image metadata must not override per-role API transfer." );
		}
		return 7;
	}

	private static int CheckMipControl()
	{
		byte[] source = [0, 0, 0, 0, 255, 255, 255, 255];
		Require( global::Sandbox.GltfTextureCompiler.Downsample( source, 2, 1, true ).AsSpan().SequenceEqual( new byte[] { 188, 188, 188, 128 } ), "sRGB RGB must be decoded before averaging, with linear alpha." );
		Require( global::Sandbox.GltfTextureCompiler.Downsample( source, 2, 1, false ).AsSpan().SequenceEqual( new byte[] { 128, 128, 128, 128 } ), "Linear data channels must not receive sRGB transfer." ); return 2;
	}

	private static int CheckRoles( Form form )
	{
		using var doc = GltfReader.Read( form.Bytes, form.Resolve ); Require( doc.Root.GetProperty( "materials" ).GetArrayLength() == 14 && doc.Root.GetProperty( "images" ).GetArrayLength() == 8, "Pinned source shape changed." );
		for ( var column = 0; column < 4; column++ )
		{
			var color = doc.ReadMaterial( column ); var emissive = doc.ReadMaterial( column + 4 ); var surface = doc.ReadMaterial( column + 8 );
			Require( !color.Unlit && color.AlphaMode == "OPAQUE" && !color.DoubleSided, "Color sphere mode changed." );
			if ( column == 0 )
			{
				Require( color.BaseColorTexture is null && color.BaseColorFactor == new NVector4( 0, (float)Linear136, 0, 1 ), "JSON base factor must remain linear." );
				Require( emissive.EmissiveTexture is null && emissive.EmissiveFactor == new NVector3( 0, (float)Linear136, 0 ), "JSON emissive factor must remain linear." );
				Require( surface.MetallicRoughnessTexture is null && surface.MetallicFactor == 1 && surface.RoughnessFactor == (float)(136.0 / 255), "JSON MR factors must remain linear." );
			}
			else
			{
				Require( color.BaseColorTexture is { IsSrgb: true } && color.BaseColorTexture.Index == column - 1 && color.BaseColorFactor == NVector4.One, "Base texture role must use sRGB RGB." );
				Require( emissive.EmissiveTexture is { IsSrgb: true } && emissive.EmissiveTexture.Index == column - 1 && emissive.EmissiveFactor == NVector3.One, "Emissive texture role must use sRGB RGB." );
				Require( surface.MetallicRoughnessTexture is { IsSrgb: false } && surface.MetallicRoughnessTexture.Index == column + 2 && surface.MetallicFactor == 1 && surface.RoughnessFactor == 1, "MR texture must remain linear data." );
			}
		}
		for ( var index = 12; index < 14; index++ ) { var label = doc.ReadMaterial( index ); Require( label.AlphaMode == "MASK" && label.AlphaCutoff == .5f && label.DoubleSided && label.BaseColorTexture.IsSrgb && label.EmissiveTexture.IsSrgb, "Label alpha coverage/material roles changed." ); }
		return 19;
	}

	private static int CheckDependencies( Form form, TextureEncodingReference reference )
	{
		var resolved = new List<string>(); using var doc = GltfReader.Read( form.Bytes, uri => { resolved.Add( uri ); return form.Resolve( uri ); } );
		for ( var image = 0; image < 8; image++ ) { var actual = doc.ReadImage( image ); Require( actual.MimeType == "image/png" && actual.Bytes.AsSpan().SequenceEqual( reference.Images[image] ), "Actual source/portable dependency lost encoded image identity." ); }
		if ( form.Embedded ) Require( resolved.Count == 0, "Embedded input unexpectedly resolved any sidecar." );
		else if ( form.Portable ) Require( resolved.Count == 8 && resolved.All( x => x.StartsWith( "gltf-images/", StringComparison.Ordinal ) && x.EndsWith( ".png", StringComparison.Ordinal ) ), "Portable dependency closure still references the source tree or geometry buffers." );
		else Require( resolved.Count == 10 && resolved.Count( x => x is "Plane.bin" or "Sphere.bin" ) == 2 && ImageNames.All( resolved.Contains ), "Actual source dependency closure changed." );
		if ( !form.Embedded )
		{
			var missing = doc.Root.GetProperty( "images" )[2].GetProperty( "uri" ).GetString();
			using var broken = GltfReader.Read( form.Bytes, uri => uri == missing ? null : form.Resolve( uri ) );
			try { _ = broken.ReadImage( 2 ); } catch ( GltfException error ) when ( error.Message.Contains( missing, StringComparison.Ordinal ) ) { return 10; }
			throw new InvalidOperationException( "Missing image dependency was not reported with its actual package-local URI." );
		}
		return 9;
	}

	private static int CheckColorResources( Form form )
	{
		using var doc = GltfReader.Read( form.Bytes, form.Resolve );
		for ( var index = 0; index < 6; index++ ) foreach ( var srgb in new[] { true, false } )
		{
			var actual = Native.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( doc.ReadImage( index ), srgb ) );
			Require( actual.Width == 1 && actual.Height == 1 && actual.MipCount == 1 && actual.Format == 4, "Expected literal 1x1 RGBA resource." );
			Require( actual.Payload.AsSpan().SequenceEqual( LiteralTexel( index ) ), "Decoder applied gamma/ICC or changed RGB expansion." );
			Require( ((actual.Flags & 0x0200) == 0) == srgb, "Encoded image metadata overrode the material role." );
		}
		return 36;
	}

	private static int CheckBaseColor( Form form )
	{
		using var doc = GltfReader.Read( form.Bytes, form.Resolve );
		for ( var index = 0; index < 4; index++ )
		{
			var prepared = global::Sandbox.GltfStockMaterialAdapter.Prepare( doc, doc.ReadMaterial( index ) );
			if ( index == 0 ) Require( prepared.BaseColorResource is null && Math.Abs( prepared.ColorTint.y - 136f / 255 ) < 1e-6 && prepared.ColorTint.x == 0 && prepared.ColorTint.z == 0, "Linear authored factor must be encoded once for the stock tint." );
			else { var native = Native.ParseNativeTextureResource( prepared.BaseColorResource ); Require( native.Payload.AsSpan().SequenceEqual( LiteralTexel( index - 1 ) ) && (native.Flags & 0x0200) == 0 && prepared.ColorTint.y == 1, "Texture color must use sRGB API storage without factor double conversion." ); }
		}
		return 4;
	}

	private static int CheckMetallicRoughness( Form form )
	{
		using var doc = GltfReader.Read( form.Bytes, form.Resolve );
		for ( var index = 8; index < 12; index++ )
		{
			var prepared = global::Sandbox.GltfStockMaterialAdapter.Prepare( doc, doc.ReadMaterial( index ) ); var normal = Native.ParseNativeTextureResource( prepared.NormalRoughnessResource );
			Require( normal.Format == 14 && normal.MipCount == 1 && (normal.Flags & 0x0200) != 0, "Packed normal/roughness must use linear RGBA32F." );
			Require( Math.Abs( BinaryPrimitives.ReadSingleLittleEndian( normal.Payload.AsSpan( 8 ) ) - 136f / 255 ) < 1e-7, "Roughness G was decoded as color or multiplied incorrectly." );
			if ( index == 8 ) Require( prepared.MetalnessResource is null && prepared.MetallicFactor == 1, "Factor-only metalness changed." );
			else { var metal = Native.ParseNativeTextureResource( prepared.MetalnessResource ); Require( metal.Format == 14 && (metal.Flags & 0x0200) != 0 && BinaryPrimitives.ReadSingleLittleEndian( metal.Payload.AsSpan( 4 ) ) == 1, "Metallic B must remain linear one." ); }
		}
		return 12;
	}

	private static int CheckLabels( Form form )
	{
		using var doc = GltfReader.Read( form.Bytes, form.Resolve );
		for ( var image = 6; image < 8; image++ )
		{
			var source = doc.ReadImage( image ); var expected = DecodeImage( source.Bytes, image ); var native = Native.ParseNativeTextureResource( global::Sandbox.GltfTextureCompiler.Compile( source, true ) );
			Require( native.Width == 1024 && native.Height == 256 && native.Payload.AsSpan().SequenceEqual( expected ), "4-bit palette component expansion, transparent RGB or top-left row order changed." );
			Require( expected.Where( (x, i) => i % 4 == 3 ).Any( x => x == 0 ) && expected.Where( (x, i) => i % 4 == 3 ).Any( x => x == 255 ), "Labels must expose alpha interpretation." );
			Require( !expected.AsSpan( 0, 1024 * 128 * 4 ).SequenceEqual( expected.AsSpan( 1024 * 128 * 4 ) ), "Labels must detect vertical flip." );
		}
		return 6;
	}
}
