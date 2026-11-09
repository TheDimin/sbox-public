using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Sandbox.Gltf;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NV3 = System.Numerics.Vector3;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

/// <summary>Derived fixture: adversarial accessor offsets/strides, two independent targets and a rotated joint.</summary>
[TestClass]
public sealed class CombinedMorphSkinTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 [TestMethod]
 [DataRow(false, false)]
 [DataRow(false, true)]
 [DataRow(true, false)]
 [DataRow(true, true)]
 public async Task PublicRendererMorphsAllThreeStreamsBeforeSkin(bool compiled, bool weightOverride)
 {
  await MainThread.Wait();
  var source=Path.Combine(CheckoutRoot,"research/gltf-validation/direct-gpu/models/MorphPrimitivesTest/combined-morph-skin.gltf");
  Assert.AreEqual("0e88b84032670eac0b282ae3b51c6362987e94bfe4bffb2e702956f6295368db",Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(source))),"Frozen fixture changed");
  var sourceBytes=File.ReadAllBytes(source); var bytes=compiled ? GltfCompiler.Compile(sourceBytes) : sourceBytes;
  float a=weightOverride ? -.75f : .5f, b=weightOverride ? 1.5f : -.25f;
  var canonical=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/MorphPrimitivesTest/combined-canonical-valid/combined.glb_c");
  if(compiled) { Directory.CreateDirectory(Path.GetDirectoryName(canonical)); if(File.Exists(canonical)) CollectionAssert.AreEqual(bytes,File.ReadAllBytes(canonical),"Canonical compiled bytes changed"); else File.WriteAllBytes(canonical,bytes); }
  var dir=Path.Combine(Path.GetTempPath(),"combined_morph_skin_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(dir);
  var name="combined_"+Guid.NewGuid().ToString("N")+(compiled ? ".glb_c" : ".gltf"); File.WriteAllBytes(Path.Combine(dir,name),bytes);
  var mount=new LocalFileSystem(dir); FileSystem.Mounted.Mount(mount);
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/combined-morph-skin",Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output);
  Model model=null; SkinnedModelRenderer renderer=null;
  try
  {
   model=Model.Load(name); Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null,"Managed public model failed");
   using(capture.Scene.Push()) { renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); renderer.UseAnimGraph=false; renderer.PlayAnimationsInEditorScene=true; renderer.PlaybackRate=0; renderer.Model=model; renderer.Enabled=true; capture.Scene.EditorTick(0,0); }
   if(weightOverride) { Assert.AreEqual(2,model.MorphCount); renderer.Morphs.Set(model.GetMorphName(0),a,0); renderer.Morphs.Set(model.GetMorphName(1),b,0); using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); }
   await capture.WaitFrames(); var instance=renderer.GltfInstance;
   Assert.IsTrue(instance is not null && renderer.SceneModel is null && instance.DrawCalls>0 && instance.SubmittedRevision==instance.PoseRevision && instance.CallbackErrors.Length==0,"Public skin/morph draw was not submitted");
   var row=instance.Readback().Single(); Assert.AreEqual(3,row.VertexCount);
   var pf=row.Fields.Single(x=>x.Semantic=="POSITION"); var nf=row.Fields.Single(x=>x.Semantic=="NORMAL"); var tf=row.Fields.Single(x=>x.Semantic=="TANGENT");
   var basePositions=new[]{new NV3(0,0,0),new NV3(1,0,0),new NV3(0,1,0)};
   // In glTF coordinates joint rotation is +90 degrees about Z; engine basis maps (x,y,z) to (z,x,y).
   NV3 Rotate(NV3 v)=>new(-v.Y,v.X,v.Z); NV3 Basis(NV3 v)=>new(v.Z,v.X,v.Y);
   var dn=new NV3(a,b,1); var dt=new NV3(1,a,b);
   var expectedN=NV3.Normalize(Basis(Rotate(dn))); var rawT=Basis(Rotate(dt)); var expectedT=NV3.Normalize(rawT-expectedN*NV3.Dot(rawT,expectedN));
   var wrongN=NV3.Normalize(Basis(Rotate(NV3.UnitZ)+new NV3(a,b,0)));
   var wrongRawT=Basis(Rotate(NV3.UnitX)+new NV3(0,a,b)); var wrongT=NV3.Normalize(wrongRawT-wrongN*NV3.Dot(wrongRawT,wrongN));
   Assert.IsTrue(NV3.Distance(expectedN,wrongN)>.1f && NV3.Distance(expectedT,wrongT)>.1f,"Frame order negative has insufficient sensitivity");
   float maxP=0,maxN=0,maxT=0,minWrongP=float.MaxValue; var vertices=new List<object>();
   float[] Array(NV3 v)=>new[]{v.X,v.Y,v.Z};
   for(int i=0;i<3;i++)
   {
    int sourceIndex=checked((int)row.SourceVertices[i]); var p=basePositions[sourceIndex];
    var expectedP=Basis(Rotate(p+new NV3(a,b,0)))/.0254f;
    var wrongP=Basis(Rotate(p)+new NV3(a,b,0))/.0254f;
    var actualP=Read3(row,i,pf.Offset); var actualN=Read3(row,i,nf.Offset); var actualT=Read3(row,i,tf.Offset);
    maxP=Math.Max(maxP,NV3.Distance(actualP,expectedP));maxN=Math.Max(maxN,NV3.Distance(actualN,expectedN));maxT=Math.Max(maxT,NV3.Distance(actualT,expectedT));
    minWrongP=Math.Min(minWrongP,NV3.Distance(actualP,wrongP));
    vertices.Add(new {sourceIndex,actual_position=Array(actualP),expected_position=Array(expectedP),wrong_order_position=Array(wrongP),actual_normal=Array(actualN),expected_normal=Array(expectedN),actual_tangent=Array(actualT),expected_tangent=Array(expectedT),tangent_w=Read(row,i,tf.Offset+12)});
    Assert.AreEqual(-1f,Read(row,i,tf.Offset+12),.00001f,"Tangent handedness changed");
   }
   Assert.IsTrue(maxP<.0005f && maxN<.00001f && maxT<.00001f,$"GPU errors position={maxP} normal={maxN} tangent={maxT}");
   Assert.IsTrue(minWrongP>10,$"Wrong-order position unexpectedly accepted: {minWrongP}");
   Measurements.Add(new {compiled,weightOverride,a,b,input_sha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),canonical_path=compiled?canonical:null,gpu_bytes_sha256=Convert.ToHexStringLower(SHA256.HashData(row.Bytes)),vertices,maxP,maxN,maxT,minWrongP,wrongNormalDistance=NV3.Distance(expectedN,wrongN),wrongTangentDistance=NV3.Distance(expectedT,wrongT),scope="Synthetic public Model.Load and SkinnedModelRenderer GPU readback; two nonzero independent POSITION/NORMAL/TANGENT targets before rotated skin, padded strided inputs."});
  }
  finally
  {
   await MainThread.Wait(); if(renderer is not null) {using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();} model?.Destroy(); FileSystem.Mounted.UnMount(mount); mount.Dispose(); Directory.Delete(dir,true);
  }
 }
 static float Read(GltfGpuPrimitiveSnapshot row,int i,int offset)=>BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(i*row.VertexStride+offset));
 static NV3 Read3(GltfGpuPrimitiveSnapshot row,int i,int offset)=>new(Read(row,i,offset),Read(row,i,offset+4),Read(row,i,offset+8));
}
