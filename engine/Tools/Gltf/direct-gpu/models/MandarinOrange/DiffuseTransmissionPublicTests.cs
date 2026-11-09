using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class DiffuseTransmissionPublicTests
{
 public static string CheckoutRoot {get;set;}
 public static string CanonicalCompiledPath {get;set;}
 public static string CanonicalCompiledSha256 {get;set;}
 public static List<object> Measurements {get;}=new();
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));

 // Run once as frozen suite setup after root approves the candidate controls.
 // This deliberately uses the actual installed compiler API, not copied code.
 public static void PrepareCanonicalCompiled()
 {
  ThreadSafe.AssertIsMainThread();
  string canonical=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/MandarinOrange/MandarinOrange.glb_c");
  Assert.AreEqual(Path.GetFullPath(canonical),Path.GetFullPath(CanonicalCompiledPath));
  string directory=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");byte[] source=File.ReadAllBytes(Path.Combine(directory,"MandarinOrange.gltf"));
  Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(source));
  byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(directory,uri));
  byte[] compiled=GltfCompiler.CompileWithEmbeddedImages(source,Resolve,GltfModelLoader.SupportedExtensions);
  using var decoded=GltfReader.Read(compiled,uri=>throw new InvalidOperationException("Canonical compiled artifact still depends on external URI: "+uri),GltfModelLoader.SupportedExtensions);
  Assert.AreEqual(4,decoded.Root.GetProperty("images").GetArrayLength());
  using var sourceJson=System.Text.Json.JsonDocument.Parse(source);
  for(int index=0;index<4;index++)
  {
   Assert.IsFalse(decoded.Root.GetProperty("images")[index].TryGetProperty("uri",out _));
   Assert.IsTrue(decoded.Root.GetProperty("images")[index].TryGetProperty("bufferView",out _));
   string original=sourceJson.RootElement.GetProperty("images")[index].GetProperty("uri").GetString();Assert.AreEqual(Hash(Resolve(original)),Hash(decoded.ReadImage(index).Bytes));
  }
  Assert.AreEqual(1f,decoded.ReadMaterial(0).DiffuseTransmission.Factor);
  string previous=File.Exists(canonical)?Hash(File.ReadAllBytes(canonical)):null;
  Directory.CreateDirectory(Path.GetDirectoryName(canonical));File.WriteAllBytes(canonical,compiled);CanonicalCompiledSha256=Hash(compiled);
  Measurements.Add(new{phase="single_canonical_compile",path=canonical,previous_sha256=previous,sha256=CanonicalCompiledSha256,bytes=compiled.Length,source_sha256=Hash(source),compiler="actual installed GltfCompiler.CompileWithEmbeddedImages",gltf_mvid=typeof(GltfCompiler).Assembly.ManifestModule.ModuleVersionId,encoded_images_preserved=4,scope="One stable explicit derived artifact, source dependency and all embedded image bytes checked. This does not establish automatic native asset-graph packaging."});
 }

 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task ActualMandarinDiffuseTransmissionAndInstanceOverride(bool compiled)
 {
  await MainThread.Wait();
  string sourceDirectory=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");
  string sourcePath=Path.Combine(sourceDirectory,"MandarinOrange.gltf");byte[] source=File.ReadAllBytes(sourcePath);
  Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(source));
  // Frozen suite setup refreshes this one canonical artifact through the actual
  // installed embedding compiler; never create another per-model derivative.
  byte[] bytes=compiled?File.ReadAllBytes(CanonicalCompiledPath):source;
  string inputHash=Hash(bytes);if(compiled)Assert.AreEqual(CanonicalCompiledSha256,inputHash);
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/MandarinOrange/diffuse-public",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string directory=Path.Combine(Path.GetTempPath(),"gltf_mandarin_diffuse_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null;Model model=null;ModelRenderer first=null,second=null;Material overrideZero=null;ScenePointLight rear=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(4,0,0),global::Vector3.Zero,1.25f);
  try
  {
   if(!compiled)foreach(string file in Directory.GetFiles(sourceDirectory))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
   string name="mandarin_diffuse_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(directory,name),bytes);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);
   rear=new ScenePointLight(capture.Scene.SceneWorld,new global::Vector3(-200,30,60),1000,Color.White*4);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   model=Model.Load(name,0);Assert.IsTrue(model.IsValid && !model.IsError && model.native.IsNull);
   using(capture.Scene.Push()){first=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);first.Model=model;first.Enabled=true;capture.Scene.EditorTick(0,0);}
   Assert.IsNotNull(first.GltfInstance,"Ordinary renderer must realize the actual public material lazily");
   var material=model.Materials.Single();StringAssert.Contains(material.ShaderName,"gltf_diffuse_transmission");Assert.AreEqual(1f,material.GetVector4("g_flGltfDiffuseTransmissionFactor").x);
   Assert.AreEqual(1f,material.GetVector4("g_flGltfHasDiffuseTransmissionTexture").x);Assert.AreEqual(1f,material.GetVector4("g_flGltfHasDiffuseTransmissionColorTexture").x);
   Assert.IsTrue(material.GetTexture("g_tGltfDiffuseTransmissionTexture") is {IsValid:true,IsError:false});Assert.IsTrue(material.GetTexture("g_tGltfDiffuseTransmissionColorTexture") is {IsValid:true,IsError:false});
   var decoded=model.GltfResource.Asset.Document.ReadMaterial(0);Assert.IsNotNull(decoded.DiffuseTransmission);Assert.IsFalse(decoded.DiffuseTransmission.Texture.IsSrgb);Assert.IsTrue(decoded.DiffuseTransmission.ColorTexture.IsSrgb);
   Assert.IsFalse(model.GltfResource.Asset.Document.UnsupportedOptionalExtensions.Contains("KHR_materials_diffuse_transmission"));
   var actual=await capture.Take("actual_public_diffuse_transmission");Assert.IsTrue(actual.Coverage>100);Assert.AreEqual(0,first.GltfInstance.CallbackErrors.Length,string.Join("\n",first.GltfInstance.CallbackErrors));
   var row=first.GltfInstance.Readback().Single();Assert.AreEqual("gpu-readback",row.IndexDataOrigin);Assert.AreEqual(122508,row.Indices.Length);Assert.IsTrue(first.GltfInstance.DrawCalls>0 && ReferenceEquals(material,row.SubmittedMaterial));
   // Target the new seven-UV output stride without rerunning the historical
   // full geometry/reimport suites. These source samples and the -60-degree
   // parent rotation are independent literal data from the pinned model.
   var positions=row.Fields.Single(field=>field.Semantic=="POSITION");double maximumPositionError=0;
   foreach(var sample in new[]{(Vertex:0,X:.002522495109587908,Y:-.005430183839052916,Z:-.004190611653029919),(Vertex:42,X:.0011712511768564582,Y:-.0051133232191205025,Z:-.0042830295860767365),(Vertex:12000,X:.00506800040602684,Y:.0011182439047843218,Z:.002062230370938778),(Vertex:24137,X:-.003804005915299058,Y:.0023642308078706264,Z:.0001588021987117827)})
   {
    int vertex=Array.FindIndex(row.SourceVertices,index=>index==(uint)sample.Vertex);Assert.IsTrue(vertex>=0);
    double[] expected={(-Math.Sqrt(.75)*sample.Y+.5*sample.Z)/.0254,sample.X/.0254,(.5*sample.Y+Math.Sqrt(.75)*sample.Z)/.0254};
    for(int component=0;component<3;component++)maximumPositionError=Math.Max(maximumPositionError,Math.Abs(BitConverter.ToSingle(row.Bytes,vertex*row.VertexStride+positions.Offset+4*component)-expected[component]));
   }
   Assert.IsTrue(maximumPositionError<.000002,"Seven-UV material layout changed independently expected Mandarin geometry");
   overrideZero=material.CreateCopy("mandarin_diffuse_zero_"+Guid.NewGuid().ToString("N"));material.Attributes.MergeTo(overrideZero.Attributes);overrideZero.Set("g_flGltfDiffuseTransmissionFactor",0f);
   using(capture.Scene.Push())first.MaterialOverride=overrideZero;
   var zero=await capture.Take("same_shader_factor_zero_control");int changed=actual.Pixels.Zip(zero.Pixels,(a,b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)))).Count(error=>error>.02f);
   Assert.IsTrue(changed>50,"Diffuse transmission factor had no visible effect on actual source material");Assert.AreEqual(1f,material.GetVector4("g_flGltfDiffuseTransmissionFactor").x,"Instance override changed shared original material");
   using(capture.Scene.Push())first.MaterialOverride=null;
   var restored=await capture.Take("restored_original_diffuse_material");Assert.AreEqual(actual.Sha256,restored.Sha256,"Restoring original material did not reproduce its capture");
   using(capture.Scene.Push()){second=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);second.Model=model;second.Enabled=true;first.MaterialOverride=overrideZero;capture.Scene.EditorTick(0,0);}
   await capture.Take("independent_instance_material_submission_control");
   Assert.AreSame(overrideZero,first.GltfInstance.Readback().Single().SubmittedMaterial);Assert.AreSame(material,second.GltfInstance.Readback().Single().SubmittedMaterial);
   using(capture.Scene.Push()){first.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}first=null;
   var survivor=await capture.Take("second_instance_survives_first_override_and_delete");Assert.AreEqual(actual.Sha256,survivor.Sha256);Assert.IsTrue(model.IsValid);Assert.AreEqual(0,second.GltfInstance.CallbackErrors.Length);
   using(capture.Scene.Push()){second.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}second=null;
   Assert.AreEqual(0,(await capture.Take("empty_after_delete")).Coverage);
   Measurements.Add(new{form=compiled?"glTF/canonical-compiled":"glTF/source",input_sha256=inputHash,canonical_compiled=compiled?CanonicalCompiledPath:null,actual.Path,actual.Sha256,actual.Coverage,output,selected_scene=model.GltfSelectedScene,authored_default_scene=model.GltfDefaultScene,source_shader=material.ShaderName,compute_shader=model.GltfResource.Asset.ComputeMaterial?.ShaderName,maximum_targeted_position_error_inches=maximumPositionError,changed_factor_zero_pixels=changed,factor_zero_path=zero.Path,restored_path=restored.Path,survivor_path=survivor.Path,scope="Actual public Model.Load/ModelRenderer, original textures and standard diffuse transmission; independently overridden instance and surviving original. Inferred matched camera/front+rear point-light recipe; user appearance approval pending."});
  }
  finally
  {
   await MainThread.Wait();using(capture.Scene.Push()){first?.GameObject.Destroy();second?.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}overrideZero?.Destroy();rear?.Delete();model?.Destroy();
   try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}
   Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(File.ReadAllBytes(sourcePath)));
  }
 }
}
