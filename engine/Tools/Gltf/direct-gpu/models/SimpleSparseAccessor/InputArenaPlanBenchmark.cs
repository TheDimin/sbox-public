using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.SimpleSparseAccessorValidation;
[TestClass]
internal sealed class InputArenaPlanBenchmark
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 [DataRow("SimpleSkin")]
 [DataRow("AnimatedMorphCube")]
 public void WarmPlan(string model)
 {
  string dir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models",model,"glTF");
  using var doc=GltfReader.Read(File.ReadAllBytes(Path.Combine(dir,model+".gltf")),uri=>File.ReadAllBytes(Path.Combine(dir,uri)));
  for(int i=0;i<10;i++)_ = GltfGpuPrimitivePlan.Create(doc,0,0);
  for(int repeat=0;repeat<2;repeat++)
  {
   long allocated=GC.GetAllocatedBytesForCurrentThread();var sw=Stopwatch.StartNew();
   for(int i=0;i<200;i++)_ = GltfGpuPrimitivePlan.Create(doc,0,0);
   sw.Stop();allocated=GC.GetAllocatedBytesForCurrentThread()-allocated;
   Measurements.Add(new{model,iterations=200,elapsed_ms=sw.Elapsed.TotalMilliseconds,allocated_bytes=allocated,scope="Warm CPU primitive-plan construction, already-read document; not native GPU or cold load"});
  }
 }
}
