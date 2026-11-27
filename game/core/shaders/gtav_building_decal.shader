HEADER
{
	Description = "GTA authored building decal";

}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();

}

COMMON
{
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vDecalColor : COLOR0 < Semantic( Color ); >;
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	float g_flUseDecalVertexTint < Default( 1 ); >;

	PixelInput MainVs( VertexInput i )
	{
		PixelInput o = ProcessVertex( i );
		o.vVertexColor *= lerp( 1.0, i.vDecalColor, g_flUseDecalVertexTint );
		return FinalizeVertex( o );
	}
}

PS
{
	#define BLEND_MODE_ALREADY_SET
	#define DEPTH_STATE_ALREADY_SET
	#define S_TRANSLUCENT 1
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal,10/20", Default3( 0.5, 0.5, 1.0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	BoolAttribute( overlay, true );
	BoolAttribute( DoNotCastShadows, true );
	RenderState( DepthEnable, true );
	RenderState( DepthWriteEnable, false );
	RenderState( DepthFunc, GREATER_EQUAL );
	// A small constant offset separates coplanar decals. A slope offset
	// can pull a steep tunnel decal through the road above it.
	RenderState( DepthBias, 1 );
	RenderState( BlendEnable, true );
	RenderState( SrcBlend, SRC_ALPHA );
	RenderState( DstBlend, INV_SRC_ALPHA );

	float4 g_vDecalMask < Default4( 1, 0, 0, 0 ); >;
	float g_flDecalMode < Default( 0 ); >;
	float g_flColorReady < Default( 0 ); >;
	float g_flHasNormal < Default( 0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float4 sampleColor = g_tColor.Sample( g_sAniso, uv );
		Material m = Material::Init( i );
		m.Albedo = sampleColor.rgb;
		float coverage = sampleColor.a;
		if ( g_flDecalMode > 2.5 )
		{
			// Runtime distance textures store the glyph contour at the midpoint of the red channel.
			float distanceValue = sampleColor.r;
			float width = max( fwidth( distanceValue ), 0.001 );
			coverage = smoothstep( 0.5 - width, 0.5 + width, distanceValue );
			clip( coverage - 1.0 / 255.0 );
			m.Albedo = g_vDecalMask.rgb;
		}
		else if ( g_flDecalMode > 0.5 )
		{
			float3 channels = g_flDecalMode > 1.5 ? 1.0 - sampleColor.rgb : sampleColor.rgb;
			coverage = saturate( dot( channels, g_vDecalMask.xyz ) );
			m.Albedo = i.vVertexColor.rgb;
		}
		m.Opacity = coverage * i.vVertexColor.a * g_flColorReady;
		if ( g_flHasNormal > 0.5 )
		{
			float2 n = g_tNormal.Sample( g_sAniso, uv ).xy * 2 - 1;
			m.Normal = normalize( n.x * i.vTangentUWs + n.y * i.vTangentVWs + sqrt( saturate( 1 - dot( n, n ) ) ) * normalize( i.vNormalWs.xyz ) );
		}
		return ShadingModelStandard::Shade( i, m );
	}
}
