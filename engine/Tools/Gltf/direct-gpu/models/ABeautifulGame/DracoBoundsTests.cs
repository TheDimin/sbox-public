using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
public static class DracoBoundsTests
{
 public sealed record Result(string Name,bool Passed,string Error,double MaxErrorMeters);
 public static Result[] RunAll(string checkout)
 {
  var rows=new List<Result>();var root=Path.Combine(checkout,"Temp/gltf-reference/glTF-Sample-Assets/Models/ABeautifulGame/glTF-Binary-KTX-ETC1S-Draco/ABeautifulGame.glb");using var d=GltfReader.Read(File.ReadAllBytes(root),null,new HashSet<string>{"KHR_draco_mesh_compression","KHR_texture_basisu"});using var oracle=JsonDocument.Parse(File.ReadAllBytes(Path.Combine(checkout,"research/gltf-validation/direct-gpu/models/ABeautifulGame/raw-position-bounds.json")));
  for(int mesh=0;mesh<15;mesh++) { int i=mesh;double maxError=0;try { var stream=d.MaterializePrimitiveUpload(i,0).Attributes["POSITION"];Require(stream.ComponentType==5126 && stream.Components==3 && !stream.Normalized && stream.IsMaterialized,"Expected decoded FLOAT VEC3 Draco stream");var actualMin=new[]{float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};var actualMax=new[]{float.NegativeInfinity,float.NegativeInfinity,float.NegativeInfinity};var bytes=stream.Storage.Bytes.Span;
   for(int v=0;v<stream.Count;v++)for(int axis=0;axis<3;axis++){float x=BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(stream.ByteOffset+v*stream.ByteStride+axis*4,4));Require(float.IsFinite(x),"Nonfinite decoded position");actualMin[axis]=Math.Min(actualMin[axis],x);actualMax[axis]=Math.Max(actualMax[axis],x);}
   var expected=oracle.RootElement.GetProperty("meshes")[i];for(int axis=0;axis<3;axis++){maxError=Math.Max(maxError,Math.Abs(actualMin[axis]-expected.GetProperty("min")[axis].GetDouble()));maxError=Math.Max(maxError,Math.Abs(actualMax[axis]-expected.GetProperty("max")[axis].GetDouble()));}Require(maxError<=.0001,$"Decoded geometry extents differ from independent raw upstream source by {maxError:R} meters (limit 0.1 mm)");rows.Add(new("mesh_"+i,true,null,maxError));
  }catch(Exception e){rows.Add(new("mesh_"+i,false,e.ToString(),maxError));} }
  return rows.ToArray();
 }
 static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
}
