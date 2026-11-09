using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Editor;
using Sandbox.Gltf;
namespace Sandbox.Gltf.DirectGpu.CompilerPolicyValidation;
[TestClass]
internal sealed class AssetGraphAuditTests
{
 public static List<object> Measurements {get;}=new();
 [TestMethod]
 public void ObserveExistingGltfAssetGraph()
 {
  var models=AssetSystem.All.Where(x=>x.RelativePath is { } p && (p.EndsWith(".gltf",StringComparison.OrdinalIgnoreCase)||p.EndsWith(".glb",StringComparison.OrdinalIgnoreCase))).ToArray();
  Measurements.Add(new{total_gltf_assets=models.Length,inspected=Math.Min(models.Length,20),scope="Read-only existing asset observations, no completeness guarantee"});
  foreach(var asset in models.Take(20))
  {
   var refs=asset.GetReferences(false).Select(x=>x.RelativePath).ToArray();var unresolved=asset.GetUnrecognizedReferencePaths().ToArray();
   var rows=new List<object>();string error=null;
   try
   {
    byte[] Resolve(string uri){var path=GltfModelLoader.ResolveMountedUri(asset.RelativePath,uri);var full=Editor.FileSystem.Content.GetFullPath(path);return System.IO.File.ReadAllBytes(full);}
    using var doc=GltfReader.Read(System.IO.File.ReadAllBytes(asset.AbsoluteSourcePath),Resolve,GltfModelLoader.SupportedExtensions);
    if(doc.Root.TryGetProperty("images",out var images)&&images.ValueKind==System.Text.Json.JsonValueKind.Array)
     foreach(var image in images.EnumerateArray())if(image.TryGetProperty("uri",out var uri)&&uri.ValueKind==System.Text.Json.JsonValueKind.String&&uri.GetString() is string value&&!value.StartsWith("data:",StringComparison.OrdinalIgnoreCase))
     {
      var path=GltfModelLoader.ResolveMountedUri(asset.RelativePath,value);var target=AssetSystem.FindByPath(path);
      rows.Add(new{uri=value,path,asset_found=target!=null,target_type=target?.GetType().Name,compiled_up_to_date=target?.IsCompiledAndUpToDate,dependants=target?.GetDependants(false).Select(x=>x.RelativePath).ToArray(),unresolved=target?.GetUnrecognizedReferencePaths().ToArray()});
     }
   }
   catch(Exception e){error=e.GetType().Name+": "+e.Message;}
   Measurements.Add(new{model=asset.RelativePath,asset_type=asset.GetType().Name,compiled_up_to_date=asset.IsCompiledAndUpToDate,references=refs,unresolved,images=rows,error});
  }
 }
}
