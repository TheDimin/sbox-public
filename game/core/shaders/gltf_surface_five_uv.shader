HEADER
{
	Description = "glTF metal/rough and spec/gloss source textures, five UV inputs, stock Surface/Lit base with the KHR material layers";
}
FEATURES
{
	#include "common/features.hlsl"
	Feature( F_ALPHA_TEST, 0..1, "Rendering" );
	Feature( F_TRANSLUCENT, 0..1, "Rendering" );
	FeatureRule( Allow1( F_ALPHA_TEST, F_TRANSLUCENT ), "Mask and blend are exclusive" );
	// A static combo multiplies the compiled variants, so only a layer that changes render state or the stock
	// lighting structs has one. Clearcoat, sheen, specular, ior, iridescence and spec/gloss are uniform branches.
	// F_GLTF_CLEARCOAT has no combo; it stays declared so a host built before that change still loads.
	Feature( F_GLTF_CLEARCOAT, 0..1, "glTF" );
	Feature( F_GLTF_ANISOTROPY, 0..1, "glTF" );
	Feature( F_GLTF_TRANSMISSION, 0..1, "glTF" );
	FeatureRule( Requires1( F_GLTF_TRANSMISSION, F_TRANSLUCENT ), "Transmission draws in the translucent pass" );
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
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	StaticCombo( S_GLTF_ANISOTROPY, F_GLTF_ANISOTROPY, Sys( ALL ) );
	StaticCombo( S_GLTF_TRANSMISSION, F_GLTF_TRANSMISSION, Sys( ALL ) );
	// KHR_materials_anisotropy uses the stock anisotropic GGX and anisotropic cubemap path.
	#define S_ANISOTROPIC_GLOSS S_GLTF_ANISOTROPY
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
};
struct PixelInput
{
	#include "common/pixelinput.hlsl"
	float4 vGltfUv23 : TEXCOORD8;
	float2 vGltfUv4 : TEXCOORD9;
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
		return FinalizeVertex( i );
	}
}
PS
{
	#define DEPTH_STATE_ALREADY_SET
	#define BLEND_MODE_ALREADY_SET
	#include "common/pixel.hlsl"
	RenderState( DepthEnable, true );
	RenderState( DepthFunc, GREATER_EQUAL );
	// KHR_materials_transmission samples the opaque scene behind the surface.
	BoolAttribute( bWantsFBCopyTexture, S_GLTF_TRANSMISSION );
	Texture2D g_tFrameBufferCopyTexture < Attribute( "FrameBufferCopyTexture" ); SrgbRead( false ); >;
	#if S_TRANSLUCENT
		RenderState( BlendEnable, true );
		RenderState( SrcBlend, SRC_ALPHA );
		RenderState( DstBlend, INV_SRC_ALPHA );
		RenderState( BlendOp, ADD );
		RenderState( SrcBlendAlpha, ONE );
		RenderState( DstBlendAlpha, INV_SRC_ALPHA );
		RenderState( BlendOpAlpha, ADD );
		RenderState( DepthWriteEnable, false );
	#else
		RenderState( BlendEnable, false );
		RenderState( DepthWriteEnable, true );
	#endif
	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : BACK );
	SamplerState g_sGltfBaseColor < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfBaseColorSampler" ); >;
	SamplerState g_sGltfMetallicRoughness < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfMetallicRoughnessSampler" ); >;
	SamplerState g_sGltfNormal < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfNormalSampler" ); >;
	SamplerState g_sGltfOcclusion < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfOcclusionSampler" ); >;
	SamplerState g_sGltfEmissive < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfEmissiveSampler" ); >;
	// One sampler per extension family, to stay inside the 32 sampler bindings of a pixel shader.
	// The host binds the family sampler of the texture(s) present and rejects a material whose textures
	// in one family use different glTF samplers.
	SamplerState g_sGltfClearcoat < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfClearcoatSampler" ); >;
	SamplerState g_sGltfSheen < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfSheenSampler" ); >;
	SamplerState g_sGltfSpecular < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfSpecularSampler" ); >;
	SamplerState g_sGltfIridescence < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfIridescenceSampler" ); >;
	SamplerState g_sGltfTransmission < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfTransmissionSampler" ); >;
	SamplerState g_sGltfAnisotropy < Filter( BILINEAR ); AddressU( WRAP ); AddressV( WRAP ); Attribute( "GltfAnisotropySampler" ); >;

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
	CreateInputTexture2D( GltfClearcoatTexture, Linear, 8, "None", "_gltf_clearcoat", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfClearcoatRoughnessTexture, Linear, 8, "None", "_gltf_clearcoatroughness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	CreateInputTexture2D( GltfClearcoatNormalTexture, Linear, 8, "None", "_gltf_clearcoatnormal", ",0/,0/0", Default4( 0.5, 0.5, 1, 1 ) );
	CreateInputTexture2D( GltfAnisotropyTexture, Linear, 8, "None", "_gltf_anisotropy", ",0/,0/0", Default4( 1, 0.5, 1, 1 ) );
	Texture2D g_tGltfClearcoatTexture < Channel( RGBA, Box( GltfClearcoatTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfClearcoatRoughnessTexture < Channel( RGBA, Box( GltfClearcoatRoughnessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfClearcoatNormalTexture < Channel( RGBA, Box( GltfClearcoatNormalTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfSheenColorTexture, Srgb, 8, "None", "_gltf_sheencolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfSheenColorTexture < Channel( RGBA, Box( GltfSheenColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	CreateInputTexture2D( GltfSheenRoughnessTexture, Linear, 8, "None", "_gltf_sheenroughness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfSheenRoughnessTexture < Channel( RGBA, Box( GltfSheenRoughnessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfSpecularTexture, Linear, 8, "None", "_gltf_specular", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfSpecularTexture < Channel( RGBA, Box( GltfSpecularTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfSpecularColorTexture, Srgb, 8, "None", "_gltf_specularcolor", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfSpecularColorTexture < Channel( RGBA, Box( GltfSpecularColorTexture ), Srgb ); OutputFormat( RGBA8888 ); SrgbRead( True ); >;
	CreateInputTexture2D( GltfIridescenceTexture, Linear, 8, "None", "_gltf_iridescence", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfIridescenceTexture < Channel( RGBA, Box( GltfIridescenceTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfIridescenceThicknessTexture, Linear, 8, "None", "_gltf_iridescencethickness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfIridescenceThicknessTexture < Channel( RGBA, Box( GltfIridescenceThicknessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfTransmissionTexture, Linear, 8, "None", "_gltf_transmission", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfTransmissionTexture < Channel( RGBA, Box( GltfTransmissionTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	CreateInputTexture2D( GltfThicknessTexture, Linear, 8, "None", "_gltf_thickness", ",0/,0/0", Default4( 1, 1, 1, 1 ) );
	Texture2D g_tGltfThicknessTexture < Channel( RGBA, Box( GltfThicknessTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;
	Texture2D g_tGltfAnisotropyTexture < Channel( RGBA, Box( GltfAnisotropyTexture ), Linear ); OutputFormat( RGBA8888 ); SrgbRead( False ); >;

	float g_flGltfPointLineNoNormal < Attribute( "GltfPointLineNoNormal" ); Default( 0 ); >;
	float4 g_vGltfBaseColor < Default4( 1, 1, 1, 1 ); >;
	float3 g_vGltfEmissiveFactor < Default3( 0, 0, 0 ); >;
	float g_flGltfMetallicFactor < Default1( 1 ); >;
	float g_flGltfRoughnessFactor < Default1( 1 ); >;
	float g_flGltfOcclusionStrength < Default1( 1 ); >;
	float g_flGltfAlphaCutoff < Default1( 0.5 ); >;
	float g_flGltfNormalScale < Default1( 1 ); >;
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

	// Set when the material uses an extension the reader lacks: draw the engine error pattern.
	float g_flGltfUnsupported < Default1( 0 ); >;
	// KHR_materials_clearcoat
	float g_flGltfClearcoatFactor < Default1( 0 ); >;
	float g_flGltfClearcoatRoughnessFactor < Default1( 0 ); >;
	float g_flGltfClearcoatNormalScale < Default1( 1 ); >;
	float g_flGltfHasClearcoatTexture < Default1( 0 ); >;
	float g_flGltfClearcoatUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasClearcoatRoughnessTexture < Default1( 0 ); >;
	float g_flGltfClearcoatRoughnessUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatRoughnessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatRoughnessUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasClearcoatNormalTexture < Default1( 0 ); >;
	float g_flGltfClearcoatNormalUvSlot < Default1( 0 ); >;
	float3 g_vGltfClearcoatNormalUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfClearcoatNormalUvRowV < Default3( 0, 1, 0 ); >;

	// KHR_materials_ior, specular, sheen, iridescence: switched on per material by the *On flags.
	// KHR_materials_pbrSpecularGlossiness: the host puts diffuse in the base colour slot and the specular-glossiness
	// texture (sRGB specular in RGB, linear glossiness in A) in the specular colour slot.
	float g_flGltfSpecGloss < Default1( 0 ); >;
	float g_flGltfGlossinessFactor < Default1( 1 ); >;
	float g_flGltfIorOn < Default1( 0 ); >;
	float g_flGltfIor < Default1( 1.5 ); >;
	float g_flGltfSpecularOn < Default1( 0 ); >;
	float g_flGltfSpecularFactor < Default1( 1 ); >;
	float3 g_vGltfSpecularColorFactor < Default3( 1, 1, 1 ); >;
	float g_flGltfSheenOn < Default1( 0 ); >;
	float g_flGltfSheenRoughnessFactor < Default1( 0 ); >;
	float3 g_vGltfSheenColorFactor < Default3( 0, 0, 0 ); >;
	float g_flGltfIridescenceOn < Default1( 0 ); >;
	float g_flGltfIridescenceFactor < Default1( 0 ); >;
	float g_flGltfIridescenceIor < Default1( 1.3 ); >;
	float g_flGltfIridescenceThicknessMin < Default1( 100 ); >;
	float g_flGltfIridescenceThicknessMax < Default1( 400 ); >;
	float g_flGltfHasSheenColorTexture < Default1( 0 ); >;
	float g_flGltfSheenColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfSheenColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfSheenColorUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasSheenRoughnessTexture < Default1( 0 ); >;
	float g_flGltfSheenRoughnessUvSlot < Default1( 0 ); >;
	float3 g_vGltfSheenRoughnessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfSheenRoughnessUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasSpecularTexture < Default1( 0 ); >;
	float g_flGltfSpecularUvSlot < Default1( 0 ); >;
	float3 g_vGltfSpecularUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfSpecularUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasSpecularColorTexture < Default1( 0 ); >;
	float g_flGltfSpecularColorUvSlot < Default1( 0 ); >;
	float3 g_vGltfSpecularColorUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfSpecularColorUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasIridescenceTexture < Default1( 0 ); >;
	float g_flGltfIridescenceUvSlot < Default1( 0 ); >;
	float3 g_vGltfIridescenceUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfIridescenceUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasIridescenceThicknessTexture < Default1( 0 ); >;
	float g_flGltfIridescenceThicknessUvSlot < Default1( 0 ); >;
	float3 g_vGltfIridescenceThicknessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfIridescenceThicknessUvRowV < Default3( 0, 1, 0 ); >;
	// KHR_materials_transmission, volume, dispersion (thickness in metres; coefficient per metre).
	float g_flGltfTransmissionFactor < Default1( 0 ); >;
	float g_flGltfDispersion < Default1( 0 ); >;
	float g_flGltfThicknessFactor < Default1( 0 ); >;
	float3 g_vGltfAttenuationCoefficient < Default3( 0, 0, 0 ); >;
	float g_flGltfHasTransmissionTexture < Default1( 0 ); >;
	float g_flGltfTransmissionUvSlot < Default1( 0 ); >;
	float3 g_vGltfTransmissionUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfTransmissionUvRowV < Default3( 0, 1, 0 ); >;
	float g_flGltfHasThicknessTexture < Default1( 0 ); >;
	float g_flGltfThicknessUvSlot < Default1( 0 ); >;
	float3 g_vGltfThicknessUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfThicknessUvRowV < Default3( 0, 1, 0 ); >;
	// KHR_materials_anisotropy
	float g_flGltfAnisotropyStrength < Default1( 0 ); >;
	float2 g_vGltfAnisotropyRotation < Default2( 1, 0 ); >;
	float g_flGltfHasAnisotropyTexture < Default1( 0 ); >;
	float g_flGltfAnisotropyUvSlot < Default1( 0 ); >;
	float3 g_vGltfAnisotropyUvRowU < Default3( 1, 0, 0 ); >;
	float3 g_vGltfAnisotropyUvRowV < Default3( 0, 1, 0 ); >;

	// Host code validates slot as an exact integer in [0,4]. Each branch is
	// uniform per draw; transformed coordinates remain independent per role.
	float2 GltfUv( PixelInput i, float slot, float3 rowU, float3 rowV )
	{
		float2 uv;
		if ( slot < 0.5 ) uv = i.vTextureCoords.xy;
		else if ( slot < 1.5 ) uv = i.vTextureCoords.zw;
		else if ( slot < 2.5 ) uv = i.vGltfUv23.xy;
		else if ( slot < 3.5 ) uv = i.vGltfUv23.zw;
		else uv = i.vGltfUv4;
		return float2( dot( float3( uv, 1 ), rowU ), dot( float3( uv, 1 ), rowV ) );
	}

	// Per-pixel values of the layers. Whether a layer is on is a material uniform (g_flGltf*On, the
	// clearcoat factor), never a field here: a branch on a uniform is the same for every pixel of a draw,
	// so a material pays only for the layers it has.
	struct GltfLayers
	{
		float ior; float specFactor; float3 specColor;
		float3 sheenColor; float sheenRoughness;
		float irid; float iridIor; float iridThickness;
		float coat; float coatRoughness; float3 coatNormal;
		float transmission; float3 transmitted;
	};

	// Khronos glTF Sample Renderer, brdf.glsl / iridescence: thin-film interference Fresnel (Belcour and Barla).
	float3 GltfEvalSensitivity( float opd, float3 shift )
	{
		float phase = 2.0 * 3.14159265 * opd * 1.0e-9;
		float3 val = float3( 5.4856e-13, 4.4201e-13, 5.2481e-13 );
		float3 pos = float3( 1.6810e+06, 1.7953e+06, 2.2084e+06 );
		float3 var = float3( 4.3278e+09, 9.3046e+09, 6.6121e+09 );
		float3 xyz = val * sqrt( 2.0 * 3.14159265 * var ) * cos( pos * phase + shift ) * exp( -var * phase * phase );
		xyz.x += 9.7470e-14 * sqrt( 2.0 * 3.14159265 * 4.5282e+09 ) * cos( 2.2399e+06 * phase + shift.x ) * exp( -4.5282e+09 * phase * phase );
		xyz /= 1.0685e-7;
		return float3( dot( xyz, float3( 3.2404542, -1.5371385, -0.4985314 ) ), dot( xyz, float3( -0.9692660, 1.8760108, 0.0415560 ) ), dot( xyz, float3( 0.0556434, -0.2040259, 1.0572252 ) ) );
	}

	float3 GltfEvalIridescence( float outsideIor, float eta2, float cosTheta1, float thickness, float3 baseF0 )
	{
		float iridIor = lerp( outsideIor, eta2, smoothstep( 0.0, 0.03, thickness ) );
		float sinTheta2Sq = pow( outsideIor / iridIor, 2.0 ) * ( 1.0 - cosTheta1 * cosTheta1 );
		float cosTheta2Sq = 1.0 - sinTheta2Sq;
		if ( cosTheta2Sq < 0.0 ) return 1.0.xxx;
		float cosTheta2 = sqrt( cosTheta2Sq );
		float r0 = pow( ( iridIor - outsideIor ) / ( iridIor + outsideIor ), 2.0 );
		float r12 = r0 + ( 1.0 - r0 ) * pow( 1.0 - cosTheta1, 5.0 );
		float t121 = 1.0 - r12;
		float phi12 = iridIor < outsideIor ? 3.14159265 : 0.0;
		float phi21 = 3.14159265 - phi12;
		float3 f0c = clamp( baseF0, 0.0, 0.9999 );
		float3 baseIor = ( 1.0 + sqrt( f0c ) ) / ( 1.0 - sqrt( f0c ) );
		float3 r1 = pow( ( baseIor - iridIor ) / ( baseIor + iridIor ), 2.0 );
		float3 r23 = r1 + ( 1.0 - r1 ) * pow( 1.0 - cosTheta2, 5.0 );
		float3 phi23 = float3( baseIor.x < iridIor ? 3.14159265 : 0.0, baseIor.y < iridIor ? 3.14159265 : 0.0, baseIor.z < iridIor ? 3.14159265 : 0.0 );
		float opd = 2.0 * iridIor * thickness * cosTheta2;
		float3 phi = phi21.xxx + phi23;
		float3 r123 = clamp( r12 * r23, 1e-5, 0.9999 );
		float3 rr123 = sqrt( r123 );
		float3 rs = t121 * t121 * r23 / ( 1.0 - r123 );
		float3 i = r12 + rs;
		float3 cm = rs - t121;
		cm *= rr123; i += cm * 2.0 * GltfEvalSensitivity( opd, phi );
		cm *= rr123; i += cm * 2.0 * GltfEvalSensitivity( 2.0 * opd, 2.0 * phi );
		return max( i, 0.0 );
	}

	// Directional albedo of the Charlie sheen lobe, as an analytic fit (the fit three.js uses, after Estevez and
	// Kulla). The Khronos viewer reads the same quantity from a lookup texture; a fit costs no texture or sampler binding.
	float GltfSheenAlbedo( float flNdotV, float flRoughness )
	{
		float r2 = flRoughness * flRoughness;
		float a = flRoughness < 0.25 ? -339.2 * r2 + 161.4 * flRoughness - 25.9 : -8.48 * r2 + 14.3 * flRoughness - 9.95;
		float b = flRoughness < 0.25 ? 44.0 * r2 - 23.7 * flRoughness + 3.26 : 1.97 * r2 - 3.27 * flRoughness + 0.72;
		float flDG = exp( a * flNdotV + b ) + ( flRoughness < 0.25 ? 0.0 : 0.1 * ( flRoughness - 0.25 ) );
		return saturate( flDG * 0.31830988 );
	}

	// One light for the lobes that the stock base lighting does not have: clearcoat specular (GGX, F0 0.04,
	// its own normal and roughness) and sheen (Charlie). The stock BRDF helpers return BRDF * PI, as the
	// stock light colours expect.
	void GltfLayerLight( float3 vLightDir, float3 vRadiance, float3 vViewDir, float3 vNormalWs, GltfLayers layers, float2 vPositionSs, inout float3 vCoat, inout float3 vSheen )
	{
		float3 vHalf = normalize( vLightDir + vViewDir );
		if ( g_flGltfClearcoatFactor > 0.0 )
		{
			float flNdotL = saturate( dot( layers.coatNormal, vLightDir ) );
			float flNdotV = saturate( dot( layers.coatNormal, vViewDir ) );
			float flNdotH = saturate( dot( layers.coatNormal, vHalf ) );
			float flFresnel = 0.04 + 0.96 * pow( 1.0 - saturate( dot( vLightDir, vHalf ) ), 5.0 );
			vCoat += vRadiance * ( ComputeGGXBRDF( layers.coatRoughness.xx, flNdotL, flNdotV, flNdotH, vPositionSs ).x * flNdotL * flFresnel );
		}
		if ( g_flGltfSheenOn > 0.5 )
		{
			float flNdotL = saturate( dot( vNormalWs, vLightDir ) );
			float flNdotV = saturate( dot( vNormalWs, vViewDir ) );
			float flNdotH = saturate( dot( vNormalWs, vHalf ) );
			vSheen += vRadiance * ( ComputeCharlieSheenBRDF( layers.sheenRoughness, flNdotL, flNdotV, flNdotH ) * flNdotL );
		}
	}

	// All lights for the extra lobes, in one loop: each light's shadow is sampled once for coat and sheen
	// together. The sun visibility comes from the caller, which also gives it to the stock base lighting.
	void GltfLayerLighting( Material m, float3 vNormalWs, GltfLayers layers, float3 vReceiverNormalWs, float flSunVisibility, inout float3 vCoat, inout float3 vSheen )
	{
		float3 vViewDir = CalculatePositionToCameraDirWs( m.WorldPosition );
		uint count = Light::Count( m.ScreenPosition );
		for ( uint index = 0; index < count; ++index )
		{
			Light light = Light::From( m.WorldPosition, m.ScreenPosition, index, m.LightmapUV, vReceiverNormalWs );
			if ( light.LightData.IsSpecularEnabled() )
			{
				float3 direction = light.LightData.Type == LightType::LightTypeDirectional ? -light.LightData.GetDirection() : light.Direction;
				GltfLayerLight( direction, light.Color * light.Attenuation * light.Visibility, vViewDir, vNormalWs, layers, m.ScreenPosition.xy, vCoat, vSheen );
			}
		}
		if ( g_DirectionalLightEnabled && !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
		{
			float3 radiance = g_DirectionalLightColor.rgb * ( flSunVisibility < 0.0 ? 1.0 : flSunVisibility );
			if ( LightModulators::Active() ) radiance *= LightModulators::EvaluateSun( m.WorldPosition, -g_DirectionalLightDirection.xyz );
			GltfLayerLight( -g_DirectionalLightDirection.xyz, radiance, vViewDir, vNormalWs, layers, m.ScreenPosition.xy, vCoat, vSheen );
		}
	}

	// Screen UV of the opaque scene seen through the surface. Without a volume (thickness 0) or in an
	// orthographic view the ray is not bent.
	float2 GltfRefractionUv( PixelInput i, float3 vRayWs, float3 vNormalWs, float flIor, float flDistanceIn, bool bStraight )
	{
		float2 vUv = i.vPositionSs.xy * g_vInvViewportSize.xy;
		if ( !bStraight )
		{
			float3 vRefract = refract( vRayWs, vNormalWs, 1.0 / flIor );
			float4 vPs = Position4WsToPs( float4( i.vPositionWithOffsetWs.xyz + vRefract * flDistanceIn, 0 ) );
			vUv = vPs.xy / vPs.w * 0.5 + 0.5; vUv.y = 1.0 - vUv.y;
		}
		return vUv * g_vFrameBufferCopyInvSizeAndUvScale.zw;
	}

	// ShadingModelStandard::Shade with the glTF layers. The stock direct and indirect lighting shade the base
	// (metal/rough or spec/gloss, with the F0 of ior, specular and iridescence); the layers go on top.
	float4 GltfShade( Material m, GltfLayers layers, float3 anisoDirection, float anisoStrength )
	{
		Decals::Apply( m.WorldPosition, m );
		AdjustAlphaToCoverage( m );
		LightingTerms_t lightingTerms = InitLightingTerms();
		CombinerInput combinerInput = ShadingModelStandard::MaterialToCombinerInput( m );
		#if ( S_ANISOTROPIC_GLOSS )
		{
			// Khronos: alphaT = mix(alpha, 1, strength^2) along the direction, alphaB = alpha.
			// The stock BRDF squares perceptual roughness, so pass sqrt(alphaT) and roughness.
			float alpha = m.Roughness * m.Roughness;
			float alphaT = lerp( alpha, 1.0, anisoStrength * anisoStrength );
			float3 t = normalize( anisoDirection - combinerInput.vNormalWs * dot( anisoDirection, combinerInput.vNormalWs ) );
			float3 b = cross( combinerInput.vNormalWs, t );
			combinerInput.vRoughness = max( float2( sqrt( alphaT ), m.Roughness ), 0.001 );
			combinerInput.vTangentUWs = t;
			combinerInput.vTangentVWs = b;
			float2 ratio = combinerInput.vRoughness.xy / combinerInput.vRoughness.yx;
			combinerInput.vAnisotropy.x = step( ratio.y, ratio.x );
			combinerInput.vAnisotropy.y = 1.0 - min( ratio.x, ratio.y );
			combinerInput.vPerPixelTangentUWs = t;
			combinerInput.vPerPixelTangentVWs = b;
		}
		#endif
		float3 vViewDir = CalculatePositionToCameraDirWs( m.WorldPosition );
		float flNoV = saturate( dot( combinerInput.vNormalWs, vViewDir ) );
		// KHR_materials_ior, specular, iridescence and spec/gloss replace the stock dielectric F0 (0.04).
		if ( g_flGltfIorOn > 0.5 || g_flGltfSpecularOn > 0.5 || g_flGltfIridescenceOn > 0.5 )
		{
			float f0Ior = pow( ( layers.ior - 1.0 ) / ( layers.ior + 1.0 ), 2.0 );
			float3 f0Dielectric = min( f0Ior * layers.specColor, 1.0 ) * layers.specFactor;
			if ( g_flGltfSpecGloss > 0.5 )
			{
				// Spec/gloss: F0 is the specular colour, and the diffuse colour loses what the specular reflects.
				f0Dielectric = layers.specColor;
				combinerInput.vDiffuseColor.rgb = m.Albedo * ( 1.0 - max( layers.specColor.r, max( layers.specColor.g, layers.specColor.b ) ) );
			}
			float3 f0 = lerp( f0Dielectric, m.Albedo, m.Metalness );
			if ( g_flGltfIridescenceOn > 0.5 )
				f0 = lerp( f0, GltfEvalIridescence( 1.0, layers.iridIor, flNoV, layers.iridThickness, f0 ), layers.irid );
			combinerInput.vSpecularColor = f0;
		}
		// Clearcoat and sheen need the lights a second time. Take the sun shadow once and give the same value
		// to the stock lighting, which accepts a shared visibility for the same position and receiver normal.
		bool bLayerLights = g_flGltfClearcoatFactor > 0.0 || g_flGltfSheenOn > 0.5;
		float3 vReceiverNormalWs = float3( 0, 0, 1 );
		float flSunVisibility = -1.0;
		if ( bLayerLights )
		{
			vReceiverNormalWs = ComputeShadowReceiverNormal( m.WorldPositionWithOffset );
			if ( g_DirectionalLightEnabled && g_DirectionalLightCascadeCount > 0 && !LightmappedLight::UsesLightmaps() && !ProbeLight::UsesProbes() )
				flSunVisibility = DirectionalLightShadow::GetVisibilityWithOffset( m.WorldPositionWithOffset, vReceiverNormalWs, m.ScreenPosition );
		}
		ComputeDirectLighting( lightingTerms, combinerInput, flSunVisibility );
		CalculateIndirectLighting( lightingTerms, combinerInput );
		// Stock indirect diffuse comes only from lightmaps, probe volumes or a flat ambient colour;
		// the environment cubemap feeds only specular. glTF image-based lighting expects diffuse
		// irradiance from the environment as well. The roughest prefiltered mip along the normal
		// approximates it. Without an environment map in range, keep the stock value.
		float3 vEnvDiffuse = EnvMap::From( m.WorldPosition, combinerInput.vPositionSs, combinerInput.vNormalWs, 1.0.xx );
		if ( dot( vEnvDiffuse, 1.0 ) > 0.0 )
			lightingTerms.vIndirectDiffuse.rgb = vEnvDiffuse;
		float3 vDiffuseAO = CalculateDiffuseAmbientOcclusion( combinerInput, lightingTerms );
		lightingTerms.vIndirectDiffuse.rgb *= vDiffuseAO.rgb;
		lightingTerms.vDiffuse.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vDiffuseAO.rgb, combinerInput.flAmbientOcclusionDirectDiffuse );
		float3 vSpecularAO = CalculateSpecularAmbientOcclusion( combinerInput, lightingTerms );
		lightingTerms.vIndirectSpecular.rgb *= vSpecularAO.rgb;
		lightingTerms.vSpecular.rgb *= lerp( float3( 1.0, 1.0, 1.0 ), vSpecularAO.rgb, combinerInput.flAmbientOcclusionDirectSpecular );
		float3 vDiffuse = ( lightingTerms.vDiffuse.rgb + lightingTerms.vIndirectDiffuse.rgb ) * combinerInput.vDiffuseColor.rgb;
		#if ( S_GLTF_TRANSMISSION )
			// Khronos: the diffuse lobe of the dielectric part is replaced by the light transmitted through the volume.
			vDiffuse = lerp( vDiffuse, layers.transmitted * m.Albedo * ( 1.0 - m.Metalness ), layers.transmission );
		#endif
		float3 color = vDiffuse + lightingTerms.vSpecular.rgb + lightingTerms.vIndirectSpecular.rgb;
		float3 vCoatLight = float3( 0, 0, 0 );
		float3 vSheenLight = float3( 0, 0, 0 );
		if ( bLayerLights )
		{
			// Derivatives (geometric roughness) are taken here, in control flow that is uniform for the draw.
			if ( g_flGltfClearcoatFactor > 0.0 )
				layers.coatRoughness = AdjustRoughnessByGeometricNormal( layers.coatRoughness.xx, layers.coatNormal ).x;
			layers.sheenRoughness = max( layers.sheenRoughness, 0.07 );
			GltfLayerLighting( m, combinerInput.vNormalWs, layers, vReceiverNormalWs, flSunVisibility, vCoatLight, vSheenLight );
		}
		if ( g_flGltfSheenOn > 0.5 )
		{
			// Khronos layering: the base loses the energy that the sheen reflects (albedo scaling).
			float flSheenAlbedo = GltfSheenAlbedo( flNoV, layers.sheenRoughness );
			float3 vSheenEnv = EnvMap::From( m.WorldPosition, combinerInput.vPositionSs, combinerInput.vNormalWs, layers.sheenRoughness.xx );
			float flSheenMax = max( layers.sheenColor.r, max( layers.sheenColor.g, layers.sheenColor.b ) );
			color = color * ( 1.0 - flSheenMax * flSheenAlbedo ) + layers.sheenColor * ( vSheenEnv * flSheenAlbedo * vDiffuseAO.rgb + vSheenLight );
		}
		color += combinerInput.vEmissive.rgb;
		if ( g_flGltfClearcoatFactor > 0.0 )
		{
			// Khronos layering: ( emission + base ) * ( 1 - coat * Fcoat ) + coat * coatSpecular.
			// The coat reflects the environment with one cubemap lookup; it has no diffuse lobe.
			float flCoatNoV = saturate( dot( layers.coatNormal, vViewDir ) );
			float flCoatFresnel = 0.04 + 0.96 * pow( 1.0 - flCoatNoV, 5.0 );
			float3 vCoatEnv = EnvMap::From( m.WorldPosition, combinerInput.vPositionSs, layers.coatNormal, layers.coatRoughness.xx );
			vCoatEnv *= CalcBRDFReflectionFactor( flCoatNoV, layers.coatRoughness, float3( 0.04, 0.04, 0.04 ) ) * vSpecularAO.rgb;
			color = color * ( 1.0 - layers.coat * flCoatFresnel ) + layers.coat * ( vCoatLight + vCoatEnv );
		}
		float4 result = float4( color, m.Opacity );
		if ( g_bWireframeMode ) return g_vWireframeColor;
		if ( ToolsVis::WantsToolsVis() ) return ShadingModelStandard::DoToolsVis( result, m, lightingTerms );
		if ( g_bFogEnabled ) result.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, result.rgb );
		return result;
	}

	float4 MainPs( PixelInput i ) : SV_Target0
	{
		Material m = Material::Init( i );
		// A material with an extension the reader lacks draws the error pattern and samples nothing.
		if ( g_flGltfUnsupported > 0.5 && !DepthNormals::WantsDepthNormals() )
		{
			float2 vCell = floor( i.vTextureCoords.xy * 8.0 );
			return float4( fmod( vCell.x + vCell.y, 2.0 ) < 1.0 ? float3( 1, 0, 1 ) : float3( 0, 0, 0 ), 1 );
		}
		// A texture is sampled only inside a branch on its presence uniform. The branch is the same for every
		// pixel of the draw, so a missing texture costs no fetch and screen-space derivatives stay defined.
		float2 uvBaseColor = GltfUv( i, g_flGltfBaseColorUvSlot, g_vGltfBaseColorUvRowU, g_vGltfBaseColorUvRowV );
		float4 sampleBaseColor = float4( 1, 1, 1, 1 );
		if ( g_flGltfHasBaseColorTexture > 0.5 )
			sampleBaseColor = g_tGltfBaseColorTexture.Sample( g_sGltfBaseColor, uvBaseColor );
		float4 sampleEmissive = float4( 1, 1, 1, 1 );
		if ( g_flGltfHasEmissiveTexture > 0.5 )
			sampleEmissive = g_tGltfEmissiveTexture.Sample( g_sGltfEmissive, GltfUv( i, g_flGltfEmissiveUvSlot, g_vGltfEmissiveUvRowU, g_vGltfEmissiveUvRowV ) );
		float sourceAlpha = sampleBaseColor.a * g_vGltfBaseColor.a * saturate( i.vColor.a );
		if ( g_flGltfPointLineNoNormal > .5 && !DepthNormals::WantsDepthNormals() && !ToolsVis::WantsToolsVis() )
		{
			#if S_ALPHA_TEST
				clip( sourceAlpha - g_flGltfAlphaCutoff );
			#endif
			m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
			m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
			#if S_TRANSLUCENT
				m.Opacity = sourceAlpha * i.vVertexColor.a;
			#else
				m.Opacity = i.vVertexColor.a;
			#endif
			m.TextureCoords = uvBaseColor;
			float4 color = float4( m.Albedo + m.Emission, m.Opacity );
			if ( g_bWireframeMode ) return g_vWireframeColor;
			if ( g_bFogEnabled ) color.rgb = Fog::Apply( m.WorldPosition, m.ScreenPosition.xy, color.rgb );
			return color;
		}
		float4 sampleMetallicRoughness = float4( 1, 1, 1, 1 );
		if ( g_flGltfHasMetallicRoughnessTexture > 0.5 )
			sampleMetallicRoughness = g_tGltfMetallicRoughnessTexture.Sample( g_sGltfMetallicRoughness, GltfUv( i, g_flGltfMetallicRoughnessUvSlot, g_vGltfMetallicRoughnessUvRowU, g_vGltfMetallicRoughnessUvRowV ) );
		float sampleOcclusion = 1.0;
		if ( g_flGltfHasOcclusionTexture > 0.5 )
			sampleOcclusion = g_tGltfOcclusionTexture.Sample( g_sGltfOcclusion, GltfUv( i, g_flGltfOcclusionUvSlot, g_vGltfOcclusionUvRowU, g_vGltfOcclusionUvRowV ) ).r;
		float3 normalTs = float3( 0, 0, 1 );
		if ( g_flGltfHasNormalTexture > 0.5 )
		{
			normalTs = g_tGltfNormalTexture.Sample( g_sGltfNormal, GltfUv( i, g_flGltfNormalUvSlot, g_vGltfNormalUvRowU, g_vGltfNormalUvRowV ) ).xyz * 2 - 1;
			normalTs.xy *= g_flGltfNormalScale;
			normalTs = normalize( normalTs );
		}
		GltfLayers layers;
		layers.ior = g_flGltfIorOn > 0.5 ? g_flGltfIor : 1.5;
		layers.specFactor = 1.0; layers.specColor = 1.0.xxx;
		float flGlossiness = 1.0;
		if ( g_flGltfSpecularOn > 0.5 )
		{
			// KHR_materials_specular: factor in A of one texture, colour in RGB of the other. Spec/gloss: both in the colour texture.
			layers.specFactor = g_flGltfSpecularFactor;
			if ( g_flGltfHasSpecularTexture > 0.5 )
				layers.specFactor *= g_tGltfSpecularTexture.Sample( g_sGltfSpecular, GltfUv( i, g_flGltfSpecularUvSlot, g_vGltfSpecularUvRowU, g_vGltfSpecularUvRowV ) ).a;
			layers.specColor = g_vGltfSpecularColorFactor;
			flGlossiness = g_flGltfGlossinessFactor;
			if ( g_flGltfHasSpecularColorTexture > 0.5 )
			{
				float4 sampleSpecularColor = g_tGltfSpecularColorTexture.Sample( g_sGltfSpecular, GltfUv( i, g_flGltfSpecularColorUvSlot, g_vGltfSpecularColorUvRowU, g_vGltfSpecularColorUvRowV ) );
				layers.specColor *= sampleSpecularColor.rgb;
				flGlossiness *= sampleSpecularColor.a;
			}
		}
		// The depth pass and the colour pass must clip the same pixels. Clip after the samples that both passes
		// take: discard leaves the derivatives of later samples undefined on a partly clipped quad.
		#if S_ALPHA_TEST
			clip( sourceAlpha - g_flGltfAlphaCutoff );
		#endif
		m.Albedo = sampleBaseColor.rgb * g_vGltfBaseColor.rgb * saturate( i.vColor.rgb ) * i.vVertexColor.rgb;
		m.Roughness = saturate( sampleMetallicRoughness.g * g_flGltfRoughnessFactor );
		m.Metalness = saturate( sampleMetallicRoughness.b * g_flGltfMetallicFactor );
		if ( g_flGltfSpecGloss > 0.5 )
		{
			m.Roughness = saturate( 1.0 - flGlossiness );
			m.Metalness = 0.0;
		}
		m.AmbientOcclusion = saturate( lerp( 1, sampleOcclusion, g_flGltfOcclusionStrength ) );
		m.Emission = sampleEmissive.rgb * g_vGltfEmissiveFactor;
		#if S_TRANSLUCENT && !S_GLTF_TRANSMISSION
			m.Opacity = sourceAlpha * i.vVertexColor.a;
		#else
			m.Opacity = i.vVertexColor.a;
		#endif
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
		// The depth pass writes only normal, roughness and coverage. Return before any layer or light, so its
		// variants are small and the same for every layer.
		if ( DepthNormals::WantsDepthNormals() )
		{
			Decals::Apply( m.WorldPosition, m );
			AdjustAlphaToCoverage( m );
			return DepthNormals::Output( m.Normal, m.Roughness, m.Opacity );
		}
		layers.coat = 0; layers.coatRoughness = 0; layers.coatNormal = m.GeometricNormal;
		if ( g_flGltfClearcoatFactor > 0.0 )
		{
			layers.coat = g_flGltfClearcoatFactor;
			if ( g_flGltfHasClearcoatTexture > 0.5 )
				layers.coat *= g_tGltfClearcoatTexture.Sample( g_sGltfClearcoat, GltfUv( i, g_flGltfClearcoatUvSlot, g_vGltfClearcoatUvRowU, g_vGltfClearcoatUvRowV ) ).r;
			layers.coatRoughness = g_flGltfClearcoatRoughnessFactor;
			if ( g_flGltfHasClearcoatRoughnessTexture > 0.5 )
				layers.coatRoughness *= g_tGltfClearcoatRoughnessTexture.Sample( g_sGltfClearcoat, GltfUv( i, g_flGltfClearcoatRoughnessUvSlot, g_vGltfClearcoatRoughnessUvRowU, g_vGltfClearcoatRoughnessUvRowV ) ).g;
			layers.coatRoughness = saturate( layers.coatRoughness );
			// The coat uses its own normal map, or the geometric normal; never the base normal map.
			float3 coatTs = float3( 0, 0, 1 );
			if ( g_flGltfHasClearcoatNormalTexture > 0.5 )
			{
				coatTs = g_tGltfClearcoatNormalTexture.Sample( g_sGltfClearcoat, GltfUv( i, g_flGltfClearcoatNormalUvSlot, g_vGltfClearcoatNormalUvRowU, g_vGltfClearcoatNormalUvRowV ) ).xyz * 2 - 1;
				coatTs.xy *= g_flGltfClearcoatNormalScale;
				coatTs = normalize( coatTs );
			}
			coatTs.y = -coatTs.y;
			layers.coatNormal = TransformNormal( coatTs, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );
			#if ( S_RENDER_BACKFACES )
				if ( !i.face ) layers.coatNormal = -layers.coatNormal;
			#endif
		}
		float3 anisoDirection = i.vTangentUWs;
		float anisoStrength = 0;
		#if ( S_GLTF_ANISOTROPY )
		{
			// Texture RG: direction in tangent space in [-1,1]; B: strength multiplier.
			float3 aniso = float3( 1, 0.5, 1 );
			if ( g_flGltfHasAnisotropyTexture > 0.5 )
				aniso = g_tGltfAnisotropyTexture.Sample( g_sGltfAnisotropy, GltfUv( i, g_flGltfAnisotropyUvSlot, g_vGltfAnisotropyUvRowU, g_vGltfAnisotropyUvRowV ) ).rgb;
			float2 dir = aniso.rg * 2 - 1;
			float2 r = g_vGltfAnisotropyRotation;
			dir = float2( r.x * dir.x - r.y * dir.y, r.y * dir.x + r.x * dir.y );
			// Same basis as the normal map: after the pre-flip, glTF (T, B) maps to stock (U, V).
			anisoDirection = normalize( dir.x * i.vTangentUWs + dir.y * i.vTangentVWs );
			anisoStrength = saturate( g_flGltfAnisotropyStrength * aniso.b );
		}
		#endif
		layers.sheenColor = 0; layers.sheenRoughness = 0;
		if ( g_flGltfSheenOn > 0.5 )
		{
			layers.sheenColor = g_vGltfSheenColorFactor;
			if ( g_flGltfHasSheenColorTexture > 0.5 )
				layers.sheenColor *= g_tGltfSheenColorTexture.Sample( g_sGltfSheen, GltfUv( i, g_flGltfSheenColorUvSlot, g_vGltfSheenColorUvRowU, g_vGltfSheenColorUvRowV ) ).rgb;
			layers.sheenRoughness = g_flGltfSheenRoughnessFactor;
			if ( g_flGltfHasSheenRoughnessTexture > 0.5 )
				layers.sheenRoughness *= g_tGltfSheenRoughnessTexture.Sample( g_sGltfSheen, GltfUv( i, g_flGltfSheenRoughnessUvSlot, g_vGltfSheenRoughnessUvRowU, g_vGltfSheenRoughnessUvRowV ) ).a;
			layers.sheenRoughness = saturate( layers.sheenRoughness );
		}
		layers.irid = 0; layers.iridIor = g_flGltfIridescenceIor; layers.iridThickness = g_flGltfIridescenceThicknessMax;
		if ( g_flGltfIridescenceOn > 0.5 )
		{
			layers.irid = g_flGltfIridescenceFactor;
			if ( g_flGltfHasIridescenceTexture > 0.5 )
				layers.irid *= g_tGltfIridescenceTexture.Sample( g_sGltfIridescence, GltfUv( i, g_flGltfIridescenceUvSlot, g_vGltfIridescenceUvRowU, g_vGltfIridescenceUvRowV ) ).r;
			if ( g_flGltfHasIridescenceThicknessTexture > 0.5 )
				layers.iridThickness = lerp( g_flGltfIridescenceThicknessMin, g_flGltfIridescenceThicknessMax, g_tGltfIridescenceThicknessTexture.Sample( g_sGltfIridescence, GltfUv( i, g_flGltfIridescenceThicknessUvSlot, g_vGltfIridescenceThicknessUvRowU, g_vGltfIridescenceThicknessUvRowV ) ).g );
		}
		layers.transmission = 0; layers.transmitted = 0;
		#if ( S_GLTF_TRANSMISSION )
		{
			layers.transmission = g_flGltfTransmissionFactor;
			if ( g_flGltfHasTransmissionTexture > 0.5 )
				layers.transmission *= g_tGltfTransmissionTexture.Sample( g_sGltfTransmission, GltfUv( i, g_flGltfTransmissionUvSlot, g_vGltfTransmissionUvRowU, g_vGltfTransmissionUvRowV ) ).r;
			layers.transmission = saturate( layers.transmission );
			float flThicknessM = g_flGltfThicknessFactor;
			if ( g_flGltfHasThicknessTexture > 0.5 )
				flThicknessM *= g_tGltfThicknessTexture.Sample( g_sGltfTransmission, GltfUv( i, g_flGltfThicknessUvSlot, g_vGltfThicknessUvRowU, g_vGltfThicknessUvRowV ) ).g;
			float3 vRayWs = normalize( i.vPositionWithOffsetWs.xyz );
			float3 vN = normalize( m.Normal );
			float flIor = layers.ior;
			float flRoughnessLod = m.Roughness * clamp( flIor * 2.0 - 2.0, 0.0, 1.0 ) * 7.0;
			// Metres to inches; refraction ray length through the volume.
			float flDistanceIn = flThicknessM * 39.3701;
			bool bStraight = g_matViewToProjection[3].w != 0 || flDistanceIn <= 0.0;
			float3 vSampled = g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearMirror, GltfRefractionUv( i, vRayWs, vN, flIor, flDistanceIn, bStraight ), flRoughnessLod ).rgb;
			// KHR_materials_dispersion: red and blue refract with their own index. Only a material with
			// dispersion pays for the two more scene lookups.
			if ( g_flGltfDispersion > 0.0 )
			{
				float flHalfSpread = ( flIor - 1.0 ) * 0.025 * g_flGltfDispersion;
				vSampled.r = g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearMirror, GltfRefractionUv( i, vRayWs, vN, flIor - flHalfSpread, flDistanceIn, bStraight ), flRoughnessLod ).r;
				vSampled.b = g_tFrameBufferCopyTexture.SampleLevel( g_sTrilinearMirror, GltfRefractionUv( i, vRayWs, vN, flIor + flHalfSpread, flDistanceIn, bStraight ), flRoughnessLod ).b;
			}
			layers.transmitted = vSampled * exp( -g_vGltfAttenuationCoefficient * flThicknessM );
		}
		#endif
		return GltfShade( m, layers, anisoDirection, anisoStrength );
	}
}
