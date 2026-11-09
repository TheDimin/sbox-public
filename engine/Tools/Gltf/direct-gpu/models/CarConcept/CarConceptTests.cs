using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text.Json;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.CarConceptValidation;

public static class CarConceptTests
{
 public sealed record Result(string Name, string Form, bool Passed, string Error);
 static readonly HashSet<string> Selected = new(StringComparer.Ordinal) { "KHR_materials_variants", "KHR_texture_transform", "KHR_materials_emissive_strength" };
 public static Result[] Run(string root)
 {
  var results = new List<Result>();
  using var audit = JsonDocument.Parse(File.ReadAllBytes(Path.Combine(root,"research/gltf-validation/direct-gpu/models/CarConcept/reader-source-audit.json")));
  Require(audit.RootElement.GetProperty("revision").GetString()=="90d7ede14c7e280af263824604b427a1ca02cb66", "Corpus revision");
  foreach(var file in audit.RootElement.GetProperty("files").EnumerateArray()) {
   var path=Path.Combine(root,file.GetProperty("path").GetString());
   Require(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(path)))==file.GetProperty("sha256").GetString(),"Pinned dependency changed: "+path);
  }
  var model = Path.Combine(root, "Temp/gltf-reference/glTF-Sample-Assets/Models/CarConcept");
  var forms = new[] {
   ("glTF", "CarConcept.gltf", "1a2395a86bdef0a1e9b3d3875bb056f688c624c19cf27c867b6d5cde6f37722f"),
   ("glTF-Binary", "CarConcept.glb", "c272098089d78c5cd9fd9f24ff50ee8acf8d932c55f2d55fc10adb6c8998966b"),
   ("glTF-JPG", "CarConcept.gltf", "76afe91f52c938f1f33025dc73dd7f638be7aa75f074d3e4323dd6c9b521d470") };
  foreach (var (form, file, hash) in forms)
  {
   var dir = Path.Combine(model, form); var bytes = File.ReadAllBytes(Path.Combine(dir,file));
   Require(Convert.ToHexStringLower(SHA256.HashData(bytes)) == hash, "Pinned source identity changed");
   byte[] Resolve(string uri) => File.ReadAllBytes(Path.Combine(dir,uri));
   foreach (bool compiled in new[]{false,true})
   {
    var label = form + (compiled ? "/compiled" : "/source");
    byte[] input;
    try { input = compiled ? GltfCompiler.Compile(bytes,Resolve,Selected) : bytes; }
    catch(Exception e) { results.Add(new("compile",label,false,e.ToString())); continue; }
    void Check(string name, Action<GltfDocument> action)
    {
     try { using var doc = GltfReader.Read(input,Resolve,Selected); action(doc); results.Add(new(name,label,true,null)); }
     catch(Exception e) { results.Add(new(name,label,false,e.ToString())); }
    }
    Check("inventory_and_variant_mapping", doc => {
     Require(doc.Root.GetProperty("nodes").GetArrayLength()==101 && doc.Root.GetProperty("meshes").GetArrayLength()==97 && doc.Root.GetProperty("materials").GetArrayLength()==29,"inventory");
     Require(doc.ReadMaterialVariantNames().SequenceEqual(new[]{"Carmine Candy","Pearly Swirly","Torched Graphite"}),"variant catalog");
     var counts = new Dictionary<int,int>{{6,0},{10,0},{11,0}};
     var meshes = doc.Root.GetProperty("meshes");
     for(int m=0;m<meshes.GetArrayLength();m++) {
      var prims=meshes[m].GetProperty("primitives");
      for(int p=0;p<prims.GetArrayLength();p++) {
       var pr=prims[p]; int? original=pr.TryGetProperty("material",out var mat)?mat.GetInt32():null;
       bool mapped=pr.TryGetProperty("extensions",out var ex) && ex.TryGetProperty("KHR_materials_variants",out _);
       int[] expected = mapped ? original switch { 6 => new[]{6,26,27}, 10 => new[]{10,24,25}, 11 => new[]{11,22,23}, _ => throw new Exception("Unexpected mapped material") } : null;
       if(mapped) counts[original.Value]++;
       foreach(var v in new int?[]{null,2,0,1,null}) Require(doc.GetMaterialForVariant(m,p,v)==(v.HasValue && mapped ? expected[v.Value] : original),$"mesh {m} primitive {p} variant {v}");
      }
     }
     Require(counts[6]==9 && counts[10]==9 && counts[11]==7,"mapped primitive inventory");
    });
    Check("emissive_strength_reader", doc => { foreach(var pair in new[]{(8,3f),(12,2f),(13,2f),(14,10f),(0,1f)}) Require(doc.ReadMaterialEmissiveStrength(pair.Item1)==pair.Item2,"emissive strength"); });
    Check("literal_asymmetric_normal_texel", doc => {
     // Literal 4x4 linear normal payload, repeat wrapping, nearest sampling.
     // Row 2 column 3 is the spec address; the reversed transform selects row 1 column 2.
     var texels = new Vector3[16];
     texels[2 * 4 + 3] = new Vector3(.75f,.25f,1f);
     texels[1 * 4 + 2] = new Vector3(.125f,.875f,1f);
     var uv=doc.ReadMaterial(10).NormalTexture.TransformUv(new Vector2(.25f,.5f));
     float Repeat(float x) => x-MathF.Floor(x);
     int column=(int)MathF.Floor(Repeat(uv.X)*4), row=(int)MathF.Floor(Repeat(uv.Y)*4);
     Require(texels[row*4+column]==new Vector3(.75f,.25f,1f), $"Spec texel (3,2), got ({column},{row}); wrong-sign control (2,1)");
    });
    Check("authored_texture_transform_spec", doc => {
     var uv = new Vector2(.25f,.5f);
     var rotated=doc.ReadMaterial(10).NormalTexture;
     Require(rotated.Index==8 && rotated.Tiling==new Vector2(30,30) && Math.Abs(rotated.Rotation-.585398f)<1e-6,"paint source parameters");
     var actual=rotated.TransformUv(uv);
     Require(Math.Abs(actual.X - (-2.0367755f))<.0001f && Math.Abs(actual.Y - 16.6463673f)<.0001f,$"KHR counterclockwise rotation: {actual}");
     var tire=doc.ReadMaterial(19).NormalTexture.TransformUv(uv);
     Require(Vector2.Distance(tire,new Vector2(.25f,3.5f))<1e-5f,"tire offset and scale");
    });
    try { using var fallback=GltfReader.Read(input,Resolve); Require(fallback.GetMaterialForVariant(0,0,1)==fallback.GetMaterialForVariant(0,0,null),"variant fallback"); Require(fallback.ReadMaterialEmissiveStrength(14)==1,"emissive fallback"); var uv=fallback.ReadMaterial(10).NormalTexture.TransformUv(new Vector2(.25f,.5f)); Require(uv==new Vector2(.25f,.5f),"UV fallback"); results.Add(new("optional_fallback",label,true,null)); }
    catch(Exception e) { results.Add(new("optional_fallback",label,false,e.ToString())); }
   }
  }
  return results.ToArray();
 }
 static void Require(bool condition,string message) { if(!condition) throw new InvalidOperationException(message); }
}
