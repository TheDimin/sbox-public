HEADER
{
	DevShader = true;
	Description = "glTF sampler binding diagnostic";
}

MODES
{
	Default();
	Forward();
}

COMMON
{
	#include "postprocess/shared.hlsl"
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
		o.vPositionPs = float4( i.vPositionOs.yz / 40.0, 0.5, 1.0 );
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
	SamplerState g_sProbe < Filter( ANISOTROPIC ); MaxAniso( 8 ); >;
	float2 g_vProbeUv < Attribute( "ProbeUv" ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		return g_tProbe.SampleGrad( g_sProbe, g_vProbeUv, float2( 0.0001, 0 ), float2( 0, 0.0001 ) );
	}
}
