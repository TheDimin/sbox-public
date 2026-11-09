using System;
using System.Collections.Generic;
using Sandbox.Tests;
namespace Sandbox.Gltf.DirectGpu.ABeautifulGameValidation;
public static class LoaderPolicyTests
{
 public sealed record Result(string Name,bool Passed,string Error);
 public static Result[] RunAll()
 {
  var rows=new List<Result>();var test=new GltfModelDocumentTests();
  foreach(var extension in new[]{"KHR_texture_basisu","KHR_materials_unlit","KHR_materials_variants"}) { string value=extension; Run("selected_required_"+value,()=>test.PublicLoaderDocumentPolicyAcceptsSelectedRequiredExtension(value)); }
  foreach(var extension in new[]{"KHR_materials_transmission","EXT_texture_webp"}) { string value=extension; Run("unsupported_required_"+value,()=>test.PublicLoaderDocumentPolicyRejectsUnsupportedRequiredExtension(value)); }
  Run("optional_fallback",test.PublicLoaderDocumentPolicyUsesCoreImageForUnsupportedOptionalExtension);
  Run("corrupt_required_basis_rejects_without_fallback",test.PublicLoaderDocumentPolicyRejectsCorruptRequiredBasisImageInsteadOfFallback);
  return rows.ToArray();
  void Run(string name,Action body){try{body();rows.Add(new(name,true,null));}catch(Exception e){rows.Add(new(name,false,e.ToString()));}}
 }
}
