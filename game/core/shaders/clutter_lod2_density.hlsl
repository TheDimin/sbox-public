#ifndef CLUTTER_LOD2_DENSITY_HLSL
#define CLUTTER_LOD2_DENSITY_HLSL

// Both native culling and consuming materials use this exact placement decision.
// Rest translation is stable across append order, tile slots and frames.
uint ClutterDensityHash( uint value )
{
    value ^= value >> 16;
    value *= 0x7feb352du;
    value ^= value >> 15;
    value *= 0x846ca68bu;
    return value ^ (value >> 16);
}

// Whole depth layers share one decision across the view width. The phase is world
// anchored, so camera translation does not slide the pattern through placements.
float ClutterDepthLayerCoverage( float3 root, float4 layers, float retainedFraction )
{
    float horizontalLength = length( layers.xy );
    // Looking nearly straight down/up has no stable horizontal heading. Fade the
    // reduction out smoothly instead of selecting an arbitrary replacement axis.
    float headingWeight = smoothstep( 0.1, 0.25, horizontalLength );
    if ( headingWeight <= 0.0 ) return 1.0;
    if ( retainedFraction <= 0.0 ) return 1.0 - headingWeight;
    float depth = dot( root.xy, layers.xy / horizontalLength ) / layers.w;
    float cell = floor( depth );
    float local = depth - cell - 0.5;
    float random = float( ClutterDensityHash( asuint( (int)cell ) ) >> 8 ) * (1.0 / 16777216.0);
    float jitter = (random * 2.0 - 1.0) * (1.0 - retainedFraction) * 0.25;
    float halfWidth = retainedFraction * 0.5;
    float edge = min( retainedFraction, 1.0 - retainedFraction ) * 0.25;
    // Support fits inside its cell for every fraction. Value and slope are zero
    // at cell boundaries, even when the next cell has a different centre jitter.
    float coverage = 1.0 - smoothstep( halfWidth - edge, halfWidth + edge, abs( local - jitter ) );
    return lerp( 1.0, coverage, headingWeight );
}
float ClutterLod2Coverage( float3x4 transform, float3 boundsCenter, float3 camera,
    float tanHalfFov, float viewportWidth, float orthoWidth, float4 switches,
    float blendWidth, int lodCount, float retainedFraction, float4 depthLayers )
{
    if ( retainedFraction >= 1.0 || lodCount != 3 ) return 1.0;
    float3 root = float3( transform[0][3], transform[1][3], transform[2][3] );
    float farCoverage;
    if ( depthLayers.w > 0.0 )
    {
        farCoverage = ClutterDepthLayerCoverage( root, depthLayers, retainedFraction );
    }
    else
    {
        // Canonicalize signed zero before hashing the stored float bits.
        uint3 key = asuint( float3( root.x == 0 ? 0.0 : root.x,
            root.y == 0 ? 0.0 : root.y, root.z == 0 ? 0.0 : root.z ) );
        uint hash = ClutterDensityHash( key.x ^ ClutterDensityHash( key.y ^ ClutterDensityHash( key.z ) ) );
        farCoverage = float( hash >> 8 ) * (1.0 / 16777216.0) < retainedFraction ? 1.0 : 0.0;
    }
    if ( farCoverage >= 1.0 ) return 1.0;
    float scale = max( length( mul( transform, float4( 1, 0, 0, 0 ) ) ),
        max( length( mul( transform, float4( 0, 1, 0, 0 ) ) ), length( mul( transform, float4( 0, 0, 1, 0 ) ) ) ) );
    float threshold = switches.z * scale;
    if ( threshold <= 0.0 ) return 1.0;
    float width = max( 0.0, min( threshold * blendWidth, (threshold - switches.y * scale) * 0.49 ) );
    float3 center = mul( transform, float4( boundsCenter, 1 ) );
    float screen = orthoWidth > 0.0 ? saturate( 1.0 / orthoWidth )
        : saturate( 0.5 / max( length( center - camera ) * max( tanHalfFov, 0.00001 ), 0.00001 ) );
    float pixels = screen * viewportWidth;
    float metric = pixels > 0.0 ? 50.0 / pixels : 0.0;
    // Leave the complete native blend untouched, including its upper endpoint.
    float start = threshold + width;
    return lerp( 1.0, farCoverage, smoothstep( start, start + threshold * 0.25, metric ) );
}
#endif