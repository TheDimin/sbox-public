using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NativeEngine;
using Sandbox;
using Sandbox.Gltf;
using Sandbox.Rendering;

namespace Sandbox.Gltf.DirectGpu.EnvironmentValidation;

[TestClass]
internal sealed class FieldHdrAndExposureTests
{
    public static string CheckoutRoot { get; set; }
    public static string OutputDirectory { get; set; }
    public static List<object> Measurements { get; } = new();
    const string FieldPath = "materials/gltf_reference_environment/field.vtex";
    const string FieldSha = "01cc0d0f0f69aa856b6650c71d5a4c8528667f61444b35b7c9bc382cda98fc55";
    static string Hash(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    static string HashFloats(float[] value) => Convert.ToHexStringLower(SHA256.HashData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(value.AsSpan())));
    static object SafeFloat(float value) => float.IsFinite(value) ? value : value.ToString(System.Globalization.CultureInfo.InvariantCulture);
    static object[] Observe(float[] data,int offset,int count) => data.Skip(offset).Take(count).Select(SafeFloat).ToArray();

    static Texture Field()
    {
        Assert.AreEqual("a9ad16a6d516171e425d0fc22cdde643b2116ed544578945899958cea21e1e7f", Hash(File.ReadAllBytes(Path.Combine(CheckoutRoot, "game/core/materials/gltf_reference_environment/field.exr"))));
        Assert.AreEqual("562deb2ba9abd7d44a2b1b15ca4725cf4711a0feb6e6117f3b58e6894ae8eb6d", Hash(File.ReadAllBytes(Path.Combine(CheckoutRoot, "game/core/" + FieldPath))));
        Assert.AreEqual(FieldSha, Hash(File.ReadAllBytes(Path.Combine(CheckoutRoot, "game/core/" + FieldPath + "_c"))));
        var texture = Texture.Load(FieldPath);
        Assert.IsTrue(texture is { IsValid: true, IsError: false } && texture.ResourcePath == FieldPath);
        Assert.AreEqual(1024, texture.Width); Assert.AreEqual(1024, texture.Height);
        Assert.AreEqual(6, texture.Depth); Assert.AreEqual(9, texture.Mips); Assert.AreEqual(ImageFormat.BC6H, texture.ImageFormat);
        return texture;
    }

