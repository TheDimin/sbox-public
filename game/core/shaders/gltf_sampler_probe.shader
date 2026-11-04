HEADER
{
	DevShader = true;
	Description = "Integration test for glTF sampler filters and mip clamps.";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
	#include "common/Bindless.hlsl"
}

struct VertexInput
{
	float3 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

struct PixelInput
{
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		o.vPositionPs = float4( i.vPositionOs.xy, 0.0, 1.0 );
		return o;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, false );

	Texture2D g_tProbe < Attribute( "ProbeTexture" ); SrgbRead( false ); >;
	SamplerState g_sProbe < Attribute( "ProbeSampler" ); >;
	float2 g_vProbeUv < Attribute( "ProbeUv" ); >;
	float2 g_vProbeDdx < Attribute( "ProbeDdx" ); >;
	float2 g_vProbeDdy < Attribute( "ProbeDdy" ); >;
	int g_nProbeSamplerIndex < Attribute( "ProbeSamplerIndex" ); >;
	int g_nProbeUseBindless < Attribute( "ProbeUseBindless" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		if ( g_nProbeUseBindless != 0 )
			return g_tProbe.SampleGrad( Bindless::GetSampler( g_nProbeSamplerIndex ), g_vProbeUv, g_vProbeDdx, g_vProbeDdy );
		return g_tProbe.SampleGrad( g_sProbe, g_vProbeUv, g_vProbeDdx, g_vProbeDdy );
	}
}
