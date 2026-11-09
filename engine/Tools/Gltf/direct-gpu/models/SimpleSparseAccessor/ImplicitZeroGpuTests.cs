using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;

// Synthetic fixture, independent of the upstream sample. Sparse view offsets are
// deliberately nonzero; sparse indices are strictly increasing as glTF requires.
[TestClass]
internal sealed class ImplicitZeroGpuTests
{
 public static string CheckoutRoot { get; set; }
 public static List<object> Measurements { get; } = new();
 static float[,] Positions(int variant)=>variant==0?new float[,]{{0,0,0},{2,0,0},{0,2,0}}:new float[,]{{0,2,0},{0,0,0},{2,0,0}};
 static byte[] Fixture(int variant,bool indexed)
 {
  var b=new byte[88]; Array.Fill(b,(byte)0xa5);
  // index view starts 4, accessor offset 2; 3 unsigned shorts.
  for(int i=0;i<3;i++)BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(6+i*2), (ushort)i);
  // sparse index view starts 16, offset 1, UBYTE values (sorted, not arbitrary permutations).
  b[17]=(byte)(variant==0?1:0);b[18]=2;
  var pos=Positions(variant);
  // sparse values view starts 24, offset 4, packed FLOAT VEC3; no byteStride allowed.
  for(int i=0;i<2;i++){int v=i==0?b[17]:b[18];for(int c=0;c<3;c++)BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(28+i*12+c*4),pos[v,c]);}
  // UV view starts 52, accessor offset 4, explicit stride 12 and sentinel padding.
  for(int i=0;i<3;i++){BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(56+i*12),i*.25f);BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(60+i*12),1-i*.25f);}
  var json="""
  {"asset":{"version":"2.0"},"scene":0,"scenes":[{"nodes":[0]}],"nodes":[{"mesh":0}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1}@INDEX@}]}],
   "buffers":[{"byteLength":88,"uri":"data:application/octet-stream;base64,@DATA@"}],
   "bufferViews":[{"buffer":0,"byteOffset":4,"byteLength":8},{"buffer":0,"byteOffset":16,"byteLength":3},{"buffer":0,"byteOffset":24,"byteLength":28},{"buffer":0,"byteOffset":52,"byteLength":36,"byteStride":12,"target":34962}],
   "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[2,2,0],"sparse":{"count":2,"indices":{"bufferView":1,"byteOffset":1,"componentType":5121},"values":{"bufferView":2,"byteOffset":4}}},
   {"bufferView":3,"byteOffset":4,"componentType":5126,"count":3,"type":"VEC2"},{"bufferView":0,"byteOffset":2,"componentType":5123,"count":3,"type":"SCALAR"}]}
  """;
  return Encoding.UTF8.GetBytes(json.Replace("@INDEX@",indexed?",\"indices\":2":"").Replace("@DATA@",Convert.ToBase64String(b)));
 }
 [TestMethod]
 [DataRow(0,false,false)] [DataRow(0,false,true)] [DataRow(0,true,false)] [DataRow(0,true,true)]
 [DataRow(1,false,false)] [DataRow(1,false,true)] [DataRow(1,true,false)] [DataRow(1,true,true)]
 public async Task PublicImplicitZeroSparsePosition(int variant,bool indexed,bool compiled)
 {
  await MainThread.Wait();var source=Fixture(variant,indexed); byte[] bytes=compiled?GltfCompiler.Compile(source,_=>throw new Exception("unexpected URI")):source;
  var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/SimpleSparseAccessor/implicit-zero-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  var dir=Path.Combine(Path.GetTempPath(),"sparse_zero_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
  var name="zero_"+Guid.NewGuid().ToString("N")+(compiled?".glb_c":".gltf");File.WriteAllBytes(Path.Combine(dir,name),bytes);
  var mount=new LocalFileSystem(dir);var fs=FileSystem.Mounted; Model model=null;ModelRenderer renderer=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new Vector3(200,39,39),new Vector3(0,39,39),100);
  try
  {
   fs.Mount(mount);model=Model.Load(name);Assert.IsTrue(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null);
   Assert.AreEqual(0,(await capture.Take("empty_before")).Coverage);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.WorldTransform=Transform.Zero;renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   var instance=renderer.GltfInstance;Assert.IsTrue(instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null);
   var positive=await capture.Take("public_positive");Assert.IsTrue(positive.Coverage>64,"Expected visible triangle");
   Assert.IsTrue(instance.DrawCalls>0 && instance.CallbackErrors.Length==0 && instance.PoseRevision==instance.SubmittedRevision);
   var row=instance.Readback().Single();Assert.AreEqual("gpu-readback",row.IndexDataOrigin);Assert.AreEqual(3,row.Indices.Length);
   CollectionAssert.AreEqual(new uint[]{0,1,2},row.Indices.Select(i=>row.SourceVertices[i]).ToArray());
   var position=row.Fields.Single(x=>x.Semantic=="POSITION");var uv=row.Fields.Single(x=>x.Semantic=="TEXCOORD");var expected=Positions(variant);float max=0;
   for(int v=0;v<row.VertexCount;v++) {int i=(int)row.SourceVertices[v];Assert.IsTrue(i>=0&&i<3);float[] p={expected[i,2]/.0254f,expected[i,0]/.0254f,expected[i,1]/.0254f};for(int c=0;c<3;c++)max=Math.Max(max,Math.Abs(p[c]-BitConverter.ToSingle(row.Bytes,v*row.VertexStride+position.Offset+c*4)));Assert.AreEqual(i*.25f,BitConverter.ToSingle(row.Bytes,v*row.VertexStride+uv.Offset));Assert.AreEqual(1-i*.25f,BitConverter.ToSingle(row.Bytes,v*row.VertexStride+uv.Offset+4));}
   Assert.IsTrue(max<.001f,"Implicit-zero/sparse POSITION mismatch: "+max);
   var draws=((IEnumerable)typeof(GltfGpuInstance).GetField("draws",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(instance)).Cast<object>().ToArray();Assert.AreEqual(1,draws.Length);
   var ib=(GpuBuffer)draws[0].GetType().GetField("Indices",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(draws[0]);var original=row.Indices.ToArray();var bad=new uint[]{original[0],original[0],original[0]};
   try {ib.SetData<uint>(bad);await capture.WaitFrames();CollectionAssert.AreEqual(bad,instance.Readback().Single().Indices);var negative=await capture.Take("corrupt_actual_bound_indices");Assert.AreEqual(0,negative.Coverage,"Collapsed bound index negative still rendered");Measurements.Add(new{variant,indexed,compiled,positive.Path,positive.Sha256,positive.Coverage,negative_path=negative.Path,negative_sha256=negative.Sha256,negative_coverage=negative.Coverage,max_position_error_inches=max,input_sha256=Convert.ToHexStringLower(SHA256.HashData(bytes)),source_sha256=Convert.ToHexStringLower(SHA256.HashData(source)),camera=positive.CameraStateJson,scope="Synthetic public implicit-zero sparse POSITION and padded strided UV, actual GPU output/index readback, bound index corruption and visible triangle; separate from upstream asset"});}
   finally{ib.SetData<uint>(original);await capture.WaitFrames();}
   using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}renderer=null;var count=instance.DrawCalls;Assert.AreEqual(0,(await capture.Take("empty_after")).Coverage);Assert.AreEqual(count,instance.DrawCalls);
  }
  finally {await MainThread.Wait();if(renderer is not null){using(capture.Scene.Push()){renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}}if(model is not null && model.ResourcePath==Resource.FixPath(name))model.Destroy();fs.UnMount(mount);mount.Dispose();Directory.Delete(dir,true);}
 }
}
