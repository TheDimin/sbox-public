using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Synthetic independent extension controls; no native renderer claim.</summary>
public static class PointerSpecificationTests
{
    public static int Run()
    {
        // Two keys and a signed out-tangent of 8 yield red=1.5 at the midpoint;
        // the tangent is not itself a factor and overshoot is not silently clamped.
        using ( var d=Read( "CUBICSPLINE", 5126, false, [0,0,0,0, .5f,.25f,.25f,1, 8,0,0,0, 0,0,0,0, .5f,.25f,.25f,1, 0,0,0,0] ) )
        {
            var p=d.EvaluatePose(0,.5f); Check(p.TryGetAnimatedBaseColorFactor(0,out var c) && Math.Abs(c.X-1.5f)<1e-6f,"cubic tangent or overshoot");
            Check(Math.Abs(p.GetWorldTransform(0).M41-1)<1e-6f,"core translation did not coexist with pointer");
        }
        using (var d=Read("LINEAR",5121,true,[0,128,255,255,255,128,0,255]))
        { var p=d.EvaluatePose(0,.5f); Check(p.TryGetAnimatedBaseColorFactor(0,out var c)&&Vector4.Distance(c,new Vector4(.5f,128f/255f,.5f,1))<1e-6f,"normalized byte conversion"); }
        using (var d=Read("STEP",5121,false,[0,1,0,1,1,0,1,1]))
        { var p=d.EvaluatePose(0,.5f); Check(p.TryGetAnimatedBaseColorFactor(0,out var c)&&c==new Vector4(0,1,0,1),"non-normalized integer STEP conversion"); }
        using (var d=Read("LINEAR",5126,false,[0,0,0,1,1,1,1,1],duplicate:true))
        { var bad=false;try {d.EvaluatePose(0,.5f);}catch(GltfException){bad=true;}Check(bad,"duplicate target accepted"); }
        using (var d=Read("LINEAR",5126,false,[0,0,0,1,1,1,1,1],omitPbr:true))
        { var bad=false;try {d.EvaluatePose(0,.5f);}catch(GltfException){bad=true;}Check(bad,"missing enclosing object accepted"); }
        using (var d=Read("LINEAR",5126,false,[0,0,0,1,1,1,1,1],unknown:true))
        { Check(!d.EvaluatePose(0,.5f).TryGetAnimatedBaseColorFactor(0,out _),"unsupported optional target must fallback"); }
        using (var d=Read("LINEAR",5126,false,[0,0,0,1,2,1,1,1]))
        { var bad=false;try {d.EvaluatePose(0,0);}catch(GltfException){bad=true;}Check(bad,"invalid future key accepted"); }
        return 8;
    }
    static GltfDocument Read(string interpolation,int component,bool normalized,float[] colors,bool duplicate=false,bool omitPbr=false,bool unknown=false)
    {
        using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);
        writer.Write(0f);writer.Write(1f);foreach(var f in new float[]{0,0,0,2,0,0})writer.Write(f);
        int colorOffset=(int)stream.Position;
        foreach(var x in colors) { if(component==5126)writer.Write(x); else writer.Write(checked((byte)x)); }
        writer.Flush();var bytes=stream.ToArray();
        var pointer=unknown?"/materials/0/pbrMetallicRoughness/roughnessFactor":"/materials/0/pbrMetallicRoughness/baseColorFactor";
        var target=new {path="pointer",extensions=new Dictionary<string,object>{{"KHR_animation_pointer",new {pointer}}}};
        var channels=new List<object>{new {sampler=0,target=new {node=0,path="translation"}},new {sampler=1,target}};
        if(duplicate)channels.Add(new {sampler=1,target});
        object material=omitPbr?new {}:new {pbrMetallicRoughness=new {}};
        var json=JsonSerializer.SerializeToUtf8Bytes(new {asset=new {version="2.0"},extensionsUsed=new[]{"KHR_animation_pointer"},buffers=new[]{new {uri="data.bin",byteLength=bytes.Length}},
            bufferViews=new[]{new {buffer=0,byteOffset=0,byteLength=8},new {buffer=0,byteOffset=8,byteLength=24},new {buffer=0,byteOffset=colorOffset,byteLength=bytes.Length-colorOffset}},
            accessors=new object[]{new {bufferView=0,componentType=5126,count=2,type="SCALAR",min=new[]{0},max=new[]{1}},new {bufferView=1,componentType=5126,count=2,type="VEC3"},new {bufferView=2,componentType=component,normalized,count=colors.Length/4,type="VEC4"}},
            nodes=new[]{new {}},materials=new[]{material},animations=new[]{new {channels,samplers=new object[]{new {input=0,output=1,interpolation="LINEAR"},new {input=0,output=2,interpolation}}}}});
        return GltfReader.ReadWithPartialMaterialColorAnimation(json,_=>bytes);
    }
    static void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
}
