using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Engine;
using Sandbox.Gltf;
using NV4=System.Numerics.Vector4;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
[TestClass]
public sealed class NormalTangentPublicRendererTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 [DataRow("glTF",false)]
 [DataRow("glTF-Binary",true)]
 public async Task PublicRendererUsesGeneratedCornerFramesAndSourceMaterial(string variant,bool compiled)
 {
  await MainThread.Wait();var root=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/NormalTangentTest",variant);
  var source=File.ReadAllBytes(Path.Combine(root,variant=="glTF"?"NormalTangentTest.gltf":"NormalTangentTest.glb")); byte[] Resolve(string uri)=>File.ReadAllBytes(Path.Combine(root,uri));
  var bytes=compiled?GltfCompiler.Compile(source,Resolve):source; using var doc=GltfReader.Read(bytes,Resolve);
  var plan=GltfGpuPrimitivePlan.Create(doc,0,0).ProjectMaterial(0,5,null,[0,1,4,5,6]);var route=GltfGpuGeneratedTangentRoute.Prepare(plan,false);
  Assert.IsNotNull(route,"CPU generated tangent route was unexpectedly null");var descriptor=route.InputDescriptors[route.OutputDescriptors.Single(x=>x.Role==2).Source];
  var temp=Path.Combine(Path.GetTempPath(),"normal_tangent_public_gpu_"+Guid.NewGuid().ToString("N"));var prefix="normal_tangent_gpu_"+Guid.NewGuid().ToString("N");var folder=Path.Combine(temp,prefix);Directory.CreateDirectory(folder);var path=prefix+"/model"+(compiled?".glb_c":variant=="glTF"?".gltf":".glb"); File.WriteAllBytes(Path.Combine(temp,path),bytes);
  if(variant=="glTF") { foreach(var dep in new[]{"NormalTangentTest_BaseColor.png","NormalTangentTest_Normal.png","NormalTangentTest_OcclusionRoughnessMetallic.png"})File.Copy(Path.Combine(root,dep),Path.Combine(folder,dep)); if(!compiled) File.Copy(Path.Combine(root,"NormalTangentTest0.bin"),Path.Combine(folder,"NormalTangentTest0.bin")); }
  var aggregate=Sandbox.FileSystem.Mounted;var mount=new LocalFileSystem(temp);aggregate.Mount(mount);Model model=null;ModelRenderer renderer=null;
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/normal-tangent-public-renderer",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string payloadPath=null, payloadHash=null; if(compiled){payloadPath=Path.Combine(output,"canonical.glb_c");File.WriteAllBytes(payloadPath,bytes);payloadHash=Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));}
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(200,0,-5),new global::Vector3(0,0,-5),110);
  try
  {
   var empty=await capture.Take("empty_before");Assert.AreEqual(0,empty.Coverage);
   model=Model.Load(path);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public Model.Load did not produce managed source resource.");
   var realized=model.Materials; Assert.AreEqual(1,realized.Length,"Expected one source material slot");
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   await capture.WaitFrames(); await MainThread.Wait();
   var instance=renderer.GltfInstance;Assert.IsNotNull(instance,"Enabled public ModelRenderer has no GltfInstance after EditorTick");Assert.IsTrue(renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null);
   var actual=await capture.Take("public_source_material");Assert.IsTrue(actual.Coverage>100);Assert.IsTrue(instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision && instance.CallbackErrors.Length==0,string.Join("\n",instance.CallbackErrors));
   var row=instance.Readback().Single();Assert.AreEqual(23322,row.VertexCount);Assert.AreEqual("gpu-readback",row.IndexDataOrigin);CollectionAssert.AreEqual(route.SourceVertices,row.SourceVertices);CollectionAssert.AreEqual(route.Indices,row.Indices);
   var field=row.Fields.Single(x=>x.Semantic=="TANGENT" && x.SemanticIndex==0);float maximum=0;
   for(var i=0;i<row.VertexCount;i++)
   {
    int a=(int)descriptor.ByteOffset+i*16,b=i*row.VertexStride+field.Offset;float F(byte[] v,int o)=>BinaryPrimitives.ReadSingleLittleEndian(v.AsSpan(o));
    var expected=new NV4(F(route.InputBytes,a+8),F(route.InputBytes,a),F(route.InputBytes,a+4),F(route.InputBytes,a+12));var observed=new NV4(F(row.Bytes,b),F(row.Bytes,b+4),F(row.Bytes,b+8),F(row.Bytes,b+12));maximum=Math.Max(maximum,NV4.Distance(expected,observed));
   }
   Assert.IsTrue(maximum<.0001f,$"Public generated tangent frames differ from per-corner CPU frames: {maximum}");
   var material=model.Materials.Single();Assert.AreEqual(1,material.GetFeature("F_RENDER_BACKFACES"));Assert.AreEqual(1f,material.GetVector4("g_flGltfHasNormalTexture").x);Assert.AreEqual(1f,material.GetVector4("g_flGltfHasOcclusionTexture").x);
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;var deleted=await capture.Take("empty_after_delete");Assert.AreEqual(0,deleted.Coverage);
   Measurements.Add(new{variant,compiled,payloadPath,payloadHash,actual.Path,actual.Sha256,actual.Coverage,maximum,drawCalls=instance.DrawCalls,scope="Public Model.Load/ModelRenderer, actual GPU corners and source material metadata; independently validated shader fixture handles pixels, upstream visual review remains."});
  }
  finally
  {
   await MainThread.Wait();if(renderer is not null){using var s=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(model is not null&&model.ResourcePath==Resource.FixPath(path))model.Destroy();aggregate.UnMount(mount);mount.Dispose();if(Directory.Exists(temp))Directory.Delete(temp,true);
  }
 }
}
