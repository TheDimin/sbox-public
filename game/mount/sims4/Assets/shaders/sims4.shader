// =============================================================================
// Sims 4 Ubershader for s&box (Source 2)
// =============================================================================
// Reverse-engineered from D3D11 shader disassembly of The Sims 4.
//
// TS4 uses a forward-rendered, non-PBR lighting model with:
//   - DXT5nm-style normals (X=alpha, Y=blue, Z reconstructed)
//   - Custom specular: Kelemen/Szirmay-Kalos visibility function
//   - Triband environment reflections via gloss band selector
//   - 4 directional lights + sun shadow + ambient + SSAO
//
// Since s&box uses PBR (GGX / Cook-Torrance), we reverse-map TS4's specular
// parameters into roughness, reflectance, and metalness to produce visually
// faithful results under Source 2 lighting.
// =============================================================================

HEADER
{
	Description = "Sims 4 Ubershader";
}

FEATURES
{
	#include "common/features.hlsl"

	Feature( F_ALPHA_TEST, 0..1, "Alpha Test" );
	Feature( F_EMISSIVE, 0..1, "Emissive" );
}

MODES
{
	Forward();
	Depth();
}

COMMON
{
	#include "common/shared.hlsl"

	#define CUSTOM_MATERIAL_INPUTS
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		return FinalizeVertex( i );
	}
}

