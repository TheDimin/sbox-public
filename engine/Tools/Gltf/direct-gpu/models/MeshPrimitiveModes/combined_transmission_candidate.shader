HEADER
{
	Description = "glTF thin-surface KHR diffuse transmission candidate; native light/specular services";
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
	#ifndef S_TRANSLUCENT
	#define S_TRANSLUCENT 0
	#endif
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

	float g_flGltfPointLineNoNormal < Attribute( "GltfPointLineNoNormal" ); Default( 0 ); >;
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

	SamplerState g_sGltfDiffuseTransmission < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfDiffuseTransmissionSampler" ); >;
	CreateInputTexture2D( GltfDiffuseTransmissionTexture, Linear, 8, "None", "_gltf_diffusetransmission", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfDiffuseTransmissionTexture < Channel( RGBA, Box( GltfDiffuseTransmissionTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	float g_flGltfHasDiffuseTransmissionTexture < Default1( 0 ); >;
	float g_flGltfDiffuseTransmissionUvSlot < Default1( 0 ); >;
	float3 g_vGltfDiffuseTransmissionUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfDiffuseTransmissionUvRowV < Default3( 0, 1, 0 ); >;

	SamplerState g_sGltfDiffuseTransmissionColor < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfDiffuseTransmissionColorSampler" ); >;
	CreateInputTexture2D( GltfDiffuseTransmissionColorTexture, Srgb, 8, "None", "_gltf_diffusetransmissioncolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfDiffuseTransmissionColorTexture < Channel( RGBA, Box( GltfDiffuseTransmissionColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	float g_flGltfHasDiffuseTransmissionColorTexture < Default1( 0 ); >;
	float g_flGltfDiffuseTransmissionColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfDiffuseTransmissionColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfDiffuseTransmissionColorUvRowV < Default3( 0, 1, 0 ); >;

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
	float g_flGltfDiffuseTransmissionFactor < Default1( 0 ); >;
	float3 g_vGltfDiffuseTransmissionColorFactor < Default3( 1, 1, 1 ); >;

	// Release Candidate KHR_materials_diffuse_transmission, thin surface only.
	// Native transmission is thickness-based and does not express this BSDF.
	// Keep native light selection, cookies, shadows, specular and IBL services.
	float3 GltfDiffuseLight( Material m, float3 view, float3 direction, float3 radiance,
		float strength, float3 transmissionColor )
	{
		float cosine = dot( m.Normal, direction );
		float3 mirrorDirection = cosine < 0 ? direction - 2 * m.Normal * cosine : direction;
		float3 halfDirection = normalize( mirrorDirection + view );
		float fresnel = 0.04 + 0.96 * pow( 1 - saturate( dot( view, halfDirection ) ), 5 );
		float3 diffuse = cosine >= 0 ? (1 - strength) * m.Albedo : strength * transmissionColor;
		// Native light colors use the engine's PI-normalized BSDF convention:
		// ComputeGGXBRDF explicitly returns physical BRDF * PI, and native
		// Lambert irradiance has the same compensation. Keep both lobes in
		// that convention rather than dimming only diffuse by another PI.
		return radiance * abs( cosine ) * diffuse * (1 - m.Metalness) * (1 - fresnel);
	}
	float4 ShadeDiffuseTransmission( Material m, float strength, float3 transmissionColor )
	{
		AdjustAlphaToCoverage( m );
		CombinerInput front = ShadingModelStandard::MaterialToCombinerInput( m );
		LightingTerms_t frontTerms = InitLightingTerms();
		ComputeDirectLighting( frontTerms, front );
		CalculateIndirectLighting( frontTerms, front );
		// Rear irradiance must be sampled independently. Reusing front irradiance
		// leaks light through the wrong hemisphere; no thickness is fabricated.
		Material rearMaterial = m;
		rearMaterial.Normal = -m.Normal;
		rearMaterial.GeometricNormal = -m.GeometricNormal;
		CombinerInput rear = ShadingModelStandard::MaterialToCombinerInput( rearMaterial );
		LightingTerms_t rearTerms = InitLightingTerms();
		CalculateIndirectLighting( rearTerms, rear );
		float3 view = CalculatePositionToCameraDirWs( m.WorldPosition );
		float3 receiverNormal = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );
		float3 directDiffuse = 0;
		uint count = Light::Count( m.ScreenPosition );
		for ( uint index = 0; index < count; ++index )
		{
			Light light = Light::From( m.WorldPosition, m.ScreenPosition, index, m.LightmapUV, receiverNormal );
			if ( light.LightData.IsDiffuseEnabled() )
			{
				float3 direction = light.LightData.Type == LightType::LightTypeDirectional ? -light.LightData.GetDirection() : light.Direction;
				directDiffuse += GltfDiffuseLight( m, view, direction, light.Color * light.Attenuation * light.Visibility, strength, transmissionColor );
			}
		}
		if ( g_DirectionalLightEnabled && !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
		{
			float visibility = 1;
			if ( g_DirectionalLightCascadeCount > 0 ) visibility = DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, receiverNormal, m.ScreenPosition );
			float3 radiance = g_DirectionalLightColor.rgb;
			if ( LightModulators::Active() ) radiance *= LightModulators::EvaluateSun( m.WorldPosition, -g_DirectionalLightDirection.xyz );
			directDiffuse += GltfDiffuseLight( m, view, -g_DirectionalLightDirection.xyz, radiance * visibility, strength, transmissionColor );
		}
		float3 diffuseAO = CalculateDiffuseAmbientOcclusion( front, frontTerms );
		directDiffuse *= lerp( 1, diffuseAO, front.flAmbientOcclusionDirectDiffuse );
		// Use the native environment Fresnel integral; native specular is kept
		// unchanged, including metallic reflection and its authored albedo.
		float viewCosine = saturate( dot( m.Normal, view ) );
		float3 dielectricFresnel = CalcBRDFReflectionFactor( viewCosine, m.Roughness, float3( 0.04, 0.04, 0.04 ) );
		float3 indirectDiffuse = lerp( frontTerms.vIndirectDiffuse * m.Albedo,
			rearTerms.vIndirectDiffuse * transmissionColor, strength ) * (1 - m.Metalness) * (1 - dielectricFresnel) * diffuseAO;
		float3 specularAO = CalculateSpecularAmbientOcclusion( front, frontTerms );
		float3 specular = frontTerms.vSpecular * lerp( 1, specularAO, front.flAmbientOcclusionDirectSpecular ) + frontTerms.vIndirectSpecular * specularAO;
		float4 color = float4( directDiffuse + indirectDiffuse + specular + m.Emission, m.Opacity );
		if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, color.a );
		if ( g_bWireframeMode ) return g_vWireframeColor;
		if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( color, m, frontTerms );
		if ( g_bFogEnabled ) color.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, color.rgb );
		return color;
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
		float4 sampleEmissive = g_flGltfHasEmissiveTexture > 0.5 ? g_tGltfEmissiveTexture.Sample( g_sGltfEmissive, uvEmissive ) : float4( 1, 1, 1, 1 );
		#if ( S_ALPHA_TEST )
		float sourceAlpha = sampleBaseColor.a * g_vGltfBaseColor.a * saturate( i.vColor.a );
		clip( sourceAlpha - g_flGltfAlphaCutoff );
		#endif
		if ( g_flGltfPointLineNoNormal > .5 && !DepthNormals::WantsDepthNormals() && !ToolsVis::WantsToolsVis() )
		{
			m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
			m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
			m.Opacity = i.vVertexColor.a;
			m.TextureCoords = uvBaseColor;
			float4 color = float4( m.Albedo + m.Emission, m.Opacity );
			if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, color.a );
			if ( g_bWireframeMode ) return g_vWireframeColor;
			if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( color, m, InitLightingTerms() );
			if ( g_bFogEnabled ) color.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, color.rgb );
			return color;
		}
		float4 sampleMetallicRoughness = g_flGltfHasMetallicRoughnessTexture > 0.5 ? g_tGltfMetallicRoughnessTexture.Sample( g_sGltfMetallicRoughness, uvMetallicRoughness ) : float4( 1, 1, 1, 1 );
		float4 sampleNormal = g_flGltfHasNormalTexture > 0.5 ? g_tGltfNormalTexture.Sample( g_sGltfNormal, uvNormal ) : float4( 1, 1, 1, 1 );
		float4 sampleOcclusion = g_flGltfHasOcclusionTexture > 0.5 ? g_tGltfOcclusionTexture.Sample( g_sGltfOcclusion, uvOcclusion ) : float4( 1, 1, 1, 1 );
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
		// Debug/depth visualizations need the complete original material state.
		// Normal forward rendering took the early branch before sampling lit roles.
		if ( g_flGltfPointLineNoNormal > .5 )
		{
			float4 pointLineColor = float4( m.Albedo + m.Emission, m.Opacity );
			if ( DepthNormals::WantsDepthNormals() ) return DepthNormals::Output( m.Normal, m.Roughness, pointLineColor.a );
			if ( g_bWireframeMode ) return g_vWireframeColor;
			if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( pointLineColor, m, InitLightingTerms() );
			if ( g_bFogEnabled ) pointLineColor.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, pointLineColor.rgb );
			return pointLineColor;
		}
		float2 uvStrength = GltfUv( i, g_flGltfDiffuseTransmissionUvSlot, g_vGltfDiffuseTransmissionUvRowU, g_vGltfDiffuseTransmissionUvRowV );
		float2 uvColor = GltfUv( i, g_flGltfDiffuseTransmissionColorUvSlot, g_vGltfDiffuseTransmissionColorUvRowU, g_vGltfDiffuseTransmissionColorUvRowV );
		float alpha = g_flGltfHasDiffuseTransmissionTexture > 0.5 ? g_tGltfDiffuseTransmissionTexture.Sample( g_sGltfDiffuseTransmission, uvStrength ).a : 1;
		float3 color = g_flGltfHasDiffuseTransmissionColorTexture > 0.5 ? g_tGltfDiffuseTransmissionColorTexture.Sample( g_sGltfDiffuseTransmissionColor, uvColor ).rgb : 1;
		return ShadeDiffuseTransmission( m, g_flGltfDiffuseTransmissionFactor * alpha, g_vGltfDiffuseTransmissionColorFactor * color );
	}
}
