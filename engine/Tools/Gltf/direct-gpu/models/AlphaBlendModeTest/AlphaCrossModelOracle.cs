using System;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Derived cross-model nonintersecting plane fixture and independent over oracle; no rendering claim.</summary>
public static class AlphaCrossModelOracle
{
	public sealed record Source(string Name,byte[] Bytes);
	public static readonly double[] Correct=[.125,.5,.25];
	public static readonly double[] WholeModelABeforeB=[.125,.25,.5];
	public static readonly double[] WholeModelBBeforeA=[.25,.5,.125];
	public static Source[] Sources()
	{
		var source=AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first");
		var a=JsonNode.Parse(source.Bytes)!.AsObject();
		// Existing literal planes: slot0/sourceZ .1 (near); slot1/sourceZ0 (far).
		Set(a,0,0,1,0);Set(a,1,1,0,0);
		var b=a.DeepClone().AsObject();b["scenes"]![0]!["nodes"]=new JsonArray(0);b["nodes"]![0]!["translation"]=new JsonArray(0,0,-.05);
		Set(b,0,0,0,1);
		return[new("derived_model_A_far_red_near_green",Encoding.UTF8.GetBytes(a.ToJsonString())),new("derived_model_B_middle_blue",Encoding.UTF8.GetBytes(b.ToJsonString()))];
	}
	private static void Set(JsonObject root,int material,float r,float g,float b)
	{
		var m=root["materials"]![material]!;m["pbrMetallicRoughness"]!["baseColorFactor"]=new JsonArray(0,0,0,.5);m["emissiveFactor"]=new JsonArray(r,g,b);
		// No direct/environment light in the numeric capture. Emissive controls
		// supply unit linear RGB independently of lit base-color response.
	}
	public static void RequireCorrect(double[] actual,double tolerance)
	{
		if(actual is not{Length:3}||!double.IsFinite(tolerance)||tolerance<0||tolerance>=.0625)throw new InvalidOperationException("Use actual linear RGB and a reviewed tolerance below either whole-model-order error.");
		for(var c=0;c<3;c++)if(!double.IsFinite(actual[c])||Math.Abs(actual[c]-Correct[c])>tolerance)throw new InvalidOperationException("Cross-model interleaved nonintersecting BLEND planes do not produce far-red then middle-blue then near-green over composition.");
	}
}
