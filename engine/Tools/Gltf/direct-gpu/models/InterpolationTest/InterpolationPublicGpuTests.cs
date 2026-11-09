using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.InterpolationTestValidation;

[TestClass]
public sealed class InterpolationPublicGpuTests
{
 static string checkout;
 public static List<object> Measurements { get; } = new();
 public static void Configure( string root ) { checkout = root; Measurements.Clear(); }
 [TestMethod] public Task Source() => Run( false );
 [TestMethod] public Task Compiled() => Run( true );
 static async Task Run( bool compiled )
 {
  await MainThread.Wait();
  Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
  using var input = new InterpolationConformanceTests.Inputs( checkout, "glTF" );
  var parent = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/InterpolationTest/glTF" );
  var source = File.ReadAllBytes( Path.Combine( parent, "InterpolationTest.gltf" ) );
  byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( parent, uri ) );
  var directory = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/interpolation-public", Guid.NewGuid().ToString( "N" ) );
  Directory.CreateDirectory( directory );
  var path = "interpolation_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb" : ".gltf");
  if ( compiled )
  {
   var bundle = GltfArtifactDependencies.Prepare( GltfCompiler.Compile( source, Resolve ), Resolve );
   File.WriteAllBytes( Path.Combine( directory, path + "_c" ), bundle.Artifact );
   foreach ( var dependency in bundle.Dependencies ) { var file = Path.Combine( directory, dependency.Key ); Directory.CreateDirectory( Path.GetDirectoryName( file ) ); File.WriteAllBytes( file, dependency.Value ); }
  }
  else { File.WriteAllBytes( Path.Combine( directory, path ), source ); foreach ( var uri in new[] { "InterpolationTest_data.bin", "InterpolationTest_img0.png" } ) File.Copy( Path.Combine( parent, uri ), Path.Combine( directory, uri ) ); }
  var aggregate = FileSystem.Mounted; var mount = new LocalFileSystem( directory ); aggregate.Mount( mount );
  Model model = null;
  try
  {
   model = Model.Load( path ); Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull );
   Assert.AreEqual( 9, model.AnimationNames.Count );
   var center = model.RenderBounds.Center; var size = model.RenderBounds.Size.Length;
   var cameraPosition = center + new global::Vector3( 1, 1, 1 ).Normal * size * 2;
   using var capture = new GltfGpuTriangleGateTests.NativeCapture( directory, cameraPosition, center, size * 1.5f );
   capture.Camera.ZFar = Math.Max( 1000, size * 10 );
   var empty = await capture.Take( "empty-before" ); Assert.AreEqual( 0, empty.Coverage );
   SkinnedModelRenderer first, second;
   using ( capture.Scene.Push() )
   {
    first = capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
    first.UseAnimGraph = false; first.PlayAnimationsInEditorScene = true; first.PlaybackRate = 0; first.Model = model; first.Enabled = true;
    second = capture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
    second.UseAnimGraph = false; second.PlayAnimationsInEditorScene = true; second.PlaybackRate = 0; second.Model = model; second.WorldPosition = new global::Vector3( 1, 0, 0 ); second.Enabled = true;
    capture.Scene.EditorTick( 0, 0 );
   }
   Assert.IsNotNull( first.GltfInstance ); Assert.IsNotNull( second.GltfInstance );
   second.Sequence.Name = model.AnimationNames[8]; second.Sequence.Time = .375f;
   using ( capture.Scene.Push() ) capture.Scene.EditorTick( 0, 0 );
   await capture.WaitFrames();
   var secondary = second.GltfInstance.Readback().Select( x => Convert.ToHexStringLower( SHA256.HashData( x.Bytes ) ) ).ToArray();
   var rows = new List<object>(); var frames = new List<object>();
   for ( var clip = 0; clip < 9; clip++ )
   {
    first.Sequence.Name = model.AnimationNames[clip]; first.Sequence.Looping = false;
    foreach ( var time in new[] { 0f, .25f, MathF.BitDecrement(.5f), .5f, .75f, 1f, 1.25f, 1.5f, 2f } )
    {
     first.Sequence.Time = time; using ( capture.Scene.Push() ) capture.Scene.EditorTick( 0, 0 );
     var instance = first.GltfInstance; var revision = instance.PoseRevision;
     await capture.WaitFrames();
     Assert.AreEqual( revision, instance.SubmittedRevision ); Assert.IsTrue( instance.DrawCalls > 0 && instance.CallbackErrors.Length == 0 );
     var measured = InterpolationGpuChecks.Measure( checkout, input.Reference, clip, time, instance.Placement, instance.Readback() );
     InterpolationGpuChecks.RequireWithin( measured, 0.0002, 0.00002, 0.02 );
     Assert.IsTrue( secondary.SequenceEqual( second.GltfInstance.Readback().Select( x => Convert.ToHexStringLower( SHA256.HashData( x.Bytes ) ) ) ) );
     rows.Add( measured );
     if ( clip is 0 or 4 or 8 && time == .75f ) { var frame = await capture.Take( $"clip-{clip}-075" ); Assert.IsTrue( frame.Coverage > 64 ); frames.Add( new { clip, time, frame.Path, frame.Sha256, frame.Coverage } ); }
    }
   }
   first.GameObject.Destroy(); second.GameObject.Destroy(); using ( capture.Scene.Push() ) capture.Scene.EditorTick( 0, 0 );
   var deleted = await capture.Take( "empty-after" ); Assert.AreEqual( 0, deleted.Coverage );
   Measurements.Add( new { compiled, output = directory, samples = rows, frames, empty_before = empty.Sha256, empty_after = deleted.Sha256, limits = new { position_inches = .0002, normal = .00002, angle_degrees = .02 }, scope = "Public Model.Load and ordinary SkinnedModelRenderer; source oracle and actual bound GPU vertex/index readback; diagnostic camera, not matched appearance or wall-clock." } );
  }
  finally { if ( model is not null && model.ResourcePath == Resource.FixPath( path ) ) model.Destroy(); aggregate.UnMount( mount ); mount.Dispose(); }
 }
}
