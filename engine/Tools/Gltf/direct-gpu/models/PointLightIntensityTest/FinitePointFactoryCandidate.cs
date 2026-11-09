using System;
using Sandbox.Gltf.DirectGpu.Lighting;

namespace Sandbox.Gltf.DirectGpu.PointLightIntensityValidation;

// Model-local candidate, called only by explicit research probes. Marker and
// shader/view attributes are borrowed and owned by the caller's context.
internal sealed class FinitePointFactoryCandidate : GltfPunctualLightInstances.IFactory
{
 readonly SceneWorld world;
 readonly Texture marker;
 internal FinitePointFactoryCandidate(SceneWorld world,Texture marker)
 {
  this.world=world ?? throw new ArgumentNullException(nameof(world));
  this.marker=marker is {IsValid:true} ? marker : throw new ArgumentException("Live marker texture required",nameof(marker));
 }
 public GltfPunctualLightInstances.ILight Create(GltfPunctualLightPlan.Binding binding)
 {
  if(binding.Kind!=GltfPunctualLightPlan.LightKind.Point || binding.RangeMeters is not > 0)
   throw new GltfException("Finite point candidate requires a point light with authored finite range.");
  double radius=binding.RangeInches.Value;
  if(!double.IsFinite(radius) || radius>MathF.Sqrt(float.MaxValue))
   throw new GltfException("Finite point range exceeds representable native squared radius.");
  return new Owned(world,marker,binding,(float)radius);
 }
 sealed class Owned : GltfPunctualLightInstances.ILight
 {
  readonly GltfPunctualLightPlan.Binding binding;ScenePointLight light;
  internal Owned(SceneWorld world,Texture marker,GltfPunctualLightPlan.Binding binding,float radius)
  {
   this.binding=binding;light=new ScenePointLight(world,Vector3.Zero,radius,Color.Black){LightCookie=marker,ShadowsEnabled=false};
  }
  public void Apply(GltfPunctualLightPlan.State state,bool visible)
  {
   ObjectDisposedException.ThrowIf(light is null,this);if(state.Binding!=binding)throw new GltfException("Point state binding mismatch.");
   double v=visible?binding.Intensity:0;double r=v*binding.LinearColor.X,g=v*binding.LinearColor.Y,b=v*binding.LinearColor.Z;
   if(!double.IsFinite(r)||!double.IsFinite(g)||!double.IsFinite(b)||r>float.MaxValue||g>float.MaxValue||b>float.MaxValue)throw new GltfException("Point intensity exceeds finite native color.");
   var p=state.PositionInches;light.Position=new Vector3(p.X,p.Y,p.Z);light.LightColor=new Color((float)r,(float)g,(float)b);
  }
  public void Dispose(){var owned=light;light=null;owned?.Delete();}
 }
}
