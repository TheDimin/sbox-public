using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sandbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

// Presentation follow-up only. Does not repeat or replace accepted public3.
[TestClass]
internal sealed class DiffuseTransmissionConvergenceTests
{
 public static string CheckoutRoot {get;set;}
 public static List<object> Measurements {get;}=new();
 static string Hash(byte[] bytes)=>Convert.ToHexStringLower(SHA256.HashData(bytes));
 [TestMethod]
 public async Task ActualSourceReferenceSizeExposureConvergence()
 {
  await MainThread.Wait();const int width=1151,height=1309,maxRenders=64;
  string sourceDirectory=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");
  byte[] source=File.ReadAllBytes(Path.Combine(sourceDirectory,"MandarinOrange.gltf"));Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(source));
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/MandarinOrange/diffuse-convergence",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string directory=Path.Combine(Path.GetTempPath(),"gltf_mandarin_convergence_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  LocalFileSystem mount=null;Model model=null;ModelRenderer renderer=null;ScenePointLight rear=null;
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(4,0,0),global::Vector3.Zero,1.25f);
  using var target=Texture.CreateRenderTarget().WithSize(width,height).WithFormat(ImageFormat.RGBA8888).Create("mandarin_convergence_"+Guid.NewGuid().ToString("N"));
  try
  {
   foreach(string file in Directory.GetFiles(sourceDirectory))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));
   string name="mandarin_convergence_"+Guid.NewGuid().ToString("N")+".gltf";File.WriteAllBytes(Path.Combine(directory,name),source);
   mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);
   capture.Camera.CustomSize=new global::Vector2(width,height);
   rear=new ScenePointLight(capture.Scene.SceneWorld,new global::Vector3(-200,30,60),1000,Color.White*4);
   model=Model.Load(name,0);
   using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);renderer.Model=model;renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
   await capture.WaitFrames();Assert.IsNotNull(renderer.GltfInstance);var material=model.Materials.Single();StringAssert.Contains(material.ShaderName,"gltf_diffuse_transmission");Assert.AreEqual(1f,material.GetVector4("g_flGltfDiffuseTransmissionFactor").x);
   var camera=capture.Camera;using(capture.Scene.Push()){capture.Scene.PreCameraRender();camera.InitializeRendering(false);}
   Measurements.Add(new{phase="runtime_presentation_settings",camera.EnablePostProcessing,auto_exposure_enabled=camera.AutoExposure.Enabled,exposure_compensation=camera.AutoExposure.Compensation,minimum_exposure=camera.AutoExposure.MinimumExposure,maximum_exposure=camera.AutoExposure.MaximumExposure,exposure_rate=camera.AutoExposure.Rate,native_tonemap_enabled=camera.SceneCamera.Tonemap.Enabled,camera.SceneCamera.AntiAliasing,width,height,camera_position=camera.WorldPosition.ToString(),camera_rotation=camera.WorldRotation.ToString(),camera.OrthographicHeight,front_light="200,-30,60 radius1000 white4",rear_light="-200,30,60 radius1000 white4",scope="Same original camera/framing/lights/material and postprocessing policy, changed capture resolution to authored screenshot-large dimensions. Runtime flags recorded instead of assuming AutoExposure defaultfalse survives editor policy."});
   var previous=new Color32[width*height];var current=new Color32[previous.Length];
   var rows=new List<object>();var timer=Stopwatch.StartNew();int stablePairs=0,renders=0;double firstMean=0,finalMean=0;string firstPath=null,firstSha=null,lastPath=null,lastSha=null;bool settled=false;
   for(int frame=0;frame<maxRenders;frame++)
   {
    await capture.WaitFrames();using(capture.Scene.Push()){capture.Scene.PreCameraRender();camera.InitializeRendering(false);Assert.AreEqual(new global::Vector2(width,height),camera.CustomSize.Value);Assert.IsTrue(camera.RenderToTexture(target));}
    await capture.WaitFrames();Array.Fill(current,new Color32(255,0,255,255));target.GetPixels<Color32>((0,0,width,height),0,0,current,ImageFormat.RGBA8888);
    Assert.IsTrue(current.All(pixel=>!(pixel.r==255 && pixel.g==0 && pixel.b==255)),"Readback retained clear/sentinel magenta");
    Assert.IsTrue(new[]{0,width-1,width*(height-1),width*height-1}.All(index=>current[index].r==0 && current[index].g==0 && current[index].b==0),"Source frame did not replace sentinel with expected black background");
    int coverage=0,maxDelta=0,changedPixels=0;long sumDelta=0;double luminance=0;
    for(int pixel=0;pixel<current.Length;pixel++)
    {
     var a=current[pixel];bool foreground=a.r>7 || a.g>7 || a.b>7;
     if(foreground){coverage++;luminance+=(.2126*a.r+.7152*a.g+.0722*a.b)/255;}
     if(frame==0)continue;
     var b=previous[pixel];if(!foreground && b.r<=7 && b.g<=7 && b.b<=7)continue;
     int dr=Math.Abs(a.r-b.r),dg=Math.Abs(a.g-b.g),db=Math.Abs(a.b-b.b);int delta=Math.Max(dr,Math.Max(dg,db));maxDelta=Math.Max(maxDelta,delta);sumDelta+=dr+dg+db;if(delta>0)changedPixels++;
    }
    Assert.IsTrue(coverage>1000,"Actual source render has no nonempty foreground");double mean=luminance/coverage;finalMean=mean;if(frame==0)firstMean=mean;
    double averageDelta=frame==0?0:(double)sumDelta/(3*coverage);
    // This is an 8-bit presentation settling criterion, never a relaxation of
    // the accepted float/Half physics controls: <=1 output code step anywhere,
    // <=.05 code steps mean per foreground component, four consecutive pairs.
    if(frame>0 && maxDelta<=1 && averageDelta<=.05)stablePairs++;else stablePairs=0;
    rows.Add(new{render=frame+1,coverage,foreground_mean_display_luminance=mean,maximum_channel_delta_codes=frame==0?(int?)null:maxDelta,mean_absolute_delta_codes=frame==0?(double?)null:averageDelta,changed_pixels=frame==0?(int?)null:changedPixels,stable_consecutive_pairs=stablePairs,engine_frame=Application.FrameCount,elapsed_seconds=timer.Elapsed.TotalSeconds,native_tonemap_enabled=camera.SceneCamera.Tonemap.Enabled,auto_exposure_enabled=camera.AutoExposure.Enabled});
    renders=frame+1;
    if(frame==0){(firstPath,firstSha)=Save("first_actual_render");}
    if(stablePairs>=4){settled=true;(lastPath,lastSha)=Save("settled_actual_source_diffuse_transmission");break;}
    (previous,current)=(current,previous);
   }
   if(!settled)(lastPath,lastSha)=Save("unsettled_last_actual_render");
   Measurements.Add(new{phase="actual_render_to_texture_convergence",settled,renders,max_renders=maxRenders,width,height,source_sha256=Hash(source),selected_scene=0,authored_default_scene=model.GltfDefaultScene,first_path=firstPath,first_sha256=firstSha,final_path=lastPath,final_sha256=lastSha,first_mean_display_luminance=firstMean,final_mean_display_luminance=finalMean,criteria="Four successive actual RTT/readback pairs: maxchannel delta<=1RGBA8 code and mean absolute foreground-component delta<=.05 code. Presentation convergence only, not radiometric equality.",rows,output,scope="One actual source-only reference-sized settled visual capture; original source+canonical192px functional3 proof remains accepted and separate. No new high-resolution compiled capture claim, no environment/upstream photometric match."});
   Assert.IsTrue(settled,"Actual RTT output did not satisfy bounded presentation convergence; do not promote last image as settled");Assert.AreEqual(0,renderer.GltfInstance.CallbackErrors.Length);Assert.IsTrue(renderer.GltfInstance.DrawCalls>0);
   (string Path,string Sha) Save(string label){using var bitmap=target.GetBitmap();Assert.IsNotNull(bitmap);byte[] png=bitmap.ToPng();string path=Path.Combine(output,label+".png");File.WriteAllBytes(path,png);return(path,Hash(png));}
  }
  finally
  {
   await MainThread.Wait();using(capture.Scene.Push()){renderer?.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}rear?.Delete();model?.Destroy();try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}
   Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(File.ReadAllBytes(Path.Combine(sourceDirectory,"MandarinOrange.gltf"))));
  }
 }
}
