HEADER
{
 Description = "Isolated glTF cookie identity diagnostic; not production photometry";
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
   if (ProbeMarkerIndex>=0 && data.HasLightCookie() && data.LightCookieTextureIndex==(uint)ProbeMarkerIndex)
   {
    // Branch before ComputeDirectLightingForLight samples cookie UV. The
    // factor is a visible diagnostic marker, not a physical light mapping.
    data.Flags = data.Flags & ~(uint)LightFlags::LightCookieEnabled;
    data.Color *= 2.0;
   }
   ComputeDirectLightingForLight(f,receiver,data,diffuse,specular,transmissive,false,LightModulators::Active());
  }
  return float4(diffuse,1);
 }
}
