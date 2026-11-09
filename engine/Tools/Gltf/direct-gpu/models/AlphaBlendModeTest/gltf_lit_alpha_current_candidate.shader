HEADER
{
	Description = "Staged glTF core source textures, five UV inputs, stock Surface/Lit";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_TRANSLUCENT, 0..1, "Rendering" );
	FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), "Mask and blend are exclusive" );
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
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		float2 uvBaseColor = GltfUv( i, g_flGltfBaseColorUvSlot, g_vGltfBaseColorUvRowU, g_vGltfBaseColorUvRowV );
		float2 uvMetallicRoughness = GltfUv( i, g_flGltfMetallicRoughnessUvSlot, g_vGltfMetallicRoughnessUvRowU, g_vGltfMetallicRoughnessUvRowV );
		float2 uvNormal = GltfUv( i, g_flGltfNormalUvSlot, g_vGltfNormalUvRowU, g_vGltfNormalUvRowV );
		float2 uvOcclusion = GltfUv( i, g_flGltfOcclusionUvSlot, g_vGltfOcclusionUvRowU, g_vGltfOcclusionUvRowV );
		float2 uvEmissive = GltfUv( i, g_flGltfEmissiveUvSlot, g_vGltfEmissiveUvRowU, g_vGltfEmissiveUvRowV );
		float4 sampleBaseColor = g_flGltfHasBaseColorTexture > 0.5 ? g_tGltfBaseColorTexture.Sample( g_sGltfBaseColor, uvBaseColor ) : float4( 1, 1, 1, 1 );
		float4 sampleMetallicRoughness = g_flGltfHasMetallicRoughnessTexture > 0.5 ? g_tGltfMetallicRoughnessTexture.Sample( g_sGltfMetallicRoughness, uvMetallicRoughness ) : float4( 1, 1, 1, 1 );
		float4 sampleNormal = g_flGltfHasNormalTexture > 0.5 ? g_tGltfNormalTexture.Sample( g_sGltfNormal, uvNormal ) : float4( 1, 1, 1, 1 );
		float4 sampleOcclusion = g_flGltfHasOcclusionTexture > 0.5 ? g_tGltfOcclusionTexture.Sample( g_sGltfOcclusion, uvOcclusion ) : float4( 1, 1, 1, 1 );
		float4 sampleEmissive = g_flGltfHasEmissiveTexture > 0.5 ? g_tGltfEmissiveTexture.Sample( g_sGltfEmissive, uvEmissive ) : float4( 1, 1, 1, 1 );
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
		// Texture alpha is linear; glTF factor and vertex alpha multiply it.
		float sourceAlpha = sampleBaseColor.a * g_vGltfBaseColor.a * saturate( i.vColor.a );
		#if S_ALPHA_TEST
			clip( sourceAlpha - g_flGltfAlphaCutoff );
		#endif
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
		return ShadingModelStandard::Shade( m );
	}
}
