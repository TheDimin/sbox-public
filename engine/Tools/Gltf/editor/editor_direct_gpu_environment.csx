// Bounded read-only asset type and supplied shader schema audit. No import, shader compile or material creation.
await MainThread.Wait();
if (!Application.IsEditor || Game.IsPlaying || Graphics.IsActive) throw new InvalidOperationException("Requires idle editor outside rendering.");
var types = new[] { "gltf", "glb", "glb_c" }.Select(extension => {
 var type = Editor.AssetType.FromExtension(extension);
 return new { extension, registered = type is not null, primary_extension = type?.FileExtension, extensions = type?.FileExtensions, simple_asset = type?.IsSimpleAsset, resource_type = type?.ResourceType?.FullName, friendly_name = type?.FriendlyName };
}).ToArray();
var shaders = new[] { "shaders/unlit.shader", "shaders/tools_textured_unlit.shader" }.Select(path => {
 var shader = Shader.Load(path); var schema = shader is { IsValid: true, IsError: false } ? shader.Schema : null;
 return new { path, valid = shader is { IsValid: true }, error = shader?.IsError,
  variables = schema?.Variables?.Select(v => new { v.Name,v.Type,v.AttributeName,v.SourceType,v.UiType,v.DefaultInputTexture, defaults = new[] {v.FloatDefault.x,v.FloatDefault.y,v.FloatDefault.z,v.FloatDefault.w} }).ToArray(),
  combos = schema?.Combos?.Select(c=>new{c.Name,c.Min,c.Max,c.Values}).ToArray() };
}).ToArray();
var assemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>!a.IsDynamic && (a.GetName().Name.Contains("Tools",StringComparison.OrdinalIgnoreCase)||a.GetName().Name.Contains("Shader",StringComparison.OrdinalIgnoreCase)||a.GetName().Name.Contains("Editor",StringComparison.OrdinalIgnoreCase))).Select(a=>new{name=a.GetName().Name,mvid=a.ManifestModule.ModuleVersionId.ToString(),a.Location}).OrderBy(a=>a.name).Take(40).ToArray();
var engine=typeof(Model).Assembly;var gltf=typeof(Sandbox.Gltf.GltfReader).Assembly;
var output=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(engine.Location),"../../../Temp/gltf-validation/direct-gpu/environment-"+Guid.NewGuid().ToString("N")+".json");output=System.IO.Path.GetFullPath(output);
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
System.IO.File.WriteAllText(output,System.Text.Json.JsonSerializer.Serialize(new{passed=true,types,shaders,assemblies,engine_mvid=engine.ManifestModule.ModuleVersionId.ToString(),gltf_mvid=gltf.ManifestModule.ModuleVersionId.ToString(),scope="Read-only registered asset type and supplied shader schema; no rendering or material suitability pass."},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
return new{passed=true,receipt_path=output};
