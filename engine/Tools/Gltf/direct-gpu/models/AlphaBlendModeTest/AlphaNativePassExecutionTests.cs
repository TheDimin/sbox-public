using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Self-contained coordinator MSTest entrypoints for the frozen native8/override8 probes.</summary>
[TestClass]
public sealed class AlphaNativePassExecutionTests
{
	public static List<object> Measurements { get; } = new();
	private static string output;
	private static string computePath;
	public static void Configure(string checkoutRoot,string outputDirectory,string deployedComputeShaderPath)
	{
		var root=Path.GetFullPath(checkoutRoot);output=Path.GetFullPath(outputDirectory);
		Require(output.StartsWith(Path.Combine(root,"Temp/gltf-validation/direct-gpu")+Path.DirectorySeparatorChar,StringComparison.Ordinal),"Evidence output must remain in direct-GPU temporary artifacts.");
		Require(!Directory.Exists(output)||!Directory.EnumerateFileSystemEntries(output).Any(),"Use fresh output to retain prior receipts.");
		Directory.CreateDirectory(output);computePath=deployedComputeShaderPath;Measurements.Clear();
	}
	[TestMethod]
	public async Task ActualNativePassCallbacksAndOrdering()
	{
		RequireIdle();Require(!string.IsNullOrEmpty(output),"Configure first.");
		var materials=Materials();CaptureContext context=null;
		try
		{
			context=new CaptureContext(output);
			var rows=await AlphaNativePassProbe.Run(context.Scene,context.Camera,materials.Opaque,materials.Blend,context.WaitFrames,context.Take);
			Measurements.Add(new{phase="actual_native_routing",nested_cases=rows.Length,rows});
			Require(rows.Length==8&&rows.All(r=>r.Passed),"Native routing probe failed: "+string.Join("\n",rows.Where(r=>!r.Passed).Select(r=>r.Name+": "+r.Error)));
		}
		finally{await MainThread.Wait();try{context?.Dispose();}finally{try{materials.Blend.Destroy();}finally{materials.Opaque.Destroy();}}}
	}
	[TestMethod]
	public void ActualEffectiveMaterialOverrideClassification()
	{
		RequireIdle();Require(!string.IsNullOrEmpty(computePath),"Configure exact deployed deformation shader path first.");
		var materials=Materials();Scene scene=null;
		try
		{
			scene=Scene.CreateEditorScene();
			var compute=Material.FromShader(computePath);Require(compute is{IsValid:true,IsError:false},"Deployed deformation shader unavailable.");
			var rows=AlphaMaterialRoutingProbe.Run(scene.SceneWorld,compute,materials.Opaque,materials.Blend);
			Measurements.Add(new{phase="actual_effective_material_routing",compute_shader_path=computePath,nested_cases=rows.Length,rows});
			Require(rows.Length==8&&rows.All(r=>r.Passed),"Actual backend classification failed: "+string.Join("\n",rows.Where(r=>!r.Passed).Select(r=>r.Name+": "+r.Error)));
		}
		finally{try{scene?.Destroy();}finally{try{materials.Blend.Destroy();}finally{materials.Opaque.Destroy();}}}
	}
	[TestMethod]
	public async Task ActualMixedPassAnimatedPoseAndNoOpOverrides()
	{
		RequireIdle();Require(!string.IsNullOrEmpty(output)&&!string.IsNullOrEmpty(computePath),"Configure first.");
		var materials=Materials();CaptureContext context=null;
		try
		{
			context=new CaptureContext(output);var compute=Material.FromShader(computePath);Require(compute is{IsValid:true,IsError:false},"Deployed deformation shader unavailable.");
			var rows=await AlphaMixedPassProbe.Run(context.Scene,context.Camera,compute,materials.Opaque,materials.Blend,context.WaitFrames,context.Take,measurement=>Measurements.Add(measurement));
			Measurements.Add(new{phase="mixed_pass_animated_pose_noop_overrides",rows});
		}
		finally{await MainThread.Wait();try{context?.Dispose();}finally{try{materials.Blend.Destroy();}finally{materials.Opaque.Destroy();}}}
	}
	[TestMethod]
	public void ActualEmptyInstanceReadiness()
	{
		RequireIdle();Require(!string.IsNullOrEmpty(computePath),"Configure first.");var materials=Materials();Scene scene=null;
		try
		{
			scene=Scene.CreateEditorScene();var compute=Material.FromShader(computePath);Require(compute is{IsValid:true,IsError:false},"Deployed deformation shader unavailable.");
			Measurements.Add(new{phase="empty_instance_vacuous_readiness",observed=AlphaMixedPassProbe.Empty(scene.SceneWorld,compute,materials.Opaque)});
		}
		finally{try{scene?.Destroy();}finally{try{materials.Blend.Destroy();}finally{materials.Opaque.Destroy();}}}
	}
	private static (Material Opaque,Material Blend) Materials()
	{
		var template=Material.Load("materials/default/white.vmat");Require(template is{IsValid:true,IsError:false},"Native white material unavailable.");
		Material opaque=null,blend=null;
		try
		{
			opaque=template.CreateCopy();blend=template.CreateCopy();opaque.SetFeature("F_TRANSLUCENT",0);blend.SetFeature("F_TRANSLUCENT",1);
			foreach(var material in new[]{opaque,blend}){material.SetFeature("F_RENDER_BACKFACES",1);material.Set("g_vColorTint",new global::Vector4(1,1,1,1));material.Set("g_flMetalness",0);}
			Measurements.Add(new{phase="native_control_materials",template=template.ResourcePath,opaque_shader=opaque.ShaderName,blend_shader=blend.ShaderName,opaque_translucent=opaque.Flags.IsTranslucent,blend_translucent=blend.Flags.IsTranslucent,opaque_alpha_test=opaque.Flags.IsAlphaTest,blend_alpha_test=blend.Flags.IsAlphaTest});
			Require(!opaque.Flags.IsTranslucent&&blend.Flags.IsTranslucent,"Actual native material flags do not match requested control features; retain this failure rather than infer successful classification.");
			return(opaque,blend);
		}
		catch{try{blend?.Destroy();}finally{opaque?.Destroy();}throw;}
	}
	private sealed class CaptureContext:IDisposable
	{
		public Scene Scene{get;}
		public CameraComponent Camera{get;}
		private readonly Editor.SceneRenderingWidget widget;
		private readonly ScenePointLight light;
		private readonly Action onFrame;
		private readonly string directory;
		private int frames;
		private bool disposed;
		public CaptureContext(string directory)
		{
			this.directory=directory;Scene=Scene.CreateEditorScene();
			try
			{
				using(Scene.Push())Camera=Scene.CreateObject().Components.Create<CameraComponent>();
				light=new ScenePointLight(Scene.SceneWorld,new global::Vector3(200,0,0),1000,Color.White*4);
				widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=Camera,WindowTitle="Alpha native pass audit",Size=new global::Vector2(384,384)};
				onFrame=()=>{frames++;LockExposure();};widget.OnPreFrame+=onFrame;widget.Show();
			}
			catch{Dispose();throw;}
		}
		private void LockExposure(){Camera.AutoExposure.Enabled=false;Camera.AutoExposure.MinimumExposure=1;Camera.AutoExposure.MaximumExposure=1;Camera.AutoExposure.Compensation=0;if(Camera.SceneCamera is not null)Camera.SceneCamera.Tonemap.Enabled=false;}
		public async Task WaitFrames()
		{
			await MainThread.Wait();RequireIdle();var start=frames;var timer=Stopwatch.StartNew();
			while(frames<start+3){Require(timer.Elapsed<TimeSpan.FromSeconds(10),"Visible native audit preview stopped rendering.");await Task.Delay(20);await MainThread.Wait();RequireIdle();}
		}
		public async Task<Color[]> Take(string label)
		{
			await WaitFrames();using(Scene.Push()){Scene.PreCameraRender();LockExposure();Camera.InitializeRendering(false);LockExposure();}
			using var target=Texture.CreateRenderTarget().WithSize(192,192).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create("alpha_pass_"+Guid.NewGuid().ToString("N"));
			bool submitted;using(Scene.Push()){target.Clear(Color.Magenta);submitted=Camera.RenderToTexture(target);}Require(submitted,"Native audit camera rejected target.");
			await WaitFrames();using var bitmap=target.GetBitmap();Require(bitmap is not null,"Native audit image readback absent.");var pixels=bitmap.GetPixels();Require(pixels.Length==192*192,"Native audit capture extent changed.");
			var png=bitmap.ToPng();var path=Path.Combine(directory,label+".png");Require(!File.Exists(path),"Capture would overwrite earlier evidence.");File.WriteAllBytes(path,png);
			Measurements.Add(new{phase="native_routing_capture",label,path,sha256=Convert.ToHexStringLower(SHA256.HashData(png)),frame=Application.FrameCount,widget_frames=frames,width=192,height=192,Camera.DebugMode,Camera.EnablePostProcessing,native_tonemap=Camera.SceneCamera.Tonemap.Enabled});return pixels;
		}
		public void Dispose()
		{
			if(disposed)return;disposed=true;
			try{if(widget is not null && widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}}
			finally{try{if(light.IsValid())light.Delete();}finally{Scene.Destroy();}}
		}
	}
	private static void RequireIdle(){ThreadSafe.AssertIsMainThread();Require(Application.IsEditor&&!Game.IsPlaying&&!Graphics.IsActive,"Requires coordinator verified idle editor outside rendering.");}
	private static void Require(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