PS
{
	#include "common/pixel.hlsl"

	// -------------------------------------------------------------------------
	// Samplers
	// -------------------------------------------------------------------------
	SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;

	// -------------------------------------------------------------------------
	// Texture inputs
	// -------------------------------------------------------------------------
	// Diffuse / albedo (sRGB, alpha channel may contain opacity)
	CreateInputTexture2D( DiffuseMap, Srgb, 8, "None", "_color", "Textures,10/10", Default3( 1.0, 1.0, 1.0 ) );
	Texture2D g_tDiffuse < Channel( RGBA, Box( DiffuseMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	// TS4 normal map (DXT5nm: X in alpha, Y in blue — we sample .zwxy to get .xy = alpha,blue)
	CreateInputTexture2D( NormalMapTex, Linear, 8, "None", "_normal", "Textures,10/20", Default4( 0.0, 0.0, 0.5, 0.5 ) );
	Texture2D g_tNormalMap < Channel( RGBA, Box( NormalMapTex ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	// TS4 specular map:
	//   .x = gloss band selector (triband: 0/0.5/1.0 tent functions for env reflection)
	//   .y = specular intensity (direct specular multiplier)
	//   .z = exponent remap (z*190+10, doubled = Phong-like exponent)
	//   .w = specular weight (Fresnel-like multiplier on direct spec highlight)
	CreateInputTexture2D( SpecularMap, Linear, 8, "None", "_spec", "Textures,10/30", Default4( 0.0, 0.0, 0.0, 0.0 ) );
	Texture2D g_tSpecular < Channel( RGBA, Box( SpecularMap ), Linear ); OutputFormat( DXT5 ); SrgbRead( false ); >;

	// Emissive / self-illumination (sRGB)
	CreateInputTexture2D( EmissiveMap, Srgb, 8, "None", "_emissive", "Textures,10/40", Default3( 0.0, 0.0, 0.0 ) );
	Texture2D g_tEmissive < Channel( RGBA, Box( EmissiveMap ), Srgb ); OutputFormat( DXT5 ); SrgbRead( true ); >;

	// -------------------------------------------------------------------------
	// Material parameters
	// -------------------------------------------------------------------------
	float g_flNormalStrength < UiGroup( "Material,20/Normal,10/10" ); Default1( 1.0 ); Range1( 0.0, 2.0 ); >;
	float g_flSpecularScale < UiGroup( "Material,20/Specular,20/10" ); Default1( 1.0 ); Range1( 0.0, 2.0 ); >;
	float g_flEmissiveScale < UiGroup( "Material,20/Emissive,30/10" ); Default1( 1.0 ); Range1( 0.0, 10.0 ); >;
	float g_flAlphaTestThreshold < UiGroup( "Material,20/Alpha,40/10" ); Default1( 0.5 ); Range1( 0.0, 1.0 ); >;
	float3 g_vDiffuseTint < UiType( Color ); UiGroup( "Material,20/Diffuse,5/10" ); Default3( 1.0, 1.0, 1.0 ); >;

	// -------------------------------------------------------------------------
	// Static combos
	// -------------------------------------------------------------------------
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_EMISSIVE, F_EMISSIVE, Sys( ALL ) );

	// -------------------------------------------------------------------------
	// TS4 Specular → PBR Roughness mapping
	// -------------------------------------------------------------------------
	// TS4's specular exponent: spec.z * 380 + 20 (from assembly: z*190+10, then doubled)
	// Phong exponent ≈ 2/α² - 2  →  α = sqrt(2 / (exp + 2))
	// This converts the TS4 specular exponent to a PBR roughness value.
	//
	// However, TS4 objects are overwhelmingly non-metallic (wood, fabric, plastic,
	// ceramic). The specular intensity (.y) and weight (.w) modulate how much
	// specular is visible. Low .y and .w = matte surface = high roughness.
	//
	// We blend between the exponent-derived roughness and a high roughness floor
	// based on specular intensity, so that:
	//   - Matte fabrics (low spec) → roughness ~0.85-0.95
	//   - Glossy plastic (mid spec) → roughness ~0.3-0.5
	//   - Shiny metal/glass (high spec) → roughness ~0.05-0.2
	// -------------------------------------------------------------------------

	float3 DecodeTS4Normal( float4 normalSample, float strength )
	{
		// TS4 DXT5nm: X stored in alpha (.w), Y stored in blue (.z)
		// The texture is sampled with .zwxy swizzle in the original shader,
		// but we sample RGBA and pick .w (alpha) and .z (blue) manually.
		float2 nxy;
		nxy.x = normalSample.w * 2.007874 - 1.03937006;
		nxy.y = normalSample.z * 2.007874 - 1.03937006;
		nxy *= strength;

		float nz = sqrt( max( 0.0, 1.0 - dot( nxy, nxy ) ) );
		return float3( nxy, nz );
	}

	float TS4ExponentToRoughness( float specZ )
	{
		// TS4 assembly: exponent = specZ * 190 + 10, then doubled
		float exponent = specZ * 380.0 + 20.0;

		// Phong-to-roughness: α = sqrt(2 / (n + 2))
		float roughness = sqrt( 2.0 / ( exponent + 2.0 ) );

		return roughness;
	}

	float ComputeTS4Roughness( float4 specSample )
	{
		// Base roughness from the exponent channel
		float baseRoughness = TS4ExponentToRoughness( specSample.z );

		// Specular intensity (.y) and weight (.w) tell us how prominent the
		// specular highlight is. When both are low, the surface is perceptually
		// matte regardless of exponent. Blend toward a matte floor.
		float specStrength = specSample.y * specSample.w;

		// Lerp between a matte floor and the exponent-derived roughness
		// Low specStrength → mostly matte (0.9)
		// High specStrength → trust the exponent
		float roughness = lerp( 0.90, baseRoughness, saturate( specStrength * 2.0 ) );

		return saturate( roughness );
	}

	float ComputeTS4Reflectance( float4 specSample )
	{
		// In TS4, the triband gloss (.x) and specular weight (.w) control
		// environment reflections. High .x with high .w = reflective surface.
		// Map this to dielectric reflectance (F0).
		//
		// Most TS4 objects: F0 ≈ 0.02-0.04 (plastic, wood, fabric)
		// Shiny objects:    F0 ≈ 0.04-0.08 (polished surfaces)
		float reflectance = lerp( 0.02, 0.06, specSample.w * specSample.y );

		return reflectance;
	}

	// -------------------------------------------------------------------------
	// Main pixel shader
	// -------------------------------------------------------------------------
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init();

		float2 uv = i.vTextureCoords.xy;

		// ----- Diffuse / Albedo -----
		float4 diffuseSample = Tex2DS( g_tDiffuse, g_sSampler0, uv );
		m.Albedo = diffuseSample.rgb * g_vDiffuseTint;
		m.Opacity = diffuseSample.a;

		// ----- Alpha Test -----
		if ( S_ALPHA_TEST )
		{
			// TS4 assembly: diffuse.w * 255 - threshold, discard if < 0
			float alphaRef = diffuseSample.a - g_flAlphaTestThreshold;
			clip( alphaRef );
		}

		// ----- Normal Map (TS4 DXT5nm: X=alpha, Y=blue) -----
		float4 normalSample = Tex2DS( g_tNormalMap, g_sSampler0, uv );
		float3 tangentNormal = DecodeTS4Normal( normalSample, g_flNormalStrength );
		m.Normal = TransformNormal( tangentNormal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );

		// ----- Specular → PBR mapping -----
		float4 specSample = Tex2DS( g_tSpecular, g_sSampler0, uv );
		specSample *= g_flSpecularScale;

		m.Roughness = ComputeTS4Roughness( specSample );
		m.Metalness = 0.0; // TS4 objects are non-metallic (dielectric)

		// Approximate F0 from TS4 specular parameters
		// s&box doesn't have a direct reflectance input on Material,
		// but we can influence it through a subtle metalness boost for
		// very reflective surfaces (glass, polished ceramic).
		// Keep metalness capped low to avoid color bleeding.
		float reflectance = ComputeTS4Reflectance( specSample );
		if ( reflectance > 0.04 )
		{
			// Only nudge metalness for clearly reflective surfaces
			m.Metalness = saturate( ( reflectance - 0.04 ) * 5.0 );
		}

		// ----- Emissive -----
		if ( S_EMISSIVE )
		{
			float3 emissiveSample = Tex2DS( g_tEmissive, g_sSampler0, uv ).rgb;
			m.Emission = emissiveSample * g_flEmissiveScale;
		}
		else
		{
			m.Emission = float3( 0, 0, 0 );
		}

		// ----- Final material properties -----
		m.AmbientOcclusion = 1.0;
		m.TintMask = 1.0;
		m.Transmission = 0.0;

		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uv;

		return ShadingModelStandard::Shade( i, m );
	}
}
