HEADER
{
 Description = "Finite point photometry candidate diagnostic; not installed";
}
FEATURES
{
 #include "common/features.hlsl"
}
MODES
{
 Forward();
 Depth();
}
COMMON
{
 #define S_ALPHA_TEST 0
 #define S_TRANSLUCENT 0
 #define S_RENDER_BACKFACES 0
 #include "common/shared.hlsl"
 #include "procedural.hlsl"
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
 PixelInput MainVs(VertexInput v) { return FinalizeVertex(ProcessVertex(v)); }
}
PS
{
 #include "common/pixel.hlsl"
 RenderState(CullMode,BACK);
 int ProbeMarkerIndex < Attribute("ProbeMarkerIndex"); Default(-1); >;
 float ProbeNativePerCandela < Attribute("ProbeNativePerCandela"); Default1(0); >;
 float4 MainPs(PixelInput i):SV_Target0
 {
  Material m=Material::Init(i);m.Normal=-normalize(i.vNormalWs);m.Roughness=.5;m.Metalness=0;m.Albedo=1;
  CombinerInput f=ShadingModelStandard::MaterialToCombinerInput(m);
  float3 receiver=ComputeShadowReceiverNormal(f.vPositionWithOffsetWs.xyz);
  float3 diffuse=0,specular=0,transmissive=0;
  ClusterRange range=Cluster::Query(ClusterItemType_Light,f.vPositionSs);
  ClusterForEach(range)
  {
   BinnedLight data=DynamicLightConstantByIndex(range.Item);
   if (ProbeMarkerIndex>=0 && data.Type==LightType::LightTypePoint && data.HasLightCookie() && data.LightCookieTextureIndex==(uint)ProbeMarkerIndex)
   {
    // Finite authored range only. The stock direct-light routine remains
    // responsible for visibility, shadows, modulation and BRDF. Neutralize
    // its distance factor on a local copy, then supply finite glTF falloff.
    data.Flags = data.Flags & ~(uint)LightFlags::LightCookieEnabled;
    float3 ray=data.GetPosition()-f.vPositionWs.xyz;
    float distanceSq=dot(ray,ray);
    float radiusSq=data.RadiusSquared;
    float ratioSq=distanceSq/radiusSq;
    float smoothRange=saturate(1.0-ratioSq*ratioSq);
    float metersSq=max(distanceSq*(0.0254*0.0254),1e-12);
    data.Color *= ProbeNativePerCandela*smoothRange/metersSq;
    data.LinearFalloff=0.0;
    data.QuadraticFalloff=1.0/max(distanceSq,1.0);
    data.FalloffBias=0.0;
   }
   ComputeDirectLightingForLight(f,receiver,data,diffuse,specular,transmissive,false,LightModulators::Active());
  }
  return float4(diffuse,1);
 }
}