    [TestMethod]
    public async Task A_ComputeHdrControlsThenActualFieldResidencyAndEveryMip()
    {
        await MainThread.Wait(); Measurements.Clear(); Directory.CreateDirectory(OutputDirectory);
        Assert.IsTrue(Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && Graphics.IsAvailable);
        using var sampler = await CubeSampler.Create();
        using var known = LiteralCube(false);
        using var black = LiteralCube(true);
        var control = sampler.Sample(known, 16, 5, false);
        var wrongMip = sampler.Sample(known, 16, 5, true);
        var blackControl = sampler.Sample(black, 16, 5, false);
        var observedControls=new List<object>();
        for(int mip=0;mip<5;mip++)for(int face=0;face<6;face++){int width=Math.Max(1,16>>mip),at=(control.Start(mip)+face*width*width)*4;observedControls.Add(new{face,mip,expected=Literal(face,mip,false),actual=Observe(control.Data,at,4),forced_base=Observe(wrongMip.Data,at,4),black=Observe(blackControl.Data,at,4)});}
        Measurements.Add(new{stage="literal-hdr-observed-before-assertions",values=observedControls});
        int rows = 0, wrongRows = 0;
        for (int mip = 0; mip < 5; mip++)
        {
            int width = Math.Max(1, 16 >> mip), start = control.Start(mip);
            Assert.AreEqual(width, control.Header(mip).Width);
            for (int face = 0; face < 6; face++)
                for (int pixel = 0; pixel < width * width; pixel++)
                {
                    var expected = Literal(face, mip, false);
                    int at = start + face * width * width + pixel;
                    for (int channel = 0; channel < 4; channel++)
                    {
                        Assert.IsTrue(Math.Abs(control.Data[at * 4 + channel] - expected[channel]) < 2e-6, $"Literal face{face}/mip{mip}/channel{channel}");
                        Assert.AreEqual(channel == 3 ? 1f : 0f, blackControl.Data[at * 4 + channel], "Black binding/sentinel control");
                    }
                    if (mip > 0 && Math.Abs(wrongMip.Data[at * 4] - expected[0]) > .01f) wrongRows++;
                    rows++;
                }
        }
        Assert.IsTrue(wrongRows > 100, "Forced-base-mip negative control was not observable.");
        Measurements.Add(new { stage = "literal-hdr-controls", rows, wrong_mip_rows = wrongRows, exact_hdr_maximum = control.Data.Max(), scalar_transfer = "point cube sample -> float4 UAV -> GpuBuffer.GetData, no Texture.GetPixels conversion" });

        var field = Field();
        var clock = Stopwatch.StartNew(); var attempts = new List<object>(); SampledCube initial = null, resident = null;
        ulong firstFrame = Application.FrameCount;
        do
        {
            field.MarkUsed(ushort.MaxValue);
            var sample = sampler.Sample(field, 8, 9, false);
            initial ??= sample;
            attempts.Add(new { seconds = clock.Elapsed.TotalSeconds, frame = Application.FrameCount, sample.Width, sample.Height, sample.Mips });
            if (sample.Width == 1024 && sample.Height == 1024 && sample.Mips == 9) { resident = sample; break; }
            await Task.Delay(100); await MainThread.Wait();
        } while (clock.Elapsed < TimeSpan.FromSeconds(45));
        Measurements.Add(new { stage = "field-sampled-gpu-view-residency", attempts, first_frame = firstFrame, last_frame = Application.FrameCount, scope = "Existing-cache state and bounded MarkUsed requests, not cold streaming/load performance or full resident byte identity." });
        Assert.IsNotNull(resident, "Bounded MarkUsed did not expose the full 1024px/9-mip GPU view.");
        var full = sampler.Sample(field, 1024, 9, false);
        Assert.AreEqual(1024, full.Width); Assert.AreEqual(9, full.Mips);
        var summaries = await Task.Run(() => Summarize(full)); await MainThread.Wait();
        Measurements.Add(new { stage = "field-full-sampled-mips", source_texture = FieldPath, source_compiled_sha256 = FieldSha, gpu_values_sha256 = HashFloats(full.Data), summaries,
            scope = "Every GPU-visible texel center at each of nine mips sampled to float UAV; no raw BC6H CPU conversion, tone map or image editing. Does not establish reference HDRI/orientation/photometric equivalence or independent GGX convolution correctness." });
        Assert.IsTrue(summaries.All(x => x.Nonfinite == 0 && x.InvalidAlpha == 0 && x.Minimum >= 0 && x.Maximum > 0), "A sampled mip is empty/nonfinite/negative or has invalid opaque alpha.");
        Assert.IsTrue(summaries[0].Maximum > 1.01, "Full base-mip samples did not retain HDR values above one.");
        Assert.IsTrue(summaries.Skip(1).Any(x => Math.Abs(x.Maximum - summaries[0].Maximum) > .01), "Actual sampled mip levels did not have distinct ranges.");
        Field();
    }

