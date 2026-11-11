HEADER
{
	DevShader = true;
	Description = "glTF reference sky: the environment cubemap at a fixed blur mip, like the Khronos Sample Viewer background";
	Version = 1;
}

MODES
{
	Forward();
}

FEATURES
{
	#include "vr_common_features.fxc"
}

COMMON
{
	#include "system.fxc"
	#include "vr_common.fxc"
}

struct VS_INPUT
{
	float4 vPositionOs : POSITION < Semantic( PosXyz ); >;
};

struct PS_INPUT
{
	float3 vPositionWs : TEXCOORD1;

	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
};

VS
{
	#define IS_SPRITECARD 1
	#include "system.fxc"

	PS_INPUT MainVs( const VS_INPUT i )
	{
		PS_INPUT o;
		float flSkyboxScale = g_flNearPlane + g_flFarPlane;
		float3 vPositionWs = g_vCameraPositionWs.xyz + i.vPositionOs.xyz * flSkyboxScale;
		o.vPositionPs = Position3WsToPs( vPositionWs );
		o.vPositionWs = vPositionWs;
		return o;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );

	BoolAttribute( sky, true );

	SamplerState g_sGltfSky < Filter( MIN_MAG_MIP_LINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
	TextureCube g_tSkyTexture < Attribute( "GltfSkyTexture" ); >;
	// Sample Viewer: textureLod( env, blur * ( mipCount - 1 ) ) with blur 0.6.
	float g_flGltfSkyBlur < Attribute( "GltfSkyBlur" ); Default( 0.6 ); >;
	float g_flGltfSkyGain < Attribute( "GltfSkyGain" ); Default( 1.0 ); >;
	float g_flGltfSkyYaw < Attribute( "GltfSkyYaw" ); Default( 0.0 ); >;

	struct PS_OUTPUT
	{
		float4 vColor0 : SV_Target0;
	};

	PS_OUTPUT MainPs( PS_INPUT i )
	{
		PS_OUTPUT o;
		float3 vRay = normalize( i.vPositionWs - g_vCameraPositionWs );
		float s, c;
		sincos( radians( -g_flGltfSkyYaw ), s, c );
		float3 vDir = float3( c * vRay.x - s * vRay.y, s * vRay.x + c * vRay.y, vRay.z );

		float flWidth, flHeight, flMips;
		g_tSkyTexture.GetDimensions( 0, flWidth, flHeight, flMips );
		float3 vColor = g_tSkyTexture.SampleLevel( g_sGltfSky, vDir, g_flGltfSkyBlur * ( flMips - 1.0 ) ).rgb;

		o.vColor0 = float4( vColor * g_flGltfSkyGain, 1.0 );
		return o;
	}
}
