using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using NVector4 = System.Numerics.Vector4;

internal static class UnlitFeatureFixtures
{
 internal sealed record Fixture(string Name, string Mode, NVector4 Factor, NVector4 Color, float Cutoff = .5f, bool DoubleSided = false, bool Reversed = false, bool Texture = false, float Uv = .25f, bool Transform = false, bool LinearSampling = false, bool Repeat = false);
 internal static byte[] Build(Fixture f)
 {
  using var buffer=new MemoryStream();using var writer=new BinaryWriter(buffer,Encoding.UTF8,true);
  var views=new List<object>();var accessors=new List<object>();
  int Stream(float[] data,int components,string type)
  {
   var offset=buffer.Position;foreach(var x in data)writer.Write(x);var view=views.Count;views.Add(new{buffer=0,byteOffset=offset,byteLength=data.Length*4});
   var accessor=accessors.Count;accessors.Add(new{bufferView=view,componentType=5126,count=data.Length/components,type});return accessor;
  }
  float[] positions=[-.762f,-.762f,0,.762f,-.762f,0,0,.762f,0];
  if(f.Reversed) for(var axis=0;axis<3;axis++)(positions[3+axis],positions[6+axis])=(positions[6+axis],positions[3+axis]);
  var p=Stream(positions,3,"VEC3");accessors[p]=new{bufferView=0,componentType=5126,count=3,type="VEC3",min=new[]{-.762f,-.762f,0},max=new[]{.762f,.762f,0}};
  var n=Stream([0,0,1,0,0,1,0,0,1],3,"VEC3");var c=Stream(Enumerable.Range(0,3).SelectMany(_=>new[]{f.Color.X,f.Color.Y,f.Color.Z,f.Color.W}).ToArray(),4,"VEC4");
  var attributes=new Dictionary<string,int>{{"POSITION",p},{"NORMAL",n},{"COLOR_0",c}};
  if(f.Texture)attributes[f.Transform ? "TEXCOORD_9" : "TEXCOORD_0"]=Stream(Enumerable.Range(0,3).SelectMany(_=>new[]{f.Uv,.5f}).ToArray(),2,"VEC2");
  if(f.Transform)for(var set=0;set<9;set++)attributes["TEXCOORD_"+set]=attributes["TEXCOORD_9"];
  var pbr=new Dictionary<string,object>{{"baseColorFactor",new[]{f.Factor.X,f.Factor.Y,f.Factor.Z,f.Factor.W}}};if(f.Texture)pbr["baseColorTexture"]=f.Transform ? new Dictionary<string,object>{{"index",0},{"texCoord",9},{"extensions",new Dictionary<string,object>{{"KHR_texture_transform",new{offset=new[]{.5f,0f}}}}}} : new Dictionary<string,object>{{"index",0}};
  var json=new Dictionary<string,object>{{"asset",new{version="2.0"}},{"scene",0},{"scenes",new[]{new{nodes=new[]{0}}}},{"nodes",new[]{new{mesh=0}}},{"meshes",new[]{new{primitives=new[]{new{attributes,material=0}}}}},{"materials",new[]{new{extensions=new Dictionary<string,object>{{"KHR_materials_unlit",new{}}},pbrMetallicRoughness=pbr,alphaMode=f.Mode,alphaCutoff=f.Cutoff,doubleSided=f.DoubleSided}}},{"extensionsUsed",new[]{"KHR_materials_unlit"}},{"extensionsRequired",new[]{"KHR_materials_unlit"}},{"buffers",new[]{new{byteLength=buffer.Length,uri="data:application/octet-stream;base64,"+Convert.ToBase64String(buffer.ToArray())}}},{"bufferViews",views},{"accessors",accessors}};
  if(f.Transform)json["extensionsUsed"]=new[]{"KHR_materials_unlit","KHR_texture_transform"};
  if(f.Texture){json["images"]=new[]{new{uri="data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGNocFBoUGhwcAAAC8kCgZHWr1kAAAAASUVORK5CYII="}};json["samplers"]=new[]{new{magFilter=f.LinearSampling?9729:9728,minFilter=f.LinearSampling?9729:9728,wrapS=f.Repeat?10497:33071,wrapT=33071}};json["textures"]=new[]{new{source=0,sampler=0}};}
  return JsonSerializer.SerializeToUtf8Bytes(json);
 }
 internal static NVector4 TextureValue(float uv, bool linear = false, bool repeat = false)
 {
  float Linear(float x)=>x<=.04045f?x/12.92f:MathF.Pow((x+.055f)/1.055f,2.4f);
  var a=new NVector4(Linear(128f/255),Linear(64f/255),Linear(32f/255),128f/255);var b=new NVector4(Linear(32f/255),Linear(128f/255),Linear(64f/255),64f/255);
  uv=repeat?uv-MathF.Floor(uv):Math.Clamp(uv,0,1);
  if(!linear)return uv<.5f?a:b;
  var coordinate=uv*2-.5f;var first=(int)MathF.Floor(coordinate);var mix=coordinate-first;
  NVector4 Texel(int index)=>repeat?((index%2+2)%2==0?a:b):(index<=0?a:b);
  return NVector4.Lerp(Texel(first),Texel(first+1),mix);
 }
}
