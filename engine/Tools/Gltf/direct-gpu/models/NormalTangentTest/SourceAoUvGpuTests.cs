using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Sandbox.Engine;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;
/// <summary>Public source AO sampler, strength and UV transform image controls; no legacy material override.</summary>
[TestClass]
public sealed class SourceAoUvGpuTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 [DataRow(false)]
 [DataRow(true)]
 public async Task AuthoredOcclusionStrengthAndUvAreVisible(bool compiled)
 {
  await MainThread.Wait();var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/source-ao-uv",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(200,0,0),global::Vector3.Zero,60);capture.Camera.DebugMode=SceneCameraDebugMode.AmbientOcclusion;
  var before=await capture.Take("empty");Assert.AreEqual(0,before.Coverage);
  var normal=await Draw(1,false,"ao_strength_1");var zero=await Draw(0,false,"ao_strength_0");var mirrored=await Draw(1,true,"ao_uv_mirrored");
  // Fixed central landmarks lie comfortably inside each half of the quad.
  float Sample(GltfGpuTriangleGateTests.NativeCapture.Frame f,int x)=>f.Pixels[96*192+x].r;
  var nl=Sample(normal,72);var nr=Sample(normal,120);var zl=Sample(zero,72);var zr=Sample(zero,120);var ml=Sample(mirrored,72);var mr=Sample(mirrored,120);
  Measurements.Add(new{compiled,normal=normal.Path,normal.Sha256,zero=zero.Path,zeroHash=zero.Sha256,mirrored=mirrored.Path,mirroredHash=mirrored.Sha256,nl,nr,zl,zr,ml,mr,scope="Public source AO texture and TEXCOORD_0 transform under AO debug; output values/negative controls, not full lighting fidelity."});
  Assert.IsTrue(Math.Abs(nl-nr)>.1f,"Authored AO texture must produce distinct left and right pixels.");
  Assert.IsTrue(Math.Abs(zl-zr)<.03f && zl>Math.Min(nl,nr)+.1f,"Zero strength must suppress authored occlusion.");
  Assert.IsTrue(Math.Abs(ml-nr)<.04f && Math.Abs(mr-nl)<.04f,"Mirrored UV transform must swap the two authored occlusion samples.");
  async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Draw(float strength,bool mirror,string label)
  {
   var temp=Path.Combine(Path.GetTempPath(),"source_ao_"+Guid.NewGuid().ToString("N"));var prefix="ao_"+Guid.NewGuid().ToString("N");var folder=Path.Combine(temp,prefix);Directory.CreateDirectory(folder);var path=prefix+(compiled?"/case.glb_c":"/case.gltf");var bytes=Fixture(strength,mirror);File.WriteAllBytes(Path.Combine(temp,path),compiled?GltfCompiler.Compile(bytes,null,new HashSet<string>{"KHR_texture_transform"}):bytes);
   var aggregate=Sandbox.FileSystem.Mounted;var mount=new LocalFileSystem(temp);aggregate.Mount(mount);Model model=null;ModelRenderer renderer=null;
   try
   {
    model=Model.Load(path);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.GltfResource is not null && model.native.IsNull);var material=model.Materials[0];Assert.AreEqual(1f,material.GetVector4("g_flGltfHasOcclusionTexture").x);Assert.AreEqual(strength,material.GetVector4("g_flGltfOcclusionStrength").x);
    using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
    var inst=renderer.GltfInstance;Assert.IsNotNull(inst);var frame=await capture.Take(label);Assert.IsTrue(inst.DrawCalls>0 && inst.SubmittedRevision==inst.PoseRevision && inst.CallbackErrors.Length==0);return frame;
   }
   finally {await MainThread.Wait();if(renderer is not null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}if(model is not null&&model.ResourcePath==Resource.FixPath(path))model.Destroy();aggregate.UnMount(mount);mount.Dispose();if(Directory.Exists(temp))Directory.Delete(temp,true);}
  }
 }
 private static byte[] Fixture(float strength,bool mirror)
 {
  var bytes=new byte[128];float[] p=[-.5f,-.5f,0,.5f,-.5f,0,-.5f,.5f,0,.5f,.5f,0];float[] uv=[0,0,1,0,0,1,1,1];for(int i=0;i<p.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i*4),p[i]);for(int i=0;i<4;i++)BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(48+i*12+8),1);for(int i=0;i<uv.Length;i++)BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(96+i*4),uv[i]);
  const string png="iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAD0lEQVR4nGNQ+P//wf//AA9bBP1zUew9AAAAAElFTkSuQmCC";
  var transform=mirror?",\"extensions\":{\"KHR_texture_transform\":{\"offset\":[1,0],\"scale\":[-1,1]}}":"";
  var json=$$$"""
  {"asset":{"version":"2.0"},"extensionsUsed":["KHR_texture_transform"],"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"buffers":[{"uri":"data:application/octet-stream;base64,{{{Convert.ToBase64String(bytes)}}}","byteLength":128}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":48},{"buffer":0,"byteOffset":48,"byteLength":48},{"buffer":0,"byteOffset":96,"byteLength":32}],"accessors":[{"bufferView":0,"componentType":5126,"count":4,"type":"VEC3","min":[-0.5,-0.5,0],"max":[0.5,0.5,0]},{"bufferView":1,"componentType":5126,"count":4,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":4,"type":"VEC2"}],"images":[{"uri":"data:image/png;base64,{{{png}}}"}],"textures":[{"source":0,"sampler":0}],"samplers":[{"magFilter":9728,"minFilter":9728,"wrapS":33071,"wrapT":33071}],"materials":[{"pbrMetallicRoughness":{"metallicFactor":0,"roughnessFactor":1},"occlusionTexture":{"index":0,"strength":{{{strength.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}{{{transform}}}}}],"meshes":[{"primitives":[{"mode":5,"attributes":{"POSITION":0,"NORMAL":1,"TEXCOORD_0":2},"material":0}]}]}
  """;return Encoding.UTF8.GetBytes(json);
 }
}
