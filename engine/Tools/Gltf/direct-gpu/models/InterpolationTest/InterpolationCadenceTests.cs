using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

[TestClass]
public sealed class InterpolationCadenceTests
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure( string root ) { checkout = root; Measurements.Clear(); }
 [TestMethod] public Task Source() => Run( false );
 
 static async Task Run( bool compiled )
 {
  await MainThread.Wait(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
  using var input = new InterpolationConformanceTests.Inputs( checkout, "glTF" );
  var parent = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/InterpolationTest/glTF" );
  var source = File.ReadAllBytes( Path.Combine( parent, "InterpolationTest.gltf" ) );
  byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( parent, uri ) );
  var directory = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/interpolation-presentation", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( directory );
  var path = "interpolation_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb" : ".gltf");
  if ( compiled ) { var bundle = GltfArtifactDependencies.Prepare( GltfCompiler.Compile( source, Resolve ), Resolve ); File.WriteAllBytes( Path.Combine( directory, path + "_c" ), bundle.Artifact ); foreach ( var file in bundle.Dependencies ) { var dest = Path.Combine( directory, file.Key ); Directory.CreateDirectory( Path.GetDirectoryName( dest ) ); File.WriteAllBytes( dest, file.Value ); } }
  else { File.WriteAllBytes( Path.Combine( directory, path ), source ); foreach ( var uri in new[] { "InterpolationTest_data.bin", "InterpolationTest_img0.png" } ) File.Copy( Path.Combine( parent, uri ), Path.Combine( directory, uri ) ); }
  var aggregate = FileSystem.Mounted; var mount = new LocalFileSystem( directory ); aggregate.Mount( mount ); Model model = null;
  try
  {
   model = Model.Load( path ); Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull ); Assert.AreEqual( 9, model.AnimationNames.Count );
   var pos = new global::Vector3( 20.1967457f / .0254f, 0, 2.8202687f / .0254f ); var target = new global::Vector3( .0018373973f / .0254f, 0, 2.8202687f / .0254f );
   using var capture = new GltfGpuTriangleGateTests.NativeCapture( directory, pos, target, 12.449328f / .0254f ); capture.Camera.ZFar = 3000;
   var empty = await capture.Take( "empty-before" ); Assert.AreEqual( 0, empty.Coverage );
   SkinnedModelRenderer renderer; GltfAnimationController controller;
   using ( capture.Scene.Push() )
   {
    var obj = capture.Scene.CreateObject(); renderer = obj.Components.Create<SkinnedModelRenderer>( false ); renderer.UseAnimGraph = false; renderer.PlayAnimationsInEditorScene = true; renderer.Model = model; renderer.Enabled = true;
    controller = obj.Components.Create<GltfAnimationController>( false ); controller.Target = renderer; controller.SetClips( model.AnimationNames.ToArray() ); controller.PlaybackRate = 0; controller.Looping = true; controller.Enabled = true;
    capture.Scene.EditorTick( 0, 0 );
   }
   Assert.IsTrue( controller.IsBound, controller.ValidationError ); Assert.AreEqual( 2f, controller.Duration );
   controller.Time = 0; controller.PlaybackRate = 1; var watch = System.Diagnostics.Stopwatch.StartNew(); var last = watch.Elapsed.TotalSeconds; var records = new List<object>(); string error = null; double maxError = 0, maxGap = 0, rounding = 0;
   Action tick = () =>
   {
    try
    {
     var now = watch.Elapsed.TotalSeconds; var delta = now - last; last = now; var entry = (float)delta; rounding += Math.Abs( delta - entry ); maxGap = Math.Max( maxGap, delta );
     using ( capture.Scene.Push() ) capture.Scene.EditorTick( (float)now, entry );
     var phase = now % 2; var diff = Math.Abs( controller.Time - phase ); diff = Math.Min( diff, 2 - diff ); maxError = Math.Max( maxError, diff );
     if ( diff > rounding + .00001 ) throw new InvalidDataException( "Public controller phase diverged from monotonic elapsed time." );
     records.Add( new { elapsed = now, delta, input_delta = entry, observed = controller.Time, expected = phase, phase_error = diff, renderer.GltfInstance.PoseRevision } );
    }
    catch ( Exception ex ) { error = ex.ToString(); }
   };
   capture.Widget.OnPreFrame += tick;
   try { while ( watch.Elapsed.TotalSeconds < 4.1 && error is null ) { await Task.Delay( 20 ); await MainThread.Wait(); } }
   finally { capture.Widget.OnPreFrame -= tick; controller.PlaybackRate = 0; }
   Assert.IsNull( error, error ); Assert.IsTrue( records.Count >= 6 ); await capture.WaitFrames();
   var measurement = InterpolationGpuChecks.MeasureComposed( checkout, input.Reference, controller.Time, renderer.GltfInstance.Placement, renderer.GltfInstance.Readback() ); InterpolationGpuChecks.RequireWithin( measurement, .0002, .00002, .02 );
   renderer.GameObject.Destroy(); using ( capture.Scene.Push() ) capture.Scene.EditorTick( 0, 0 ); var deleted = await capture.Take( "empty-after" ); Assert.AreEqual( 0, deleted.Coverage );
   Measurements.Add( new { compiled, output=directory, records, maxError, maxGap, rounding, measurement, empty_before=empty.Sha256, empty_after=deleted.Sha256, clips=model.AnimationNames.ToArray(), scope="Monotonic widget callback deltas drive normal Scene.EditorTick and nine simultaneous public clips for two loops. Initial seek only, no per-frame seeks; final actual GPU pose compared to independent oracle. Not visual frame rate." } );
  }
  finally { if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) model.Destroy(); aggregate.UnMount( mount ); mount.Dispose(); }
 }
}
