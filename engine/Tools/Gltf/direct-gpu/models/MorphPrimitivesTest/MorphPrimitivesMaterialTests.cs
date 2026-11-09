using System;
using System.Collections.Generic;
using System.Linq;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using static MorphPrimitivesReference;

/// <summary>Source material metadata/encoded-image evidence; stock material rendered fidelity remains separate.</summary>
public static class MorphPrimitivesMaterialTests
{
	public static MorphPrimitivesCoreTests.Result[] RunAll( string checkoutRoot )
	{
		var reference = new MorphPrimitivesReference( checkoutRoot ); var rows = new List<MorphPrimitivesCoreTests.Result>();
		var imageHash = reference.Oracle.GetProperty( "forms" ).GetProperty( "glTF" ).GetProperty( "files" ).GetProperty( "uv_texture.jpg" ).GetProperty( "sha256" ).GetString();
		foreach ( var form in reference.Forms ) MorphPrimitivesCoreTests.RunCase( rows, "both_red_green_source_materials_share_the_exact_authored_jpeg", form.Name, () =>
		{
			using var d = form.Read();
			for ( var p = 0; p < 2; p++ )
			{
				var m = d.ReadMaterial( p ); var wanted = p == 0 ? new System.Numerics.Vector4( 1, 0, 0, 1 ) : new System.Numerics.Vector4( 0, 1, 0, 1 );
				Require( m.BaseColorFactor == wanted && m.MetallicFactor == 0 && m.RoughnessFactor == 1 && !m.DoubleSided && !m.Unlit && m.AlphaMode == "OPAQUE", "Red/green material factors changed." );
				Require( m.BaseColorTexture.Index == 0 && m.BaseColorTexture.TexCoord == 0 && m.BaseColorTexture.IsSrgb && m.NormalTexture is null, "Source base-color UV/color space or absent normal texture changed." );
			}
			var texture = d.ReadTexture( 0 ); var sampler = d.ReadSampler( 0 ); var image = d.ReadImage( 0 );
			Require( texture.Image == 0 && texture.Sampler == 0 && sampler.MinFilter == 9729 && sampler.WrapS == 10497 && sampler.WrapT == 10497, "Shared authored linear/repeat sampler changed." );
			Require( image.MimeType == "image/jpeg" && Hash( image.Bytes ) == imageHash, "Source/Binary/Draco JPEG identity differs." );
		} );
		return rows.ToArray();
	}
}
