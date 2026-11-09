HEADER
{
	DevShader = true;
	Description = "glTF stock lighting with material sampler attributes";
	Version = 1;
}

MODES
{
	Forward();

	Depth( "depth_only.shader" ); 
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

FEATURES
{
	#include "vr_common_features.fxc"
	Feature( F_GLTF_LINEAR_VERTEX_COLOR, 0..1, "glTF linear vertex color" );
	Feature( F_GLTF_METALLIC_TEXTURE, 0..1, "glTF metallic texture" );
}

COMMON
{
	#define BLEND_MODE_ALREADY_SET
	#define MATERIAL_CONTROLLED_BACKFACES

	#include "system.fxc"
	#include "vr_common.fxc" 
}

struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"

	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
};

VS
{
	StaticCombo( S_GLTF_LINEAR_VERTEX_COLOR, F_GLTF_LINEAR_VERTEX_COLOR, Sys( ALL ) );
	#include "vr_shared_standard_vs_code.fxc"

	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );
		
		#if S_GLTF_LINEAR_VERTEX_COLOR
			o.vVertexColor *= i.vColor;
		#else
			ExtraShaderData_t extraShaderData = GetExtraPerInstanceShaderData( i.nInstanceTransformID );
			o.vVertexColor.rgb = SrgbGammaToLinear( i.vColor.rgb ) * extraShaderData.vTint.rgb;
		#endif

		return VS_CommonProcessing_Post( o );
	}
}

PS
{
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );
	StaticCombo( S_GLTF_LINEAR_VERTEX_COLOR, F_GLTF_LINEAR_VERTEX_COLOR, Sys( ALL ) );
	StaticCombo( S_METALNESS_TEXTURE, F_GLTF_METALLIC_TEXTURE, Sys( ALL ) );

	#if S_GLTF_LINEAR_VERTEX_COLOR
		#define ENABLE_NORMAL_MAPS
	#endif

	// Preserve the stock shared material depth state before preloading common PS.
	#define DEPTH_STATE_ALREADY_SET
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	#if ( S_TRANSLUCENT )
		RenderState( DepthWriteEnable, false );
	#else
		RenderState( DepthWriteEnable, true );
	#endif

	// Compile common lighting and decals with their original stock sampler names.
	// Only the material processing include below sees the aliases.
	#include "vr_common_ps_code.fxc"
	#include "common_samplers.fxc"
	#include "vr_detail_texture.fxc"
	#define MINIMAL_MATERIAL
	#include "common/classes/Decals.hlsl"

	SamplerState g_sGltfMaterialAniso < Filter( ANISOTROPIC ); MaxAniso( 8 ); Attribute( "g_sAniso" ); >;
	SamplerState g_sGltfMaterialTrilinear < Filter( TRILINEAR ); AddressU( WRAP ); AddressV( WRAP ); AddressW( WRAP ); Attribute( "g_sTrilinearWrap" ); >;
	#define g_sAniso g_sGltfMaterialAniso
	#define g_sTrilinearWrap g_sGltfMaterialTrilinear
	#include "vr_shared_standard_ps_code.fxc"
	#undef g_sAniso
	#undef g_sTrilinearWrap

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t finalCombinerInput = PS_SharedStandardProcessing( i );

		LightingTerms_t lightingTerms = InitLightingTerms();

		PS_OUTPUT ps_output;
		ps_output = PS_FinalCombinerDoLighting( finalCombinerInput, lightingTerms );
		ps_output = PS_FinalCombinerDoPostProcessing( finalCombinerInput, lightingTerms, ps_output );

		return ps_output;
	}
}
