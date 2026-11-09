HEADER
{
	DevShader = true;
	Description = "Independent glTF linear COLOR_0 reference over the standard PBR pipeline";
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
}
COMMON
{
	#define BLEND_MODE_ALREADY_SET
	#include "system.fxc"
	#include "vr_common.fxc"
}
struct VS_INPUT
{
	#include "vr_shared_standard_vs_input.fxc"
	float4 vGltfLinearColor : COLOR0 < Semantic( Color ); >;
};
struct PS_INPUT
{
	#include "vr_shared_standard_ps_input.fxc"
};
VS
{
	#include "vr_shared_standard_vs_code.fxc"
	PS_INPUT MainVs( VS_INPUT i )
	{
		PS_INPUT o = VS_SharedStandardProcessing( i );
		// Standard processing already decodes material tint. glTF COLOR_0
		// is linear and must not pass through SrgbGammaToLinear again.
		o.vVertexColor *= i.vGltfLinearColor;
		return VS_CommonProcessing_Post( o );
	}
PS
{
	DynamicCombo( D_OPAQUE_FADE, 0..1, Sys( ALL ) );
	#include "vr_shared_standard_ps_code.fxc"
	PS_OUTPUT MainPs( PS_INPUT i )
	{
		FinalCombinerInput_t f = PS_SharedStandardProcessing( i );
		LightingTerms_t l = InitLightingTerms();
		PS_OUTPUT o = PS_FinalCombinerDoLighting( f, l );
		return PS_FinalCombinerDoPostProcessing( f, l, o );
	}
}
