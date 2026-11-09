using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;
using NVector4=System.Numerics.Vector4;

public sealed partial class UnlitTestGraphCandidateGate
{
 [TestMethod]
 public async Task SourceAdapterTextureAlphaSidednessAndOverrides()
 {
  RequireIdle();var cfg=JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(configurationPath))!;
  var generation=JsonSerializer.Deserialize<Generation>(ReadVerified(cfg.GenerationManifestPath,cfg.GenerationManifestSha256))!;
  var output=FreshOutput(checkout,cfg.OutputDirectory+"-features");
  var shaders=new List<Shader>();var materials=new List<Material>();CandidateCapture capture=null;
  try
  {
   async Task<Material> Compile(string path,string hash)
   {
    using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));var shader=await Shader.CompileAsync(Encoding.UTF8.GetString(ReadVerified(path,hash)),timeout.Token);await MainThread.Wait();
    Require(shader is {IsValid:true,IsAnonymous:true},"Feature shader failed to compile.");shaders.Add(shader);var mat=Material.Create("unlit_feature_"+Guid.NewGuid().ToString("N"),shader);materials.Add(mat);return mat;
   }
   var candidate=cfg.PublicSource?null:await Compile(cfg.SourceShaderPath,cfg.SourceShaderSha256);var reference=await Compile(generation.ReferenceSourcePath,cfg.ReferenceSourceSha256);
   var compute=cfg.PublicSource?null:await Compile(Path.Combine(checkout,"engine/Tools/Gltf/direct-gpu/shaders/gltf_direct_deform.shader"),cfg.ComputeSourceSha256);
   var templates=new Dictionary<string,Material>();if(!cfg.PublicSource)foreach(var mode in new[]{"OPAQUE","MASK","BLEND"}){var m=candidate.CreateCopy();materials.Add(m);m.SetFeature("F_ALPHA_TEST",mode=="MASK"?1:0);m.SetFeature("F_TRANSLUCENT",mode=="BLEND"?1:0);Require(m.Flags.IsAlphaTest==(mode=="MASK")&&m.Flags.IsTranslucent==(mode=="BLEND"),"Native alpha classification mismatch: "+mode);templates.Add(mode,m);}
   capture=new CandidateCapture(output);AssertEmpty(await capture.Take("feature_empty_before"));
   var cases=new[]{
    new UnlitFeatureFixtures.Fixture("opaque_ignores_alpha","OPAQUE",new(1,.25f,0,0),new(1,1,1,0)),
    new UnlitFeatureFixtures.Fixture("mask_below","MASK",new(1,.25f,0,.25f),NVector4.One),
    new UnlitFeatureFixtures.Fixture("mask_equality","MASK",new(1,.25f,0,.5f),NVector4.One),
    new UnlitFeatureFixtures.Fixture("mask_zero_equality","MASK",new(1,.25f,0,0),NVector4.One,Cutoff:0),
    new UnlitFeatureFixtures.Fixture("mask_cutoff_above_one","MASK",new(1,.25f,0,1),NVector4.One,Cutoff:1.25f),
    new UnlitFeatureFixtures.Fixture("blend_straight_alpha_product","BLEND",new(1,.5f,.25f,.5f),new(.5f,1,.5f,.5f)),
    new UnlitFeatureFixtures.Fixture("single_sided_back_empty","OPAQUE",new(1,.25f,0,1),NVector4.One,Reversed:true),
    new UnlitFeatureFixtures.Fixture("double_sided_front_visible","OPAQUE",new(1,.25f,0,1),NVector4.One,DoubleSided:true),
    new UnlitFeatureFixtures.Fixture("double_sided_back_visible","OPAQUE",new(1,.25f,0,1),NVector4.One,DoubleSided:true,Reversed:true),
    new UnlitFeatureFixtures.Fixture("texture_factor_vertex_linear","OPAQUE",new(.8f,.5f,1,0),new(.5f,1,.25f,0),Texture:true),
    new UnlitFeatureFixtures.Fixture("texture_alpha_factor_vertex_product","BLEND",new(.8f,.5f,1,.75f),new(.5f,1,.25f,.5f),Texture:true),
    new UnlitFeatureFixtures.Fixture("texture_nearest_second_texel","OPAQUE",NVector4.One,NVector4.One,Texture:true,Uv:.75f),
    new UnlitFeatureFixtures.Fixture("texture_nearest_offcenter","OPAQUE",NVector4.One,NVector4.One,Texture:true,Uv:.4f),
    new UnlitFeatureFixtures.Fixture("texture_bilinear_offcenter","OPAQUE",NVector4.One,NVector4.One,Texture:true,Uv:.4f,LinearSampling:true),
    new UnlitFeatureFixtures.Fixture("texture_clamp_outside","OPAQUE",NVector4.One,NVector4.One,Texture:true,Uv:1.25f),
    new UnlitFeatureFixtures.Fixture("texture_repeat_outside","OPAQUE",NVector4.One,NVector4.One,Texture:true,Uv:1.25f,Repeat:true),
    new UnlitFeatureFixtures.Fixture("texture_transform_texcoord9","OPAQUE",NVector4.One,NVector4.One,Texture:true,Transform:true)
   };
   var extensions=new HashSet<string>{"KHR_materials_unlit","KHR_texture_transform"};
   foreach(var test in cases)
   {
    var product=test.Factor*test.Color*(test.Texture?UnlitFeatureFixtures.TextureValue(test.Transform ? test.Uv+.5f : test.Uv,test.LinearSampling,test.Repeat):NVector4.One);
    var empty=(!test.DoubleSided&&test.Reversed)||(test.Mode=="MASK"&&product.W<test.Cutoff);
    if(test.Mode=="BLEND")product=new(product.X*product.W,product.Y*product.W,product.Z*product.W,1);else product.W=1;
    CandidateCapture.Frame expected=null;IReadOnlyList<int> mask=null;
    if(!empty){using(var literal=new LiteralDraw(capture.Scene.SceneWorld,new[]{TriangleDraw(0,reference,product,global::Vector3.Forward)}))expected=await capture.Take(test.Name+"_reference");mask=Interior(expected,3,64);await capture.FlushDeletes();AssertEmpty(await capture.Take(test.Name+"_reference_deleted"));}
    var source=UnlitFeatureFixtures.Build(test);
    foreach(var compiled in new[]{false,true})
    {
     var bytes=compiled?GltfCompiler.Compile(source,null,extensions):source;using var doc=GltfReader.Read(bytes,null,extensions);
     using var publicMount=cfg.PublicSource?new UnlitPublicMount(new UnlitTestReference.Form{Name="derived_"+test.Name,Compiled=compiled,Bytes=bytes}):null;
     var publicModel=publicMount?.Load();var asset=cfg.PublicSource?publicModel.GltfResource.Asset:GltfGpuAsset.Create(doc,compute,_=>null);using var ownedAsset=cfg.PublicSource?null:asset;
     if(!cfg.PublicSource)asset.ConfigureProjectedSourceMaterials(1024*1024,(_,_)=>templates[test.Mode],global::Sandbox.GltfUnlitMaterialPlan.Features.All);
     ModelRenderer publicRenderer=null;GltfGpuInstance instance;
     if(cfg.PublicSource){Require(publicModel.native.IsNull,"Public fixture has native model.");using(capture.Scene.Push()){publicRenderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);publicRenderer.Model=publicModel;publicRenderer.Enabled=true;capture.Scene.EditorTick(0,0);}instance=publicRenderer.GltfInstance;}else instance=asset.CreateInstance(capture.Scene.SceneWorld);
     var calls=0;var label=test.Name+(compiled?"_compiled":"_source");
     try
     {
      var actual=await capture.Take(label);Require(instance.DrawCalls>0&&instance.CallbackErrors.Length==0,"Feature did not submit actual buffers.");
      if(empty)AssertEmpty(actual);else{Compare(expected,actual,mask,cfg);CompareCoverage(expected,actual);}
      Measurements.Add(new{phase="source_feature",public_path=cfg.PublicSource,name=test.Name,compiled,passed=true,empty,expected=expected?.Path,actual=actual.Path,actual.Sha256,expected_linear_rgb=new[]{product.X,product.Y,product.Z,product.W},source_sha256=Hash(source),selected_materials=asset.GetMaterials().Select(m=>new{m.ShaderName,m.Flags.IsTranslucent,m.Flags.IsAlphaTest,backfaces=m.GetFeature("F_RENDER_BACKFACES")}).ToArray()});
      if(test.Name=="opaque_ignores_alpha")
      {
       var before=actual;var stock=Material.Load("materials/default/white.vmat");if(publicRenderer is not null)publicRenderer.MaterialOverride=stock;else instance.SetMaterialOverride(0,stock);var overridden=await capture.Take(label+"_vmat_override");
       Require(instance.Readback().All(r=>r.SubmittedMaterialOverride&&ReferenceEquals(r.SubmittedMaterial,stock)),"Actual stock vmat override was not submitted through the raw route.");
       Require(!before.Rgba.SequenceEqual(overridden.Rgba),"Stock vmat override failed to change rendered pixels.");if(publicRenderer is not null)publicRenderer.ClearMaterialOverrides();else instance.ClearMaterialOverrides();var restored=await capture.Take(label+"_source_restored");Compare(before,restored,mask,cfg);
       Require(instance.Readback().All(r=>!r.SubmittedMaterialOverride),"Clearing override did not restore source material route.");Measurements.Add(new{phase="vmat_override_and_restore",compiled,passed=true,stock=stock.ResourcePath,before=before.Path,overridden=overridden.Path,restored=restored.Path});
      }
      calls=instance.DrawCalls;
     }
     finally{if(publicRenderer is not null){using(capture.Scene.Push()){publicRenderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}}else instance.Dispose();}
     await capture.FlushDeletes();AssertEmpty(await capture.Take(label+"_deleted"));Require(instance.DrawCalls==calls,"Deleted feature callback remained active.");
    }
   }
   if(!cfg.PublicSource){capture.SetHeight(800);var gpuFrame=0;
   var gpu=await UnlitTestGpuProbe.Run(checkout,capture.Scene.SceneWorld,compute,async()=>{await capture.Take("gpu20_frame_"+(gpuFrame++));},.0002f,.00001f,_=>null,asset=>asset.ConfigureProjectedSourceMaterials(1024*1024,(_,_)=>templates["OPAQUE"],global::Sandbox.GltfUnlitMaterialPlan.Features.LinearFactor));
   Measurements.Add(new{phase="source_gpu20",passed=true,measurements=gpu});}
   Measurements.Add(new{phase="source_features_complete",passed=true,cases=cases.Length*2,scope="Actual installed unlit source adapter, derived fixtures, source+portable compiled. Public loading and installed shipped shader remain separate."});
  }
  finally{await MainThread.Wait();capture?.Dispose();for(var i=materials.Count-1;i>=0;i--)materials[i]?.Destroy();for(var i=shaders.Count-1;i>=0;i--)shaders[i]?.Destroy();}
 }
}
