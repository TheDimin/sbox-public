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
using NMatrix=System.Numerics.Matrix4x4;

public sealed partial class UnlitTestGraphCandidateGate
{
 public async Task PublicRequiredUnlitSourceAndCompiledRendering()
 {
  RequireIdle();var cfg=JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(configurationPath))!;
  var generation=JsonSerializer.Deserialize<Generation>(ReadVerified(cfg.GenerationManifestPath,cfg.GenerationManifestSha256))!;
  var output=FreshOutput(checkout,cfg.OutputDirectory+"-public");var materials=new List<Material>();Shader referenceShader=null;CandidateCapture capture=null;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))referenceShader=await Shader.CompileAsync(Encoding.UTF8.GetString(ReadVerified(generation.ReferenceSourcePath,cfg.ReferenceSourceSha256)),timeout.Token);
   await MainThread.Wait();Require(referenceShader is {IsValid:true,IsAnonymous:true},"Independent reference shader failed.");
   var reference=Material.Create("unlit_public_reference_"+Guid.NewGuid().ToString("N"),referenceShader);materials.Add(reference);
   capture=new CandidateCapture(output);AssertEmpty(await capture.Take("public_empty_before"));
   var masks=new IReadOnlyList<int>[2];for(var node=0;node<2;node++){using(var literal=new LiteralDraw(capture.Scene.SceneWorld,new[]{ModelDraw(node,NVector4.One,reference,true)}))masks[node]=Interior(await capture.Take("public_mask_"+node),3);await capture.FlushDeletes();AssertEmpty(await capture.Take("public_mask_"+node+"_deleted"));}
   CandidateCapture.Frame expected;using(var literal=new LiteralDraw(capture.Scene.SceneWorld,Enumerable.Range(0,2).Select(node=>ModelDraw(node,NVector4.One,reference,true)).ToArray()))expected=await capture.Take("public_literal_reference");
   await capture.FlushDeletes();AssertEmpty(await capture.Take("public_literal_deleted"));
   var litMaterials=new Material[2];for(var index=0;index<2;index++){var factor=UnlitTestReference.BaseColors[index];litMaterials[index]=Material.Load("materials/default/white.vmat").CreateCopy();materials.Add(litMaterials[index]);litMaterials[index].Set("g_vColorTint",new global::Vector4(Srgb(factor.X),Srgb(factor.Y),Srgb(factor.Z),1));litMaterials[index].Set("g_flMetalness",0);}
   CandidateCapture.Frame darkLit,brightLit;using(var literal=new LiteralDraw(capture.Scene.SceneWorld,Enumerable.Range(0,2).Select(node=>ModelDraw(node,NVector4.One,litMaterials[node],false)).ToArray())){capture.Light(false);darkLit=await capture.Take("public_lit_negative_dark");capture.Light(true);brightLit=await capture.Take("public_lit_negative_bright");}
   await capture.FlushDeletes();AssertEmpty(await capture.Take("public_lit_negative_deleted"));
   var completed=0;
   foreach(var form in UnlitTestReference.OpenForms(checkout))
   {
    using var mount=new UnlitPublicMount(form);var model=mount.Load();Require(model is {IsValid:true,IsError:false}&&model.native.IsNull&&model.GltfResource is not null,"Required-unlit public Model.Load failed or created a native model.");
    Require(model.Materials.Length==2,"Authored orange/blue slots lost.");
    foreach(var material in model.Materials){Require(material is {IsValid:true,IsError:false}&&!material.Shader.IsAnonymous,"Public source material must use the shipped named shader.");Require(material.ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase),"Public unlit model did not select the installed source shader.");}
    ModelRenderer renderer=null;GltfGpuInstance instance=null;
    try
    {
     using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.WorldTransform=Transform.Zero;renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
     instance=renderer.GltfInstance;Require(instance is not null&&renderer.SceneObject is SceneCustomObject&&renderer.SceneObject.Model is null,"Public ordinary ModelRenderer did not use the native-free GPU path.");
     Require(instance.Placement==NMatrix.Identity&&renderer.SceneObject.Transform==Transform.Zero,"Public placement was applied twice.");
     var native=renderer.SceneObject.native.GetModelHandle();try{Require(native.IsNull||!native.IsStrongHandleValid(),"Custom object has a hidden native model.");}finally{if(!native.IsNull)native.DestroyStrongHandle();}
     capture.Light(false);var dark=await capture.Take(SafeName(form.Name)+"_public_dark");capture.Light(true);var bright=await capture.Take(SafeName(form.Name)+"_public_bright");
     var gpu=CheckGpu(instance,NVector4.One,cfg);var image=UnlitTestImageChecks.Measure(expected.Rgba,dark.Rgba,bright.Rgba,darkLit.Rgba,brightLit.Rgba,masks);
     UnlitTestImageChecks.RequireWithin(image,1,3,1,3,8);CompareCoverage(expected,dark);CompareCoverage(expected,bright);
     Require(model.GltfResource.Asset.ComputeMaterial?.ShaderName.Contains("gltf_deform_cs",StringComparison.OrdinalIgnoreCase)==true,"Public draw must use the named installed compute shader.");
     var stock=Material.Load("materials/default/white.vmat");renderer.MaterialOverride=stock;var overridden=await capture.Take(SafeName(form.Name)+"_public_stock_vmat_override");Require(instance.Readback().All(row=>row.SubmittedMaterialOverride&&ReferenceEquals(row.SubmittedMaterial,stock)),"Public stock-vmat override did not use raw source streams.");Require(!bright.Rgba.SequenceEqual(overridden.Rgba),"Stock vmat override changed no pixels.");
     renderer.ClearMaterialOverrides();var restored=await capture.Take(SafeName(form.Name)+"_public_source_restored");Compare(bright,restored,masks.SelectMany(mask=>mask).ToArray(),cfg);Require(instance.Readback().All(row=>!row.SubmittedMaterialOverride),"Public clear did not restore original source material.");
     renderer.Enabled=false;using(capture.Scene.Push()){capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}await capture.FlushDeletes();AssertEmpty(await capture.Take(SafeName(form.Name)+"_public_disabled"));
     renderer.Enabled=true;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);instance=renderer.GltfInstance;var reenabled=await capture.Take(SafeName(form.Name)+"_public_reenabled");Compare(bright,reenabled,masks.SelectMany(mask=>mask).ToArray(),cfg);
     Require(ReferenceEquals(model,mount.Load()),"Renderer lifecycle invalidated its model resource.");
     Measurements.Add(new{phase="public_unlit_model",form=form.Name,passed=true,input_sha256=Hash(form.Bytes),resource=model.ResourcePath,producer_engine=typeof(Model).Assembly.ManifestModule.ModuleVersionId.ToString(),producer_gltf=typeof(GltfReader).Assembly.ManifestModule.ModuleVersionId.ToString(),native_model=false,gpu,image,dark_image=dark.Path,bright_image=bright.Path,reference_image=expected.Path,override_image=overridden.Path,restored_image=restored.Path,shaders=model.Materials.Select(m=>m.ShaderName).ToArray(),scope="Public Model.Load and ordinary ModelRenderer, source materials from shipped shader, exact linear colors and lighting independence, raw stock-vmat overrides, disable/reenable and deletion. Linux."});completed++;
    }
    finally{if(renderer is not null){using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}}}
    var stopped=instance.DrawCalls;await capture.FlushDeletes();AssertEmpty(await capture.Take(SafeName(form.Name)+"_public_deleted"));Require(instance.DrawCalls==stopped,"Deleted public renderer still submits.");
   }
   Require(completed==4,"Public UnlitTest form inventory incomplete.");Measurements.Add(new{phase="public_unlit_complete",passed=true,forms=completed});
  }
  finally{await MainThread.Wait();capture?.Dispose();for(var i=materials.Count-1;i>=0;i--)materials[i]?.Destroy();referenceShader?.Destroy();}
 }
}

[TestClass]
public sealed class UnlitPublicCaptureExecution
{
 public static List<object> Measurements=>UnlitTestGraphCandidateGate.Measurements;
 public static void Configure(string root,string config)=>UnlitTestGraphCandidateGate.Configure(root,config);
 [TestMethod] public async Task RequiredUnlitPublicRendering()=>await new UnlitTestGraphCandidateGate().PublicRequiredUnlitSourceAndCompiledRendering();
}
