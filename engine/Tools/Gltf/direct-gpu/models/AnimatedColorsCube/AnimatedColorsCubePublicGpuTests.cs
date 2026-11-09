using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using V3 = System.Numerics.Vector3;
using Q = System.Numerics.Quaternion;
using M = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Source and derived compiled public path; literal authored poses and per-instance material controls.</summary>
[TestClass]
public sealed class AnimatedColorsCubePublicGpuTests
{
    public static string CheckoutRoot { get; set; }
    public static List<object> Measurements { get; } = new();
    public static void Configure(string root) { CheckoutRoot = root; Measurements.Clear(); }
    [TestMethod] public Task Source() => Run("glTF/source");
    [TestMethod] public Task UpstreamBinary() => Run("glTF-Binary/source");
    [TestMethod] public Task CanonicalCompiled() => Run("glTF/canonical-compiled");
    static void Require(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
    static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    static float Encode(float x) => x <= .0031308f ? 12.92f*x : 1.055f*MathF.Pow(x,1f/2.4f)-.055f;
    async Task Run(string form)
    {
        await MainThread.Wait();
        var source = Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/AnimatedColorsCube/glTF");
        var json = File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.gltf")); var bin = File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.bin"));
        Require(Hash(json)=="5639d1aa3489a4ebfeda2045876b671b94e0bb046c66e069c959b62939e8abca" && Hash(bin)=="c5980c793c830b16b60b8eac80765b26dd2f868aa13b48ee76fbab8c42dd5b88","pinned inputs changed");
        var outdir=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/AnimatedColorsCube/public-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(outdir);
        using var capture=new GltfGpuTriangleGateTests.NativeCapture(outdir,new global::Vector3(700,0,140),new global::Vector3(0,0,100),650);
        var temp=Path.Combine(Path.GetTempPath(),"colors_cube_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        var (extension,package)=form switch
        {
            "glTF/source" => (".gltf",json),
            "glTF-Binary/source" => (".glb",File.ReadAllBytes(Path.Combine(source,"../glTF-Binary/AnimatedColorsCube.glb"))),
            "glTF/canonical-compiled" => (".glb_c",GltfCompiler.Compile(json,_=>bin)),
            _ => throw new ArgumentOutOfRangeException(nameof(form))
        };
        if(extension==".glb") Require(Hash(package)=="995bb9db5bd011b64bc9fc2235c60e5455e2931e509db5e715b4d20afc5ba71e","pinned GLB changed");
        var name="colors_"+Guid.NewGuid().ToString("N")+extension;
        File.WriteAllBytes(Path.Combine(temp,name),package);
        if(extension==".gltf") File.WriteAllBytes(Path.Combine(temp,"AnimatedColorsCube.bin"),bin);
        BaseFileSystem aggregate=null; LocalFileSystem mount=null; Model model=null; SkinnedModelRenderer a=null,b=null; Material literal=null; var observations=new List<object>(); string error=null;
        try
        {
            aggregate=Sandbox.FileSystem.Mounted; mount=new LocalFileSystem(temp); aggregate.Mount(mount);
            var empty=await capture.Take("empty_before"); Require(empty.Coverage==0,"empty control failed");
            model=Model.Load(name); Require(model is {IsValid:true,IsError:false} && model.native.IsNull && model.GltfResource is not null && model.AnimationCount==1,"public managed load failed");
            var cached=model.Materials[0]; var cachedTint=cached.GetVector4("g_vColorTint");
            using(capture.Scene.Push())
            {
                a=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false); b=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false);
                foreach(var actor in new[]{a,b}) { actor.UseAnimGraph=false; actor.PlayAnimationsInEditorScene=true; actor.PlaybackRate=0; actor.Model=model; actor.Sequence.Name="Cube Animation"; actor.Sequence.Looping=false; actor.Enabled=true; }
                a.WorldPosition=new global::Vector3(0,-190,0); b.WorldPosition=new global::Vector3(0,190,0); capture.Scene.EditorTick(0,0);
            }
            var ia=a.GltfInstance; var ib=b.GltfInstance; Require(ia is not null && ib is not null && !ReferenceEquals(ia,ib),"instance isolation failed");
            b.Sequence.Time=1.25f;
            var samples=new[]{(time:0f,x:-3f,y:0f,w:1f,color:new V3(.8f,.02f,.02f)),(time:.25f,x:-1.5f,y:.3826834559f,w:.9238795638f,color:new V3(.41f,.41f,.02f)),(time:.258333333f,x:-1.425110996f,y:.400722222f,w:.916199597f,color:new V3(.3905288726f,.4294711202f,.02f)),(time:1.25f,x:1.5f,y:.9238795638f,w:.3826833665f,color:new V3(.02f,.41f,.41f)),(time:1.5f,x:3f,y:-1f,w:4.3711388e-8f,color:new V3(.02f,.02f,.8f)),(time:2.75f,x:-3f,y:0f,w:1f,color:new V3(.8f,.02f,.02f))};
            Material retainedA=null, retainedB=null;
            foreach(var s in samples)
            {
                a.Sequence.Time=s.time; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0);
                var frame=await capture.Take("sample_"+s.time.ToString("0.00",System.Globalization.CultureInfo.InvariantCulture)); await MainThread.Wait();
                Require(frame.Coverage>64 && ia.SubmittedRevision==ia.PoseRevision && ib.SubmittedRevision==ib.PoseRevision && ia.CallbackErrors.Length==0 && ib.CallbackErrors.Length==0,"current phases not rendered");
                Observe(ia,s.x,s.y,s.w,s.color,-190);
                var activeA=ia.Readback().Single(r=>r.Node==0 && r.Mesh==0).SubmittedMaterial;
                var activeB=ib.Readback().Single(r=>r.Node==0 && r.Mesh==0).SubmittedMaterial;
                retainedA ??= activeA; retainedB ??= activeB;
                Require(ReferenceEquals(activeA,retainedA) && ReferenceEquals(activeB,retainedB) && !ReferenceEquals(retainedA,retainedB) && !ReferenceEquals(retainedA,cached),"animated pose recreated/shared instance materials");
                if (s.time == .25f) { var rejected=false; try { Observe(ia,s.x,s.y,s.w,new V3(.02f,.41f,.41f),-190); } catch (InvalidOperationException) { rejected=true; } Require(rejected,"wrong-colour negative accepted"); }
                Observe(ib,1.5f,.9238795638f,.3826833665f,new V3(.02f,.41f,.41f),190);
                Require(cached.GetVector4("g_vColorTint")==cachedTint,"shared cached material mutated");
                observations.Add(new {s.time, secondTime=b.Sequence.Time,frame.Path,frame.Sha256,frame.Coverage, ia.PoseRevision, ia.SubmittedRevision});
            }
            var copiesA=ia.AnimatedMaterialCopyCount; var copiesB=ib.AnimatedMaterialCopyCount;
            for (var i=0;i<120;i++) { a.Sequence.Time=(i%120)/60f; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); }
            Require(ia.AnimatedMaterialCopyCount==copiesA && ib.AnimatedMaterialCopyCount==copiesB,"animated poses grew the native material-copy count");
            // A failing private material write must not publish the requested pose.
            a.Sequence.Time=.25f; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); await capture.Take("before_injected_failure");
            var before=ia.Readback().Single(r=>r.Node==0 && r.Mesh==0); var oldBytes=(byte[])before.Bytes.Clone(); var oldTint=before.SubmittedMaterial.GetVector4("g_vColorTint"); var oldRevision=ia.PoseRevision; var oldSubmitted=ia.SubmittedRevision; var oldPending=ia.AnimatedMaterialRetiredPending;
            ia.AnimatedMaterialWriter=(m,k,v)=>{m.Set(k,v);throw new InvalidOperationException("injected material write failure");};
            var failed=false;try {ia.SetPose(model.GltfResource.Asset.Document.EvaluatePose(0,1.25f));}catch(InvalidOperationException){failed=true;}finally{ia.AnimatedMaterialWriter=null;}
            Require(failed && ia.PoseRevision==oldRevision && ia.SubmittedRevision==oldSubmitted && ia.AnimatedMaterialRetiredPending==oldPending && before.SubmittedMaterial.GetVector4("g_vColorTint")==oldTint,"failed material update published pose or colour");
            var afterFailure=ia.Readback().Single(r=>r.Node==0 && r.Mesh==0);Require(ReferenceEquals(before.SubmittedMaterial,afterFailure.SubmittedMaterial)&&oldBytes.SequenceEqual(afterFailure.Bytes),"failure changed binding or GPU position");
            var animated=await capture.Take("after_injected_failure");Observe(ia,-1.5f,.3826834559f,.9238795638f,new V3(.41f,.41f,.02f),-190);
            // An independent literal colour on the same geometry and lighting must match the animated pixels.
            literal=cached.CreateCopy();
            var key=TintKey(literal);
            literal.Set(key,ExpectedTint(key,new V3(.41f,.41f,.02f)));
            a.Materials.SetOverride(0,literal);
            var matching=await capture.Take("literal_matching_color");
            literal.Set(key,ExpectedTint(key,new V3(.02f,.41f,.41f)));
            var mismatching=await capture.Take("literal_wrong_color");
            var matchingDiff=ChangedPixels(animated,matching);
            var mismatchingDiff=ChangedPixels(animated,mismatching);
            Require(matchingDiff<16 && mismatchingDiff>64 && mismatchingDiff>matchingDiff*4,"raster colour did not agree with the literal phase or distinguish the wrong phase");
            observations.Add(new { matchingDiff, mismatchingDiff, animated.Sha256, positiveHash=matching.Sha256, negativeHash=mismatching.Sha256 });
            a.Materials.SetOverride(0,null);
            // Override is explicit caller state, and must mask only that instance's animated source material.
            a.Sequence.Time=.25f; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0);
            var custom=capture.Material; var customTint=custom.GetVector4("g_vColorTint");
            a.MaterialOverride=custom; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); await capture.Take("explicit_override");
            var overridden=ia.Readback().Single(r=>r.Node==0 && r.Mesh==0);
            Require(overridden.SubmittedMaterialOverride && ReferenceEquals(overridden.SubmittedMaterial,custom) && custom.GetVector4("g_vColorTint")==customTint,"explicit override lost precedence or was mutated");
            Observe(ib,1.5f,.9238795638f,.3826833665f,new V3(.02f,.41f,.41f),190);
            a.MaterialOverride=null; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); await capture.Take("override_cleared"); Observe(ia,-1.5f,.3826834559f,.9238795638f,new V3(.41f,.41f,.02f),-190);
            a.Sequence.Name=""; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); await capture.Take("sequence_reset");
            var reset=ia.Readback().Single(r=>r.Node==0 && r.Mesh==0); Require(ReferenceEquals(reset.SubmittedMaterial,cached) && cached.GetVector4("g_vColorTint")==cachedTint,"reset did not restore immutable authored material");
            Observe(ib,1.5f,.9238795638f,.3826833665f,new V3(.02f,.41f,.41f),190);
            a.Sequence.Name="Cube Animation"; a.Sequence.Time=.25f; using(capture.Scene.Push()) capture.Scene.EditorTick(0,0); await capture.Take("sequence_restored"); Observe(ia,-1.5f,.3826834559f,.9238795638f,new V3(.41f,.41f,.02f),-190);
            using(capture.Scene.Push()) { a.GameObject.Destroy(); a=null; b.GameObject.Destroy(); b=null; capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); }
            int callsA=ia.DrawCalls,callsB=ib.DrawCalls; var deleted=await capture.Take("empty_after"); Require(deleted.Coverage==0 && ia.DrawCalls==callsA && ib.DrawCalls==callsB && model.IsValid && cached.GetVector4("g_vColorTint")==cachedTint && !retainedB.IsValid,"cleanup/retained material failed");
        }
        catch(Exception e) { error=e.ToString(); }
        finally { await MainThread.Wait(); try { using(capture.Scene.Push()) { a?.GameObject.Destroy(); b?.GameObject.Destroy(); capture.Scene.EditorTick(0,0); capture.Scene.ProcessDeletes(); } literal?.Destroy(); model?.Destroy(); if(mount is not null) { aggregate.UnMount(mount); mount.Dispose(); } Directory.Delete(temp,true); } catch(Exception e) { error+="\ncleanup: "+e; } }
        Measurements.Add(new {form,inputHash=Hash(package),outdir,observations,error,scope="two public managed instances, independent GPU positions, literal raster colour controls, override/reset and cleanup; deterministic seeks"}); Assert.IsNull(error,error);
    }
    [TestMethod] public async Task VariantRetirementCycles()
    {
        await MainThread.Wait();
        var source=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/AnimatedColorsCube/glTF");
        var json=File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.gltf")); var bin=File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.bin"));
        Require(Hash(json)=="5639d1aa3489a4ebfeda2045876b671b94e0bb046c66e069c959b62939e8abca" && Hash(bin)=="c5980c793c830b16b60b8eac80765b26dd2f868aa13b48ee76fbab8c42dd5b88","pinned fixture changed");
        // Test-only synthetic material variant; original corpus files stay unchanged.
        var doc=JsonNode.Parse(json).AsObject(); doc["extensionsUsed"].AsArray().Add("KHR_materials_variants");
        doc["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"variants\":[{\"name\":\"green\"}]}}");
        doc["meshes"][0]["primitives"][0]["extensions"]=JsonNode.Parse("{\"KHR_materials_variants\":{\"mappings\":[{\"material\":2,\"variants\":[0]}]}}");
        var temp=Path.Combine(Path.GetTempPath(),"colors_variant_"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp); var name="variant_"+Guid.NewGuid().ToString("N")+".gltf";
        File.WriteAllText(Path.Combine(temp,name),doc.ToJsonString()); File.WriteAllBytes(Path.Combine(temp,"AnimatedColorsCube.bin"),bin);
        var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/AnimatedColorsCube/variants-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(output);
        using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(500,0,100),new global::Vector3(0,0,100),450);
        BaseFileSystem fs=null;LocalFileSystem mount=null;Model model=null;SkinnedModelRenderer renderer=null;string error=null;
        try
        {
            fs=Sandbox.FileSystem.Mounted;mount=new LocalFileSystem(temp);fs.Mount(mount);model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull,"fixture public load failed");
            using(capture.Scene.Push()) {renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false);renderer.UseAnimGraph=false;renderer.PlayAnimationsInEditorScene=true;renderer.PlaybackRate=0;renderer.Model=model;renderer.Sequence.Name="Cube Animation";renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
            renderer.Sequence.Time=.25f;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);await capture.Take("initial");var instance=renderer.GltfInstance;
            for(var i=0;i<32;i++)
            {
                var retired=instance.Readback().Single(r=>r.Node==0&&r.Mesh==0).SubmittedMaterial;
                renderer.MaterialVariant=0;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);await capture.Take("variant_"+i);var variant=instance.Readback().Single(r=>r.Node==0&&r.Mesh==0);
                Require(variant.SubmittedMaterialIndex==2 && !retired.IsValid && instance.AnimatedMaterialRetiredPending==0,"variant binding or previous copy retirement failed");
                RequireTint(variant.SubmittedMaterial,new V3(.02f,.8f,.02f));
                renderer.MaterialVariant=null;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);await capture.Take("default_"+i);Observe(instance,-1.5f,.3826834559f,.9238795638f,new V3(.41f,.41f,.02f),0);
            }
            await capture.WaitFrames();Require(instance.AnimatedMaterialRetiredPending==0,"retired copies did not drain after frame completion");
            using(capture.Scene.Push()){renderer.GameObject.Destroy();renderer=null;capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}var after=await capture.Take("deleted");Require(after.Coverage==0&&instance.AnimatedMaterialRetiredPending==0,"variant cleanup did not converge");
        }
        catch(Exception e){error=e.ToString();}
        finally{await MainThread.Wait();try{if(renderer is not null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();if(mount is not null){fs.UnMount(mount);mount.Dispose();}Directory.Delete(temp,true);}catch(Exception e){error+="\ncleanup: "+e;}}
        Measurements.Add(new{output,error,scope="synthetic optional material variant on unchanged pinned sample; 32 switch cycles and frame-end copy retirement"});Assert.IsNull(error,error);
    }
    [TestMethod] public async Task OpaqueAnimatedAlphaIgnored()
    {
        await MainThread.Wait();
        var source=Path.Combine(CheckoutRoot,"Temp/gltf-reference/glTF-Sample-Assets/Models/AnimatedColorsCube/glTF");
        var json=File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.gltf"));var bin=File.ReadAllBytes(Path.Combine(source,"AnimatedColorsCube.bin"));
        Require(Hash(json)=="5639d1aa3489a4ebfeda2045876b671b94e0bb046c66e069c959b62939e8abca" && Hash(bin)=="c5980c793c830b16b60b8eac80765b26dd2f868aa13b48ee76fbab8c42dd5b88","pinned fixture changed");
        var output=Path.Combine(CheckoutRoot,"Temp/gltf-validation/direct-gpu/AnimatedColorsCube/opaque-alpha-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(output);
        using var capture=new GltfGpuTriangleGateTests.NativeCapture(output,new global::Vector3(500,0,100),new global::Vector3(0,0,100),450);
        var temp=Path.Combine(Path.GetTempPath(),"colors_alpha_"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(temp);BaseFileSystem fs=null;LocalFileSystem mount=null;Model model=null;SkinnedModelRenderer renderer=null;string error=null;var frames=new List<object>();
        try
        {
            fs=Sandbox.FileSystem.Mounted;mount=new LocalFileSystem(temp);fs.Mount(mount);
            foreach(var projected in new[]{false,true})
            {
                GltfGpuTriangleGateTests.NativeCapture.Frame reference=null;
                foreach(var zero in new[]{false,true})
                {
                    var doc=JsonNode.Parse(json).AsObject();var name="alpha_"+Guid.NewGuid().ToString("N")+".gltf";var dep=name+".bin";doc["buffers"][0]["uri"]=dep;
                    // Nonzero emissive factor selects the projected source-material route; both runs use the same route/lighting.
                    if(projected)doc["materials"][0]["emissiveFactor"]=JsonNode.Parse("[0.001,0,0]");
                    var modified=(byte[])bin.Clone();if(zero)for(int key=0;key<151;key++)Array.Clear(modified,8772+key*16+12,4);
                    File.WriteAllText(Path.Combine(temp,name),doc.ToJsonString());File.WriteAllBytes(Path.Combine(temp,dep),modified);
                    model=Model.Load(name);Require(model is {IsValid:true,IsError:false} && model.native.IsNull,"opaque alpha synthetic load failed");
                    using(capture.Scene.Push()){renderer=capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>(false);renderer.UseAnimGraph=false;renderer.PlayAnimationsInEditorScene=true;renderer.PlaybackRate=0;renderer.Model=model;renderer.Sequence.Name="Cube Animation";renderer.Enabled=true;capture.Scene.EditorTick(0,0);}
                    renderer.Sequence.Time=.25f;using(capture.Scene.Push())capture.Scene.EditorTick(0,0);var frame=await capture.Take((projected?"projected":"stock")+(zero?"_alpha0":"_alpha1"));
                    var row=renderer.GltfInstance.Readback().Single(x=>x.Node==0&&x.Mesh==0);var material=row.SubmittedMaterial;
                    Require(frame.Coverage>64 && (projected?material.HasParameter("g_vGltfBaseColor"):material.HasParameter("g_vColorTint")),"expected material route did not render");
                    if(!zero)reference=frame;else {var changed=frame.Pixels.Zip(reference.Pixels,(a,b)=>Math.Abs(a.r-b.r)+Math.Abs(a.g-b.g)+Math.Abs(a.b-b.b)>.01f).Count(x=>x);Require(changed<16 && frame.Coverage==reference.Coverage,"OPAQUE animated alpha changed pixels/coverage");}
                    frames.Add(new{projected,zero,frame.Path,frame.Sha256,frame.Coverage});
                    using(capture.Scene.Push()){renderer.GameObject.Destroy();renderer=null;capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model.Destroy();model=null;await capture.WaitFrames();
                }
            }
            Require((await capture.Take("deleted")).Coverage==0,"opaque alpha cleanup failed");
        }
        catch(Exception e){error=e.ToString();}
        finally{await MainThread.Wait();try{if(renderer is not null){using var scope=capture.Scene.Push();renderer.GameObject.Destroy();capture.Scene.EditorTick(0,0);capture.Scene.ProcessDeletes();}model?.Destroy();if(mount is not null){fs.UnMount(mount);mount.Dispose();}Directory.Delete(temp,true);}catch(Exception e){error+="\ncleanup: "+e;}}
        Measurements.Add(new{output,frames,error,scope="synthetic opaque pointer alpha=0 versus alpha=1, stock and projected source routes; unchanged corpus files"});Assert.IsNull(error,error);
    }
    static void Observe(GltfGpuInstance instance,float x,float y,float w,V3 color,float offset)
    {
        var row=instance.Readback().Single(r=>r.Node==0 && r.Mesh==0); var field=row.Fields.Single(f=>f.Semantic=="POSITION"&&f.SemanticIndex==0); var index=Array.FindIndex(row.SourceVertices,v=>v==0); Require(index>=0 && row.IndexDataOrigin=="gpu-readback","missing actual vertex");
        var bytes=row.Bytes.AsSpan(index*row.VertexStride+field.Offset,12); var observed=new V3(BinaryPrimitives.ReadSingleLittleEndian(bytes),BinaryPrimitives.ReadSingleLittleEndian(bytes[4..]),BinaryPrimitives.ReadSingleLittleEndian(bytes[8..]));
        var world=V3.Transform(new V3(1,1,-1),M.CreateFromQuaternion(Q.Normalize(new Q(0,y,0,w)))*M.CreateTranslation(x,3,0)); var expected=new V3(world.Z,world.X,world.Y)/.0254f+new V3(0,offset,0);
        Require(V3.Distance(observed,expected)<.002f,$"wrong GPU position {observed} expected {expected}");
        RequireTint(row.SubmittedMaterial,color);
    }

    static string TintKey(Material material) => material.HasParameter("g_vGltfBaseColor") ? "g_vGltfBaseColor" : "g_vColorTint";
    static global::Vector4 ExpectedTint(string key,V3 color) => key=="g_vGltfBaseColor"
        ? new global::Vector4(color.X,color.Y,color.Z,1) : new global::Vector4(Encode(color.X),Encode(color.Y),Encode(color.Z),1);
    static void RequireTint(Material material,V3 color)
    {
        var key=TintKey(material);
        Require(material.HasParameter(key),"submitted material lacks a tint parameter");
        var tint=material.GetVector4(key);
        var expected=ExpectedTint(key,color);
        Require(Math.Abs(tint.x-expected.x)<2e-5 && Math.Abs(tint.y-expected.y)<2e-5 && Math.Abs(tint.z-expected.z)<2e-5,"wrong submitted material tint");
    }
    static int ChangedPixels(GltfGpuTriangleGateTests.NativeCapture.Frame a,GltfGpuTriangleGateTests.NativeCapture.Frame b)
        => a.Pixels.Zip(b.Pixels,(x,y)=>Math.Abs(x.r-y.r)+Math.Abs(x.g-y.g)+Math.Abs(x.b-y.b)>.01f).Count(x=>x);
}
