using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Authored supplemental fixtures, explicitly not upstream AlphaBlendModeTest variants.</summary>
public static class AlphaControlFixtures
{
	public sealed record Fixture( string Name, byte[] Bytes, double[] ExpectedCenter, string CaptureSetup );
	private sealed record Plane( float Z, string Mode, float[] Factor, float Cutoff=.5f, bool DoubleSided=false, int ColorComponents=0, float VertexAlpha=1, bool AlphaTexture=false );
	public static Fixture[] All()
	{
		var result=new List<Fixture>();
		var near=new Plane(.1f,"BLEND",[1,0,0,.5f]);var far=new Plane(0,"BLEND",[0,1,0,.25f]);
		foreach(var reverse in new[]{false,true})result.Add(new("two_separated_blend_layers_"+(reverse?"far_first":"near_first"),Build(reverse?[far,near]:[near,far]),[.5,.125,.375],"Front source +Z orthographic camera; blue opaque background; use independently calibrated unlit/emission unit-RGB material for numeric over, not arbitrary lit radiance."));
		foreach(var test in new[]{("below",.25f,.5f), ("equal",.5f,.5f), ("above",.75f,.5f), ("zero_zero",0f,0f), ("cutoff_above_one",1f,1.25f)})
		{
			var accepted=test.Item2>=test.Item3;
			result.Add(new("mask_depth_"+test.Item1,Build([new(.1f,"MASK",[1,0,0,test.Item2],test.Item3),new(0,"OPAQUE",[0,0,1,1])]),accepted?[1,0,0]:[0,0,1],"Draw near mask before farther opaque blue; black clear; accepted samples must occlude blue and discarded samples must expose it."));
		}
		foreach(var components in new[]{3,4})result.Add(new("linear_texture_factor_color"+components,Build([new(0,"BLEND",[1,1,1,.375f],ColorComponents:components,VertexAlpha:.25f,AlphaTexture:true)]),components==3?[.1875]:[.046875],"Measure coverage with black/white background pair. Bilinear 2x1 alpha0/alpha1 texture at UV(.5,.5) yields exact .5 alpha. RGB COLOR_0 supplies alpha1; RGBA uses .25."));
		foreach(var sided in new[]{false,true}) result.Add(new("backface_"+(sided?"double":"single"),Build([new(0,"OPAQUE",[1,1,1,0],DoubleSided:sided)]),[],"Capture source +Z front and -Z back with corresponding front-facing directional light and matched view. Single-sided back must be empty; double-sided back must match independently reversed-normal reference, proving both no-cull and full normal reversal."));
		return result.ToArray();
	}
	private static byte[] Build(Plane[] planes)
	{
		using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer,Encoding.UTF8,true);
		var views=new List<object>();var accessors=new List<object>();var meshes=new List<object>();var materials=new List<object>();
		int Stream(float[] data,int components,string type)
		{
			var offset=buffer.Position;foreach(var f in data)writer.Write(f);var view=views.Count;views.Add(new{buffer=0,byteOffset=offset,byteLength=data.Length*4});
			var accessor=accessors.Count;accessors.Add(new{bufferView=view,componentType=5126,count=data.Length/components,type});return accessor;
		}
		foreach(var plane in planes)
		{
			float[] positions=[-.5f,-.5f,plane.Z,.5f,-.5f,plane.Z,.5f,.5f,plane.Z,-.5f,-.5f,plane.Z,.5f,.5f,plane.Z,-.5f,.5f,plane.Z];
			var position=Stream(positions,3,"VEC3");
			// POSITION min/max are required. Preserve literal geometry extrema.
			accessors[position]=new{bufferView=position,componentType=5126,count=6,type="VEC3",min=new[]{-.5f,-.5f,plane.Z},max=new[]{.5f,.5f,plane.Z}};
			var normal=Stream(Enumerable.Range(0,6).SelectMany(_=>new[]{0f,0f,1f}).ToArray(),3,"VEC3");
			var attributes=new Dictionary<string,int>{{"POSITION",position},{"NORMAL",normal}};
			if(plane.ColorComponents>0)attributes["COLOR_0"]=Stream(Enumerable.Range(0,6).SelectMany(_=>plane.ColorComponents==4?new[]{1f,1f,1f,plane.VertexAlpha}:new[]{1f,1f,1f}).ToArray(),plane.ColorComponents,"VEC"+plane.ColorComponents);
			if(plane.AlphaTexture)attributes["TEXCOORD_0"]=Stream(Enumerable.Repeat(.5f,12).ToArray(),2,"VEC2");
			var pbr=new Dictionary<string,object>{{"baseColorFactor",plane.Factor},{"metallicFactor",0},{"roughnessFactor",1}};
			if(plane.AlphaTexture)pbr["baseColorTexture"]=new{index=0};
			materials.Add(new{alphaMode=plane.Mode,alphaCutoff=plane.Cutoff,doubleSided=plane.DoubleSided,pbrMetallicRoughness=pbr});
			meshes.Add(new{primitives=new[]{new{attributes,material=materials.Count-1}}});
		}
		var json=new Dictionary<string,object>{{"asset",new{version="2.0"}},{"scene",0},{"scenes",new[]{new{nodes=Enumerable.Range(0,planes.Length).ToArray()}}},{"nodes",Enumerable.Range(0,planes.Length).Select(mesh=>new{mesh}).ToArray()},{"meshes",meshes},{"materials",materials},{"buffers",new[]{new{byteLength=buffer.Length,uri="data:application/octet-stream;base64,"+Convert.ToBase64String(buffer.ToArray())}}},{"bufferViews",views},{"accessors",accessors}};
		if(planes.Any(p=>p.AlphaTexture))
		{
			json["images"]=new[]{new{uri="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGP8//8/AwMDw38AFfwD/r+i5HMAAAAASUVORK5CYII="}};
			json["samplers"]=new[]{new{magFilter=9729,minFilter=9729,wrapS=33071,wrapT=33071}};json["textures"]=new[]{new{source=0,sampler=0}};
		}
		return JsonSerializer.SerializeToUtf8Bytes(json);
	}
}
