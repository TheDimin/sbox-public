using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;

/// <summary>CPU/codec/data evidence only. No GPU, native Model or native Mesh is constructed.</summary>
public static class MeshoptCubeCoreTests
{
	public sealed record Result( string Name, string Form, bool Passed, string Error );
	public static Result[] RunAll( string checkoutRoot )
	{
		var reference = new MeshoptCubeReference( checkoutRoot ); var rows = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			Run( "pinned_shape_extensions_and_codec_matrix", form.Name, () => Inventory( form ) );
			Run( "all_109_accessors_match_independent_fallback", form.Name, () => Accessors( form, reference ) );
			Run( "all_35_primitive_layouts_owned_view_bytes_and_sharing", form.Name, () => Uploads( form, reference ) );
			Run( "independent_rest_and_slerp_world_geometry", form.Name, () => Poses( form, reference ) );
			Run( "ten_label_materials_png_identity_and_sampler", form.Name, () => Materials( form, reference ) );
			Run( "portable_package_has_no_external_geometry_dependency", form.Name, () => Packaging( form ) );
			Run( "owned_upload_survives_document_and_exact_budget", form.Name, () => Ownership( form, reference ) );
			if ( form.Variant == "glTF" )
				Run( "unsupported_optional_codec_uses_real_fallback", form.Name, () => { using var d = form.Read( false ); Require( d.UnsupportedOptionalExtensions.SequenceEqual( new[] { Compression } ), "Unsupported optional codec was hidden." ); VerifyAccessors( d, reference ); } );
			else
				Run( "unsupported_required_codec_rejected", form.Name, () => ExpectGltf( () => { using var d = form.Read( false ); }, Compression ) );
		}
		Run( "selected_codec_rejects_bad_bitstream_instead_of_fallback", "mutated optional source", () =>
		{
			var form = reference.Forms.First( f => f.Name == "glTF/source" ); var bad = (byte[])form.Resolve( "MeshoptCubeTest.bin" ).Clone();
			using var json = JsonDocument.Parse( form.Bytes ); var codec = json.RootElement.GetProperty( "bufferViews" ).EnumerateArray().First( v => v.TryGetProperty( "extensions", out _ ) ).GetProperty( "extensions" ).GetProperty( Compression );
			bad[codec.GetProperty( "byteOffset" ).GetInt32()] = 0xff;
			byte[] Resolve( string uri ) => uri == "MeshoptCubeTest.bin" ? bad : form.Resolve( uri );
			ExpectGltf( () => { using var d = GltfReader.Read( form.Bytes, Resolve, Selected ); _ = d.MaterializePrimitiveUpload( 15, 0 ); }, "header" );
			using var fallback = GltfReader.Read( form.Bytes, Resolve, QuantizationOnly ); VerifyAccessors( fallback, reference );
		} );
		Run( "independent_oracle_detects_ignored_interleaved_offset", "oracle sensitivity", () =>
		{
			var normal = reference.Value( 5, 0 ); var position = reference.Value( 4, 0 ); Require( !normal.SequenceEqual( position ), "Fixture cannot expose normal offset ignored as position." );
			Require( reference.Accessor( 5 ).GetProperty( "offset" ).GetInt32() == 12 && reference.Accessor( 6 ).GetProperty( "offset" ).GetInt32() == 16, "Expected interleaved offsets changed." );
		} );
		Run( "independent_oracle_detects_wrong_axis_phase_and_double_basis", "oracle sensitivity", () =>
		{
			var rest = reference.Engine( 14, 0, 0 ); var turn = reference.Engine( 14, 0, 1 ); Require( rest.Zip( turn, (a, b) => Math.Abs( a - b ) ).Max() > 10, "Rotation samples cannot detect a frozen or wrong-phase cube." );
			Require( Math.Abs( turn[2] - turn[1] ) > 1, "Placement fixture cannot detect duplicated basis permutation." );
		} );
		return rows.ToArray();
		void Run( string name, string form, Action body ) { try { body(); rows.Add( new( name, form, true, null ) ); } catch ( Exception e ) { rows.Add( new( name, form, false, e.ToString() ) ); } }
	}

	private static void Inventory( Form form )
	{
		using var d = form.Read(); var root = d.Root;
		Require( d.GetDefaultScene() == 0 && root.GetProperty( "nodes" ).GetArrayLength() == NodeCount && root.GetProperty( "meshes" ).GetArrayLength() == PrimitiveCount && root.GetProperty( "accessors" ).GetArrayLength() == AccessorCount, "Pinned scene inventory changed." );
		Require( root.GetProperty( "animations" ).GetArrayLength() == 1 && !root.TryGetProperty( "skins", out _ ) && d.UnsupportedOptionalExtensions.Count == 0, "Selected codec/animation inventory changed." );
		var required = root.GetProperty( "extensionsRequired" ).EnumerateArray().Select( x => x.GetString() ).ToArray();
		Require( required.SequenceEqual( form.Variant == "glTF" ? new[] { Quantization } : new[] { Quantization, Compression } ), "Optional fallback variant was conflated with required codec." );
		var codecs = root.GetProperty( "bufferViews" ).EnumerateArray().Where( x => x.TryGetProperty( "extensions", out _ ) ).Select( x => x.GetProperty( "extensions" ).GetProperty( Compression ) ).ToArray();
		Require( codecs.Length == 60 && codecs.Select( x => x.GetProperty( "mode" ).GetString() ).Distinct().Count() == 3, "All modes are required in this sample." );
		Require( codecs.Select( x => x.TryGetProperty( "filter", out var f ) ? f.GetString() : "NONE" ).Distinct().Order().SequenceEqual( new[] { "COLOR", "EXPONENTIAL", "NONE", "OCTAHEDRAL", "QUATERNION" } ), "Filter coverage changed." );
	}
	private static void Accessors( Form form, MeshoptCubeReference reference ) { using var d = form.Read(); VerifyAccessors( d, reference ); }
	private static void VerifyAccessors( GltfDocument d, MeshoptCubeReference reference )
	{
		for ( var index = 0; index < AccessorCount; index++ )
		{
			var expected = reference.Accessor( index ).GetProperty( "values" ).EnumerateArray().SelectMany( Numbers ).ToArray(); var actual = d.ReadFloatAccessor( index );
			Require( actual.Length == expected.Length, "Accessor length changed." ); for ( var i = 0; i < actual.Length; i++ ) Close( (float)expected[i], actual[i], 0, $"Accessor {index}/{i}" );
		}
	}
	internal static void Uploads( Form form, MeshoptCubeReference reference )
	{
		using var d = form.Read(); var vertices = 0; var indices = 0;
		for ( var mesh = 0; mesh < PrimitiveCount; mesh++ )
		{
			var upload = d.MaterializePrimitiveUpload( mesh, 0 ); vertices += upload.Layout.VertexCount; indices += upload.Indices.Count;
			var expectedPrimitive = reference.Primitive( mesh );
			Require( upload.Layout.SourceMode == 4 && !upload.Layout.RequiresTopologyExpansion && upload.Attributes.Count == expectedPrimitive.GetProperty( "attributes" ).EnumerateObject().Count(), "Primitive attribute count or topology changed." );
			foreach ( var (semantic, stream) in upload.Attributes.Append( new KeyValuePair<string, GltfUploadStream>( "INDICES", upload.Indices ) ) ) CheckStream( d, reference, mesh, semantic, stream );
			var names = upload.Attributes.Keys.ToArray();
			for ( var i = 0; i < names.Length; i++ ) for ( var j = i + 1; j < names.Length; j++ )
				Require( ReferenceEquals( upload.Attributes[names[i]].Storage, upload.Attributes[names[j]].Storage ) == (reference.Accessor( reference.AccessorFor( mesh, names[i] ) ).GetProperty( "view" ).GetInt32() == reference.Accessor( reference.AccessorFor( mesh, names[j] ) ).GetProperty( "view" ).GetInt32()), "Interleaved source storage sharing changed." );
		}
		Require( vertices == TotalVertices && indices == TotalIndices, "Full scene vertex/index totals changed." );
	}
	internal static void CheckStream( GltfDocument d, MeshoptCubeReference reference, int mesh, string semantic, GltfUploadStream stream )
	{
		var index = reference.AccessorFor( mesh, semantic ); var expected = reference.Accessor( index ); var viewIndex = expected.GetProperty( "view" ).GetInt32(); var view = reference.Oracle.GetProperty( "views" )[viewIndex];
		Require( stream.Binding.AccessorIndex == index && stream.Binding.SourceBufferView == viewIndex && stream.ByteOffset == expected.GetProperty( "offset" ).GetInt32() && stream.ByteStride == expected.GetProperty( "stride" ).GetInt32(), "Source accessor offset/stride/view was replaced: " + semantic );
		Require( stream.ComponentType == expected.GetProperty( "component_type" ).GetInt32() && stream.Components == expected.GetProperty( "components" ).GetInt32() && stream.Normalized == expected.GetProperty( "normalized" ).GetBoolean() && stream.Count == expected.GetProperty( "count" ).GetInt32(), "Source component representation changed: " + semantic );
		var authored = d.Root.GetProperty( "bufferViews" )[viewIndex]; var selectedCodec = authored.TryGetProperty( "extensions", out var ext ) && ext.TryGetProperty( Compression, out _ );
		Require( stream.IsMaterialized == selectedCodec && stream.Binding.ViewByteOffset == (authored.TryGetProperty( "byteOffset", out var offset ) ? offset.GetInt32() : 0), "Decoded-view materialization or source view origin changed." );
		Require( stream.Storage.Bytes.Length == view.GetProperty( "length" ).GetInt32() && Hash( stream.Storage.Bytes.Span ) == view.GetProperty( "sha256" ).GetString(), "Entire decoded view differs from independent real fallback: " + mesh + "/" + semantic );
	}
	private static void Poses( Form form, MeshoptCubeReference reference )
	{
		using var d = form.Read();
		foreach ( var time in PoseTimes )
		{
			var pose = d.EvaluatePose( 0, time );
			for ( var node = 0; node < NodeCount; node++ ) for ( var vertex = 0; vertex < reference.Accessor( reference.AccessorFor( node, "POSITION" ) ).GetProperty( "count" ).GetInt32(); vertex++ )
			{
				var expected = reference.World( node, vertex, time ); var actual = d.EvaluateVertexPosition( node, 0, vertex, pose );
				Close( expected[0], actual.X, 3e-6, "world X" ); Close( expected[1], actual.Y, 3e-6, "world Y" ); Close( expected[2], actual.Z, 3e-6, "world Z" );
			}
		}
	}
	private static void Materials( Form form, MeshoptCubeReference reference )
	{
		using var d = form.Read();
		for ( var i = 0; i < 10; i++ )
		{
			var material = d.ReadMaterial( i ); Require( material.BaseColorFactor == System.Numerics.Vector4.One && material.MetallicFactor == 0 && material.RoughnessFactor == 1 && material.AlphaMode == "MASK" && material.AlphaCutoff == .001f && material.DoubleSided && !material.Unlit, "Label source material factors changed." );
			Require( material.BaseColorTexture.Index == i && material.BaseColorTexture.TexCoord == 0 && material.BaseColorTexture.IsSrgb, "Label texture uses wrong image/UV/color space." );
			var texture = d.ReadTexture( i ); Require( texture.Image == i && texture.Sampler == i, "Distinct row/column labels were conflated." );
			var sampler = d.ReadSampler( i ); Require( sampler.MinFilter == 9729 && sampler.MagFilter == 9729 && sampler.WrapS == 33071 && sampler.WrapT == 33071, "Label requires linear filtering and clamp." );
			var image = d.ReadImage( i ); var uri = reference.Oracle.GetProperty( "images" )[i].GetProperty( "uri" ).GetString(); var expected = reference.Oracle.GetProperty( "forms" ).GetProperty( form.Variant ).GetProperty( "files" ).GetProperty( uri );
			Require( image.MimeType == "image/png" && Hash( image.Bytes ) == expected.GetProperty( "sha256" ).GetString(), "Encoded PNG source label identity changed." );
			Require( d.ReadPrimitive( i, 0 ).Material == i && d.ReadPrimitive( i + 10, 0 ).Material is null, "Textured label/default cube material binding changed." );
		}
	}
	private static void Packaging( Form form )
	{
		var resolved = new List<string>(); using var d = GltfReader.Read( form.Bytes, uri => { resolved.Add( uri ); return form.Resolve( uri ); }, Selected );
		for ( var mesh = 0; mesh < PrimitiveCount; mesh++ ) _ = d.MaterializePrimitiveUpload( mesh, 0 ); for ( var i = 0; i < 10; i++ ) _ = d.ReadImage( i );
		if ( !form.Compiled ) return;
		Require( BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes ) == 0x46546c67 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 4 ) ) == 2 && BinaryPrimitives.ReadUInt32LittleEndian( form.Bytes.AsSpan( 8 ) ) == form.Bytes.Length, "Portable compiled form is not a valid GLB container." );
		Require( !resolved.Any( x => x.EndsWith( ".bin", StringComparison.Ordinal ) ), "Compiled form still resolves external source geometry." );
		Require( resolved.All( x => x.StartsWith( "row", StringComparison.Ordinal ) || x.StartsWith( "col", StringComparison.Ordinal ) ), "Unexpected compiled dependency." );
	}
	private static void Ownership( Form form, MeshoptCubeReference reference )
	{
		GltfPrimitiveUploadData upload; using ( var d = form.Read() )
		{
			upload = d.MaterializePrimitiveUpload( 15, 0 ); var exact = d.MaterializePrimitiveUpload( 15, 0, upload.OwnedBytes ); Require( exact.OwnedBytes == upload.OwnedBytes, "Exact owned budget changed." );
			ExpectGltf( () => d.MaterializePrimitiveUpload( 15, 0, upload.OwnedBytes - 1 ), "budget" );
		}
		var view = reference.Accessor( reference.AccessorFor( 15, "POSITION" ) ).GetProperty( "view" ).GetInt32();
		Require( Hash( upload.Attributes["POSITION"].Storage.Bytes.Span ) == reference.Oracle.GetProperty( "views" )[view].GetProperty( "sha256" ).GetString(), "Owned interleaved view changed after document disposal." );
	}
	private static void ExpectGltf( Action body, string word )
	{
		try { body(); } catch ( GltfException e ) { Require( e.Message.Contains( word, StringComparison.OrdinalIgnoreCase ), "Rejection lacks useful reason: " + e.Message ); return; } throw new InvalidOperationException( "Expected a clear GltfException rejection." );
	}
}
