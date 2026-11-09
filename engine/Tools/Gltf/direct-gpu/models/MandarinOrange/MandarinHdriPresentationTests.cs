using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Sandbox;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Assert=Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

namespace Sandbox.Gltf.DirectGpu.MandarinOrangeValidation;

[TestClass]
internal sealed class MandarinHdriPresentationTests
{
 public static string CheckoutRoot {get;set;}
 public static string ReviewProfiles {get;set;}
 static readonly Vector3 ReviewPosition = new(1.11069021f, -.02845442f, .00645021f);
 static readonly Vector3 ReviewTarget = new(.00087155f, -.01676070f, -.03103364f);
 static readonly Vector3 ReviewUp = new(-.03377338f, -.00170327f, .99942807f);
 const float ReviewVerticalFov = 42.21342745f;
 public static List<object> Measurements {get;}=new();
 const int Width=1151,Height=1309;
 static string Hash(byte[] b)=>Convert.ToHexStringLower(SHA256.HashData(b));
 sealed record Frame(Color32[] Pixels,string Path,string Sha);
 [TestMethod]
 public async Task ActualSourceFieldSkyAndReducedIllumination()
 {
  await MainThread.Wait();
  string sourceDir=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/MandarinOrange/glTF");byte[] bytes=File.ReadAllBytes(Path.Combine(sourceDir,"MandarinOrange.gltf"));Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(bytes));
  string output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/MandarinOrange/hdri-presentation",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
  string directory=Path.Combine(Path.GetTempPath(),"gltf_mandarin_hdri_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
  bool aligned = !string.IsNullOrEmpty(ReviewProfiles);
  var position = aligned ? ReviewPosition : new Vector3(4,0,0);
  var lookAt = aligned ? ReviewTarget : Vector3.Zero;
  var profiles = aligned
   ? ReviewProfiles.Split(';').Select(profile => profile.Split(',')).Select(values => (
      Label: values[0],
      Yaw: float.Parse(values[1], CultureInfo.InvariantCulture),
      Exposure: float.Parse(values[2], CultureInfo.InvariantCulture))).ToArray()
   : new[] { (Label: "", Yaw: 0f, Exposure: 1f) };
  using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,position,lookAt,1.25f);
  using var target=Texture.CreateRenderTarget().WithSize(Width,Height).WithFormat(ImageFormat.RGBA8888).Create("mandarin_hdri_"+Guid.NewGuid().ToString("N"));
  Model model=null;ModelRenderer renderer=null;LocalFileSystem mount=null;SceneCubemap probe=null;SceneSkyBox sky=null;Material skyMaterial=null;
  try
  {
   var oldLight=(ScenePointLight)typeof(GltfGpuTriangleGateTests.NativeCapture).GetField("light",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(capture);oldLight.LightColor=Color.Black;
   capture.Scene.SceneWorld.AmbientLightColor=Color.Transparent;
   var camera=capture.Camera;camera.CustomSize=new Vector2(Width,Height);camera.Orthographic=false;camera.FovAxis=CameraComponent.Axis.Vertical;camera.FieldOfView=aligned ? ReviewVerticalFov : 18f;camera.ZNear=.1f;
   if(aligned) camera.WorldRotation=Rotation.LookAt(lookAt-position,ReviewUp);
   Tonemapping tone;using(capture.Scene.Push()){tone=camera.GameObject.Components.Create<Tonemapping>();tone.Mode=Tonemapping.TonemappingMode.ACES;tone.AutoExposureEnabled=true;tone.MinimumExposure=1;tone.MaximumExposure=1;tone.ExposureCompensation=0;tone.Rate=20;capture.Scene.EditorTick(0,0);}
   string texturePath="materials/gltf_reference_environment/field.vtex";
   string environmentSha=Hash(File.ReadAllBytes(Path.Combine(CheckoutRoot,"game/core/"+texturePath+"_c")));Assert.AreEqual("01cc0d0f0f69aa856b6650c71d5a4c8528667f61444b35b7c9bc382cda98fc55",environmentSha);
   var cube=Texture.Load(texturePath);Assert.IsTrue(cube is {IsValid:true,IsError:false});Assert.AreEqual(texturePath,cube.ResourcePath);Assert.AreEqual(6,cube.Depth);cube.MarkUsed(ushort.MaxValue);await capture.WaitFrames();
   var tint=new Color(.75f,.75f,.75f,1);
   skyMaterial=Material.Load("materials/skybox/skybox_day_01.vmat").CreateCopy("mandarin_field_sky_"+Guid.NewGuid().ToString("N"));Assert.IsTrue(skyMaterial is {IsValid:true,IsError:false});skyMaterial.Set("g_tSkyTexture",cube);skyMaterial.Set("g_flBrightnessExposureBias",0f);skyMaterial.Set("g_flRenderOnlyExposureBias",0f);Assert.AreEqual(texturePath,skyMaterial.GetTexture("g_tSkyTexture").ResourcePath);
   foreach(string file in Directory.GetFiles(sourceDir))File.Copy(file,Path.Combine(directory,Path.GetFileName(file)));string name="mandarin_hdri_"+Guid.NewGuid().ToString("N")+".gltf";File.WriteAllBytes(Path.Combine(directory,name),bytes);mount=new LocalFileSystem(directory);FileSystem.Mounted.Mount(mount);
   model=Model.Load(name,0);
   using(capture.Scene.Push())
   {
    renderer=capture.Scene.CreateObject().Components.Create<ModelRenderer>(false);
    renderer.Model=model;
    renderer.Enabled=false;
    capture.Scene.EditorTick(0,0);
   }
   var original=model.Materials.Single();
   StringAssert.Contains(original.ShaderName,"gltf_diffuse_transmission");
   Assert.AreEqual(1f,original.GetVector4("g_flGltfDiffuseTransmissionFactor").x);

   foreach(var profile in profiles)
   {
    var orientation = new Transform(Vector3.Zero, Rotation.FromYaw(profile.Yaw), Vector3.One);
    tone.MinimumExposure=profile.Exposure;
    tone.MaximumExposure=profile.Exposure;
    string prefix = aligned ? profile.Label+"_" : "";
    using(capture.Scene.Push())
    {
     renderer.Enabled=false;
     capture.Scene.EditorTick(0,0);
    }
    sky=new SceneSkyBox(capture.Scene.SceneWorld,skyMaterial){Transform=orientation,SkyTint=tint};
    var emptySky=await Settle(prefix+"actual_field_sky_only");
    Assert.IsTrue(emptySky.Pixels.Count(p=>p.r>8 || p.g>8 || p.b>8)>Width*Height/2,
     "Actual native sky pixels are absent");
    using(capture.Scene.Push())
    {
     renderer.Enabled=true;
     capture.Scene.EditorTick(0,0);
    }
    probe=new SceneCubemap(capture.Scene.SceneWorld,cube,BBox.FromPositionAndSize(Vector3.Zero,100000))
     {Projection=SceneCubemap.ProjectionMode.Sphere,Transform=orientation,TintColor=tint,Feathering=.01f};
    probe.RenderDirty();
    var actual=await Settle(prefix+"settled_actual_source_field_hdri_sky");
    int[] mask=Enumerable.Range(0,actual.Pixels.Length).Where(i=>Delta(actual.Pixels[i],emptySky.Pixels[i])>3).ToArray();
    Assert.IsTrue(mask.Length>10000,"Actual original source model is absent over native sky");
    Assert.AreEqual(0,renderer.GltfInstance.CallbackErrors.Length);
    Assert.IsTrue(renderer.GltfInstance.DrawCalls>0);
    Assert.AreEqual(0f,capture.Scene.SceneWorld.AmbientLightColor.a);
    probe.Delete();
    probe=null;
    bool controls = profiles.Length == 1;
    Frame noIbl = null;
    int? iblChanged = null;
    if(controls)
    {
     noIbl=await Settle(prefix+"same_sky_without_ibl_probe_control");
     iblChanged=mask.Count(i=>Delta(actual.Pixels[i],noIbl.Pixels[i])>3);
     Assert.IsTrue(iblChanged>1000,"Actual IBL did not affect original source model while sky stayed fixed");
    }
    sky.Delete();
    sky=null;
    Frame emptyBlack = null;
    if(controls)
    {
     using(capture.Scene.Push())
     {
      renderer.GameObject.Destroy();
      capture.Scene.EditorTick(0,0);
      capture.Scene.ProcessDeletes();
     }
     renderer=null;
     emptyBlack=await Settle(prefix+"empty_after_sky_model_probe_delete");
     Assert.IsTrue(emptyBlack.Pixels.All(p=>p.r==0 && p.g==0 && p.b==0),
      "Native sky/model/probe delete did not restore black scene");
    }
    double mean=mask.Average(i=>(.2126*actual.Pixels[i].r+.7152*actual.Pixels[i].g+.0722*actual.Pixels[i].b)/255);
    int nearWhite=mask.Count(i=>actual.Pixels[i].r>=230 && actual.Pixels[i].g>=230 && actual.Pixels[i].b>=230);
    Measurements.Add(new{
     phase="source_hdri_sky_presentation",profile=profile.Label,path=actual.Path,sha256=actual.Sha,width=Width,height=Height,
     source_sha256=Hash(bytes),selected_scene=0,authored_default_scene=model.GltfDefaultScene,
     environment_texture=texturePath,environment_compiled_sha256=environmentSha,environment_width=cube.Width,
     environment_height=cube.Height,environment_depth=cube.Depth,environment_mips=cube.Mips,
     environment_intensity=.75f,environment_yaw=profile.Yaw,sky_path=emptySky.Path,sky_sha256=emptySky.Sha,
     no_ibl_path=noIbl?.Path,no_ibl_sha256=noIbl?.Sha,deleted_black_path=emptyBlack?.Path,deleted_black_sha256=emptyBlack?.Sha,
     foreground_pixels=mask.Length,actual_ibl_changed_pixels=iblChanged,foreground_mean_display_luminance=mean,
     near_white_rgb230_pixels=nearWhite,near_white_fraction=(double)nearWhite/mask.Length,
     camera_position=position.ToString(),camera_target=lookAt.ToString(),camera_up=(aligned?ReviewUp:Vector3.Up).ToString(),
     projection="perspective",vertical_fov=camera.FieldOfView,near=camera.ZNear,far=camera.ZFar,
     illumination="Field IBL .75, matching visible Field sky .75; no point lights or ambient",
     tone_mode=tone.Mode.ToString(),exposure_min=camera.AutoExposure.MinimumExposure,
     exposure_max=camera.AutoExposure.MaximumExposure,exposure_compensation=camera.AutoExposure.Compensation,
     material_shader=original.ShaderName,scope="Source-only native HDRI presentation. Camera, Field rotation and fixed exposure inferred for comparison. No material edits or functional suite rerun."
    });
   }

   async Task<Frame> Settle(string label)
   {
    var previous=new Color32[Width*Height];var current=new Color32[previous.Length];var rows=new List<object>();int stable=0;var timer=Stopwatch.StartNew();
    for(int frame=0;frame<40;frame++)
    {
     await capture.WaitFrames();using(capture.Scene.Push()){capture.Scene.PreCameraRender();camera.InitializeRendering(false);Assert.IsTrue(camera.RenderToTexture(target));}await capture.WaitFrames();Array.Fill(current,new Color32(255,0,255,255));target.GetPixels<Color32>((0,0,Width,Height),0,0,current,ImageFormat.RGBA8888);Assert.IsTrue(current.All(p=>!(p.r==255 && p.g==0 && p.b==255)),"Readback retained magenta sentinel");
     int maximum=0,changed=0;long total=0;double luma=0;for(int i=0;i<current.Length;i++){var a=current[i];luma+=(.2126*a.r+.7152*a.g+.0722*a.b)/255;if(frame==0)continue;var b=previous[i];int dr=Math.Abs(a.r-b.r),dg=Math.Abs(a.g-b.g),db=Math.Abs(a.b-b.b);int d=Math.Max(dr,Math.Max(dg,db));maximum=Math.Max(maximum,d);total+=dr+dg+db;if(d>0)changed++;}
     double mean=frame==0?0:(double)total/(3*current.Length);stable=frame>0 && maximum<=1 && mean<=.05?stable+1:0;
     rows.Add(new{render=frame+1,max_channel_delta_codes=frame==0?(int?)null:maximum,mean_absolute_delta_codes=frame==0?(double?)null:mean,changed_pixels=frame==0?(int?)null:changed,mean_display_luminance=luma/current.Length,stable_pairs=stable,elapsed_seconds=timer.Elapsed.TotalSeconds});
     if(stable>=4){using var bitmap=target.GetBitmap();byte[] png=bitmap.ToPng();string path=Path.Combine(output,label+".png");File.WriteAllBytes(path,png);Measurements.Add(new{phase="actual_rtt_convergence",label,rows,path,sha256=Hash(png),native_tonemap_enabled=camera.SceneCamera.Tonemap.Enabled,auto_exposure_enabled=camera.AutoExposure.Enabled,minimum_exposure=camera.AutoExposure.MinimumExposure,maximum_exposure=camera.AutoExposure.MaximumExposure,exposure_compensation=camera.AutoExposure.Compensation,ambient=capture.Scene.SceneWorld.AmbientLightColor.ToString(),criteria="Four consecutive full-image RTT pairs: maximum1RGBA8 code and mean .05codes/component or lower; actual renders, not widget-only wait."});return new Frame(current,path,Hash(png));}
     (previous,current)=(current,previous);
    }
    Measurements.Add(new{phase="unsettled_actual_rtt",label,rows});Assert.Fail("Bounded actual RTT presentation did not settle: "+label);return null;
   }
  }
  finally
  {
   await MainThread.Wait();using(capture.Scene.Push()){renderer?.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}probe?.Delete();sky?.Delete();skyMaterial?.Destroy();model?.Destroy();try{if(mount!=null)FileSystem.Mounted.UnMount(mount);}finally{mount?.Dispose();Directory.Delete(directory,true);}Assert.AreEqual("148a260776eb8258f1d90c1623bad1bdca17b1bec42da597536ef09e816c2091",Hash(File.ReadAllBytes(Path.Combine(sourceDir,"MandarinOrange.gltf"))));
  }
 }
 static int Delta(Color32 a,Color32 b)=>Math.Max(Math.Abs(a.r-b.r),Math.Max(Math.Abs(a.g-b.g),Math.Abs(a.b-b.b)));
}
