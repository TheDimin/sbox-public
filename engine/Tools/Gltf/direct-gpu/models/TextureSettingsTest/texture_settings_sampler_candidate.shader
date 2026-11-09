HEADER
{
	DevShader = true;
	Description = "Actual TextureSettingsTest sampler attribute candidate diagnostic";
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
		o.vPositionPs = float4( i.vPositionOs.xy, 0, 1 );
		return o;
	}
}
PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, false );
	Texture2D g_tProbe < Attribute( "ProbeTexture" ); SrgbRead( true ); >;
	SamplerState g_sBaseColor < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfBaseColorSampler" ); >;
	SamplerState g_sSurface < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfMetallicRoughnessSampler" ); >;
	float2 g_vUv < Attribute( "ProbeUv" ); >;
	float2 g_vDdx < Attribute( "ProbeDdx" ); >;
	float2 g_vDdy < Attribute( "ProbeDdy" ); >;
	int g_nRole < Attribute( "ProbeRole" ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		if ( g_nRole == 1 )
			return g_tProbe.SampleGrad( g_sSurface, g_vUv, g_vDdx, g_vDdy );
		return g_tProbe.SampleGrad( g_sBaseColor, g_vUv, g_vDdx, g_vDdy );
	}
}
