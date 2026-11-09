using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

/// <summary>Native material binding and ownership checks; image parity requires a separate draw.</summary>
internal static class VertexColorMaterialBindingProbe
{
	private const string ColorPng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNwaDjwHwAFBAKAPJ4DgAAAAABJRU5ErkJggg==";
	private const string SurfacePng = "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGNgb3D4DwADIAHHbKpMFQAAAABJRU5ErkJggg==";
	public static object[] Run( string checkoutRoot )
	{
		if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor main thread." );
		ThreadSafe.AssertIsMainThread();
		var template = Material.FromShader( "shaders/gltf_stock_sampler.shader" );
		Require( template is { IsValid: true, IsError: false }, "Public glTF stock-sampler shader template is unavailable." );
		var json = "{\"asset\":{\"version\":\"2.0\"},\"images\":[{\"uri\":\"data:image/png;base64," + ColorPng + "\"},{\"uri\":\"data:image/png;base64," + SurfacePng + "\"}],\"samplers\":[{\"wrapS\":33071,\"wrapT\":33648,\"minFilter\":9729,\"magFilter\":9729}],\"textures\":[{\"source\":0},{\"source\":1},{\"source\":1,\"sampler\":0}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		var outputs = new List<object>();
		var factor = new System.Numerics.Vector4( .21404114f, .5f, .75f, 1 );
		var plain = new GltfMaterial { BaseColorFactor = factor, MetallicFactor = .25f, RoughnessFactor = .75f, AlphaMode = "OPAQUE" };
		var prepared = GltfStockMaterialAdapter.Prepare( document, plain );
		using ( var first = GltfStockMaterialAdapter.Acquire( prepared, template, true ) )
		using ( var second = GltfStockMaterialAdapter.Acquire( prepared, template, true ) )
		{
			var material = first.Value; var color = material.GetTexture( "g_tColor2" );
			Require( ReferenceEquals( material, second.Value ), "Matching immutable material keys did not share a lease." );
			CheckColor( color, 255, 255, 255 );
			Require( material.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) == 1 && material.GetFeature( "F_GLTF_METALLIC_TEXTURE" ) == 0, "Factor-only feature selection differs." );
			Require( MathF.Abs( material.GetVector4( "g_flMetalness" ).x - .25f ) < .000001f && material.GetTexture( "g_tNormal" ) is { IsValid: true }, "Factor-only PBR bindings differ." );
			var tint = material.GetVector4( "g_vColorTint" ); Require( MathF.Abs( tint.x - .5f ) < .000001f && MathF.Abs( tint.y - GltfStockMaterialAdapter.EncodeTint( .5f ) ) < .000001f && MathF.Abs( tint.z - GltfStockMaterialAdapter.EncodeTint( .75f ) ) < .000001f, "Non-white linear factor was not retained." );
			first.Dispose(); Require( second.Value.IsValid && second.Value.GetTexture( "g_tColor2" ).IsValid, "Releasing one lease invalidated the surviving material/texture." );
			outputs.Add( new { Passed = true, Case = "factor_only_white_fallback_and_shared_lease", ColorTexture = color.Index, Metallic = .25f, Roughness = .75f, Scope = "Native material bindings and retained lease; not image parity." } );
		}
		var textured = new GltfMaterial { BaseColorFactor = factor, MetallicFactor = .25f, RoughnessFactor = .75f, AlphaMode = "OPAQUE", BaseColorTexture = new GltfTextureInfo { Index = 0 }, MetallicRoughnessTexture = new GltfTextureInfo { Index = 1 } };
		var texturedPrepared = GltfStockMaterialAdapter.Prepare( document, textured );
		using ( var lease = GltfStockMaterialAdapter.Acquire( texturedPrepared, template, true ) )
		{
			var material = lease.Value; var color = material.GetTexture( "g_tColor2" ); CheckColor( color, 64, 128, 192 );
			Require( material.GetFeature( "F_GLTF_LINEAR_VERTEX_COLOR" ) == 1 && material.GetFeature( "F_GLTF_METALLIC_TEXTURE" ) == 1, "Metallic-texture shader feature is not selected." );
			Require( material.GetTexture( "g_tNormal" ) is { IsValid: true } && material.GetTexture( "g_tMetalness" ) is { IsValid: true }, "Packed metallic/roughness resources are not bound." );
			Graphics.FlushGPU(); var roughness = material.GetTexture( "g_tNormal" ).GetPixels().Single().b; var metallic = material.GetTexture( "g_tMetalness" ).GetPixels().Single().g;
			Require( Math.Abs( roughness - 96 ) <= 1 && Math.Abs( metallic - 16 ) <= 1, "GPU packed roughness/metallic factors differ from their linear G/B source channels." );
			Require( MathF.Abs( material.GetVector4( "g_vColorTint" ).x - .5f ) < .000001f && texturedPrepared.MetallicFactor == .25f && texturedPrepared.RoughnessFactor == .75f, "Textured factor values changed." );
			outputs.Add( new { Passed = true, Case = "base_texture_nonwhite_factor_and_metallic_roughness_texture", ColorTexture = color.Index, Metallic = .25f, Roughness = .75f, RoughnessByte = roughness, MetallicByte = metallic, Scope = "Native resource/feature binding and GPU packed-byte readback; actual sampler and rendered values require GPU image comparison." } );
		}
		var surfaceOnly = new GltfMaterial { BaseColorFactor = factor, MetallicFactor = .25f, RoughnessFactor = .75f, AlphaMode = "OPAQUE", MetallicRoughnessTexture = new GltfTextureInfo { Index = 2 } };
		var surfacePrepared = GltfStockMaterialAdapter.Prepare( document, surfaceOnly );
		Require( surfacePrepared.BaseColorResource is null && surfacePrepared.ColorSampler != surfacePrepared.MetallicRoughnessSampler, "Fixture must exercise a synthetic base and a different authored surface sampler." );
		var conflictRejected = false;
		try { _ = GltfStockMaterialAdapter.Prepare( document, new GltfMaterial { BaseColorFactor = factor, MetallicFactor = .25f, RoughnessFactor = .75f, AlphaMode = "OPAQUE", BaseColorTexture = new GltfTextureInfo { Index = 0 }, MetallicRoughnessTexture = new GltfTextureInfo { Index = 2 } } ); }
		catch ( GltfException e ) when ( e.Message.Contains( "sampler", StringComparison.Ordinal ) ) { conflictRejected = true; }
		Require( conflictRejected, "Two incompatible authored samplers must still be rejected." );
		using ( var lease = GltfStockMaterialAdapter.Acquire( surfacePrepared, template, true ) )
		{
			var material = lease.Value; var color = material.GetTexture( "g_tColor2" ); CheckColor( color, 255, 255, 255 );
			Require( material.GetFeature( "F_GLTF_METALLIC_TEXTURE" ) == 1 && material.GetTexture( "g_tNormal" ) is { IsValid: true } && material.GetTexture( "g_tMetalness" ) is { IsValid: true }, "Surface-only material lost its authored metallic/roughness bindings." );
			outputs.Add( new { Passed = true, Case = "surface_sampler_with_synthetic_white_base", ColorTexture = color.Index, Scope = "Native material acquisition and cache reject conflicting shared sampler keys; sampled wrap/filter behavior requires a separate image test." } );
		}
		return outputs.ToArray();
	}
	private static void CheckColor( Texture texture, byte r, byte g, byte b )
	{
		Require( texture is { IsValid: true, IsError: false } && texture.Width == 1 && texture.Height == 1, "Missing 1x1 base texture." );
		Graphics.FlushGPU(); var pixel = texture.GetPixels().Single(); Require( pixel.r == r && pixel.g == g && pixel.b == b && pixel.a == 255, "Base texture GPU readback differs." );
	}
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
