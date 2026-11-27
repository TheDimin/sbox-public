#ifndef WATER_BATCH_HLSL
#define WATER_BATCH_HLSL
struct WaterBatchTile
{
	float4 bounds, waves, bands[4], overlays;
};
StructuredBuffer<WaterBatchTile> WaterBatchTiles < Attribute( "WaterBatchTiles" ); >;
int WaterBatchEnabled < Attribute( "WaterBatchEnabled" ); Default( 0 ); >;

// Finite tiled bodies have no horizontal displacement. Keep the four local bands,
// edge derivative and shared swell identical to WaterBody.SampleBands.
void WaterBatchSample( WaterBatchTile tile, float2 world, float footprint, float2 fade, out float height, out float2 slope )
{
	float2 local = world - tile.bounds.xy;
	height = 0;
	slope = 0;
	float weights[4] = { 0.5, 0.27, 0.15, 0.08 };
	[unroll] for ( int i = 0; i < 4; ++i )
	{
		float4 b = tile.bands[i];
		float a = tile.waves.x * weights[i] * (1 - smoothstep( fade.x, fade.y, footprint * b.z ));
		float phase = b.z * dot( b.xy, world ) - b.w * WaveTime + i * 1.7;
		height += a * sin( phase );
		slope += b.xy * (a * b.z * cos( phase ));
	}
	float2 edge = tile.bounds.zw - abs( local );
	float t = saturate( min( edge.x, edge.y ) / tile.waves.z );
	float w = t * t * (3 - 2 * t);
	float2 inward = edge.x < edge.y ? float2( -sign( local.x ), 0 ) : float2( 0, -sign( local.y ) );
	float phase = SharedSwellPhase( world, WaveTime );
	slope = slope * w + inward * (height * 6 * t * (1 - t) / tile.waves.z)
		+ SharedSwellK * (tile.waves.y * cos( phase ));
	height = height * w + tile.waves.y * sin( phase );
}
#endif
