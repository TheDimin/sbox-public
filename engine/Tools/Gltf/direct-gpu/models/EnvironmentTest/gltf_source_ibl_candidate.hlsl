// Model-local, UNWIRED candidate. No extension registration or shared shader edit.
// Input cube contains source-authored mips, expanded from RGBD to linear floats
// and permuted into native engine cube axes. Coefficients stay in glTF axes.
// Caller must apply stock material/BRDF factors exactly once to these terms.
#ifndef GLTF_SOURCE_IBL_CANDIDATE_HLSL
#define GLTF_SOURCE_IBL_CANDIDATE_HLSL

TextureCube<float4> g_tGltfIblSourceCube < Attribute( "GltfIblSourceCube" ); >;
SamplerState g_sGltfIblSource < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); AddressW( CLAMP ); >;
float g_flGltfSourceIblEnabled < Default1( 0 ); >;
float g_flGltfIblIntensity < Default1( 1 ); >;
float4 g_vGltfIblWorldToEnvironmentRow0 < Default4( 1, 0, 0, 0 ); >;
float4 g_vGltfIblWorldToEnvironmentRow1 < Default4( 0, 1, 0, 0 ); >;
float4 g_vGltfIblWorldToEnvironmentRow2 < Default4( 0, 0, 1, 0 ); >;
float4 g_vGltfIblSh0 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh1 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh2 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh3 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh4 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh5 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh6 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh7 < Default4( 0, 0, 0, 0 ); >;
float4 g_vGltfIblSh8 < Default4( 0, 0, 0, 0 ); >;

float3 GltfIblLocalEngineDirection( float3 worldDirection )
{
    return normalize( float3( dot( g_vGltfIblWorldToEnvironmentRow0.xyz, worldDirection ),
                             dot( g_vGltfIblWorldToEnvironmentRow1.xyz, worldDirection ),
                             dot( g_vGltfIblWorldToEnvironmentRow2.xyz, worldDirection ) ) );
}

float3 GltfIblLambertianRadiance( float3 worldNormal )
{
    if ( g_flGltfSourceIblEnabled < 0.5f ) return 0.0f;
    // Inverse of engine=(gltf.z,gltf.x,gltf.y). The environment's inverse
    // rotation is already represented in the three world-to-environment rows.
    float3 n = GltfIblLocalEngineDirection( worldNormal ).yzx;
    // Coefficient order and Condon-Shortley signs match the primary Babylon
    // EXT loader/SH implementation. Authored coefficients are irradiance;
    // divide by pi once, without convolving an already-convolved signal.
    float3 irradiance = g_vGltfIblSh0.xyz * 0.28209479177387814f
        - g_vGltfIblSh1.xyz * (0.4886025119029199f * n.y)
        + g_vGltfIblSh2.xyz * (0.4886025119029199f * n.z)
        - g_vGltfIblSh3.xyz * (0.4886025119029199f * n.x)
        + g_vGltfIblSh4.xyz * (1.0925484305920792f * n.x * n.y)
        - g_vGltfIblSh5.xyz * (1.0925484305920792f * n.y * n.z)
        + g_vGltfIblSh6.xyz * (0.31539156525252005f * (3.0f * n.z * n.z - 1.0f))
        - g_vGltfIblSh7.xyz * (1.0925484305920792f * n.x * n.z)
        + g_vGltfIblSh8.xyz * (0.5462742152960396f * (n.x * n.x - n.y * n.y));
    return max( 0.0f, irradiance ) * (g_flGltfIblIntensity / 3.14159265358979323846f);
}

float3 GltfIblPrefilteredRadiance( float3 worldReflection, float linearRoughness )
{
    if ( g_flGltfSourceIblEnabled < 0.5f ) return 0.0f;
    uint width, height, mipCount;
    g_tGltfIblSourceCube.GetDimensions( 0, width, height, mipCount );
    float maxMip = float( max( mipCount, 1u ) - 1u );
    float lod = saturate( linearRoughness ) * maxMip;
    return g_tGltfIblSourceCube.SampleLevel( g_sGltfIblSource, GltfIblLocalEngineDirection( worldReflection ), lod ).rgb * g_flGltfIblIntensity;
}

#endif
