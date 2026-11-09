HEADER
{
 Description = "Independent native Lambert light-unit calibration; diagnostic only";
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
 PixelInput MainVs(VertexInput v)
 {
  return FinalizeVertex(ProcessVertex(v));
 }
}
PS
{
 #include "common/pixel.hlsl"
 RenderState(CullMode,BACK);
 float g_flDiffuseCalibrationMode < Default1(0); >;
 float4 MainPs(PixelInput i):SV_Target0
 {
  if(g_flDiffuseCalibrationMode>.5)
   return float4(.12345,2.3456,.4568,1);
  Material m=Material::Init(i);
  m.Normal=-normalize(i.vNormalWs);
  m.Roughness=.5;
  m.Metalness=0;
  m.Albedo=1;
  // Native roughness .5 makes its default diffuse exponent exactly one.
  // Read only the native direct Lambert term, without our extension BSDF,
  // Fresnel, indirect irradiance, emission, tonemapping or specular composition.
  CombinerInput input=ShadingModelStandard::MaterialToCombinerInput(m);
  LightingTerms_t terms=InitLightingTerms();
  ComputeDirectLighting(terms,input);
  return float4(terms.vDiffuse,1);
 }
}
