// GTA ocean detail in world metres. Decode the native s&box normal texture from RG.
// The simulation supplies displacement separately through WaterBatchSample.
float2 MountedOceanSlope( MOUNTED_NORMAL_TEXTURE normals, SamplerState filtering, float2 inches, float2 dx,
	float2 dy, float seconds, float distanceInches, float strength )
{
	float2 metres = inches * 0.0254;
	dx *= 0.0254;
	dy *= 0.0254;
	float drift = seconds * 0.015;
	float2 longA = normals.SampleGrad( filtering, metres.yx / 448 + drift, dx.yx / 448, dy.yx / 448 ).rg * 2 - 1;
	float2 longB = normals.SampleGrad( filtering, metres / 512 - drift, dx / 512, dy / 512 ).rg * 2 - 1;
	float2 slope = (longA.yx + longB) * 0.45;

	// A pixel footprint gate also handles grazing views at short range.
	float detail = (1 - smoothstep( 90, 160, distanceInches * 0.0254 ))
		* (1 - smoothstep( 0.5, 2.5, max( length( dx ), length( dy ) ) ));
	if ( detail > 0 )
	{
		float2 uv = metres / 25;
		float2 ripple = normals.SampleGrad( filtering, uv + float2( drift, 0 ), dx / 25, dy / 25 ).rg;
		float2 chop = normals.SampleGrad( filtering, uv * 3.7 + float2( 0, -drift ), dx * 0.148, dy * 0.148 ).rg;
		slope += (ripple + chop - 1) * detail;
	}
	return slope * strength;
}
