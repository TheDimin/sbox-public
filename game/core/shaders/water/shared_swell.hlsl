// World inches and seconds. WaterBody.SampleBands uses the same dispersion relation.
static const float2 SharedSwellK = float2( 0.0024, 0.0018 );
float SharedSwellPhase( float2 position, float time )
{
	return dot( position, SharedSwellK ) - time * 1.076;
}