    [TestMethod]
    public async Task B_NativeExposureScalarConvergenceAndActualPixelResponse()
    {
        await MainThread.Wait(); Directory.CreateDirectory(OutputDirectory);
        var field = Field();
        using var capture = new ExposureCapture(OutputDirectory);
        using var document = GltfReader.Read(EmissionPlane());
        var template = Material.FromShader("shaders/gltf_surface_five_uv.shader").CreateCopy("field_exposure_source_" + Guid.NewGuid().ToString("N"));
        SceneCubemap probe = null;
        try
        {
            template.SetFeature("F_ALPHA_TEST",0);template.SetFeature("F_TRANSLUCENT",0);template.SetFeature("F_RENDER_BACKFACES",0);
            using var asset = GltfGpuAsset.PrepareProjected(document, GltfGpuAsset.DefaultComputeShaderPath, 1024*1024, (_,_)=>template, GltfUnlitMaterialPlan.Features.None, sourceLitFeatureSidedness:true);
            using var instance = asset.CreateInstance(capture.Scene.SceneWorld);
            probe = new SceneCubemap(capture.Scene.SceneWorld, field, BBox.FromPositionAndSize(Vector3.Zero, 100000));
            probe.Projection = SceneCubemap.ProjectionMode.Sphere; probe.Feathering = .01f; probe.RenderDirty();
            var zero = await capture.TakeConverged(0, "exposure_ev0");
            var two = await capture.TakeConverged(2, "exposure_ev2");
            int changed = zero.Pixels.Zip(two.Pixels, (a, b) => Math.Abs(a.r - b.r) > 3 || Math.Abs(a.g - b.g) > 3 || Math.Abs(a.b - b.b) > 3).Count(x => x);
            double mean0 = zero.Pixels.Average(x => (x.r + x.g + x.b) / 3.0), mean2 = two.Pixels.Average(x => (x.r + x.g + x.b) / 3.0);
            Measurements.Add(new { stage = "fixed-exposure-native-scalar-and-pixels", zero.Samples, ev2_samples = two.Samples, ev0_sha256 = zero.Sha, ev2_sha256 = two.Sha, changed_pixels_above3of255 = changed, ev0_mean = mean0, ev2_mean = mean2,
                scope = "New authored unit-emissive black plane isolates camera exposure; Field retained in private scene. No unchanged NormalTangent/MetalRough captures and no Khronos tone-curve/photometric equivalence claim." });
            Assert.IsTrue(changed > 1000 && mean2 > mean0 + 3, "Native exposure settings/scalar did not yield a brighter actual +2EV image.");
            Assert.AreEqual(0, instance.CallbackErrors.Length); Assert.IsTrue(instance.DrawCalls > 0);
        }
        finally { await MainThread.Wait(); if (probe.IsValid()) probe.Delete(); template?.Destroy(); }
    }

