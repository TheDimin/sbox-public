HEADER
{
	Description = "glTF optical transmission and volume prototype; native lighting and framebuffer copy";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
}
MODES
{
	Forward();
	Depth();
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}
COMMON
{
	StaticCombo( S_RENDER_BACKFACES, F_RENDER_BACKFACES, Sys( ALL ) );
	StaticCombo( S_ALPHA_TEST, F_ALPHA_TEST, Sys( ALL ) );
	#define DEPTH_STATE_ALREADY_SET 1
	#define BLEND_MODE_ALREADY_SET 1
	#define S_TRANSLUCENT 1
	#include "common/shared.hlsl"
	#include "procedural.hlsl"
	#define S_UV2 1
}

// Slots 0 and 1 use the common input declarations. Slots 2, 3 and 4
// deliberately avoid TEXCOORD2/3, reserved for curvature and lightmap.
struct VertexInput
{
	#include "common/vertexinput.hlsl"
	float4 vColor : COLOR0 < Semantic( Color ); >;
	float2 vGltfUv2 : TEXCOORD4;
	float2 vGltfUv3 : TEXCOORD5;
	float2 vGltfUv4 : TEXCOORD6;
	float2 vGltfUv5 : TEXCOORD7;
	float2 vGltfUv6 : TEXCOORD8;
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vGltfUv23 : TEXCOORD8;
	float2 vGltfUv4 : TEXCOORD9;
	float4 vGltfUv56 : TEXCOORD10;
	float4 vColor : COLOR0;
};
VS
{
	#include "common/vertex.hlsl"
	PixelInput MainVs( VertexInput v )
	{
		PixelInput i = ProcessVertex( v );
		i.vColor = v.vColor;
		i.vGltfUv23 = float4( v.vGltfUv2, v.vGltfUv3 );
		i.vGltfUv4 = v.vGltfUv4;
		i.vGltfUv56 = float4( v.vGltfUv5, v.vGltfUv6 );
		return FinalizeVertex( i );
	}
}
PS
{
	#include "common/pixel.hlsl"
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : BACK );
	BoolAttribute( bWantsFBCopyTexture, true );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;
	SamplerState g_sGltfFrameCopy < Filter( TRILINEAR ); AddressU( CLAMP ); AddressV( CLAMP ); >;
	SamplerState g_sGltfBaseColor < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfBaseColorSampler" ); >;
	SamplerState g_sGltfMetallicRoughness < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfMetallicRoughnessSampler" ); >;
	SamplerState g_sGltfNormal < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfNormalSampler" ); >;
	SamplerState g_sGltfOcclusion < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfOcclusionSampler" ); >;
	SamplerState g_sGltfEmissive < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfEmissiveSampler" ); >;

	CreateInputTexture2D( GltfBaseColorTexture, Srgb, 8, "None", "_gltf_basecolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfMetallicRoughnessTexture, Linear, 8, "None", "_gltf_metallicroughness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfNormalTexture, Linear, 8, "None", "_gltf_normal", ",0/,0/0", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( GltfOcclusionTexture, Linear, 8, "None", "_gltf_occlusion", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfEmissiveTexture, Srgb, 8, "None", "_gltf_emissive", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfBaseColorTexture < Channel( RGBA, Box( GltfBaseColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	Texture2D g_tGltfMetallicRoughnessTexture < Channel( RGBA, Box( GltfMetallicRoughnessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfNormalTexture < Channel( RGBA, Box( GltfNormalTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfOcclusionTexture < Channel( RGBA, Box( GltfOcclusionTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfEmissiveTexture < Channel( RGBA, Box( GltfEmissiveTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;

	float4 g_vGltfBaseColor < Default4( 1, 1, 1, 1 ); >;
	float3 g_vGltfEmissiveFactor < Default3( 0, 0, 0 ); >;
	float g_flGltfMetallicFactor < Default1( 1 ); >;
	float g_flGltfRoughnessFactor < Default1( 1 ); >;
	float g_flGltfOcclusionStrength < Default1( 1 ); >;
	float g_flGltfNormalScale < Default1( 1 ); >;
	float g_flGltfAlphaCutoff < Default1( 0.5 ); >;
	float g_flGltfHasBaseColorTexture < Default1( 0 ); >;
	float g_flGltfBaseColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfBaseColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfBaseColorUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasMetallicRoughnessTexture < Default1( 0 ); >;
	float g_flGltfMetallicRoughnessUvSlot < Default1( 0 ); >;
	float3 g_vGltfMetallicRoughnessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfMetallicRoughnessUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasNormalTexture < Default1( 0 ); >;
	float g_flGltfNormalUvSlot < Default1( 0 ); >;
	float3 g_vGltfNormalUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfNormalUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasOcclusionTexture < Default1( 0 ); >;
	float g_flGltfOcclusionUvSlot < Default1( 0 ); >;
	float3 g_vGltfOcclusionUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfOcclusionUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasEmissiveTexture < Default1( 0 ); >;
	float g_flGltfEmissiveUvSlot < Default1( 0 ); >;
	float3 g_vGltfEmissiveUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfEmissiveUvRowV < Default3( 0, 1, 0 ); >;

	SamplerState g_sGltfOpticalTransmission < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfOpticalTransmissionSampler" ); >;
	CreateInputTexture2D( GltfOpticalTransmissionTexture, Linear, 8, "None", "_gltf_opticaltransmission", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfOpticalTransmissionTexture < Channel( RGBA, Box( GltfOpticalTransmissionTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	float g_flGltfHasOpticalTransmissionTexture < Default1( 0 ); >;
	float g_flGltfOpticalTransmissionUvSlot < Default1( 0 ); >;
	float3 g_vGltfOpticalTransmissionUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfOpticalTransmissionUvRowV < Default3( 0, 1, 0 ); >;
	SamplerState g_sGltfVolumeThickness < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfVolumeThicknessSampler" ); >;
	CreateInputTexture2D( GltfVolumeThicknessTexture, Linear, 8, "None", "_gltf_volumethickness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfVolumeThicknessTexture < Channel( RGBA, Box( GltfVolumeThicknessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	float g_flGltfHasVolumeThicknessTexture < Default1( 0 ); >;
	float g_flGltfVolumeThicknessUvSlot < Default1( 0 ); >;
	float3 g_vGltfVolumeThicknessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfVolumeThicknessUvRowV < Default3( 0, 1, 0 ); >;

	// Host code validates slot as an exact integer in [0,6]. Each branch is
	// uniform per draw; transformed coordinates remain independent per role.
	float2 GltfUv( PixelInput i, float slot, float3 rowU, float3 rowV )
	{
		float2 uv;
		if ( slot < 0.5 ) uv = i.vTextureCoords.xy;
		else if ( slot < 1.5 ) uv = i.vTextureCoords.zw;
		else if ( slot < 2.5 ) uv = i.vGltfUv23.xy;
		else if ( slot < 3.5 ) uv = i.vGltfUv23.zw;
		else if ( slot < 4.5 ) uv = i.vGltfUv4;
		else if ( slot < 5.5 ) uv = i.vGltfUv56.xy;
		else uv = i.vGltfUv56.zw;
		return float2( dot( float3( uv, 1 ), rowU ), dot( float3( uv, 1 ), rowV ) );
	}
	float g_flGltfOpticalTransmissionFactor < Default1( 0 ); >;
	float g_flOpticalDebug < Default1( 0 ); >;
	float g_flGltfVolumeThicknessMeshMeters < Default1( 0 ); >;
	float g_flGltfAttenuationDistanceInches < Default1( 0 ); >; // zero sentinel: +infinity
	float3 g_vGltfAttenuationColor < Default3( 1, 1, 1 ); >;
	float3 g_vGltfVolumeInverseRow0 < Attribute( "GltfVolumeInverseRow0" ); Default3( 0, 0, 0 ); >;
	float3 g_vGltfVolumeInverseRow1 < Attribute( "GltfVolumeInverseRow1" ); Default3( 0, 0, 0 ); >;
	float3 g_vGltfVolumeInverseRow2 < Attribute( "GltfVolumeInverseRow2" ); Default3( 0, 0, 0 ); >;

	float4 ShadeOpticalCandidate( Material m, PixelInput i, float strength, float thickness )
	{
		// Sample-renderer approximation, not a full path-traced BTDF. The caller
		// must bind the current inverse rigid node/placement transform and reject unsupported states.
		CombinerInput ci = ShadingModelStandard::MaterialToCombinerInput( m );
		LightingTerms_t terms = InitLightingTerms();
		ComputeDirectLighting( terms, ci );
		CalculateIndirectLighting( terms, ci );
		float3 diffuseAO = CalculateDiffuseAmbientOcclusion( ci, terms );
		float3 specularAO = CalculateSpecularAmbientOcclusion( ci, terms );
		float3 diffuse = (terms.vDiffuse * lerp( 1, diffuseAO, ci.flAmbientOcclusionDirectDiffuse ) + terms.vIndirectDiffuse * diffuseAO) * ci.vDiffuseColor;
		float3 specular = terms.vSpecular * lerp( 1, specularAO, ci.flAmbientOcclusionDirectSpecular ) + terms.vIndirectSpecular * specularAO;
		float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
		float3 fresnel = CalcBRDFReflectionFactor( saturate( dot( m.Normal, view ) ), m.Roughness, float3( .04, .04, .04 ) );
		float3 direction = normalize( refract( -view, normalize( m.Normal ), 1.0 / 1.5 ) );
		float3 localDirection = float3( dot( g_vGltfVolumeInverseRow0, direction ), dot( g_vGltfVolumeInverseRow1, direction ), dot( g_vGltfVolumeInverseRow2, direction ) );
		float inverseScale = length( localDirection );
		float3 ray = inverseScale > 1e-8 ? direction * thickness / inverseScale : 0;
		float2 uv = i.vPositionSs.xy * g_vInvViewportSize.xy;
		if ( thickness > 0 )
		{
			float4 p = Position4WsToPs( float4( i.vPositionWithOffsetWs.xyz + ray, 0 ) );
			uv = p.xy / p.w * .5 + .5; uv.y = 1 - uv.y;
		}
		uint w, h, levels; g_tFrameBufferCopyTexture.GetDimensions( 0, w, h, levels );
		float lod = saturate( m.Roughness ) * max( (float)levels - 1, 0 );
		float3 background = g_tFrameBufferCopyTexture.SampleLevel( g_sGltfFrameCopy, uv * g_vFrameBufferCopyInvSizeAndUvScale.zw, lod ).rgb;
		if ( g_flOpticalDebug == 1 ) return float4( strength, 1 - m.Metalness, 1 - fresnel.r, 1 );
		if ( g_flOpticalDebug == 2 ) return float4( uv, 0, 1 );
		if ( g_flOpticalDebug == 3 ) return float4( g_vFrameBufferCopyInvSizeAndUvScale.zw, 0, 1 );
		if ( g_flOpticalDebug == 4 ) return float4( background, 1 );
		if ( g_flOpticalDebug == 5 ) return float4( g_tFrameBufferCopyTexture.SampleLevel( g_sGltfFrameCopy, i.vPositionSs.xy / float2(w,h), 0 ).rgb, 1 );
		if ( g_flOpticalDebug == 6 ) return float4( g_tFrameBufferCopyTexture.Load( int3( i.vPositionSs.xy, 0 ) ).rgb, 1 );
		if ( g_flOpticalDebug == 7 ) return float4( g_tFrameBufferCopyTexture.SampleLevel( g_sGltfFrameCopy, uv, 0 ).rgb, 1 );
		if ( g_flOpticalDebug == 8 ) return float4( min( 1, w / 512.0 ), min( 1, h / 512.0 ), min( 1, levels / 16.0 ), 1 );
		float distanceInches = length( ray );
		float3 attenuation = 1;
		if ( g_flGltfAttenuationDistanceInches > 0 && distanceInches > 0 ) attenuation = pow( g_vGltfAttenuationColor, distanceInches / g_flGltfAttenuationDistanceInches );
		float3 surface = diffuse * (1 - strength) + specular + m.Emission;
		if ( g_bFogEnabled ) surface = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, surface );
		float3 transmitted = background * m.Albedo * attenuation * strength * (1 - m.Metalness) * (1 - fresnel);
		return float4( surface + transmitted, 1 );
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		float2 uvBaseColor = GltfUv( i, g_flGltfBaseColorUvSlot, g_vGltfBaseColorUvRowU, g_vGltfBaseColorUvRowV );
		float2 uvMetallicRoughness = GltfUv( i, g_flGltfMetallicRoughnessUvSlot, g_vGltfMetallicRoughnessUvRowU, g_vGltfMetallicRoughnessUvRowV );
		float2 uvNormal = GltfUv( i, g_flGltfNormalUvSlot, g_vGltfNormalUvRowU, g_vGltfNormalUvRowV );
		float2 uvOcclusion = GltfUv( i, g_flGltfOcclusionUvSlot, g_vGltfOcclusionUvRowU, g_vGltfOcclusionUvRowV );
		float2 uvEmissive = GltfUv( i, g_flGltfEmissiveUvSlot, g_vGltfEmissiveUvRowU, g_vGltfEmissiveUvRowV );
		float4 sampleBaseColor = g_flGltfHasBaseColorTexture > 0.5 ? g_tGltfBaseColorTexture.Sample( g_sGltfBaseColor, uvBaseColor ) : float4( 1, 1, 1, 1 );
		float4 sampleMetallicRoughness = g_flGltfHasMetallicRoughnessTexture > 0.5 ? g_tGltfMetallicRoughnessTexture.Sample( g_sGltfMetallicRoughness, uvMetallicRoughness ) : float4( 1, 1, 1, 1 );
		float4 sampleNormal = g_flGltfHasNormalTexture > 0.5 ? g_tGltfNormalTexture.Sample( g_sGltfNormal, uvNormal ) : float4( 1, 1, 1, 1 );
		float4 sampleOcclusion = g_flGltfHasOcclusionTexture > 0.5 ? g_tGltfOcclusionTexture.Sample( g_sGltfOcclusion, uvOcclusion ) : float4( 1, 1, 1, 1 );
		float4 sampleEmissive = g_flGltfHasEmissiveTexture > 0.5 ? g_tGltfEmissiveTexture.Sample( g_sGltfEmissive, uvEmissive ) : float4( 1, 1, 1, 1 );
		float3 normalTs = float3( 0, 0, 1 );
		if ( g_flGltfHasNormalTexture > 0.5 )
		{
			normalTs = sampleNormal.xyz * 2 - 1;
			normalTs.xy *= g_flGltfNormalScale;
			normalTs = normalize( normalTs );
		}
		m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
		m.Roughness = saturate( sampleMetallicRoughness.g * g_flGltfRoughnessFactor );
		m.Metalness = saturate( sampleMetallicRoughness.b * g_flGltfMetallicFactor );
		m.AmbientOcclusion = saturate( lerp( 1, sampleOcclusion.r, g_flGltfOcclusionStrength ) );
		m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
		#if ( S_ALPHA_TEST )
		float sourceAlpha = sampleBaseColor.a * g_vGltfBaseColor.a * saturate( i.vColor.a );
		clip( sourceAlpha - g_flGltfAlphaCutoff );
		#endif
		m.Opacity = i.vVertexColor.a; // OPAQUE ignores alpha; accepted MASK samples retain full coverage.
		// Stock TransformNormal flips Y for legacy Source1 textures; pre-flip
		// exactly once because glTF uses the tangent.w * cross(N,T) basis.
		normalTs.y = -normalTs.y;
		m.Normal = TransformNormal( normalTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
		#if ( S_RENDER_BACKFACES )
		if ( !i.face )
		{
			m.Normal = -m.Normal;
			m.GeometricNormal = -m.GeometricNormal;
		}
		#endif
		m.WorldTangentU = i.vTangentUWs;
		m.WorldTangentV = i.vTangentVWs;
		m.TextureCoords = uvBaseColor;
		float2 uvTransmission = GltfUv( i, g_flGltfOpticalTransmissionUvSlot, g_vGltfOpticalTransmissionUvRowU, g_vGltfOpticalTransmissionUvRowV );
		float2 uvThickness = GltfUv( i, g_flGltfVolumeThicknessUvSlot, g_vGltfVolumeThicknessUvRowU, g_vGltfVolumeThicknessUvRowV );
		float t = g_flGltfHasOpticalTransmissionTexture > .5 ? g_tGltfOpticalTransmissionTexture.Sample( g_sGltfOpticalTransmission, uvTransmission ).r : 1;
		float h = g_flGltfHasVolumeThicknessTexture > .5 ? g_tGltfVolumeThicknessTexture.Sample( g_sGltfVolumeThickness, uvThickness ).g : 1;
		return ShadeOpticalCandidate( m, i, saturate( g_flGltfOpticalTransmissionFactor * t ), g_flGltfVolumeThicknessMeshMeters * h );
	}
}
