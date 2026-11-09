using System;
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Sandbox.Gltf.DirectGpu.DirectionalLightValidation;
/// <summary>Compiles and verifies an ordinary game assembly against the loaded public API.</summary>
[TestClass]
public sealed class DirectionalPublicAccessTests
{
 [TestMethod]
 public void GameAssemblyCanUseOptInScaleAndAuthoredLuxMetadata()
 {
  var core=Path.GetDirectoryName(typeof(object).Assembly.Location);
  var compilation=CSharpCompilation.Create("package.directional_light",[CSharpSyntaxTree.ParseText("""
   using Sandbox;
   public static class Consumer
   {
    public static float Use(ModelRenderer renderer)
    {
     Model model=Model.LoadGltf("models/light.gltf",0,true);
     renderer.Model=model;renderer.GltfDirectionalLightScale=1.0f;
     GltfDirectionalLightInfo light=model.GltfDirectionalLights[0];
     return light.IntensityLux+light.LinearColor.x+light.Node+light.Light;
    }
   }
   """)],[MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
    MetadataReference.CreateFromFile(Path.Combine(core,"System.Runtime.dll")),MetadataReference.CreateFromFile(Path.Combine(core,"System.Collections.dll")),
    MetadataReference.CreateFromFile(typeof(Model).Assembly.Location),MetadataReference.CreateFromFile(typeof(IValid).Assembly.Location)],
    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
  using var stream=new MemoryStream();var emitted=compilation.Emit(stream);Assert.IsTrue(emitted.Success,string.Join("\n",emitted.Diagnostics));
  stream.Position=0;using var access=new AccessControl();var result=access.VerifyAssembly(stream,out var trusted);trusted?.Dispose();
  Assert.IsTrue(result.Success,string.Join("\n",result.Errors));
 }
}
