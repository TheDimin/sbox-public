using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

public static class AlphaBlendModeCoreTests
{
	public sealed record Result( string Name, string Form, int Assertions, bool Passed, string Error );
	public const int ExpectedCases = 44;
	public static Result[] RunAll( string checkoutRoot )
	{
		var rows = new List<Result>();
		foreach ( var form in AlphaBlendModeReference.OpenForms( checkoutRoot ) )
		{
			Run( "six_authored_material_modes_factors_cutoffs_sidedness", form.Name, () => Materials( form ) );
			Run( "five_literal_panel_geometry_uv_and_material_bindings", form.Name, () => Panels( form ) );
			Run( "unmodified_source_rgba_image_bytes", form.Name, () => Image( checkoutRoot, form ) );
			for ( var index = 0; index < 6; index++ )
			{
				var material = index;
				Run( "default_material_realization_accepts_authored_material_" + material, form.Name, () =>
				{
					using var document = form.Read();
					// An explicit source-support acceptance row, expected red until real
					// adapter capability exists. This never marks preparation as pixels.
					var prepared = GltfStockMaterialAdapter.Prepare( document, document.ReadMaterial( material ) );
					Require( prepared is not null, "Source material preparation returned null." ); return 1;
				} );
			}
		}
		Run( "default_opaque_cutoff_half_single_sided", "derived_material", () =>
		{
			using var d = Material( "{}" ); var m = d.ReadMaterial( 0 );
			Require( m.AlphaMode == "OPAQUE" && m.AlphaCutoff == .5f && !m.DoubleSided && m.BaseColorFactor == System.Numerics.Vector4.One, "Core material defaults differ." ); return 1;
		} );
		Run( "mask_zero_cutoff_is_valid", "derived_material", () => Cutoff( "MASK", 0 ) );
		Run( "mask_cutoff_above_one_is_valid", "derived_material", () => Cutoff( "MASK", 1.25f ) );
		Run( "opaque_irrelevant_cutoff_retained", "derived_material", () => Cutoff( "OPAQUE", .125f ) );
		Run( "blend_irrelevant_cutoff_retained", "derived_material", () => Cutoff( "BLEND", .875f ) );
		Run( "negative_cutoff_is_rejected", "derived_invalid_material", () => Rejected( "{\"alphaMode\":\"MASK\",\"alphaCutoff\":-0.1}" ) );
		Run( "unknown_alpha_mode_is_rejected", "derived_invalid_material", () => Rejected( "{\"alphaMode\":\"blend\"}" ) );
		Run( "base_color_factor_stays_linear_including_alpha", "derived_material", () =>
		{
			using var d = Material( "{\"alphaMode\":\"BLEND\",\"pbrMetallicRoughness\":{\"baseColorFactor\":[0.25,0.5,0.75,0.375]}}" );
			Require( d.ReadMaterial( 0 ).BaseColorFactor == new System.Numerics.Vector4( .25f, .5f, .75f, .375f ), "Reader gamma-decoded/premultiplied the linear factor." ); return 1;
		} );
		Require( rows.Count == ExpectedCases, "Frozen alpha case inventory changed." ); return rows.ToArray();
		void Run( string name, string form, Func<int> body )
		{
			try { rows.Add( new( name, form, body(), true, null ) ); }
			catch ( Exception error ) { rows.Add( new( name, form, 0, false, error.ToString() ) ); }
		}
	}
	private static int Materials( AlphaBlendModeReference.Form form )
	{
		using var d = form.Read();
		for ( var i = 0; i < 6; i++ )
		{
			var m = d.ReadMaterial( i );
			Require( m.AlphaMode == AlphaBlendModeReference.Modes[i] && m.AlphaCutoff == AlphaBlendModeReference.Cutoffs[i], "Authored alpha mode/cutoff changed at " + i );
			Require( m.DoubleSided == (i != 0) && !m.Unlit && m.BaseColorFactor == System.Numerics.Vector4.One, "Sidedness, core lighting, or linear factor changed." );
			Require( m.BaseColorTexture.Index == (i == 0 ? 2 : 3) && m.BaseColorTexture.IsSrgb && m.BaseColorTexture.TexCoord == 0, "Base-color binding changed." );
			Require( m.MetallicFactor == (i == 0 ? 1 : 0) && m.RoughnessFactor == (i == 0 ? 1 : .800000011920929f), "Source PBR factors changed." );
		}
		Require( d.ReadMaterial( 0 ).NormalTexture.Index == 0 && d.ReadMaterial( 0 ).OcclusionTexture.Index == 1, "The full model's unsupported bed roles must remain explicit." );
		return 25;
	}
	private static int Panels( AlphaBlendModeReference.Form form )
	{
		using var d = form.Read(); var assertions = 0;
		for ( var mesh = 0; mesh < 9; mesh++ ) { Require( d.ReadPrimitive( mesh, 0 ).Material == AlphaBlendModeReference.MeshMaterials[mesh], "Source material slot swapped." ); assertions++; }
		float[] x = [.550000011920929f, -.550000011920929f, .550000011920929f, -.550000011920929f];
		float[] y = [1.9699997901916504f, .10000008344650269f, .10000008344650269f, 1.9699997901916504f];
		float[] u = [.675000011920929f, .1250002235174179f, .675000011920929f, .1250002235174179f];
		float[] v = [.050000011920928955f, .9850000143051147f, .9850000143051147f, .050000011920928955f];
		foreach ( var mesh in AlphaBlendModeReference.PanelMeshes )
		{
			var upload = d.MaterializePrimitiveUpload( mesh, 0 );
			Require( upload.Layout.VertexCount == 4 && upload.Layout.SourceMode == 4 && !upload.Attributes.ContainsKey( "COLOR_0" ), "Authored panel shape changed; COLOR_0 must only be claimed by supplemental fixtures." ); assertions++;
			for ( var vertex = 0; vertex < 4; vertex++ )
			{
				Require( Read( upload.Attributes["POSITION"], vertex, 0 ) == x[vertex] && Read( upload.Attributes["POSITION"], vertex, 1 ) == y[vertex] && Read( upload.Attributes["POSITION"], vertex, 2 ) == 0, "Literal panel position differs." );
				Require( Read( upload.Attributes["TEXCOORD_0"], vertex, 0 ) == u[vertex] && Read( upload.Attributes["TEXCOORD_0"], vertex, 1 ) == v[vertex], "Literal panel UV differs." );
				Require( Read( upload.Attributes["NORMAL"], vertex, 0 ) == 0 && Read( upload.Attributes["NORMAL"], vertex, 1 ) == 0 && Read( upload.Attributes["NORMAL"], vertex, 2 ) == 1, "Panel normal differs." ); assertions += 3;
			}
		}
		return assertions;
	}
	private static float Read( GltfUploadStream stream, int vertex, int component )
	{
		Require( stream.ComponentType == 5126 && !stream.Normalized, "Expected literal FLOAT stream." );
		return BitConverter.Int32BitsToSingle( BinaryPrimitives.ReadInt32LittleEndian( stream.Storage.Bytes.Span.Slice( stream.ByteOffset + vertex * stream.ByteStride + component * 4, 4 ) ) );
	}
	private static int Image( string root, AlphaBlendModeReference.Form form )
	{
		using var d = form.Read(); var image = d.ReadImage( 3 );
		Require( image.Bytes.AsSpan().SequenceEqual( AlphaBlendModeReference.ReadPinned( root, "glTF/AlphaBlendLabels.png" ) ), "Source alpha image changed or was premultiplied during packaging." ); return 1;
	}
	private static GltfDocument Material( string body ) => GltfReader.Read( Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"materials\":[" + body + "]}" ) );
	private static int Cutoff( string mode, float cutoff )
	{
		using var d = Material( "{\"alphaMode\":\"" + mode + "\",\"alphaCutoff\":" + cutoff.ToString( System.Globalization.CultureInfo.InvariantCulture ) + "}" );
		Require( d.ReadMaterial( 0 ).AlphaCutoff == cutoff && d.ReadMaterial( 0 ).AlphaMode == mode, "Legal cutoff was clamped/ignored by parser." ); return 1;
	}
	private static int Rejected( string body )
	{
		try { using var d = Material( body ); _ = d.ReadMaterial( 0 ); }
		catch ( GltfException ) { return 1; }
		throw new InvalidOperationException( "Invalid material unexpectedly accepted." );
	}
	internal static void Require( bool value, string error ) { if ( !value ) throw new InvalidOperationException( error ); }
}
