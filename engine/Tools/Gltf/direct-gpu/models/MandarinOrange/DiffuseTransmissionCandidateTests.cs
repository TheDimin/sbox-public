using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Gltf;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

// Development candidate only. This does not replace the installed parser or
// advertise a public extension capability. Public acceptance follows integration.
[TestClass]
internal sealed class DiffuseTransmissionCandidateTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 static string provenQuantizer;
 const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
 static string Owned=>Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/MandarinOrange");

 [TestMethod]
 public async Task NativeLinearAlphaSrgbColorAndEnergyControls()
 {
  await MainThread.Wait();
  provenQuantizer=null;
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/mandarin-diffuse-candidate",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  byte[] shaderBytes=File.ReadAllBytes(Path.Combine(Owned,"gltf_diffuse_transmission_candidate.shader"));
  Shader shader=null,nativeReferenceShader=null;Material template=null,nativeReferenceMaterial=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(200,0,0),global::Vector3.Zero,100);
  var light=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",Private).GetValue(capture);
  capture.Camera.EnablePostProcessing=false;
  capture.Scene.SceneWorld.AmbientLightColor=Color.Black;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(shaderBytes),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(shader is {IsValid:true,IsAnonymous:true});
   template=Material.Create("gltf_diffuse_candidate_"+Guid.NewGuid().ToString("N"),shader);
   template.SetFeature("F_RENDER_BACKFACES",0);
   Assert.IsTrue(template is {IsValid:true,IsError:false});
   byte[] encoded=File.ReadAllBytes(Path.Combine(Owned,"diffuse-transmission-role-control.png"));
   byte[] fixture=File.ReadAllBytes(Path.Combine(Owned,"diffuse-transmission-plane.gltf"));
   var linearColor=new global::Vector3(.10793025005694963f,.03845209378053243f,.13177878142645328f);
   var colorFactor=new global::Vector3(.5f,.75f,.25f);
   float strength=.5f*(64f/255f);
   light.Position=new global::Vector3(-200,0,0);
   var zero=await Draw("back_factor_zero",0,global::Vector3.One);
   var full=await Draw("back_factor_one",1,global::Vector3.One);
   byte[] nativeReferenceSource=File.ReadAllBytes(Path.Combine(Owned,"diffuse-native-lambert-reference.shader"));
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))nativeReferenceShader=await Shader.CompileAsync(System.Text.Encoding.UTF8.GetString(nativeReferenceSource),timeout.Token);
   await MainThread.Wait();Assert.IsTrue(nativeReferenceShader is {IsValid:true,IsAnonymous:true});nativeReferenceMaterial=Material.Create("diffuse_native_lambert_"+Guid.NewGuid().ToString("N"),nativeReferenceShader);
   float[] nativeLambert;
   using(var referenceDocument=GltfReader.Read(fixture))using(var referenceAsset=GltfGpuAsset.Prepare(referenceDocument,GltfManagedModelResource.ComputeShaderPath,_=>nativeReferenceMaterial))using(var reference=referenceAsset.CreateInstance(capture.Scene.SceneWorld))
   {
    Assert.IsTrue(nativeReferenceMaterial.HasParameter("g_flDiffuseCalibrationMode"));nativeReferenceMaterial.Set("g_flDiffuseCalibrationMode",1f);
    var literalHdr=await ReadHdr(capture);float[] literals={.12345f,2.3456f,.4568f};
    var observedLiteral=new[]{Center(literalHdr,0),Center(literalHdr,1),Center(literalHdr,2)};var nearestLiteral=literals.Select(value=>(float)(Half)value).ToArray();var truncateLiteral=literals.Select(TruncateHalf).ToArray();
    File.WriteAllBytes(Path.Combine(output,"literal_hdr_precision_reference.rgba32f"),System.Runtime.InteropServices.MemoryMarshal.AsBytes(literalHdr.AsSpan()).ToArray());
    Measurements.Add(new{phase="literal_hdr_observations",literal_rgb=literals,observed_center_rgb=observedLiteral,expected_nearest_half_rgb=nearestLiteral,expected_truncate_half_rgb=truncateLiteral,anti_aliasing=capture.Camera.SceneCamera.AntiAliasing});
    bool nearest=true,truncating=true;for(int channel=0;channel<3;channel++){nearest&=observedLiteral[channel]==nearestLiteral[channel];truncating&=observedLiteral[channel]==truncateLiteral[channel];}
    Assert.IsTrue(nearest!=truncating,"Independent literal HDR control did not uniquely prove a supported native half quantizer");provenQuantizer=truncating?"positive-fp16-truncation":"fp16-nearest-even";
    Assert.IsFalse(capture.Camera.SceneCamera.AntiAliasing,"Numeric precision camera must disable AA/filtering");
    Assert.IsTrue(Center(literalHdr,1)>1,"Native HDR view clipped radiance above one");
    Assert.IsTrue(Math.Abs(Center(literalHdr,0)*255-Math.Round(Center(literalHdr,0)*255))>.01,"Literal fractional HDR control unexpectedly collapsed to 8-bit precision");
    Measurements.Add(new{phase="actual_pipeline_precision",literal_rgb=literals,observed_center_rgb=observedLiteral,quantizer=provenQuantizer,hdr_above_one_verified=true,non8bit_fraction_verified=true,anti_aliasing=false,scope="Independent constant fragment output proves the actual native FP16 HDR quantizer before RGBA32F destination; later bounds use that quantizer's per-value adjacent-half interval, not a percentage epsilon."});
    nativeReferenceMaterial.Set("g_flDiffuseCalibrationMode",0f);
    nativeLambert=await ReadHdr(capture);Assert.AreEqual(0,reference.CallbackErrors.Length);Assert.IsTrue(reference.DrawCalls>0);
    File.WriteAllBytes(Path.Combine(output,"native_lambert_reference.rgba32f"),System.Runtime.InteropServices.MemoryMarshal.AsBytes(nativeLambert.AsSpan()).ToArray());
    var preview=await capture.Take("independent_native_lambert_unit_reference");
    var cb=CenterBounds(full);var rb=CenterBounds(nativeLambert);double lower=cb.Lower/rb.Upper,upper=cb.Upper/rb.Lower;
    Measurements.Add(new{phase="native_pi_fresnel_calibration",preview.Path,preview.Sha256,shader_sha256=Hash(nativeReferenceSource),candidate_to_native_lambert_ratio=Center(full)/Center(nativeLambert),expected_ratio=.96,accepted_lower_bound=lower,accepted_upper_bound=upper,scope="Actual native direct diffuse at roughness .5 (native exponent one), with independent reversed normal, versus extension diffuse BTDF. Expected .96 is fixed; adjacent-half rounding intervals bound the observed ratio. No SI photometric calibration claim."});
    Assert.IsTrue(lower<=.96 && .96<=upper,"Native PI/Fresnel calibration failed derived half-rounding interval");
    foreach(double wrongRatio in new[]{1d,.96/Math.PI,.96*Math.PI})Assert.IsTrue(wrongRatio<lower || wrongRatio>upper,"Wrong PI/Fresnel control lies inside accepted quantization bounds");
   }
   var quarter=await Draw("back_factor_quarter",.25f,global::Vector3.One);
   AssertLinear(full,quarter,zero,.25f,"factor energy linearity");
   var metal=await Draw("metallic_suppresses_diffuse_transmission",1,global::Vector3.One,metallic:1);
   AssertEquivalent(metal,zero,"Metallic surface incorrectly acquired a diffuse transmission lobe");Assert.IsTrue(AnyDisjoint(metal,full),"Metallic suppression negative is insensitive");
   light.Position=new global::Vector3(-100,173.20508075688772f,0);
   var angled=await Draw("back_sixty_degree_lambert_fresnel_control",1,global::Vector3.One);
   // Equal-distance light at sixty degrees. A four-pixel symmetric center
   // average removes the first-order raster-center position difference. The
   // literal expectation is cos(60)*(1-Schlick(cos(30)))/(1-.04).
   double angularRatio=Center(angled)/Center(full);
   var angularBounds=CenterBounds(angled);var baselineBounds=CenterBounds(full);double angularLower=angularBounds.Lower/baselineBounds.Upper,angularUpper=angularBounds.Upper/baselineBounds.Lower;
   Assert.IsTrue(angularLower<=.4999784184671149 && .4999784184671149<=angularUpper,"Independent Lambert/Fresnel angular ratio failed derived half bounds: "+angularRatio);
   Measurements.Add(new{phase="angular_lambert_fresnel",observed_ratio=angularRatio,expected_ratio=.4999784184671149,accepted_lower_bound=angularLower,accepted_upper_bound=angularUpper});
   Assert.IsTrue(Math.Abs(angularRatio-Math.Pow(.5,.6))>.1,"Native roughness-dependent diffuse negative is insensitive");
   light.Position=new global::Vector3(-200,0,0);
   var textured=await Draw("alpha_linear_color_srgb",.5f,colorFactor,textureStrength:true,textureColor:true);
   var expected=await Draw("independent_uniform_reference",strength,linearColor);
   var wrong=await Draw("wrong_color_transfer_negative",strength,new global::Vector3((128f/255f)*.5f,(64f/255f)*.75f,(192f/255f)*.25f));
   Compare(textured,expected,wrong,"literal sRGB values and linear alpha");
   var wrongAlpha=await Draw("wrong_rgb_strength_negative",.5f*(128f/255f),linearColor);
   Assert.IsTrue(Error(textured,wrongAlpha)>.0001,"Wrong alpha-channel control is insensitive");
   byte[] sevenFixture=File.ReadAllBytes(Path.Combine(Owned,"diffuse-transmission-seven-uv-samplers.gltf"));
   var seven=await Draw("seven_uv_repeat_mirror_actual",.5f,colorFactor,sourceFixture:sevenFixture);
   var sevenExpected=await Draw("seven_uv_independent_uniform_reference",.5f*16f/255f,new global::Vector3(.10793025005694963f,.3953363442793598f,.01281736459351081f));
   var clampRoot=System.Text.Json.Nodes.JsonNode.Parse(sevenFixture);clampRoot["samplers"][0]["wrapS"]=33071;
   var wrongClamp=await Draw("wrong_strength_clamp_negative",.5f,colorFactor,sourceFixture:System.Text.Encoding.UTF8.GetBytes(clampRoot.ToJsonString()));
   var wrapRoot=System.Text.Json.Nodes.JsonNode.Parse(sevenFixture);wrapRoot["samplers"][1]["wrapS"]=10497;
   var wrongMirror=await Draw("wrong_color_repeat_negative",.5f,colorFactor,sourceFixture:System.Text.Encoding.UTF8.GetBytes(wrapRoot.ToJsonString()));
   Compare(seven,sevenExpected,wrongClamp,"Seven nonconsecutive UV sets, normalized stride/offset and repeat strength sampler");
   Compare(seven,sevenExpected,wrongMirror,"Independent color UV transform and mirror sampler");
   light.Position=new global::Vector3(200,0,0);
   var frontZero=await Draw("front_factor_zero",0,global::Vector3.One);
   var frontOne=await Draw("front_factor_one",1,global::Vector3.One);
   var frontQuarter=await Draw("front_factor_quarter",.25f,global::Vector3.One);
   AssertLinear(frontZero,frontQuarter,frontOne,.75f,"reflection loses exactly transmitted diffuse energy; specular remains");
   Assert.IsTrue(Center(frontOne)>.0001,"Factor one removed front specular reflection along with diffuse");
   Assert.IsTrue(full.Skip(2).Where((_,i)=>i%4==0).Max()>.0001,"Backlit transmitted radiance is empty");
   // The cube's authored face signal is independent of the shader's irradiance
   // evaluator. Verify every native face/mip before using its direction signal.
   byte[] cubeBytes=LiteralCube();using var cubeLease=GltfNativeTextureCache.Acquire(cubeBytes);var cube=cubeLease.Value;
   Assert.AreEqual(8,cube.Width);Assert.AreEqual(8,cube.Height);Assert.AreEqual(6,cube.Depth);Assert.AreEqual(4,cube.Mips);cube.MarkUsed(8);await capture.WaitFrames();
   for(int mip=0;mip<4;mip++)for(int face=0;face<6;face++)
   {
    int size=8>>mip;var observed=Enumerable.Repeat(float.NaN,size*size*4).ToArray();cube.GetPixels<float>((0,0,size,size),face,mip,observed,ImageFormat.RGBA32323232F);
    var literal=CubeFace(face);for(int i=0;i<observed.Length;i++)Assert.IsTrue(float.IsFinite(observed[i]) && Math.Abs(observed[i]-literal[i%4])<.000001f,"Literal cube readback did not preserve face/mip channel "+face+"/"+mip+"/"+i+" expected "+literal[i%4]+" observed "+observed[i]);
   }
   light.LightColor=Color.Black;capture.Scene.SceneWorld.AmbientLightColor=Color.Transparent;
   var probe=new SceneCubemap(capture.Scene.SceneWorld,cube,BBox.FromPositionAndSize(global::Vector3.Zero,100000));
   try
   {
    probe.Projection=SceneCubemap.ProjectionMode.Sphere;probe.Feathering=.01f;probe.RenderDirty();await capture.WaitFrames();
    var iblZero=await Draw("ibl_front_red_factor_zero",0,global::Vector3.One);var iblOne=await Draw("ibl_back_green_factor_one",1,global::Vector3.One);var iblQuarter=await Draw("ibl_factor_quarter",.25f,global::Vector3.One);
    Assert.IsTrue(Center(iblZero,0)>3*Center(iblZero,1),"Authored +X/front red cube face signal is absent");
    Assert.IsTrue(Center(iblOne,1)>3*Center(iblOne,0),"Transmitted IBL sampled front irradiance instead of authored -X/rear green hemisphere");
    AssertLinear(iblZero,iblQuarter,iblOne,.75f,"Independent opposite-hemisphere IBL energy mix with unchanged specular");
    Measurements.Add(new{phase="opposite_hemisphere_ibl",cube_sha256=Hash(cubeBytes),gpu_face_mip_checks=24,front_red=Center(iblZero,0),front_green=Center(iblZero,1),transmitted_red=Center(iblOne,0),transmitted_green=Center(iblOne,1),scope="Source-authored literal face colors verified by every native face/mip readback, then actual native SceneCubemap/irradiance rendering; no imported environment or physical preconvolution claim"});
   }
   finally{probe.Delete();capture.Scene.SceneWorld.AmbientLightColor=Color.Black;light.LightColor=Color.White*4;}
   Measurements.Add(new{scope="Anonymous model-local candidate only; actual float target readback, native point-light shading, independent uniform transfer, angular Lambert/Fresnel oracle and energy controls. Not public extension acceptance.",shader_sha256=Hash(shaderBytes),output,expected_strength=strength,expected_color=linearColor.ToString(),angular_ratio=angularRatio,back_factor_quarter_error=Error(quarter,full.Select((v,i)=>i%4==3?v:v*.25f).ToArray()),textured_reference_error=Error(textured,expected),wrong_transfer_error=Error(textured,wrong),wrong_alpha_error=Error(textured,wrongAlpha)});

   async Task<float[]> Draw(string label,float factor,global::Vector3 color,bool textureStrength=false,bool textureColor=false,byte[] sourceFixture=null,float metallic=0)
   {
    // Every positive and shader-negative input is validated by the pinned
    // official core validator before dispatch. Consume those exact frozen bytes.
    byte[] validated=File.ReadAllBytes(Path.Combine(Owned,"diffuse-positive-fixtures",label+".gltf"));
    var root=System.Text.Json.Nodes.JsonNode.Parse(validated);
    var authored=root["materials"][0]["extensions"]["KHR_materials_diffuse_transmission"];
    Assert.AreEqual(factor,authored["diffuseTransmissionFactor"].GetValue<float>());
    Assert.AreEqual(metallic,root["materials"][0]["pbrMetallicRoughness"]["metallicFactor"].GetValue<float>());
    Assert.AreEqual(color.x,authored["diffuseTransmissionColorFactor"][0].GetValue<float>());
    Assert.AreEqual(color.y,authored["diffuseTransmissionColorFactor"][1].GetValue<float>());
    Assert.AreEqual(color.z,authored["diffuseTransmissionColorFactor"][2].GetValue<float>());
    using var document=GltfReader.Read(validated,supportedExtensions:new HashSet<string>(StringComparer.Ordinal){"KHR_materials_diffuse_transmission","KHR_texture_transform"});
    using var asset=GltfGpuAsset.Prepare(document,GltfManagedModelResource.ComputeShaderPath,_=>null);
    asset.ConfigureProjectedVariants(1024*1024,(_,plan,images)=>
    {
     var prepared=GltfStockMaterialAdapter.PrepareSource(document,document.ReadMaterial(0),images.ReadImage);
     var core=GltfStockMaterialAdapter.AcquireSource(prepared,template,plan.MaterialUv,plan.UvProjection,true);
     try
     {
     Assert.AreEqual(factor,core.Value.GetVector4("g_flGltfDiffuseTransmissionFactor").x);
     if(label=="alpha_linear_color_srgb")
     {
      using var duplicate=GltfStockMaterialAdapter.AcquireSource(prepared,template,plan.MaterialUv,plan.UvProjection,true);Assert.AreSame(core.Value,duplicate.Value,"Identical standard transmission material did not share its immutable cache entry");
      var differentBytes=File.ReadAllBytes(Path.Combine(Owned,"diffuse-positive-fixtures","distinct_factor_cache.gltf"));
      using var differentDocument=GltfReader.Read(differentBytes,supportedExtensions:new HashSet<string>(StringComparer.Ordinal){"KHR_materials_diffuse_transmission"});
      var differentPrepared=GltfStockMaterialAdapter.PrepareSource(differentDocument,differentDocument.ReadMaterial(0));
      using var different=GltfStockMaterialAdapter.AcquireSource(differentPrepared,template,plan.MaterialUv,plan.UvProjection,true);
      Assert.AreNotSame(core.Value,different.Value,"Distinct diffuse factors aliased one cached native material");Assert.AreEqual(.75f,different.Value.GetVector4("g_flGltfDiffuseTransmissionFactor").x);Assert.AreEqual(.5f,core.Value.GetVector4("g_flGltfDiffuseTransmissionFactor").x);
     }
     return core;
     }
     catch{core.Dispose();throw;}

    });
    using var instance=asset.CreateInstance(capture.Scene.SceneWorld);
    await capture.WaitFrames();
    float[] values=await ReadHdr(capture);
    File.WriteAllBytes(Path.Combine(output,label+".rgba32f"),System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray());
    Assert.AreEqual(0,instance.CallbackErrors.Length,string.Join("\n",instance.CallbackErrors));Assert.IsTrue(instance.DrawCalls>0);
    var row=instance.Readback().Single();Assert.AreEqual("gpu-readback",row.IndexDataOrigin);Assert.AreEqual(6,row.Indices.Length);
    if(sourceFixture!=null)
    {
     var uv41=row.Fields.Single(f=>f.Semantic=="TEXCOORD" && f.SemanticIndex==7);var uv49=row.Fields.Single(f=>f.Semantic=="TEXCOORD" && f.SemanticIndex==8);
     for(int vertex=0;vertex<row.VertexCount;vertex++)
     {
      Assert.IsTrue(Math.Abs(BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+uv41.Offset)-32768f/65535f)<.000001f);
      Assert.IsTrue(Math.Abs(BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+uv41.Offset+4)-16384f/65535f)<.000001f);
      Assert.AreEqual(1.5f,BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+uv49.Offset));Assert.AreEqual(.75f,BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+uv49.Offset+4));
     }
    }
    var preview=await capture.Take(label);
    Measurements.Add(new{label,factor,color=color.ToString(),preview.Path,preview.Sha256,hdr_sha256=Hash(System.Runtime.InteropServices.MemoryMarshal.AsBytes(values.AsSpan()).ToArray()),minimum=values.Min(),maximum=values.Max()});
    return values;
   }
  }
  finally{await MainThread.Wait();nativeReferenceMaterial?.Destroy();nativeReferenceShader?.Destroy();template?.Destroy();shader?.Destroy();}
 }

 static async Task<float[]> ReadHdr(GltfGpuTriangleGateTests.NativeCapture capture)
 {
  await capture.WaitFrames();using(capture.Scene.Push()){capture.Scene.PreCameraRender();capture.Camera.InitializeRendering(false);capture.Camera.SceneCamera.AntiAliasing=false;}
  using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA32323232F).Create("diffuse_linear_"+Guid.NewGuid().ToString("N"));
  // Clear uses the native packed-color API; endpoint colors are exact in
  // both that API and float targets. Shader output precision is checked by
  // the separate HDR radiance, PI/Fresnel and factor-linearity assertions.
  target.Clear(Color.Magenta);
  await capture.WaitFrames();
  var sentinel=new float[192*192*4];Array.Fill(sentinel,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,sentinel,ImageFormat.RGBA32323232F);
  Measurements.Add(new{phase="float_target_clear_positive",target.ImageFormat,target.Width,target.Height,target.Depth,target.Mips,first_rgba=sentinel.Take(4).Select(value=>value.ToString("R",System.Globalization.CultureInfo.InvariantCulture)).ToArray(),minimum=sentinel.Min().ToString("R",System.Globalization.CultureInfo.InvariantCulture),maximum=sentinel.Max().ToString("R",System.Globalization.CultureInfo.InvariantCulture),expected_rgba=new[]{1f,0f,1f,1f},native_frames_waited_after_clear=true});
  Assert.IsTrue(sentinel.All(float.IsFinite));Assert.IsTrue(sentinel.Select((value,index)=>Math.Abs(value-(index%4==1?0f:1f))).Max()<.000001f,"Float render target clear/readback positive control failed");
  using(capture.Scene.Push())Assert.IsTrue(capture.Camera.RenderToTexture(target));
  await capture.WaitFrames();var data=new float[sentinel.Length];Array.Fill(data,float.NaN);target.GetPixels<float>((0,0,192,192),0,0,data,ImageFormat.RGBA32323232F);
  Assert.IsTrue(data.All(float.IsFinite),"Float render readback left nonfinite/sentinel values");Assert.IsTrue(Error(data,sentinel)>.01,"Render failed to replace clear sentinel");return data;
 }
 static string Hash(byte[] value)=>Convert.ToHexStringLower(SHA256.HashData(value));
 static double Center(float[] pixels,int channel=0)=>new[]{95,96}.Sum(y=>new[]{95,96}.Sum(x=>(double)pixels[(y*192+x)*4+channel]))/4;
 static float TruncateHalf(float value){Half h=(Half)value;ushort bits=BitConverter.HalfToUInt16Bits(h);return(float)h>value?(float)BitConverter.UInt16BitsToHalf((ushort)(bits-1)):(float)h;}
 static (double Lower,double Upper) HalfBounds(float value)
 {
  Assert.IsNotNull(provenQuantizer,"No interval acceptance before the independent actual pipeline precision proof");
  Assert.IsTrue(float.IsFinite(value) && value>=0 && value<65504 && (float)(Half)value==value,"Actual native HDR sample is outside proven finite half representation");
  if(value==0)return provenQuantizer=="positive-fp16-truncation"?(0,Math.Pow(2,-24)):(-Math.Pow(2,-25),Math.Pow(2,-25));
  ushort bits=BitConverter.HalfToUInt16Bits((Half)value);
  double previous=bits==0?-(float)BitConverter.UInt16BitsToHalf(1):(float)BitConverter.UInt16BitsToHalf((ushort)(bits-1));
  double next=(float)BitConverter.UInt16BitsToHalf((ushort)(bits+1));
  if(provenQuantizer=="positive-fp16-truncation")return(value,next);
  return ((previous+value)/2,(next+value)/2);
 }
 static (double Lower,double Upper) CenterBounds(float[] pixels,int channel=0)
 {
  double lower=0,upper=0;foreach(int y in new[]{95,96})foreach(int x in new[]{95,96}){var bound=HalfBounds(pixels[(y*192+x)*4+channel]);lower+=bound.Lower/4;upper+=bound.Upper/4;}return(lower,upper);
 }
 // The constant plane covers this interior fully; aliased edge coverage is not
 // a radiance oracle. Image framing/visibility remains a separate capture gate.
 static IEnumerable<int> InteriorRgb(){for(int y=64;y<128;y++)for(int x=64;x<128;x++)for(int channel=0;channel<3;channel++)yield return(y*192+x)*4+channel;}
 static bool Intersects((double Lower,double Upper) a,(double Lower,double Upper) b)=>a.Lower<=b.Upper && b.Lower<=a.Upper;
 static bool AnyDisjoint(float[] a,float[] b)=>InteriorRgb().Any(index=>!Intersects(HalfBounds(a[index]),HalfBounds(b[index])));
 static void AssertEquivalent(float[] a,float[] b,string label)
 {
  int checkedValues=0;double maximumError=0,maximumIntervalWidth=0;
  foreach(int index in InteriorRgb()){var aa=HalfBounds(a[index]);var bb=HalfBounds(b[index]);Assert.IsTrue(Intersects(aa,bb),label+" at sample "+index+" values "+a[index]+"/"+b[index]);maximumError=Math.Max(maximumError,Math.Abs((double)a[index]-b[index]));maximumIntervalWidth=Math.Max(maximumIntervalWidth,Math.Max(aa.Upper-aa.Lower,bb.Upper-bb.Lower));checkedValues++;}
  Measurements.Add(new{phase="quantized_equivalence",label,quantizer=provenQuantizer,checked_values=checkedValues,maximum_observed_error=maximumError,maximum_half_rounding_interval_width=maximumIntervalWidth,acceptance="Each pair of measured-quantizer adjacent-half intervals must intersect; no absolute or percentage epsilon"});
 }
 static float[] CubeFace(int face)=>face switch{0=>new[]{4f,.1f,.1f,1f},1=>new[]{.1f,4f,.1f,1f},2=>new[]{.1f,.1f,2f,1f},3=>new[]{.2f,.2f,.1f,1f},4=>new[]{.2f,.1f,.2f,1f},_=>new[]{.1f,.2f,.2f,1f}};
 static byte[] LiteralCube()
 {
  var writer=new Sandbox.Resources.VTexWriter();writer.Header.Width=8;writer.Header.Height=8;writer.Header.Depth=1;writer.Header.MipCount=4;writer.Header.Format=Sandbox.Resources.VTexWriter.VTEX_Format_t.VTEX_FORMAT_RGBA32323232F;
  writer.Header.Flags=Sandbox.Resources.VTexWriter.VTEX_Flags_t.VTEX_FLAG_CUBE_TEXTURE|Sandbox.Resources.VTexWriter.VTEX_Flags_t.VTEX_FLAG_CREATE_LINEAR_API_TEXTURE|Sandbox.Resources.VTexWriter.VTEX_Flags_t.VTEX_FLAG_NO_LOD;
  using var stream=new MemoryStream();using(var bytes=new BinaryWriter(stream,System.Text.Encoding.UTF8,true))for(int mip=3;mip>=0;mip--)for(int face=0;face<6;face++)for(int pixel=0;pixel<(8>>mip)*(8>>mip);pixel++)foreach(float value in CubeFace(face))bytes.Write(value);
  var resource=new Sandbox.Resources.ResourceWriter{ResourceVersion=1};resource.SetDataBlock(writer.GetData());resource.SetStreamingData(stream.ToArray());return resource.ToArray();
 }
 static double Error(float[] a,float[] b)=>a.Select((v,i)=>i%4==3?0:Math.Abs((double)v-b[i])).Max();
 static void Compare(float[] actual,float[] expected,float[] wrong,string label)
 {
  AssertEquivalent(actual,expected,label);Assert.IsTrue(AnyDisjoint(actual,wrong),label+" wrong-input negative is not separated beyond quantization");
 }
 static void AssertLinear(float[] a,float[] middle,float[] b,float factor,string label)
 {
  int checkedValues=0;double maximumError=0,maximumPositiveBound=0,maximumNegativeBound=0;
  foreach(int index in InteriorRgb())
  {
   var aa=HalfBounds(a[index]);var bb=HalfBounds(b[index]);var mm=HalfBounds(middle[index]);var expected=(Lower:factor*aa.Lower+(1-factor)*bb.Lower,Upper:factor*aa.Upper+(1-factor)*bb.Upper);
   double observedExpected=factor*(double)a[index]+(1-factor)*b[index];
   Assert.IsTrue(Intersects(mm,expected),label+" failed half-rounding interval at "+index);maximumError=Math.Max(maximumError,Math.Abs(middle[index]-observedExpected));maximumPositiveBound=Math.Max(maximumPositiveBound,(expected.Upper-observedExpected)-(mm.Lower-middle[index]));maximumNegativeBound=Math.Max(maximumNegativeBound,(mm.Upper-middle[index])-(expected.Lower-observedExpected));checkedValues++;
  }
  Assert.IsTrue(AnyDisjoint(a,b),label+" endpoints are indistinguishable beyond quantization");
  Measurements.Add(new{phase="quantized_linearity",label,factor,quantizer=provenQuantizer,checked_values=checkedValues,maximum_observed_error=maximumError,maximum_derived_positive_error_bound=maximumPositiveBound,maximum_derived_negative_error_bound=maximumNegativeBound,acceptance="Observed middle interval intersects factor-weighted endpoint intervals of the measured native quantizer; physics factor unchanged"});
 }
}
