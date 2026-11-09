HEADER
{
	Description = "glTF metal/rough source textures, five UV inputs, stock Surface/Lit, optional clearcoat and anisotropy layers";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_TRANSLUCENT, 0..1, "Rendering" );
	FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), "Mask and blend are exclusive" );
	Feature( F_GLTF_CLEARCOAT, 0..1, "glTF" );
	Feature( F_GLTF_ANISOTROPY, 0..1, "glTF" );
}
MODES
{
	Forward();
	Depth();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}
COMMON
{
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_GLTF_CLEARCOAT, F_GLTF_CLEARCOAT, Sys( ALL ) );
	StaticCombo( S_GLTF_ANISOTROPY, F_GLTF_ANISOTROPY, Sys( ALL ) );
	// KHR_materials_anisotropy uses the stock anisotropic GGX and anisotropic cubemap path.
	#define S_ANISOTROPIC_GLOSS S_GLTF_ANISOTROPY
	#include "common/shared.hlsl"
	#include "procedural.hlsl"
	#define S_UV2 1
}

// Slots 0 and 1 use the common input declarations. Slots 2, 3 and 4
// deliberately avoid TEXCOORD2/3, reserved for curvature and lightmap.
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float2 vGltfUv2 : TEXCOORD4;
	float2 vGltfUv3 : TEXCOORD5;
	float2 vGltfUv4 : TEXCOORD6;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vGltfUv23 : TEXCOORD8;
	float2 vGltfUv4 : TEXCOORD9;
	float4 vColor : COLOR0;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vGltfUv23 = float4( v.vGltfUv2, v.vGltfUv3 );
		i.vGltfUv4 = v.vGltfUv4;
		return FinalizeVertex( i );
	}
}
PS
{
	#define DEPTH_STATE_ALREADY_SET
	#define BLEND_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	#if S_TRANSLUCENT
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( BlendOpAlpha, ADD );
		RenderState( DepthWriteEnable, false );
	#else
		RenderState( BlendEnable, false );
		RenderState( DepthWriteEnable, true );
	#endif
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : BACK );
	SamplerState g_sGltfBaseColor < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfBaseColorSampler" ); >;
	SamplerState g_sGltfMetallicRoughness < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfMetallicRoughnessSampler" ); >;
	SamplerState g_sGltfNormal < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfNormalSampler" ); >;
	SamplerState g_sGltfOcclusion < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfOcclusionSampler" ); >;
	SamplerState g_sGltfEmissive < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfEmissiveSampler" ); >;
	SamplerState g_sGltfClearcoat < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfClearcoatSampler" ); >;
	SamplerState g_sGltfClearcoatRoughness < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfClearcoatRoughnessSampler" ); >;
	SamplerState g_sGltfClearcoatNormal < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfClearcoatNormalSampler" ); >;
	SamplerState g_sGltfAnisotropy < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfAnisotropySampler" ); >;

	CreateInputTexture2D( GltfBaseColorTexture, Srgb, 8, "None", "_gltf_basecolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfMetallicRoughnessTexture, Linear, 8, "None", "_gltf_metallicroughness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfNormalTexture, Linear, 8, "None", "_gltf_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( GltfOcclusionTexture, Linear, 8, "None", "_gltf_occlusion", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfEmissiveTexture, Srgb, 8, "None", "_gltf_emissive", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfBaseColorTexture < Channel( RGBA, Box( GltfBaseColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	Texture2D g_tGltfMetallicRoughnessTexture < Channel( RGBA, Box( GltfMetallicRoughnessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfNormalTexture < Channel( RGBA, Box( GltfNormalTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfOcclusionTexture < Channel( RGBA, Box( GltfOcclusionTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfEmissiveTexture < Channel( RGBA, Box( GltfEmissiveTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	CreateInputTexture2D( GltfClearcoatTexture, Linear, 8, "None", "_gltf_clearcoat", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfClearcoatRoughnessTexture, Linear, 8, "None", "_gltf_clearcoatroughness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfClearcoatNormalTexture, Linear, 8, "None", "_gltf_clearcoatnormal", ",0/,0/0", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( GltfAnisotropyTexture, Linear, 8, "None", "_gltf_anisotropy", ",0/,0/0", Default4( 1, 0.5, 1, 1 ) );
	Texture2D g_tGltfClearcoatTexture < Channel( RGBA, Box( GltfClearcoatTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfClearcoatRoughnessTexture < Channel( RGBA, Box( GltfClearcoatRoughnessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfClearcoatNormalTexture < Channel( RGBA, Box( GltfClearcoatNormalTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfAnisotropyTexture < Channel( RGBA, Box( GltfAnisotropyTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;

	float g_flGltfPointLineNoNormal < Attribute( "GltfPointLineNoNormal" ); Default( 0 ); >;
	float4 g_vGltfBaseColor < Default4( 1, 1, 1, 1 ); >;
	float3 g_vGltfEmissiveFactor < Default3( 0, 0, 0 ); >;
	float g_flGltfMetallicFactor < Default1( 1 ); >;
	float g_flGltfRoughnessFactor < Default1( 1 ); >;
	float g_flGltfOcclusionStrength < Default1( 1 ); >;
	float g_flGltfAlphaCutoff < Default1( 0.5 ); >;
	float g_flGltfNormalScale < Default1( 1 ); >;
	float g_flGltfHasBaseColorTexture < Default1( 0 ); >;
	float g_flGltfBaseColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfBaseColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfBaseColorUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasMetallicRoughnessTexture < Default1( 0 ); >;
	float g_flGltfMetallicRoughnessUvSlot < Default1( 0 ); >;
	float3 g_vGltfMetallicRoughnessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfMetallicRoughnessUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasNormalTexture < Default1( 0 ); >;
	float g_flGltfNormalUvSlot < Default1( 0 ); >;
	float3 g_vGltfNormalUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfNormalUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasOcclusionTexture < Default1( 0 ); >;
	float g_flGltfOcclusionUvSlot < Default1( 0 ); >;
	float3 g_vGltfOcclusionUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfOcclusionUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasEmissiveTexture < Default1( 0 ); >;
	float g_flGltfEmissiveUvSlot < Default1( 0 ); >;
	float3 g_vGltfEmissiveUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfEmissiveUvRowV < Default3( 0, 1, 0 ); >;

	// KHR_materials_clearcoat
	float g_flGltfClearcoatFactor < Default1( 0 ); >;
	float g_flGltfClearcoatRoughnessFactor < Default1( 0 ); >;
	float g_flGltfClearcoatNormalScale < Default1( 1 ); >;
	float g_flGltfHasClearcoatTexture < Default1( 0 ); >;
	float g_flGltfClearcoatUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasClearcoatRoughnessTexture < Default1( 0 ); >;
	float g_flGltfClearcoatRoughnessUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatRoughnessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatRoughnessUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasClearcoatNormalTexture < Default1( 0 ); >;
	float g_flGltfClearcoatNormalUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatNormalUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatNormalUvRowV < Default3( 0, 1, 0 ); >;

	// KHR_materials_anisotropy
	float g_flGltfAnisotropyStrength < Default1( 0 ); >;
	float2 g_vGltfAnisotropyRotation < Default2( 1, 0 ); >;
	float g_flGltfHasAnisotropyTexture < Default1( 0 ); >;
	float g_flGltfAnisotropyUvSlot < Default1( 0 ); >;
	float3 g_vGltfAnisotropyUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfAnisotropyUvRowV < Default3( 0, 1, 0 ); >;

	// Host code validates slot as an exact integer in [0,4]. Each branch is
	// uniform per draw; transformed coordinates remain independent per role.
	float2 GltfUv( PixelInput i, float slot, float3 rowU, float3 rowV )
	{
		float2 uv;
		if ( slot < 0.5 ) uv = i.vTextureCoords.xy;
		else if ( slot < 1.5 ) uv = i.vTextureCoords.zw;
		else if ( slot < 2.5 ) uv = i.vGltfUv23.xy;
		else if ( slot < 3.5 ) uv = i.vGltfUv23.zw;
		else uv = i.vGltfUv4;
		return float2( dot( float3( uv, 1 ), rowU ), dot( float3( uv, 1 ), rowV ) );
	}
	// Specular lighting of a second, coat-only surface: same position, coat normal and
	// coat roughness, F0 = 0.04 (IOR 1.5), no diffuse. Reuses the stock direct and IBL code.
	float3 GltfCoatSpecular( CombinerInput baseInput, float3 coatNormal, float coatRoughness )
	{
		CombinerInput c = baseInput;
		c.vNormalWs = coatNormal;
		c.vRoughness = AdjustRoughnessByGeometricNormal( coatRoughness.xx, coatNormal );
		c.vDiffuseColor = 0;
		c.vSpecularColor = 0.04;
		#if ( S_ANISOTROPIC_GLOSS )
			c.vAnisotropy = 0;
			c.vPerPixelTangentUWs = normalize( cross( c.vTangentVWs.xyz, coatNormal ) );
			c.vPerPixelTangentVWs = normalize( cross( coatNormal, c.vTangentUWs.xyz ) );
		#endif
		LightingTerms_t t = InitLightingTerms();
		ComputeDirectLighting( t, c );
		CalculateIndirectLighting( t, c );
		return ( t.vSpecular.rgb + t.vIndirectSpecular.rgb * CalculateSpecularAmbientOcclusion( c, t ).rgb );
	}

	// ShadingModelStandard::Shade with the glTF clearcoat and anisotropy layers.
	float4 GltfShade( Material m, float coat, float coatRoughness, float3 coatNormal, float3 anisoDirection, float anisoStrength )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );
		LightingTerms_t lightingTerms = InitLightingTerms();
		CombinerInput combinerInput = ShadingModelStandard::MaterialToCombinerInput( m );
		#if ( S_ANISOTROPIC_GLOSS )
		{
			// Khronos: alphaT = mix(alpha, 1, strength^2) along the direction, alphaB = alpha.
			// The stock BRDF squares perceptual roughness, so pass sqrt(alphaT) and roughness.
			float alpha = m.Roughness * m.Roughness;
			float alphaT = lerp( alpha, 1.0, anisoStrength * anisoStrength );
			float3 t = normalize( anisoDirection - combinerInput.vNormalWs * dot( anisoDirection, combinerInput.vNormalWs ) );
			float3 b = cross( combinerInput.vNormalWs, t );
			combinerInput.vRoughness = max( float2( sqrt( alphaT ), m.Roughness ), 0.001 );
			combinerInput.vTangentUWs = t;
			combinerInput.vTangentVWs = b;
			float2 ratio = combinerInput.vRoughness.xy / combinerInput.vRoughness.yx;
			combinerInput.vAnisotropy.x = step( ratio.y, ratio.x );
			combinerInput.vAnisotropy.y = 1.0 - min( ratio.x, ratio.y );
			combinerInput.vPerPixelTangentUWs = t;
			combinerInput.vPerPixelTangentVWs = b;
		}
		#endif
		ComputeDirectLighting( lightingTerms, combinerInput );
		CalculateIndirectLighting( lightingTerms, combinerInput );
		// Stock indirect diffuse comes only from lightmaps, probe volumes or a flat ambient colour;
		// the environment cubemap feeds only specular. glTF image-based lighting expects diffuse
		// irradiance from the environment as well. The roughest prefiltered mip along the normal
		// approximates it. Without an environment map in range, keep the stock value.
		float3 vEnvDiffuse = EnvMap::From( m.WorldPosition, combinerInput.vPositionSs, combinerInput.vNormalWs, 1.0.xx );
		if ( dot( vEnvDiffuse, 1.0 ) > 0.0 )
			lightingTerms.vIndirectDiffuse.rgb = vEnvDiffuse;
		float3 vDiffuseAO = CalculateDiffuseAmbientOcclusion( combinerInput, lightingTerms );
		lightingTerms.vIndirectDiffuse.rgb *= vDiffuseAO.rgb;
		lightingTerms.vDiffuse.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vDiffuseAO.rgb, combinerInput.flAmbientOcclusionDirectDiffuse );
		float3 vSpecularAO = CalculateSpecularAmbientOcclusion( combinerInput, lightingTerms );
		lightingTerms.vIndirectSpecular.rgb *= vSpecularAO.rgb;
		lightingTerms.vSpecular.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vSpecularAO.rgb, combinerInput.flAmbientOcclusionDirectSpecular );
		float3 vDiffuse = ( lightingTerms.vDiffuse.rgb + lightingTerms.vIndirectDiffuse.rgb ) * combinerInput.vDiffuseColor.rgb;
		float3 vSpecular = lightingTerms.vSpecular.rgb + lightingTerms.vIndirectSpecular.rgb;
		float3 color = vDiffuse + vSpecular;
		#if ( S_GLTF_CLEARCOAT )
		if ( coat > 0 )
		{
			// Khronos layering: base * (1 - coat * Fcoat) + coat * coatSpecular.
			float3 v = CalculatePositionToCameraDirWs( m.WorldPosition );
			float fresnel = 0.04 + 0.96 * pow( 1.0 - saturate( dot( coatNormal, v ) ), 5.0 );
			color = color * ( 1.0 - coat * fresnel ) + coat * GltfCoatSpecular( combinerInput, coatNormal, coatRoughness );
		}
		#endif
		color += combinerInput.vEmissive.rgb;
		float4 result = float4( color, m.Opacity );
		if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, result.a );
		if ( g_bWireframeMode ) return g_vWireframeColor;
		if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( result, m, lightingTerms );
		if ( g_bFogEnabled ) result.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, result.rgb );
		return result;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		float2 uvBaseColor = GltfUv( i, g_flGltfBaseColorUvSlot, g_vGltfBaseColorUvRowU, g_vGltfBaseColorUvRowV );
		float2 uvMetallicRoughness = GltfUv( i, g_flGltfMetallicRoughnessUvSlot, g_vGltfMetallicRoughnessUvRowU, g_vGltfMetallicRoughnessUvRowV );
		float2 uvNormal = GltfUv( i, g_flGltfNormalUvSlot, g_vGltfNormalUvRowU, g_vGltfNormalUvRowV );
		float2 uvOcclusion = GltfUv( i, g_flGltfOcclusionUvSlot, g_vGltfOcclusionUvRowU, g_vGltfOcclusionUvRowV );
		float2 uvEmissive = GltfUv( i, g_flGltfEmissiveUvSlot, g_vGltfEmissiveUvRowU, g_vGltfEmissiveUvRowV );
		float4 sampleBaseColor = g_flGltfHasBaseColorTexture > 0.5 ? g_tGltfBaseColorTexture.Sample( g_sGltfBaseColor, uvBaseColor ) : float4( 1, 1, 1, 1 );
		float4 sampleEmissive = g_flGltfHasEmissiveTexture > 0.5 ? g_tGltfEmissiveTexture.Sample( g_sGltfEmissive, uvEmissive ) : float4( 1, 1, 1, 1 );
		float sourceAlpha = sampleBaseColor.a * g_vGltfBaseColor.a * saturate( i.vColor.a );
		#if S_ALPHA_TEST
			clip( sourceAlpha - g_flGltfAlphaCutoff );
		#endif
		if ( g_flGltfPointLineNoNormal > .5 && !DepthNormals::WantsDepthNormals() && !ToolsVis::WantsToolsVis() )
		{
			m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
			m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
			#if S_TRANSLUCENT
				m.Opacity = sourceAlpha * i.vVertexColor.a;
			#else
				m.Opacity = i.vVertexColor.a;
			#endif
			m.TextureCoords = uvBaseColor;
			float4 color = float4( m.Albedo + m.Emission, m.Opacity );
			if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, color.a );
			if ( g_bWireframeMode ) return g_vWireframeColor;
			if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( color, m, InitLightingTerms() );
			if ( g_bFogEnabled ) color.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, color.rgb );
			return color;
		}
		float4 sampleMetallicRoughness = g_flGltfHasMetallicRoughnessTexture > 0.5 ? g_tGltfMetallicRoughnessTexture.Sample( g_sGltfMetallicRoughness, uvMetallicRoughness ) : float4( 1, 1, 1, 1 );
		float4 sampleNormal = g_flGltfHasNormalTexture > 0.5 ? g_tGltfNormalTexture.Sample( g_sGltfNormal, uvNormal ) : float4( 1, 1, 1, 1 );
		float4 sampleOcclusion = g_flGltfHasOcclusionTexture > 0.5 ? g_tGltfOcclusionTexture.Sample( g_sGltfOcclusion, uvOcclusion ) : float4( 1, 1, 1, 1 );
		float3 normalTs = float3( 0, 0, 1 );
		if ( g_flGltfHasNormalTexture > 0.5 )
		{
			normalTs = sampleNormal.xyz * 2 - 1;
			normalTs.xy *= g_flGltfNormalScale;
			normalTs = normalize( normalTs );
		}
		m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
		m.Roughness = saturate( sampleMetallicRoughness.g * g_flGltfRoughnessFactor );
		m.Metalness = saturate( sampleMetallicRoughness.b * g_flGltfMetallicFactor );
		m.AmbientOcclusion = saturate( lerp( 1, sampleOcclusion.r, g_flGltfOcclusionStrength ) );
		m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
		#if S_TRANSLUCENT
			m.Opacity = sourceAlpha * i.vVertexColor.a;
		#else
			m.Opacity = i.vVertexColor.a;
		#endif
		// Stock TransformNormal flips Y for legacy Source1 textures; pre-flip
		// exactly once because glTF uses the tangent.w * cross(N,T) basis.
		normalTs.y = -normalTs.y;
		m.Normal = TransformNormal( normalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		#if ( S_RENDER_BACKFACES )
		if ( !i.face )
		{
			m.Normal = -m.Normal;
			m.GeometricNormal = -m.GeometricNormal;
		}
		#endif
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uvBaseColor;
		// Debug/depth visualizations need the complete original material state.
		// Normal forward rendering took the early branch before sampling lit roles.
		if ( g_flGltfPointLineNoNormal > .5 )
		{
			float4 pointLineColor = float4( m.Albedo + m.Emission, m.Opacity );
			if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, pointLineColor.a );
			if ( g_bWireframeMode ) return g_vWireframeColor;
			if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( pointLineColor, m, InitLightingTerms() );
			if ( g_bFogEnabled ) pointLineColor.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, pointLineColor.rgb );
			return pointLineColor;
		}
		float coat = 0, coatRoughness = 0;
		float3 coatNormal = m.GeometricNormal;
		#if ( S_GLTF_CLEARCOAT )
		{
			float2 uvCoat = GltfUv( i, g_flGltfClearcoatUvSlot, g_vGltfClearcoatUvRowU, g_vGltfClearcoatUvRowV );
			float2 uvCoatRough = GltfUv( i, g_flGltfClearcoatRoughnessUvSlot, g_vGltfClearcoatRoughnessUvRowU, g_vGltfClearcoatRoughnessUvRowV );
			coat = g_flGltfClearcoatFactor * ( g_flGltfHasClearcoatTexture > 0.5 ? g_tGltfClearcoatTexture.Sample( g_sGltfClearcoat, uvCoat ).r : 1.0 );
			coatRoughness = saturate( g_flGltfClearcoatRoughnessFactor * ( g_flGltfHasClearcoatRoughnessTexture > 0.5 ? g_tGltfClearcoatRoughnessTexture.Sample( g_sGltfClearcoatRoughness, uvCoatRough ).g : 1.0 ) );
			// The coat uses its own normal map, or the geometric normal; never the base normal map.
			float3 coatTs = float3( 0, 0, 1 );
			if ( g_flGltfHasClearcoatNormalTexture > 0.5 )
			{
				float2 uvCoatNormal = GltfUv( i, g_flGltfClearcoatNormalUvSlot, g_vGltfClearcoatNormalUvRowU, g_vGltfClearcoatNormalUvRowV );
				coatTs = g_tGltfClearcoatNormalTexture.Sample( g_sGltfClearcoatNormal, uvCoatNormal ).xyz * 2 - 1;
				coatTs.xy *= g_flGltfClearcoatNormalScale;
				coatTs = normalize( coatTs );
			}
			coatTs.y = -coatTs.y;
			coatNormal = TransformNormal( coatTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
			#if ( S_RENDER_BACKFACES )
				if ( !i.face ) coatNormal = -coatNormal;
			#endif
		}
		#endif
		float3 anisoDirection = i.vTangentUWs;
		float anisoStrength = 0;
		#if ( S_GLTF_ANISOTROPY )
		{
			// Texture RG: direction in tangent space in [-1,1]; B: strength multiplier.
			float3 aniso = float3( 1, 0.5, 1 );
			if ( g_flGltfHasAnisotropyTexture > 0.5 )
				aniso = g_tGltfAnisotropyTexture.Sample( g_sGltfAnisotropy, GltfUv( i, g_flGltfAnisotropyUvSlot, g_vGltfAnisotropyUvRowU, g_vGltfAnisotropyUvRowV ) ).rgb;
			float2 dir = aniso.rg * 2 - 1;
			float2 r = g_vGltfAnisotropyRotation;
			dir = float2( r.x * dir.x - r.y * dir.y, r.y * dir.x + r.x * dir.y );
			// Same basis as the normal map: after the pre-flip, glTF (T, B) maps to stock (U, V).
			anisoDirection = normalize( dir.x * i.vTangentUWs + dir.y * i.vTangentVWs );
			anisoStrength = saturate( g_flGltfAnisotropyStrength * aniso.b );
		}
		#endif
		return GltfShade( m, coat, coatRoughness, coatNormal, anisoDirection, anisoStrength );
	}
}
