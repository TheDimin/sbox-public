using System;
using System.Collections.Generic;
using System.IO;
using Sandbox.Engine;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

/// <summary>Packages an owned temporary mounted input; model creation remains public Model.Load.</summary>
internal sealed class UnlitPublicMount : IDisposable
{
 private readonly string directory;
 private readonly BaseFileSystem aggregate;
 private LocalFileSystem mount;
 private Model model;
 private readonly List<Model> selections=new();
 internal readonly string Path;
 internal UnlitPublicMount(UnlitTestReference.Form form)
 {
  directory=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"unlit_capture_"+Guid.NewGuid().ToString("N"));
  var extension=form.Compiled?".glb_c":form.Name.StartsWith("glTF-Binary/",StringComparison.Ordinal)?".glb":".gltf";
  Path="unlit_capture_"+Guid.NewGuid().ToString("N")+extension;
  try
  {
   Directory.CreateDirectory(directory);File.WriteAllBytes(System.IO.Path.Combine(directory,Path),form.Bytes);
   if(!form.Compiled&&form.Resolve is not null&&!form.Name.StartsWith("glTF-Binary/",StringComparison.Ordinal))File.WriteAllBytes(System.IO.Path.Combine(directory,"UnlitTest.bin"),form.Resolve("UnlitTest.bin"));
   aggregate=Sandbox.FileSystem.Mounted;mount=new LocalFileSystem(directory);aggregate.Mount(mount);
  }
  catch{Dispose();throw;}
 }
 internal Model Load()=>model??=Model.Load(Path);
 internal Model Load(int sceneIndex){var selected=Model.Load(Path,sceneIndex);if(!selections.Contains(selected))selections.Add(selected);return selected;}
 public void Dispose()
 {
  var errors=new List<Exception>();void Clean(Action action){try{action();}catch(Exception e){errors.Add(e);}}
  foreach(var selected in selections)if(selected is not null&&!ReferenceEquals(selected,model))Clean(selected.Destroy);selections.Clear();
  if(model is not null&&model.ResourcePath==Resource.FixPath(Path))Clean(model.Destroy);model=null;
  if(mount is not null){Clean(()=>aggregate.UnMount(mount));Clean(mount.Dispose);mount=null;}
  Clean(()=>{if(Directory.Exists(directory))Directory.Delete(directory,true);});
  if(errors.Count>0)throw new AggregateException("Unlit mounted model cleanup failed.",errors);
 }
}
