using System;
using System.Buffers.Binary;
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
 private static Draw MixedTriangle(int node,Material material,NVector4 color)
 {
  var draw=TriangleDraw(0,material,color,global::Vector3.Forward);var offset=(node==0?-1.5f:1.5f)/.0254f;
  for(var v=0;v<draw.Vertices.Length;v++)draw.Vertices[v].Position+=new global::Vector3(0,offset,0);
  return draw;
 }
 private static object CheckMixedGpu(GltfGpuInstance instance,int? variant,bool raw=false)
 {
  var rows=instance.Readback();Require(rows.Count==2&&rows.All(r=>r.VertexCount==3&&r.IndexDataOrigin=="gpu-readback"&&r.Indices.SequenceEqual(new uint[]{0,1,2})),"Mixed fixture lost actual GPU topology.");
  var maxPosition=0f;var uvErrors=new List<float>();
  foreach(var row in rows)
  {
   Require(row.Node is 0 or 1,"Mixed fixture node identity changed.");var p=row.Fields.Single(f=>f.Semantic=="POSITION"&&f.SemanticIndex==0);var uv=row.Fields.Single(f=>f.Semantic=="TEXCOORD"&&f.SemanticIndex==0);
   var points=new[]{new global::Vector3(0,-30,-30),new global::Vector3(0,30,-30),new global::Vector3(0,0,30)};
   for(var v=0;v<3;v++)
   {
    var expected=System.Numerics.Vector3.Transform(new(0,points[v].y+(row.Node==0?-1.5f:1.5f)/.0254f,points[v].z),instance.Placement);
    for(var c=0;c<3;c++){var actual=BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(v*row.VertexStride+p.Offset+c*4,4));var want=c==0?expected.X:c==1?expected.Y:expected.Z;maxPosition=MathF.Max(maxPosition,MathF.Abs(actual-want));}
    if(raw||row.Node==1){var actual=BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(v*row.VertexStride+uv.Offset,4));var want=raw ? .25f : variant.HasValue ? .25f : .75f;uvErrors.Add(MathF.Abs(actual-want));}
   }
   if(raw)Require(row.SubmittedMaterialOverride,"Mixed raw override route was not submitted.");else Require(!row.SubmittedMaterialOverride&&row.SubmittedMaterialIndex==(row.Node==0?(variant.HasValue?2:0):(variant.HasValue?3:1)),"Mixed selected material identity changed.");
  }
  Require(maxPosition<=.00002f&&uvErrors.All(e=>e<=.000001f),"Mixed transformed/raw UV or literal position mismatch.");
  return new{max_position_error_inches=maxPosition,max_uv_error=uvErrors.Count==0?0:uvErrors.Max(),rows=rows.Select(r=>new{r.Node,r.VertexCount,r.VertexStride,r.Fields,r.Indices,r.IndexDataOrigin,r.SubmittedMaterialIndex,r.SubmittedMaterialOverride,bytes_sha256=Hash(r.Bytes)}).ToArray()};
 }
 public async Task MixedPublicDefaultsAndExplicitReaderVariants()
 {
  RequireIdle();var cfg=JsonSerializer.Deserialize<Configuration>(File.ReadAllBytes(configurationPath))!;var generation=JsonSerializer.Deserialize<Generation>(ReadVerified(cfg.GenerationManifestPath,cfg.GenerationManifestSha256))!;
  var output=FreshOutput(checkout,cfg.OutputDirectory+"-mixed");Shader shader=null;Material reference=null;CandidateCapture capture=null;
  try
  {
   using(var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90)))shader=await Shader.CompileAsync(Encoding.UTF8.GetString(ReadVerified(generation.ReferenceSourcePath,cfg.ReferenceSourceSha256)),timeout.Token);
   await MainThread.Wait();Require(shader is {IsValid:true,IsAnonymous:true},"Mixed independent reference shader failed.");reference=Material.Create("mixed_reference_"+Guid.NewGuid().ToString("N"),shader);capture=new CandidateCapture(output);capture.Light(true);AssertEmpty(await capture.Take("mixed_empty_before"));
   var masks=new IReadOnlyList<int>[2];for(var node=0;node<2;node++){using(var literal=new LiteralDraw(capture.Scene.SceneWorld,new[]{MixedTriangle(node,reference,NVector4.One)}))masks[node]=Interior(await capture.Take("mixed_mask_"+node),3,64);await capture.FlushDeletes();AssertEmpty(await capture.Take("mixed_mask_"+node+"_deleted"));}
   var stock=Material.Load("materials/default/white.vmat");CandidateCapture.Frame rawReference;
   using(var literal=new LiteralDraw(capture.Scene.SceneWorld,new[]{MixedTriangle(0,stock,NVector4.One),MixedTriangle(1,stock,NVector4.One)}))rawReference=await capture.Take("mixed_raw_stock_literal");await capture.FlushDeletes();AssertEmpty(await capture.Take("mixed_raw_reference_deleted"));
   var unlitReferences=new CandidateCapture.Frame[2];for(var color=0;color<2;color++){using(var literal=new LiteralDraw(capture.Scene.SceneWorld,new[]{MixedTriangle(0,reference,UnlitMixedMaterialFixture.ExpectedUnlit(color==0?null:0))}))unlitReferences[color]=await capture.Take("mixed_independent_unlit_reference_"+color);await capture.FlushDeletes();AssertEmpty(await capture.Take("mixed_unlit_reference_"+color+"_deleted"));}
   foreach(var compiled in new[]{false,true})
   {
    var label=compiled?"compiled":"source";var litBaselines=new CandidateCapture.Frame[2];
    for(var alt=0;alt<2;alt++)
    {
     var source=UnlitMixedMaterialFixture.Build(false,alt==1);var bytes=compiled?GltfCompiler.Compile(source,null,UnlitMixedMaterialFixture.Extensions):source;
     using var mount=new UnlitPublicMount(new(){Name="derived_lit_baseline",Compiled=compiled,Bytes=bytes});var model=mount.Load();Require(model.native.IsNull&&model.Materials.Length==1&&!model.GltfResource.Asset.HasProjectedVariants&&!model.Materials[0].ShaderName.Contains("gltf_unlit",StringComparison.OrdinalIgnoreCase),"Preexisting lit-only route changed because unused unlit exists.");
     ModelRenderer renderer;using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
     try{litBaselines[alt]=await capture.Take("mixed_"+label+"_legacy_lit_baseline_"+alt);var row=renderer.GltfInstance.Readback().Single();var uv=row.Fields.Single(f=>f.Semantic=="TEXCOORD"&&f.SemanticIndex==0);var actual=BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(uv.Offset,4));Require(MathF.Abs(actual-(alt==0 ? .75f : .25f))<=.000001f,"Standalone baseline did not bake the literal source UV transform.");}
     finally{using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}}
     await capture.FlushDeletes();AssertEmpty(await capture.Take("mixed_"+label+"_baseline_"+alt+"_deleted"));
    }
    foreach(var candidateReader in new[]{false,true})
    {
     var source=UnlitMixedMaterialFixture.Build();var bytes=compiled?GltfCompiler.Compile(source,null,UnlitMixedMaterialFixture.Extensions):source;var prefix="mixed_"+label+(candidateReader?"_explicit_variant_reader":"_public_default");
     using var mount=candidateReader?null:new UnlitPublicMount(new(){Name="derived_mixed",Compiled=compiled,Bytes=bytes});
     using var document=candidateReader?GltfReader.Read(bytes,null,UnlitMixedMaterialFixture.Extensions):null;
     using var ownedAsset=candidateReader?GltfGpuAsset.PrepareProjected(document,GltfManagedModelResource.ComputeShaderPath,1024*1024,(_,_)=>Material.FromShader("shaders/gltf_unlit_five_uv.shader"),global::Sandbox.GltfUnlitMaterialPlan.Features.All,legacyLit:true):null;
     var model=mount?.Load();var asset=candidateReader?ownedAsset:model.GltfResource.Asset;ModelRenderer renderer=null;GltfGpuInstance instance;
     if(candidateReader)instance=asset.CreateInstance(capture.Scene.SceneWorld);else{Require(model.native.IsNull&&model.IsValid&&!model.IsError,"Public mixed default load failed.");using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}instance=renderer.GltfInstance;}
     GltfGpuInstance other=null;byte[][] stable=null;
     if(candidateReader){other=asset.CreateInstance(capture.Scene.SceneWorld);other.Placement=NMatrix.CreateTranslation(-10,0,0);await capture.Take(prefix+"_other_initialized");stable=other.Readback().OrderBy(r=>r.Node).Select(r=>(byte[])r.Bytes.Clone()).ToArray();}
     try
     {
      var transition=0;foreach(var variant in candidateReader?new int?[]{null,0,null}:new int?[]{null})
      {
       var step=transition++;if(candidateReader)instance.SetVariant(variant);var actual=await capture.Take(prefix+"_step_"+step+"_variant_"+(variant.HasValue?"alternate":"default"));
       var unlitReference=unlitReferences[variant.HasValue?1:0];
       await capture.FlushDeletes();Compare(unlitReference,actual,masks[0],cfg);Compare(litBaselines[variant.HasValue?1:0],actual,masks[1],cfg);var gpu=CheckMixedGpu(instance,variant);
       if(other is not null){var after=other.Readback().OrderBy(r=>r.Node).ToArray();Require(after.Select((r,i)=>r.Bytes.SequenceEqual(stable[i])).All(x=>x),"Changing mixed instance A modified B's output buffers.");Require(other.ActiveVariant is null,"Changing A mutated B's variant selection.");}
       if(variant==0)
       {
        if(renderer is null)instance.SetMaterialOverride(stock);else renderer.MaterialOverride=stock;var overridden=await capture.Take(prefix+"_raw_stock_override");var rawGpu=CheckMixedGpu(instance,variant,true);Compare(rawReference,overridden,masks.SelectMany(m=>m).ToArray(),cfg);
        if(renderer is null)instance.ClearMaterialOverrides();else renderer.ClearMaterialOverrides();var restored=await capture.Take(prefix+"_variant_restored_after_override");Compare(actual,restored,masks.SelectMany(m=>m).ToArray(),cfg);CheckMixedGpu(instance,variant);
        Measurements.Add(new{phase="mixed_raw_override_restore",compiled,candidate_reader=candidateReader,passed=true,raw_gpu=rawGpu,actual=overridden.Path,restored=restored.Path});
       }
       Measurements.Add(new{phase="mixed_material_capture",compiled,candidate_reader=candidateReader,public_load=!candidateReader,variant,transition_step=step,passed=true,input_sha256=Hash(bytes),actual=actual.Path,actual.Sha256,independent_unlit_reference=unlitReference.Path,legacy_lit_baseline=litBaselines[variant.HasValue?1:0].Path,expected_lit_base_color=new[]{UnlitMixedMaterialFixture.ExpectedLitBaseColor(variant).X,UnlitMixedMaterialFixture.ExpectedLitBaseColor(variant).Y,UnlitMixedMaterialFixture.ExpectedLitBaseColor(variant).Z},gpu,scope=candidateReader?"Explicit reader enables material variants; not public variant support.":"Public Model.Load+ordinaryModelRenderer default mixed path; optional variants retain default fallback."});
      }
     }
     finally{if(renderer is null)instance.Dispose();else using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}other?.Dispose();}
     var stopped=instance.DrawCalls;await capture.FlushDeletes();AssertEmpty(await capture.Take(prefix+"_deleted"));Require(stopped==instance.DrawCalls,"Deleted mixed instance still submits.");
    }
   }
   Measurements.Add(new{phase="mixed_matrix_complete",passed=true,public_default_forms=2,explicit_reader_forms=2,variant_pose_measurements=6,scope="Public default mixed plus separately explicit-reader material variants, existing legacy lit baseline, transformed/material-specific UV, raw stock overrides, independent instances and deletion. Public material-variant extension remains unsupported."});
  }
  finally{await MainThread.Wait();capture?.Dispose();reference?.Destroy();shader?.Destroy();}
 }
}

[TestClass]
public sealed class UnlitMixedExecution
{
 public static List<object> Measurements=>UnlitTestGraphCandidateGate.Measurements;
 public static void Configure(string root,string config)=>UnlitTestGraphCandidateGate.Configure(root,config);
 [TestMethod] public async Task MixedPublicDefaultAndCandidateVariants()=>await new UnlitTestGraphCandidateGate().MixedPublicDefaultsAndExplicitReaderVariants();
}
