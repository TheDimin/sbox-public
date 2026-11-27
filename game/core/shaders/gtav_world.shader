HEADER
{
	Description = "GTA world normal and specular material";
}

FEATURES
{
	#include "common/features.hlsl"
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
}

COMMON
{
	#include "common/shared.hlsl"
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

	PixelInput MainVs( VertexInput i )
	{
		return FinalizeVertex( ProcessVertex( i ) );
	}
}

PS
{
	#include "common/pixel.hlsl"

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Color,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureNormal, Linear, 8, "NormalizeNormals", "_normal", "Normal,10/20", Default3( 0.5, 0.5, 1.0 ) );
	CreateInputTexture2D( TextureSpecular, Linear, 8, "", "_specular", "Specular,10/30", Default4( 0.0, 0.0, 0.0, 1.0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tNormal < Channel( RGBA, Box( TextureNormal ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	Texture2D g_tSpecular < Channel( RGBA, Box( TextureSpecular ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;
	float4 g_vSpecResponse < Default4( 1.0, 0.0, 0.0, 0.125 ); >;
	float g_flSpecFalloff < Default( 100.0 ); >;

	float4 g_vColorTint < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float g_flMetalness < Default( 0.0 ); >;
	float g_flRoughnessScaleFactor < Default( 1.0 ); >;

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		float2 uv = i.vTextureCoords.xy;
		float4 color = g_tColor.Sample( g_sAniso, uv );
		float2 n = g_tNormal.Sample( g_sAniso, uv ).xy * 2.0 - 1.0;
		float3 tangentNormal = float3( n, sqrt( saturate( 1.0 - dot( n, n ) ) ) );
		// GTA stores a specular exponent in alpha. Convert it to an approximate
		// roughness for the s&box standard lighting model.
		float4 specular = g_tSpecular.Sample( g_sAniso, uv );
		float reflectance = saturate( dot( specular.rgb, g_vSpecResponse.rgb ) * g_vSpecResponse.a );
		float exponent = max( 0.0, specular.a * g_flSpecFalloff );
		Material m = Material::Init( i );
		m.Albedo = color.rgb * g_vColorTint.rgb;
		m.Metalness = g_flMetalness;
		m.Opacity = color.a;
		m.Roughness = saturate( pow( 2.0 / (exponent + 2.0), 0.25 ) * g_flRoughnessScaleFactor );
		m.Normal = normalize( tangentNormal.x * i.vTangentUWs + tangentNormal.y * i.vTangentVWs + tangentNormal.z * normalize( i.vNormalWs.xyz ) );
		return ShadingModelStandard::Shade( m, reflectance );
	}
}
