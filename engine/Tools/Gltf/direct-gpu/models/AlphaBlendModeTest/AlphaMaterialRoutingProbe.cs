using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Actual instance/override classification acceptance; native object flags are not pixel proof.</summary>
internal static class AlphaMaterialRoutingProbe
{
	public sealed record Result(string Name,bool Passed,string Error,bool? Opaque,bool? Translucent);
	public const int ExpectedCases=8;
	public static Result[] Run(SceneWorld world,Material compute,Material opaque,Material blend)
	{
		if(!Application.IsEditor||Game.IsPlaying||Graphics.IsActive)throw new InvalidOperationException("Requires coordinator idle editor boundary.");
		ThreadSafe.AssertIsMainThread();
		if(opaque is not{IsValid:true,IsError:false}||blend is not{IsValid:true,IsError:false}||opaque.Flags.IsTranslucent||!blend.Flags.IsTranslucent)throw new InvalidOperationException("Actual native opaque/blend material flags must be observed before instance tests.");
		var fixture=AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first");
		using var document=GltfReader.Read(fixture.Bytes);
		using var asset=GltfGpuAsset.Create(document,compute,_=>blend);
		using var a=asset.CreateInstance(world);using var b=asset.CreateInstance(world);
		var stable=a.SceneObject;var results=new List<Result>();
		Case("resolved_native_blend_enters_translucent_only",()=>Check(a,false,true));
		Case("whole_native_opaque_override_removes_translucency",()=>{a.SetMaterialOverride(opaque);Check(a,true,false);});
		Case("one_blend_slot_on_opaque_override_is_mixed",()=>{a.SetMaterialOverride(opaque);a.SetMaterialOverride(0,blend);Check(a,true,true);});
		Case("clear_restores_native_source_blend_classification",()=>{a.SetMaterialOverride(opaque);a.ClearMaterialOverrides();Check(a,false,true);});
		Case("instance_override_does_not_change_other_classification",()=>{a.SetMaterialOverride(opaque);Check(a,true,false);Check(b,false,true);});
		Case("classification_changes_preserve_primary_sceneobject",()=>{a.SetMaterialOverride(opaque);a.SetMaterialOverride(blend);if(!ReferenceEquals(stable,a.SceneObject))throw new InvalidOperationException("Opacity change replaced the primary scene object.");});
		var json=JsonNode.Parse(fixture.Bytes)!;foreach(var m in json["materials"]!.AsArray())m!["alphaMode"]="OPAQUE";
		using var opaqueDocument=GltfReader.Read(Encoding.UTF8.GetBytes(json.ToJsonString()));
		using var opaqueAsset=GltfGpuAsset.Create(opaqueDocument,compute,_=>opaque);using var c=opaqueAsset.CreateInstance(world);
		Case("native_blend_override_of_authored_opaque_reclassifies",()=>{c.SetMaterialOverride(blend);Check(c,false,true);},c);
		Case("clear_native_blend_override_restores_authored_opaque",()=>{c.SetMaterialOverride(blend);c.ClearMaterialOverrides();Check(c,true,false);},c);
		if(results.Count!=ExpectedCases)throw new InvalidOperationException("Classification acceptance inventory changed.");return results.ToArray();
		void Case(string name,Action action,GltfGpuInstance selected=null)
		{
			selected??=a;string failure=null;try{action();}catch(Exception e){failure=e.ToString();}
			results.Add(new(name,failure is null,failure,selected.SceneObject.Flags.IsOpaque,selected.SceneObject.Flags.IsTranslucent));
		}
	}
	private static void Check(GltfGpuInstance instance,bool opaque,bool translucent)
	{
		if(instance.SceneObject.Flags.IsOpaque!=opaque||instance.SceneObject.Flags.IsTranslucent!=translucent)throw new InvalidOperationException($"Expected effective material classification opaque={opaque}, translucent={translucent}; actual opaque={instance.SceneObject.Flags.IsOpaque}, translucent={instance.SceneObject.Flags.IsTranslucent}.");
	}
}
