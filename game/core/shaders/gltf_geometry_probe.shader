HEADER
{
	DevShader = true;
	Description = "Integration test for managed glTF geometry upload and indexed drawing.";
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
	float4 vColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#if ( PROGRAM == VFX_PROGRAM_VS )
		float4 vPositionPs : SV_Position;
	#endif
	#if ( PROGRAM == VFX_PROGRAM_PS )
		float4 vPositionSs : SV_Position;
	#endif
	float4 vColor : COLOR0;
};

VS
{
	PixelInput MainVs( VertexInput i )
	{
		PixelInput o;
		// The pinned Triangle spans glTF XY [0,1]. Draw decoded engine YZ inches
		// in clip space, keeping coordinate conversion part of this construction proof.
		o.vPositionPs = float4( i.vPositionOs.yz * 0.0254 * 1.5 - 0.75, 0.5, 1.0 );
		o.vColor = i.vColor;
		return o;
	}
}

PS
{
	RenderState( CullMode, NONE );
	RenderState( DepthEnable, false );
	RenderState( DepthWriteEnable, false );
	RenderState( BlendEnable, false );
	float4 g_vProbeColor < Attribute( "ProbeColor" ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		return i.vColor * g_vProbeColor;
	}
}
