HEADER
{
	Description = "Staged glTF unlit base color, five projected UV inputs";
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
	#include "common/shared.hlsl"
	#include "procedural.hlsl"
	#define S_UV2 1
}
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
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	#define DEPTH_STATE_ALREADY_SET
	#define BLEND_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : BACK );
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
	SamplerState g_sGltfBaseColor < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfBaseColorSampler" ); >;
	CreateInputTexture2D( GltfBaseColorTexture, Srgb, 8, "None", "_gltf_basecolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfBaseColorTexture < Channel( RGBA, Box( GltfBaseColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	float4 g_vGltfBaseColor < Default4( 1, 1, 1, 1 ); >;
	float g_flGltfHasBaseColorTexture < Default1( 0 ); >;
	float g_flGltfBaseColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfBaseColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfBaseColorUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfAlphaCutoff < Default1( 0.5 ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv;
		if ( g_flGltfBaseColorUvSlot < 0.5 ) uv = i.vTextureCoords.xy;
		else if ( g_flGltfBaseColorUvSlot < 1.5 ) uv = i.vTextureCoords.zw;
		else if ( g_flGltfBaseColorUvSlot < 2.5 ) uv = i.vGltfUv23.xy;
		else if ( g_flGltfBaseColorUvSlot < 3.5 ) uv = i.vGltfUv23.zw;
		else uv = i.vGltfUv4;
		uv = float2( dot( float3( uv, 1 ), g_vGltfBaseColorUvRowU ), dot( float3( uv, 1 ), g_vGltfBaseColorUvRowV ) );
		float4 texel = g_flGltfHasBaseColorTexture > 0.5 ? g_tGltfBaseColorTexture.Sample( g_sGltfBaseColor, uv ) : float4( 1, 1, 1, 1 );
		float4 source = texel * g_vGltfBaseColor * saturate( i.vColor );
		#if S_ALPHA_TEST
			clip( source.a - g_flGltfAlphaCutoff );
		#endif
		#if S_TRANSLUCENT
			return float4( source.rgb * i.vVertexColor.rgb, source.a * i.vVertexColor.a );
		#else
			return float4( source.rgb * i.vVertexColor.rgb, i.vVertexColor.a );
		#endif
	}
}
