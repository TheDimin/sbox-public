using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.BoxTexturedValidation;

using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

/// <summary>
/// Model-specific assertions written before a direct GPU draw. The coordinator
/// runs these inside s&box against the loaded producer, never copied reader code.
/// The reference decodes this pinned sample independently, including palette PNG.
/// These are data/CPU resource checks, not rendered material acceptance.
/// </summary>
public static class BoxTexturedCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );

	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		var reference = BoxTexturedReference.Open( checkoutRoot );
		Run( "independent_oracle_detects_wrong_offset", "reference", () => reference.TestWrongOffsetSensitivity() );
		Run( "independent_palette_image_has_asymmetric_up_and_down", "reference", () => reference.TestImageOrientationSensitivity() );
		Run( "repeat_addressing_preserves_integer_uv_tiles", "reference", () => reference.TestRepeatAddressing() );
		Run( "linear_light_mip_oracle_detects_encoded_space_averaging", "reference", () => reference.TestMipColorSpaceSensitivity() );
		Run( "independent_rgb_png_row_filters", "reference", PngTexelReference.TestRgbRowFilters );
		Run( "independent_rgba_png_row_filters", "reference", PngTexelReference.TestRgbaRowFilters );
		Run( "independent_png_crc_rejects_changed_bytes", "reference", PngTexelReference.TestCrcSensitivity );
		Run( "independent_bilinear_linear_light_and_clamp", "reference", PngTexelReference.TestBilinearAndClamp );
		foreach ( var form in reference.Forms() )
		{
			Run( "actual_source_descriptors_and_shared_view", form.Name, () => CheckDescriptors( form ) );
			Run( "all_positions_normals_uvs_and_indices", form.Name, () => CheckRawValues( form ) );
			Run( "authored_node_transform_and_engine_bounds", form.Name, () => CheckWorld( form ) );
			Run( "material_texture_sampler_and_image_source", form.Name, () => CheckMaterial( form, reference ) );
			Run( "stock_color_resource_matches_independent_palette_png_and_mips", form.Name, () => CheckNativeColorResource( form, reference ) );
			Run( "portable_package_dependencies_and_embedded_image_identity", form.Name, () => CheckPackaging( form, reference ) );
			Run( "owned_upload_survives_document_disposal", form.Name, () => CheckLifetime( form ) );
		}
		return rows.ToArray();

		void Run( string name, string form, Func<int> body )
		{
			try { rows.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}

	private static int CheckDescriptors( BoxTexturedReference.Form form )
	{
		using var document = form.Read();
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var position = upload.Attributes["POSITION"]; var normal = upload.Attributes["NORMAL"]; var uv = upload.Attributes["TEXCOORD_0"];
		Require( upload.Attributes.Count == 3 && upload.Layout.VertexCount == 24 && upload.Layout.SourceMode == 4, "Pinned primitive shape changed." );
		Require( ReferenceEquals( position.Storage, normal.Storage ), "POSITION and NORMAL must share the authored view." );
		Require( position.ByteOffset == 288 && normal.ByteOffset == 0 && position.ByteStride == 12 && normal.ByteStride == 12, "Accessor offsets or per-view stride were ignored." );
		Require( uv.ByteOffset == 0 && uv.ByteStride == 8 && !ReferenceEquals( uv.Storage, position.Storage ), "UV view offset/stride changed." );
		foreach ( var stream in upload.Attributes.Values ) Require( stream.ComponentType == 5126 && !stream.Normalized && stream.Count == 24, "Authored FLOAT stream format changed." );
		Require( position.Components == 3 && normal.Components == 3 && uv.Components == 2, "Attribute dimensions changed." );
		Require( upload.Indices.ComponentType == 5123 && upload.Indices.Count == 36 && upload.Indices.ByteStride == 2, "Source indices must remain U16." );
		Require( upload.Indices.Binding.ViewByteOffset == 768 && upload.Indices.ByteOffset == 0, "Buffer-view origin was applied as an accessor-relative offset." );
		return 10;
	}

	private static int CheckRawValues( BoxTexturedReference.Form form )
	{
		using var document = form.Read();
		var upload = document.MaterializePrimitiveUpload( 0, 0 );
		var expected = form.Independent;
		for ( var vertex = 0; vertex < 24; vertex++ )
		{
			Equal( expected.Positions[vertex], Read3( upload.Attributes["POSITION"], vertex ), 0, "raw position " + vertex );
			Equal( expected.Normals[vertex], Read3( upload.Attributes["NORMAL"], vertex ), 0, "raw normal " + vertex );
			var uv = Read2( upload.Attributes["TEXCOORD_0"], vertex );
			Require( uv.X == expected.Uvs[vertex].X && uv.Y == expected.Uvs[vertex].Y, "Source UV values, including tile indices and authored near-one floats, changed." );
		}
		var indices = upload.Indices;
		for ( var index = 0; index < 36; index++ )
			Require( BinaryPrimitives.ReadUInt16LittleEndian( indices.Storage.Bytes.Span.Slice( indices.ByteOffset + index * indices.ByteStride, 2 ) ) == expected.Indices[index], "Source triangle order changed." );
		return 24 * 3 + 36;
	}

	private static int CheckWorld( BoxTexturedReference.Form form )
	{
		using var document = form.Read(); var pose = document.EvaluatePose();
		var minimum = new NVector3( float.PositiveInfinity ); var maximum = new NVector3( float.NegativeInfinity );
		for ( var vertex = 0; vertex < 24; vertex++ )
		{
			var source = form.Independent.Positions[vertex];
			// Literal COLLADA2GLTF root rotation: source (x,y,z) -> (x,z,-y).
			var expectedWorld = new NVector3( source.X, source.Z, -source.Y );
			Equal( expectedWorld, document.EvaluateVertexPosition( 1, 0, vertex, pose ), 0, "authored root matrix " + vertex );
			var engine = BoxTexturedReference.EnginePosition( expectedWorld );
			minimum = NVector3.Min( minimum, engine ); maximum = NVector3.Max( maximum, engine );
		}
		var half = .5f / .0254f;
		Equal( new( -half ), minimum, 0, "expected minimum in inches" ); Equal( new( half ), maximum, 0, "expected maximum in inches" );
		Require( pose.GetMorphWeights( 1 ).Length == 0, "Static model acquired morph state." );
		return 27;
	}

	private static int CheckMaterial( BoxTexturedReference.Form form, BoxTexturedReference reference )
	{
		using var document = form.Read(); var material = document.ReadMaterial( 0 );
		Require( material.BaseColorFactor == NVector4.One && material.MetallicFactor == 0 && material.RoughnessFactor == 1, "glTF default factors changed." );
		Require( material.AlphaMode == "OPAQUE" && !material.DoubleSided && !material.Unlit, "Core material rendering mode changed." );
		Require( material.BaseColorTexture.Index == 0 && material.BaseColorTexture.TexCoord == 0 && material.BaseColorTexture.IsSrgb, "Base color binding must use TEXCOORD_0 and sRGB." );
		var texture = document.ReadTexture( 0 ); Require( texture.Image == 0 && texture.Sampler == 0, "Texture source/sampler link changed." );
		var sampler = document.ReadSampler( 0 );
		Require( sampler.MinFilter == 9986 && sampler.MagFilter == 9729 && sampler.WrapS == 10497 && sampler.WrapT == 10497, "Sample is NEAREST_MIPMAP_LINEAR minification, LINEAR magnification, REPEAT S/T." );
		var mapped = global::Sandbox.GltfSamplerMapping.Map( sampler, 9987, 9729 );
		Require( Convert.ToInt32( mapped.State.Filter ) == 5 && !mapped.RequiresBaseMipOnly, "Independent min/mag/mip filter descriptor was conflated." );
		Require( mapped.State.AddressModeU == global::Sandbox.Rendering.TextureAddressMode.Wrap && mapped.State.AddressModeV == global::Sandbox.Rendering.TextureAddressMode.Wrap, "Repeat sampler descriptor was lost." );
		var image = document.ReadImage( 0 );
		Require( image.MimeType == "image/png" && image.Bytes.AsSpan().SequenceEqual( reference.PngBytes ), "Encoded source PNG changed through image URI/bufferView loading." );
		return 8;
	}

	private static int CheckNativeColorResource( BoxTexturedReference.Form form, BoxTexturedReference reference )
	{
		using var document = form.Read();
		var prepared = global::Sandbox.GltfStockMaterialAdapter.Prepare( document, document.ReadMaterial( 0 ) );
		Require( prepared.BaseColorResource is { Length: > 0 }, "Stock preparation lost the base color texture." );
		var parsed = BoxTexturedReference.ParseNativeTextureResource( prepared.BaseColorResource );
		Require( parsed.Width == 256 && parsed.Height == 256 && parsed.MipCount == 9 && parsed.Format == 4, "Expected full RGBA8888 mip chain." );
		Require( (parsed.Flags & 0x0200) == 0 && (parsed.Flags & 0x0008) == 0, "sRGB color texture was forced to linear or base mip only." );
		var expected = reference.BuildMipChain( srgb: true );
		Require( parsed.Payload.AsSpan().SequenceEqual( expected ), "Native resource pixels/mips differ from independent palette PNG + linear-light mip reference." );
		Require( prepared.MetallicFactor == 0 && prepared.RoughnessFactor == 1 && prepared.ColorTint.x == 1 && prepared.ColorTint.y == 1 && prepared.ColorTint.z == 1, "Stock factors changed." );
		return 5;
	}

	private static int CheckPackaging( BoxTexturedReference.Form form, BoxTexturedReference reference )
	{
		var resolved = new List<string>();
		using var document = GltfReader.Read( form.Bytes, uri => { resolved.Add( uri ); return form.Resolve( uri ); } );
		_ = document.MaterializePrimitiveUpload( 0, 0 );
		Require( document.ReadImage( 0 ).Bytes.AsSpan().SequenceEqual( reference.PngBytes ), "Packaged image bytes were not preserved." );
		if ( form.Compiled )
		{
			Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Compiled result is not valid GLB framing." );
			Require( !resolved.Contains( "BoxTextured0.bin" ), "Compiled geometry still depends on the external source buffer." );
		}
		// Portable packages may contain explicitly packaged relative textures;
		// standalone export is a later feature. Embedded inputs specifically must
		// remain free of external image I/O. No arbitrary/network URI is accepted.
		var externalImage = form.Name.StartsWith( "glTF/", StringComparison.Ordinal );
		Require( resolved.All( uri => uri is "BoxTextured0.bin" or "CesiumLogoFlat.png" ), "Unexpected or nonportable packaged dependency." );
		Require( resolved.Contains( "CesiumLogoFlat.png" ) == externalImage, "Embedded/external texture ownership changed unexpectedly." );
		if ( externalImage )
		{
			using var missing = GltfReader.Read( form.Bytes, uri => uri == "CesiumLogoFlat.png" ? null : form.Resolve( uri ) );
			try { _ = missing.ReadImage( 0 ); }
			catch ( GltfException error ) when ( error.Message.Contains( "CesiumLogoFlat.png", StringComparison.Ordinal ) ) { return 5; }
			throw new InvalidOperationException( "Missing required packaged texture did not identify its relative dependency." );
		}
		Require( resolved.Count == 0, "Embedded/Binary source or compiled form unexpectedly used external resources." );
		return 5;
	}

	private static int CheckLifetime( BoxTexturedReference.Form form )
	{
		GltfPrimitiveUploadData upload;
		using ( var document = form.Read() ) upload = document.MaterializePrimitiveUpload( 0, 0 );
		for ( var vertex = 0; vertex < 24; vertex++ ) Equal( form.Independent.Positions[vertex], Read3( upload.Attributes["POSITION"], vertex ), 0, "owned upload after disposal" );
		Require( ReferenceEquals( upload.Attributes["POSITION"].Storage, upload.Attributes["NORMAL"].Storage ), "Shared storage identity changed after disposal." );
		return 25;
	}

	internal static NVector3 Read3( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride); var bytes = stream.Storage.Bytes.Span;
		return new( BinaryPrimitives.ReadSingleLittleEndian( bytes[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 4)..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 8)..] ) );
	}
	private static System.Numerics.Vector2 Read2( GltfUploadStream stream, int vertex )
	{
		var offset = checked(stream.ByteOffset + vertex * stream.ByteStride); var bytes = stream.Storage.Bytes.Span;
		return new( BinaryPrimitives.ReadSingleLittleEndian( bytes[offset..] ), BinaryPrimitives.ReadSingleLittleEndian( bytes[(offset + 4)..] ) );
	}
	internal static void Equal( NVector3 expected, NVector3 actual, float tolerance, string label )
	{
		Require( float.IsFinite( actual.X ) && float.IsFinite( actual.Y ) && float.IsFinite( actual.Z ) && NVector3.Abs( expected - actual ).X <= tolerance && NVector3.Abs( expected - actual ).Y <= tolerance && NVector3.Abs( expected - actual ).Z <= tolerance,
			$"{label}: expected {expected}, actual {actual}, tolerance {tolerance:R}." );
	}
	internal static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
