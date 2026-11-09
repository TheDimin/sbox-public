HEADER
{
	DevShader = true;
	Description = "Pinned glTF emissive linear interpolation numeric diagnostic";
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
	Texture2D g_tLinear < Attribute( "ProbeTexture" ); SrgbRead( true ); >;
	Texture2D g_tEncoded < Attribute( "ProbeTexture" ); SrgbRead( false ); >;
	// Deliberately wrong fallback: only the real material's sampler can pass.
	SamplerState g_sSource < Attribute( "GltfEmissiveSampler" ); Filter( POINT ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sWrongPoint < Filter( POINT ); AddressU( WRAP ); AddressV( WRAP ); >;
	float2 g_vUv < Attribute( "ProbeUv" ); >;
	float g_flFootprint < Attribute( "ProbeFootprint" ); >;
	int g_nMode < Attribute( "ProbeMode" ); >;
	float4 MainPs( PixelInput i ) : SV_Target0
	{
		if ( g_nMode == 0 )
		{
			uint w, h, n;
			g_tLinear.GetDimensions( 0, w, h, n );
			return float4( w, h, n, 1 );
		}
		if ( g_nMode == 2 )
		{
			float3 e = g_tEncoded.SampleLevel( g_sSource, g_vUv, 0 ).rgb;
			float3 lo = e / 12.92;
			float3 hi = pow( (e + .055) / 1.055, 2.4 );
			return float4( e.r <= .04045 ? lo.r : hi.r, e.g <= .04045 ? lo.g : hi.g, e.b <= .04045 ? lo.b : hi.b, 1 );
		}
		if ( g_nMode == 3 ) return g_tLinear.SampleLevel( g_sWrongPoint, g_vUv, 0 );
		return g_tLinear.SampleGrad( g_sSource, g_vUv, float2( g_flFootprint / 2, 0 ), float2( 0, g_flFootprint ) );
	}
}
