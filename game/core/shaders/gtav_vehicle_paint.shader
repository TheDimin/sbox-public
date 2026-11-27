HEADER
{
	Description = "GTA vehicle_paint1 body finish";
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

	CreateInputTexture2D( TextureColor, Srgb, 8, "", "_color", "Paint,10/10", Default3( 1.0, 1.0, 1.0 ) );
	CreateInputTexture2D( TextureSpecular, Linear, 8, "", "_specular", "Paint,10/20", Default4( 0.0, 0.0, 0.0, 1.0 ) );
	Texture2D g_tColor < Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tSpecular < Channel( RGBA, Box( TextureSpecular ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	float4 g_vColorTint < Default4( 1.0, 1.0, 1.0, 1.0 ); >;
	float4 g_vSpecResponse < Default4( 1.0, 0.0, 0.0, 0.15 ); >;
	float g_flSpecFalloff < Default( 180.0 ); >;
	float g_flMetalness < Default( 0.0 ); >;
	float g_flRoughnessScaleFactor < Default( 1.0 ); >;

	float3 WideSunHighlight( Material m )
	{
		if ( !g_DirectionalLightEnabled )
			return 0.0;

		float3 toLight = -g_DirectionalLightDirection.xyz;
		float3 toView = CalculatePositionToCameraDirWs( m.WorldPosition );
		float3 halfVector = NormalizeSafe( toLight + toView );
		float nDotL = saturate( dot( m.Normal, toLight ) );
		float nDotH = saturate( dot( m.Normal, halfVector ) );
		float4 map = g_tSpecular.Sample( g_sAniso, m.TextureCoords.xy );

		// Paint1 applies its specular map to the second lobe only. RGB selects the
		// intensity and alpha scales the authored wide falloff of 15.
		float strength = 0.75 * saturate( dot( map.rgb, g_vSpecResponse.rgb ) );
		// The wide lobe uses no extra Fresnel multiplier.
		float exponent = 15.0 * map.a;
		float specular = pow( max( nDotH, 1e-8 ), exponent ) * strength * nDotL;

		float visibility = 1.0;
		if ( g_DirectionalLightCascadeCount > 0 )
			visibility = DirectionalLightShadow::GetVisibility( m.WorldPosition, m.ScreenPosition );

		float3 lightColor = g_DirectionalLightColor.rgb;
		if ( LightModulators::Active() )
			lightColor *= LightModulators::EvaluateSun( m.WorldPosition, toLight );

		return lightColor * (specular * visibility);
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		m.Normal = NormalizeSafe( m.Normal );
		m.Albedo = g_tColor.Sample( g_sAniso, i.vTextureCoords.xy ).rgb * g_vColorTint.rgb;
		m.Metalness = saturate( g_flMetalness );
		// Map the authored sharp exponent to the engine's roughness so it also
		// controls probe and screen-space reflections.
		float exponent = clamp( g_flSpecFalloff, 1.0, 2048.0 );
		m.Roughness = saturate( pow( 2.0 / (exponent + 2.0), 0.25 ) * g_flRoughnessScaleFactor );
		if ( !DepthNormals::WantsDepthNormals() )
			m.Emission += WideSunHighlight( m );

		float reflectance = m.Metalness > 0.0 ? -1.0 : saturate( g_vSpecResponse.a );
		return ShadingModelStandard::Shade( m, reflectance );
	}
}
