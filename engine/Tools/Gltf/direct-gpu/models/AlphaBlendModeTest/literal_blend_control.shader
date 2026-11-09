HEADER
{
 Description = "Independent literal blue translucent plane for native interleaving control";
}
FEATURES
{
 #include "common/features.hlsl"
 Feature( F_TRANSLUCENT, 0..1, "Rendering" );
}
MODES
{
 Forward();
 Depth();
}
COMMON
{
 #include "common/shared.hlsl"
 #include "procedural.hlsl"
}
struct VertexInput
{
 #include "common/vertexinput.hlsl"
};
struct PixelInput
{
 #include "common/pixelinput.hlsl"
};
VS
{
 #include "common/vertex.hlsl"
 PixelInput MainVs( VertexInput v ) { PixelInput i=ProcessVertex( v ); return FinalizeVertex( i ); }
}
PS
{
 StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
 #define DEPTH_STATE_ALREADY_SET
 #define BLEND_MODE_ALREADY_SET
 #include "common/pixel.hlsl"
 RenderState( CullMode, NONE );
 RenderState( DepthEnable, true );
 RenderState( DepthFunc, GREATER_EQUAL );
 RenderState( DepthWriteEnable, false );
 RenderState( BlendEnable, true );
 RenderState( SrcBlend, SRC_ALPHA );
 RenderState( DstBlend, INV_SRC_ALPHA );
 RenderState( BlendOp, ADD );
 RenderState( SrcBlendAlpha, ONE );
 RenderState( DstBlendAlpha, INV_SRC_ALPHA );
 RenderState( BlendOpAlpha, ADD );
 float4 g_vLiteralColor < Default4( 0, 0, 1, .5 ); >;
 float4 MainPs( PixelInput i ) : SV_Target0 { return g_vLiteralColor; }
}
