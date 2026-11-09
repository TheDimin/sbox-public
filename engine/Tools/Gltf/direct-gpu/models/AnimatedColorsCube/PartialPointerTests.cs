using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>Independent fixed colour values from the pinned JSON/BIN, plus capability controls.</summary>
public static class PartialPointerTests
{
    public static int Run(string directory)
    {
        var source=File.ReadAllBytes(Path.Combine(directory,"AnimatedColorsCube.gltf"));
        var bin=File.ReadAllBytes(Path.Combine(directory,"AnimatedColorsCube.bin"));
        var supported=new HashSet<string>(StringComparer.Ordinal);
        using var partial=GltfReader.ReadWithPartialMaterialColorAnimation(source,_=>bin,supportedExtensions:supported);
        using var fallback=GltfReader.Read(source,_=>bin,supportedExtensions:supported);
        var authored=partial.ReadMaterial(0).BaseColorFactor;
        var points=new[]{(0f,new Vector4(.8f,.02f,.02f,1)),(.25f,new Vector4(.41f,.41f,.02f,1)),
            (.258333333f,new Vector4(.3905288726f,.4294711202f,.02f,1)),(.5f,new Vector4(.02f,.8f,.02f,1)),
            (1.25f,new Vector4(.02f,.41f,.41f,1)),(1.5f,new Vector4(.02f,.02f,.8f,1)),(2.75f,new Vector4(.8f,.02f,.02f,1))};
        var first=partial.EvaluatePose(0,.25f); var second=partial.EvaluatePose(0,1.25f);
        foreach(var (time,expected) in points)
        {
            var pose=partial.EvaluatePose(0,time);
            Require(pose.TryGetAnimatedBaseColorFactor(0,out var observed) && Vector4.Distance(observed,expected)<3e-6f,$"wrong colour at {time}: {observed}");
            Require(!pose.TryGetAnimatedBaseColorFactor(1,out _),"unrelated material changed");
        }
        Require(first.TryGetAnimatedBaseColorFactor(0,out var a) && second.TryGetAnimatedBaseColorFactor(0,out var b) && Vector4.Distance(a,b)>.5f,"independent phases or wrong-colour control failed");
        Require(!partial.EvaluatePose().TryGetAnimatedBaseColorFactor(0,out _) && !fallback.EvaluatePose(0,.25f).TryGetAnimatedBaseColorFactor(0,out _),"rest/disabled optional fallback changed");
        Require(partial.ReadMaterial(0).BaseColorFactor==authored && partial.UnsupportedOptionalExtensions.Contains("KHR_animation_pointer"),"immutable source or partial capability reporting failed");
        var required=Encoding.UTF8.GetString(source).Replace("\"extensionsUsed\"","\"extensionsRequired\":[\"KHR_animation_pointer\"],\"extensionsUsed\"");
        var rejected=false;try { using var invalid=GltfReader.ReadWithPartialMaterialColorAnimation(Encoding.UTF8.GetBytes(required),_=>bin,supportedExtensions:supported); } catch(GltfException) { rejected=true; }
        Require(rejected,"partial implementation accepted required KHR extension");
        return points.Length*2+4;
    }
    static void Require(bool ok,string message) { if(!ok) throw new InvalidOperationException(message); }
}