    static float[] Literal(int face, int mip, bool black)
    {
        float[][] faces = [[2.125f,.234567f,.12345f],[.5f,3.375f,.25f],[.125f,.5f,4.625f],[1.25f,2.5f,3.75f],[4.0625f,2.03125f,1.015625f],[.12345f,.23456f,.34567f]];
        return black ? [0,0,0,1] : [faces[face][0]+mip*.0625f, faces[face][1]+mip*.125f, faces[face][2]+mip*.25f, 1];
    }
    static Texture LiteralCube(bool black)
    {
        using var payload = new MemoryStream(); using var writer = new BinaryWriter(payload);
        for(int mip=4;mip>=0;mip--) for(int face=0;face<6;face++) for(int pixel=0;pixel<(16>>mip)*(16>>mip);pixel++) foreach(float channel in Literal(face,mip,black)) writer.Write(channel);
        return Texture.CreateCube(16,16,ImageFormat.RGBA32323232F).WithMips(5).WithData(payload.ToArray()).WithStaticUsage().Finish();
    }
    sealed record SampledCube(float[] Data,int Grid,int RequestedMips)
    {
        public int Width=>(int)Data[0];public int Height=>(int)Data[1];public int Mips=>(int)Data[2];
        public (int Width,int Height,int Mips) Header(int mip) {int at=(1+mip)*4;return((int)Data[at],(int)Data[at+1],(int)Data[at+2]);}
        public int Start(int mip){int start=1+RequestedMips;for(int level=0;level<mip;level++){int g=Math.Max(1,Grid>>level);start+=6*g*g;}return start;}
    }
    sealed record MipSummary(int Mip,int Width,int Height,int Samples,int Nonfinite,int InvalidAlpha,float Minimum,float Maximum,double Mean,double StandardDeviation);
    static MipSummary[] Summarize(SampledCube cube)
    {
        var rows=new List<MipSummary>();
        for(int mip=0;mip<cube.RequestedMips;mip++)
        {
            int g=Math.Max(1,cube.Grid>>mip),count=6*g*g,start=cube.Start(mip),nonfinite=0,invalidAlpha=0;float min=float.PositiveInfinity,max=float.NegativeInfinity;double sum=0,squared=0;
            for(int pixel=0;pixel<count;pixel++)for(int c=0;c<3;c++){float x=cube.Data[(start+pixel)*4+c];if(!float.IsFinite(x)){nonfinite++;continue;}min=Math.Min(min,x);max=Math.Max(max,x);sum+=x;squared+=(double)x*x;}
            for(int pixel=0;pixel<count;pixel++)if(cube.Data[(start+pixel)*4+3]!=1f)invalidAlpha++;
            if(!float.IsFinite(min))min=0;if(!float.IsFinite(max))max=0;
            double n=count*3.0,mean=sum/n;var header=cube.Header(mip);rows.Add(new(mip,header.Width,header.Height,count,nonfinite,invalidAlpha,min,max,mean,Math.Sqrt(Math.Max(0,squared/n-mean*mean))));
        }
        return rows.ToArray();
    }
    sealed class CubeSampler:IDisposable
    {
        Shader shader;Material material;
        public static async Task<CubeSampler> Create()
        {
            var result=new CubeSampler();try{using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(90));result.shader=await Shader.CompileAsync(ComputeSource,timeout.Token);await MainThread.Wait();Assert.IsTrue(result.shader is {IsValid:true,IsAnonymous:true});result.material=Material.Create("field_hdr_probe_"+Guid.NewGuid().ToString("N"),result.shader);Assert.IsTrue(result.material is {IsValid:true,IsError:false});return result;}catch{await MainThread.Wait();result.Dispose();throw;}
        }
        public SampledCube Sample(Texture source,int grid,int mips,bool forceBase)
        {
            Assert.IsFalse(Graphics.IsActive);int count=0;for(int mip=0;mip<mips;mip++){int g=Math.Max(1,grid>>mip);count=checked(count+6*g*g);}int rows=checked(1+mips+count);
            using var buffer=new GpuBuffer(rows,16,GpuBuffer.UsageFlags.Structured,"Field actual HDR sample UAV");var data=Enumerable.Repeat(float.NaN,checked(rows*4)).ToArray();buffer.SetData<float>(data);
            int dispatchWidth=Math.Min(262144,checked(((count+63)/64)*64)),dispatchHeight=(count+dispatchWidth-1)/dispatchWidth;
            using(var scope=Graphics.Scope.Create())
            {
                Graphics.Attributes.Set("ProbeCube",source);Graphics.Attributes.Set("ProbeOutput",buffer);Graphics.Attributes.Set("ProbeGrid",grid);Graphics.Attributes.Set("ProbeMipCount",mips);Graphics.Attributes.Set("ProbeForceBaseMip",forceBase?1:0);Graphics.Attributes.Set("ProbeDispatchWidth",dispatchWidth);
                Graphics.ResourceBarrierTransition(buffer,ResourceState.GenericRead,ResourceState.UnorderedAccess);RenderTools.Compute(Graphics.Context,Graphics.Attributes.Get(),material.native.GetMode(),dispatchWidth,dispatchHeight,1);Graphics.ResourceBarrierTransition(buffer,ResourceState.UnorderedAccess,ResourceState.GenericRead);
            }
            Graphics.FlushGPU();buffer.GetData<float>(data);Measurements.Add(new{stage="compute-dispatch-observed",grid,mips,forceBase,headers=Observe(data,0,(1+mips)*4),first_rgba=Observe(data,(1+mips)*4,4)});Assert.IsTrue(data.Take((1+mips)*4).All(float.IsFinite),"Compute headers retained sentinel or returned nonfinite.");return new(data,grid,mips);
        }
        public void Dispose(){material?.Destroy();shader?.Destroy();}
    }
    sealed class ExposureCapture:IDisposable
    {
        public Scene Scene{get;} readonly CameraComponent camera;readonly Tonemapping tone;readonly Editor.SceneRenderingWidget widget;readonly Action onFrame;readonly string output;int frames;
        public ExposureCapture(string output)
        {
            this.output=output;Scene=Scene.CreateEditorScene();using(Scene.Push()){camera=Scene.CreateObject().Components.Create<CameraComponent>();camera.WorldPosition=new Vector3(100,0,0);camera.WorldRotation=Rotation.LookAt(Vector3.Backward,Vector3.Up);camera.Orthographic=true;camera.OrthographicHeight=100;camera.CustomSize=new Vector2(320,240);camera.BackgroundColor=Color.Black;camera.ClearFlags=ClearFlags.All;camera.ZNear=.1f;camera.ZFar=1000;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;tone=camera.GameObject.Components.Create<Tonemapping>();tone.Mode=Tonemapping.TonemappingMode.ACES;tone.AutoExposureEnabled=true;tone.MinimumExposure=1;tone.MaximumExposure=1;tone.Rate=20;Scene.EditorTick(0,0);}widget=new Editor.SceneRenderingWidget{Scene=Scene,Camera=camera,WindowTitle="Field native exposure diagnostic",Size=new Vector2(320,240)};onFrame=()=>frames++;widget.OnPreFrame+=onFrame;widget.Show();
        }
        async Task Wait(){await MainThread.Wait();int start=frames;var time=Stopwatch.StartNew();while(frames<start+3||Graphics.IsActive){Assert.IsTrue(time.Elapsed<TimeSpan.FromSeconds(12),"Diagnostic camera frames stalled");await Task.Delay(20);await MainThread.Wait();}}
        public sealed record Frame(Color32[] Pixels,string Sha,object[] Samples);
        public async Task<Frame> TakeConverged(float ev,string name)
        {
            tone.ExposureCompensation=ev;using(Scene.Push())Scene.EditorTick(0,0);await Wait();using var target=Texture.CreateRenderTarget().WithSize(320,240).WithFormat(ImageFormat.RGBA8888).WithInitialColor(Color.Magenta).Create(name+Guid.NewGuid().ToString("N"));var samples=new List<object>();double expected=Math.Pow(2,ev);bool converged=false;var time=Stopwatch.StartNew();
            for(int attempt=0;attempt<24&&time.Elapsed<TimeSpan.FromSeconds(20);attempt++)
            {
                bool submitted;using(Scene.Push()){target.Clear(Color.Magenta);submitted=camera.RenderToTexture(target);}Assert.IsTrue(submitted);float scalar=RenderTools.TonemapFrameUpdate(camera.SceneCamera.ToneMapping.GetNative(),false);samples.Add(new{attempt,scalar=SafeFloat(scalar),expected,frame=Application.FrameCount,minimum=camera.AutoExposure.MinimumExposure,maximum=camera.AutoExposure.MaximumExposure,compensation=camera.AutoExposure.Compensation});Measurements.Add(new{stage="exposure-native-scalar-progress",ev,attempt,scalar=SafeFloat(scalar),expected});Assert.IsTrue(float.IsFinite(scalar)&&scalar>0);
                if(Math.Abs(scalar-expected)<=Math.Max(.001,expected*.005)){converged=true;break;}await Wait();
            }
            Assert.IsTrue(converged,"Native exposure failed to converge within24captures/20seconds");await Wait();using(Scene.Push())Assert.IsTrue(camera.RenderToTexture(target));await Wait();using var bitmap=target.GetBitmap();Assert.IsNotNull(bitmap);var png=bitmap.ToPng();File.WriteAllBytes(Path.Combine(output,name+".png"),png);var pixels=bitmap.GetPixels32();Assert.AreEqual(320*240,pixels.Length);Assert.IsFalse(pixels.All(x=>x.r==255&&x.g==0&&x.b==255),"Native target retained magenta sentinel");Assert.IsTrue(pixels.Any(x=>x.r!=0||x.g!=0||x.b!=0),"Exposure plane not visible");return new(pixels,Hash(png),samples.ToArray());
        }
        public void Dispose(){if(widget.IsValid){widget.OnPreFrame-=onFrame;widget.Close();widget.Destroy();}Scene.Destroy();}
    }
    static byte[] EmissionPlane()
    {
        var bytes = File.ReadAllBytes(Path.Combine(CheckoutRoot, "research/gltf-validation/direct-gpu/models/EnvironmentTest/field-exposure-plane.gltf"));
        Assert.AreEqual("9adda3a3cbe59eb1fab39571c6c0db56a3a254386ce6a19d7a9978bb16e98dbb", Hash(bytes));
        return bytes;
    }
    const string ComputeSource="""
HEADER { Description = "Private Field HDR cube sampling, no native texture conversion"; }
MODES { Default(); }
COMMON
{
    #include "system.fxc"
    #include "common.fxc"
}
CS
{
    TextureCube<float4> g_tProbeCube < Attribute("ProbeCube"); SrgbRead(false); >;
    SamplerState g_sProbePoint < Filter(POINT); AddressU(CLAMP); AddressV(CLAMP); AddressW(CLAMP); >;
    RWStructuredBuffer<float4> g_ProbeOutput < Attribute("ProbeOutput"); >;
    uint ProbeGrid < Attribute("ProbeGrid"); >;
    uint ProbeMipCount < Attribute("ProbeMipCount"); >;
    uint ProbeForceBaseMip < Attribute("ProbeForceBaseMip"); >;
    uint ProbeDispatchWidth < Attribute("ProbeDispatchWidth"); >;
    float3 Direction(uint face,float2 p) {
        if(face==0)return float3(1,-p.y,-p.x);if(face==1)return float3(-1,-p.y,p.x);
        if(face==2)return float3(p.x,1,p.y);if(face==3)return float3(p.x,-1,-p.y);
        if(face==4)return float3(p.x,-p.y,1);return float3(-p.x,-p.y,-1);
    }
    [numthreads(64,1,1)] void MainCs(uint3 tid:SV_DispatchThreadID) {
        uint id=tid.x+tid.y*ProbeDispatchWidth,local=id,mip=0,grid=ProbeGrid;
        for(;mip<ProbeMipCount;mip++){grid=max(1u,ProbeGrid>>mip);uint count=6u*grid*grid;if(local<count)break;local-=count;}
        if(mip>=ProbeMipCount)return;
        uint width,height,levels;g_tProbeCube.GetDimensions(0,width,height,levels);
        if(id==0)g_ProbeOutput[0]=float4(width,height,levels,ProbeGrid);
        if(mip>=levels){if(local==0)g_ProbeOutput[1+mip]=float4(0,0,levels,mip);g_ProbeOutput[1+ProbeMipCount+id]=-1.0f;return;}
        g_tProbeCube.GetDimensions(mip,width,height,levels);
        if(local==0)g_ProbeOutput[1+mip]=float4(width,height,levels,mip);
        uint face=local/(grid*grid),pixel=local%(grid*grid);uint2 p=uint2(pixel%grid,pixel/grid);
        uint2 texel=min(uint2(width-1,height-1),(p*uint2(width,height))/grid);
        float2 uv=(float2(texel)+.5f)/float2(width,height);float3 direction=Direction(face,uv*2-1);
        float lod=ProbeForceBaseMip!=0?0.0f:float(mip);
        g_ProbeOutput[1+ProbeMipCount+id]=g_tProbeCube.SampleLevel(g_sProbePoint,direction,lod);
    }
}
""";
}


[TestClass]
internal sealed class FieldHdrOnlyTests
{
    public static string CheckoutRoot { get => FieldHdrAndExposureTests.CheckoutRoot; set => FieldHdrAndExposureTests.CheckoutRoot = value; }
    public static string OutputDirectory { get => FieldHdrAndExposureTests.OutputDirectory; set => FieldHdrAndExposureTests.OutputDirectory = value; }
    public static List<object> Measurements => FieldHdrAndExposureTests.Measurements;
    [TestMethod]
    public Task ActualFieldHdrWithIndependentComputeControls() => new FieldHdrAndExposureTests().A_ComputeHdrControlsThenActualFieldResidencyAndEveryMip();
}
