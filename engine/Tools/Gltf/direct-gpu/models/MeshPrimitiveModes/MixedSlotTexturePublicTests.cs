using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;
namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;
[TestClass]
internal sealed class MixedSlotTexturePublicTests
{
 static string checkout;public static List<object> Measurements{get;}=new();public static void Configure(string root){checkout=root;MixedSlotCombinedPublicTests.Configure(root);Measurements.Clear();}
 static byte[] Fixture()
 {
  var j=JsonNode.Parse(MixedSlotCombinedPublicTests.Fixture());var uri=j["buffers"][0]["uri"].GetValue<string>();var old=Convert.FromBase64String(uri[(uri.IndexOf(',')+1)..]);var data=new byte[old.Length+7*16+7*16];old.CopyTo(data,0);
  for(int v=0;v<7;v++)
  {
   float x=BitConverter.ToSingle(old,132+v*12),y=BitConverter.ToSingle(old,132+v*12+4);var uv=new[]{x*.4f+.5f,y*.4f+.5f,x*.2f+.5f,y*.2f+.5f};
   for(int c=0;c<4;c++)BitConverter.GetBytes(uv[c]).CopyTo(data,old.Length+v*16+c*4);
   var tangent=new[]{1f,0f,0f,1f};for(int c=0;c<4;c++)BitConverter.GetBytes(tangent[c]).CopyTo(data,old.Length+7*16+v*16+c*4);
  }
  int uvView=j["bufferViews"].AsArray().Count;j["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=old.Length,["byteLength"]=112,["byteStride"]=16,["target"]=34962});
  int tangentView=j["bufferViews"].AsArray().Count;j["bufferViews"].AsArray().Add(new JsonObject{["buffer"]=0,["byteOffset"]=old.Length+112,["byteLength"]=112,["target"]=34962});
  int first=j["accessors"].AsArray().Count;foreach(int offset in new[]{0,8})j["accessors"].AsArray().Add(new JsonObject{["bufferView"]=uvView,["byteOffset"]=offset,["componentType"]=5126,["count"]=7,["type"]="VEC2"});
  int tangentAccessor=j["accessors"].AsArray().Count;j["accessors"].AsArray().Add(new JsonObject{["bufferView"]=tangentView,["componentType"]=5126,["count"]=7,["type"]="VEC4"});
  foreach(int mesh in new[]{1,2,4}){j["meshes"][mesh]["primitives"][0]["attributes"]["TEXCOORD_0"]=first;j["meshes"][mesh]["primitives"][0]["attributes"]["TEXCOORD_1"]=first+1;}
  j["meshes"][2]["primitives"][0]["attributes"]["TANGENT"]=tangentAccessor;
  j["materials"][0]["pbrMetallicRoughness"]["baseColorTexture"]=new JsonObject{["index"]=0,["texCoord"]=1};j["materials"][0]["normalTexture"]=new JsonObject{["index"]=0,["texCoord"]=0};
  j["textures"]=JsonNode.Parse("[{\"source\":0}]");j["images"]=JsonNode.Parse("[{\"uri\":\"data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNocFBoUGhwcAAAC8kCgZHWr1kAAAAASUVORK5CYII=\"}]");
  j["buffers"][0]["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(data);j["buffers"][0]["byteLength"]=data.Length;return Encoding.UTF8.GetBytes(j.ToJsonString());
 }
 [TestMethod] [DataRow(false)] [DataRow(true)]
 public async Task SharedSourceTextureRolesAndNoNormalLine(bool compiled)
 {
  await MainThread.Wait();var bytes=Fixture();if(compiled)bytes=GltfCompiler.Compile(bytes,null,new HashSet<string>{"KHR_materials_variants"});var dir=Path.Combine(Path.GetTempPath(),"modes_mixed_texture_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);var name="mixed_texture_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(dir,name),bytes);
  var output=Path.Combine(checkout,"Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/mixed-texture",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(500,0,0),Vector3.Zero,360);
  try
  {
   mount=new LocalFileSystem(dir);FileSystem.Mounted.Mount(mount);model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Public source-textured model failed.");
   Require(model.Materials.Length==1 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual(new int?[]{0}),"Shared slot identity changed.");using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;var frame=await capture.Take("shared_base_uv1_normal_uv0");Require(frame.Coverage>0 && instance.DrawCalls>0 && instance.CallbackErrors.Length==0,"Mixed source-textured draw failed.");var rows=instance.Readback();Require(rows.Count==3 && rows.All(x=>ReferenceEquals(x.SubmittedMaterial,model.Materials[0])),"Shared source texture roles created different material handles.");
   Require(model.Materials[0].ShaderName.Contains("gltf_surface_five_uv",StringComparison.OrdinalIgnoreCase),"Existing source program not selected.");
   renderer.MaterialVariant=0;await capture.Take("alternate_no_textures");Require(instance.Readback().All(x=>x.SubmittedMaterialIndex==1),"Variant lost shared-slot mapping.");renderer.MaterialVariant=null;await capture.Take("source_textures_restored");Require(instance.Readback().All(x=>x.SubmittedMaterialIndex==0),"Default source textures not restored.");
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;Require((await capture.Take("empty_after_delete")).Coverage==0,"Delete failed.");Measurements.Add(new{compiled,passed=true,input_sha256=Hash(bytes),actual=frame.Path,frame.Sha256,scope="Public mixed shared-slot BaseColor TEXCOORD1 and Normal TEXCOORD0; each primitive has both required UVs; normal line has tangent, triangle generates tangents. Variant reset and shared Material identity. No independent texture fidelity or GPU index rerun."});
  }
  finally{await MainThread.Wait();if(renderer is not null){using var s=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(model is not null&&model.ResourcePath==Resource.FixPath(name))model.Destroy();if(mount is not null){FileSystem.Mounted.UnMount(mount);mount.Dispose();}Directory.Delete(dir,true);}
 }
}
