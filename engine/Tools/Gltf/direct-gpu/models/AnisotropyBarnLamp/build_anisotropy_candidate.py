#!/usr/bin/env python3
"""Build the anonymous candidate from the source-lit shader without installing it."""
import hashlib
from pathlib import Path

from audit_source import ROOT

OWNED = Path(__file__).resolve().parent
BASE = ROOT / "game/core/shaders/gltf_surface_five_uv.shader"

DECLARATIONS = """
	SamplerState g_sGltfAnisotropy < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfAnisotropySampler" ); >;
	CreateInputTexture2D( GltfAnisotropyTexture, Linear, 8, "None", "_gltf_anisotropy", ",0/,0/0", Default4( 1, 0.5, 1, 1 ) );
	Texture2D g_tGltfAnisotropyTexture < Channel( RGBA, Box( GltfAnisotropyTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	float g_flGltfAnisotropyStrength < Default1( 0 ); >;
	float2 g_vGltfAnisotropyRotation < Default2( 1, 0 ); >;
	float g_flGltfHasAnisotropyTexture < Default1( 0 ); >;
	float g_flGltfAnisotropyUvSlot < Default1( 0 ); >;
	float3 g_vGltfAnisotropyUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfAnisotropyUvRowV < Default3( 0, 1, 0 ); >;
	// Test-only: zero the sampled strength channel independently of the uniform.
	float g_flGltfProbeZeroBlue < Default1( 0 ); >;
	// Test-only: replace direction while preserving sampled strength.
	float g_flGltfProbeFlatRg < Default1( 0 ); >;
"""

SHADE = """
	float4 ShadeGltfAnisotropy( PixelInput i, Material m )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );
		float2 uv = GltfUv( i, g_flGltfAnisotropyUvSlot, g_vGltfAnisotropyUvRowU, g_vGltfAnisotropyUvRowV );
		float3 map = g_flGltfHasAnisotropyTexture > .5
			? g_tGltfAnisotropyTexture.Sample( g_sGltfAnisotropy, uv ).rgb : float3( 1, .5, 1 );
		float2 d = g_flGltfProbeFlatRg > .5 ? float2( 1, 0 ) : 2 * map.rg - 1;
		float len2 = dot( d, d );
		d = len2 > 1e-8 ? d * rsqrt( len2 ) : float2( 1, 0 );
		float2 rot = g_vGltfAnisotropyRotation;
		d = float2( rot.x * d.x - rot.y * d.y, rot.y * d.x + rot.x * d.y );
		float strength = saturate( g_flGltfAnisotropyStrength * (g_flGltfProbeZeroBlue > .5 ? 0 : map.b) );

		float3 geo = normalize( m.GeometricNormal );
		float3 t = normalize( m.WorldTangentU - geo * dot( geo, m.WorldTangentU ) );
		float3 b = cross( geo, t );
		b *= dot( b, m.WorldTangentV ) < 0 ? -1 : 1;
		float3 along = d.x * t + d.y * b;
		// The anisotropic BRDF uses dot products in the perturbed shading plane.
		// When the mapped normal aligns with the along axis, the perpendicular
		// authored axis provides a stable alternative projection.
		float3 alongProjected = along - m.Normal * dot( m.Normal, along );
		float3 across = cross( m.Normal, alongProjected );
		if ( dot( alongProjected, alongProjected ) < 1e-8 )
		{
			across = -d.y * t + d.x * b;
			alongProjected = cross( across, m.Normal );
			across = cross( m.Normal, alongProjected );
		}

		CombinerInput f = ShadingModelStandard::MaterialToCombinerInput( m );
		f.vTangentUWs = normalize( alongProjected );
		f.vTangentVWs = normalize( across );
		float baseRoughness = max( m.Roughness, .001 );
		float alongAlpha = lerp( baseRoughness * baseRoughness, 1.0, strength * strength );
		f.vRoughness = float2( sqrt( alongAlpha ), baseRoughness );
		// Existing engine combiner provides clustered lights, shadows, AO,
		// emission, GGX distribution and its IBL anisotropy approximation.
		LightingTerms_t lighting = InitLightingTerms();
		PS_OUTPUT output = PS_FinalCombinerDoLighting( f, lighting );
		float4 color = output.vColor;
		if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, color.a );
		if ( g_bWireframeMode ) return g_vWireframeColor;
		if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( color, m, lighting );
		if ( g_bFogEnabled ) color.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, color.rgb );
		return color;
	}
"""


def replace_once(source, needle, replacement):
    assert source.count(needle) == 1, f"Unexpected source-lit shader shape: {needle}"
    return source.replace(needle, replacement)


def build(source):
    source = replace_once(source, 'Staged glTF core source textures, five UV inputs, stock Surface/Lit',
                          'Anonymous glTF anisotropy on the Barn Lamp source materials')
    source = replace_once(source, '#include "common/pixel.hlsl"', '#define S_ANISOTROPIC_GLOSS 1\n\t#include "common/pixel.hlsl"')
    source = replace_once(source, '\n\t// Host code validates', DECLARATIONS + '\n\t// Host code validates')
    source = replace_once(source, '\n\tfloat4 MainPs', SHADE + '\n\tfloat4 MainPs')
    return replace_once(source, 'return ShadingModelStandard::Shade( m );', 'return ShadeGltfAnisotropy( i, m );')


if __name__ == "__main__":
    data = build(BASE.read_text()).encode()
    (OWNED / "gltf_anisotropy_candidate.shader").write_bytes(data)
    print(hashlib.sha256(data).hexdigest())
