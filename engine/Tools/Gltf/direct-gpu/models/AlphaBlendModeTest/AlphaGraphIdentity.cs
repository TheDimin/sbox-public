using System;
using System.Linq;
using System.Reflection;
namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;
public static class AlphaGraphIdentity
{
 public static object[] Run(string root)
 {
  var globals=typeof(Editor.SceneRenderingWidget).Assembly.GetType("Sandbox.Internal.GlobalToolsNamespace",true);var library=globals.GetProperty("EditorTypeLibrary",BindingFlags.Public|BindingFlags.Static).GetValue(null);
  var lookup=library.GetType().GetMethods(BindingFlags.Instance|BindingFlags.Public).Single(m=>m.Name=="GetType"&&!m.IsGenericMethod&&!m.ContainsGenericParameters&&m.GetParameters() is var p&&p.Length==2&&p[0].ParameterType==typeof(string)&&p[1].ParameterType==typeof(bool));
  var types=new[]{"Editor.ShaderGraph.ShaderGraph","Editor.ShaderGraph.GraphCompiler","Editor.ShaderGraph.Sampler"}.Select(n=>{var d=lookup.Invoke(library,new object[]{n,true});var t=d?.GetType().GetProperty("TargetType")?.GetValue(d) as Type;return new{name=n,assembly=t?.Assembly.FullName,mvid=t?.Assembly.ManifestModule.ModuleVersionId.ToString(),location=t?.Assembly.Location};}).ToArray();
  return [new{Passed=true,types}];
 }
}
