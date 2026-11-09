using Sandbox.Engine;
using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

/// <summary>Per-primitive no-normal point/line source program. Does not implement KHR_materials_unlit.</summary>
internal static class GltfPointLineSourceMaterialAdapter
{
 internal static readonly GltfMaterialTextureRole[] Roles = [GltfMaterialTextureRole.BaseColor, GltfMaterialTextureRole.Emissive];
 internal sealed class Prepared
 {
  internal GltfMaterial Material;
  internal float EmissiveStrength;
  internal Dictionary<GltfMaterialTextureRole,GltfStockMaterialAdapter.SourceTexture> Textures = new();
 }
 internal static Prepared Prepare(GltfDocument document, GltfMaterial material, int? index, Func<int,GltfImage> imageReader)
 {
  ArgumentNullException.ThrowIfNull(document);ArgumentNullException.ThrowIfNull(imageReader);
  material ??= new GltfMaterial { BaseColorFactor=System.Numerics.Vector4.One, AlphaMode="OPAQUE", AlphaCutoff=.5f };
  if(material.Unlit)throw new GltfException("KHR_materials_unlit uses its own material program.");
  if(material.AlphaMode=="BLEND")throw new GltfException("No-normal point/line BLEND is not yet supported.");
  if(material.AlphaMode is not ("OPAQUE" or "MASK"))throw new GltfException("Unsupported point/line alpha mode.");
  var result=new Prepared { Material=material, EmissiveStrength=index.HasValue?document.ReadMaterialEmissiveStrength(index.Value):1 };
  foreach(var (role,binding) in GltfMaterialTextureEncodingPlan.CreateForRoles(document,material,Roles).Bindings)
  {
   if(binding.ImageIndex is not int image)throw new GltfException($"Point/line {role} has no supported image source.");
   var sampling=GltfSamplerMapping.Map(binding.Sampler,9987,9729);var encoded=imageReader(image);document.ValidateTextureImage(binding.TextureInfo.Index,encoded);
   var resource=sampling.RequiresBaseMipOnly?GltfTextureCompiler.Compile(encoded,true):GltfTextureCompiler.CompileWithMipmaps(encoded,true);
   result.Textures.Add(role,new(binding,sampling,resource));
  }
  return result;
 }
 internal static GltfMaterialLease<Material> Acquire(Prepared prepared,Material template,GltfMaterialUvPlan uv,GltfMaterialUvProjection projection)
 {
  ArgumentNullException.ThrowIfNull(prepared);ArgumentNullException.ThrowIfNull(template);ArgumentNullException.ThrowIfNull(uv);ArgumentNullException.ThrowIfNull(projection);
  ThreadSafe.AssertIsMainThread();if(Graphics.IsActive)throw new GltfException("Point/line materials cannot be realized during rendering.");
  if(!template.IsValid || template.IsError)throw new GltfException("Point/line source template unavailable.");
  if(projection.Sources.Count>5 || uv.Bindings.Count!=projection.Slots.Count || uv.Bindings.Count!=prepared.Textures.Count)throw new GltfException("Point/line UV projection differs from source texture roles.");
  var source=prepared.Material;var factor=source.BaseColorFactor;var emission=source.EmissiveFactor*prepared.EmissiveStrength;var mask=source.AlphaMode=="MASK";
  if(template.GetFeature("F_ALPHA_TEST")!=(mask?1:0) || template.GetFeature("F_TRANSLUCENT")!=0)throw new GltfException("Point/line source template alpha permutation differs.");
  var vectors=new List<KeyValuePair<string,Vector4>>{new("g_vGltfBaseColor",new(factor.X,factor.Y,factor.Z,mask?factor.W:1)),new("g_vGltfEmissiveFactor",new(emission.X,emission.Y,emission.Z,0))};
  var scalars=new List<KeyValuePair<string,float>>{new("g_flGltfHasBaseColorTexture",prepared.Textures.ContainsKey(GltfMaterialTextureRole.BaseColor)?1:0),new("g_flGltfHasEmissiveTexture",prepared.Textures.ContainsKey(GltfMaterialTextureRole.Emissive)?1:0)};
  if(mask)scalars.Add(new("g_flGltfAlphaCutoff",source.AlphaCutoff));
  var owner=GlobalContext.Current;var textures=new List<GltfMaterialLease<Texture>>();GltfMaterialLease<Material> material=null;
  try
  {
   var bindings=new List<GltfMaterialTextureBinding>();
   foreach(var (role,texture) in prepared.Textures)
   {
    if(!uv.Bindings.TryGetValue(role,out var selected) || !projection.Slots.TryGetValue(role,out var slot) || slot<0 || slot>=projection.Sources.Count || slot>=5 || projection.Sources[slot].TexCoord!=selected.TexCoord || !ReferenceEquals(projection.Sources[slot].Stream,selected.Source) || selected.TextureInfo.Index!=texture.Encoding.TextureInfo.Index || selected.TextureInfo.TexCoord!=texture.Encoding.TextureInfo.TexCoord || selected.TextureInfo.Offset!=texture.Encoding.TextureInfo.Offset || selected.TextureInfo.Tiling!=texture.Encoding.TextureInfo.Tiling || selected.TextureInfo.Rotation!=texture.Encoding.TextureInfo.Rotation)throw new GltfException($"Point/line {role} UV provenance differs.");
    var name=role==GltfMaterialTextureRole.BaseColor?"BaseColor":"Emissive";
    vectors.Add(new($"g_vGltf{name}UvRowU",new(selected.Transform.RowU.X,selected.Transform.RowU.Y,selected.Transform.RowU.Z,0)));
    vectors.Add(new($"g_vGltf{name}UvRowV",new(selected.Transform.RowV.X,selected.Transform.RowV.Y,selected.Transform.RowV.Z,0)));
    scalars.Add(new($"g_flGltf{name}UvSlot",slot));
    var lease=GltfNativeTextureCache.Acquire(texture.Resource);textures.Add(lease);
    bindings.Add(new($"g_tGltf{name}Texture",lease.Value,lease.Value.DirtyVersion,texture.Sampling.State,$"Gltf{name}Sampler"));
   }
   foreach(var v in vectors)if(!template.HasParameter(v.Key))throw new GltfException($"Point/line template missing {v.Key}.");
   foreach(var v in scalars)if(!template.HasParameter(v.Key))throw new GltfException($"Point/line template missing {v.Key}.");
   foreach(var v in bindings)if(!template.HasParameter(v.Parameter))throw new GltfException($"Point/line template missing {v.Parameter}.");
   var key=new GltfMaterialKey(template,0,[new("F_RENDER_BACKFACES",source.DoubleSided?1:0),new("F_ALPHA_TEST",mask?1:0),new("F_TRANSLUCENT",0)],vectors,bindings,scalars);
   material=GltfMaterialCache.Shared.Acquire(key);
   if(material.Value.Flags.IsTranslucent || material.Value.Flags.IsAlphaTest!=mask)throw new GltfException("Point/line material classification differs.");
   var retained=material;var retainedTextures=textures.ToArray();
   return new GltfMaterialLease<Material>(material.Value,()=>{using var scope=new GlobalContext.GlobalContextScope(owner);GltfGpuCleanup.ReleaseAll(new Action[]{retained.Dispose}.Concat(retainedTextures.Select<GltfMaterialLease<Texture>,Action>(t=>t.Dispose)));},()=>ThreadSafe.AssertIsMainThread());
  }
  catch(Exception original){GltfGpuCleanup.Rollback(original,(material is null?Array.Empty<Action>():new Action[]{material.Dispose}).Concat(textures.Select<GltfMaterialLease<Texture>,Action>(t=>t.Dispose)));throw;}
 }
}
