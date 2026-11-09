// Run only inside the model owner's guarded shared-editor lease.
var exchange = exchangeDirectory;
using var request = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllBytes(System.IO.Path.Combine(exchange, "texture-transform-compile-request.json")));
var source = request.RootElement.GetProperty("source").GetString();
var output = request.RootElement.GetProperty("output").GetString();
var directory = System.IO.Path.GetDirectoryName(source);
string Hash(byte[] bytes) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
var inputs = new System.Collections.Generic.Dictionary<string, string>();
foreach(var row in request.RootElement.GetProperty("inputs").EnumerateObject())
{
    var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory, row.Name));
    if(Hash(bytes) != row.Value.GetString()) throw new Exception("Pinned source changed: " + row.Name);
    inputs.Add(row.Name, Hash(bytes));
}
var packaged = Sandbox.Gltf.GltfCompiler.Compile(System.IO.File.ReadAllBytes(source), uri => System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory, uri)), new System.Collections.Generic.HashSet<string> { "KHR_texture_transform" });
using(var document = Sandbox.Gltf.GltfReader.Read(packaged, uri => System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory, uri)), new System.Collections.Generic.HashSet<string> { "KHR_texture_transform" }))
{
    var names = new[] { "UV.png", "Arrow.png", "Correct.png", "NotSupported.png", "Error.png" };
    for(var index = 0; index < names.Length; index++)
        if(Hash(document.ReadImage(index).Bytes) != inputs[names[index]]) throw new Exception("Compiled texture bytes changed: " + names[index]);
}
System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(output));
foreach(var name in new[] { "UV.png", "Arrow.png", "Correct.png", "NotSupported.png", "Error.png" })
{
    var bytes = System.IO.File.ReadAllBytes(System.IO.Path.Combine(directory, name));
    if(Hash(bytes) != inputs[name]) throw new Exception("Source texture changed before copying: " + name);
    using(var image = new System.IO.FileStream(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(output), name), System.IO.FileMode.CreateNew)) image.Write(bytes);
}
using(var file = new System.IO.FileStream(output, System.IO.FileMode.CreateNew)) file.Write(packaged);
return new { source, output, sha256 = Hash(packaged), inputs, compilerMvid = typeof(Sandbox.Gltf.GltfCompiler).Assembly.ManifestModule.ModuleVersionId, scope = "One derived canonical GLB with exact original external PNG dependencies; installed Compile API does not embed the images. Actual public load/render is separate." };
