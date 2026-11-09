using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;
using static NormalTangentMirrorReference;

/// <summary>
/// Source normal texture versus independently baked literal normals under the
/// native normal-debug presentation. Actual source material realization is
/// required; a diagnostic replacement cannot satisfy this material gate.
/// </summary>
internal static class NormalTangentMirrorFixtureProbe
{
	public const int ExpectedCases = 4;
	public sealed record Result( string Form, bool Back, int Captures, NormalTangentMirrorImageProbe.Measurement Comparison, bool Passed, string Error );
	public static async Task<Result[]> Run( string checkoutRoot, Scene scene, CameraComponent camera, Material compute, Func<Task> waitForActualFrames, Func<string,Task<Color[]>> capture )
	{
		Require( ReferenceEquals( camera.Scene,scene ),"Coordinator camera must belong to private scene." );
		var folder = Path.Combine( checkoutRoot,"research/gltf-validation/direct-gpu/models/NormalTangentMirrorTest" );
		var original = File.ReadAllBytes( Path.Combine( folder,"asymmetric-normal-uv-fixture.gltf" ) ); var baked = File.ReadAllBytes( Path.Combine( folder,"asymmetric-baked-normal-reference.gltf" ) );
		var wrong = JsonNode.Parse( original ); var transform = wrong["materials"][0]["normalTexture"]["extensions"]["KHR_texture_transform"]; transform["offset"] = new JsonArray( 0,0 ); transform["scale"] = new JsonArray( 1,1 );
		var wrongBytes = Encoding.UTF8.GetBytes( wrong.ToJsonString() ); var results = new List<Result>();
		foreach ( var compiled in new[] { false,true } )
		{
			byte[] Form( byte[] bytes ) => compiled ? GltfCompiler.Compile( bytes ) : bytes;
			using var actualDocument = GltfReader.Read( Form( original ) ); using var expectedDocument = GltfReader.Read( Form( baked ) ); using var negativeDocument = GltfReader.Read( Form( wrongBytes ) );
			using var actualAsset = GltfGpuAsset.Create( actualDocument,compute ); using var referenceAsset = GltfGpuAsset.Create( expectedDocument,compute ); using var wrongAsset = GltfGpuAsset.Create( negativeDocument,compute );
			foreach ( var back in new[] { false,true } )
			{
				var captures = 0; string failure = null; NormalTangentMirrorImageProbe.Measurement comparison = null; var label = (compiled ? "compiled_glb_c" : "source") + (back ? "_back" : "_front");
				try
				{
					await MainThread.Wait(); camera.WorldPosition = global::Vector3.Forward*(back ? -500 : 500); camera.WorldRotation = Rotation.LookAt( -camera.WorldPosition,global::Vector3.Up );
					camera.Orthographic = true; camera.OrthographicHeight = 185; camera.ZNear = 1; camera.ZFar = 1000; camera.CustomSize = new System.Numerics.Vector2( 384,384 ); camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All; camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
					Empty( await Take( "empty_before" ) );
					var actual = await Draw( actualAsset,"actual_normal_map_uv1" ); Empty( await Take( "empty_after_actual" ) );
					// Baked expected normals were computed directly from literal RGB8,
					// scale and W by Python, never production normal-map evaluation.
					var expected = await Draw( referenceAsset,"independent_baked_normals_no_normal_texture" ); Empty( await Take( "empty_after_reference" ) );
					var negative = await Draw( wrongAsset,"wrong_v_transform" ); Empty( await Take( "empty_after_negative" ) );
					comparison = NormalTangentMirrorImageProbe.Compare( expected,actual,negative );
				}
				catch ( Exception e ) { failure = e.ToString(); }
				results.Add( new( compiled ? "compiled_glb_c" : "source",back,captures,comparison,failure is null,failure ) );
				async Task<Color[]> Take( string suffix ) { captures++; return await capture( "literal_normal_fixture_"+label+"_"+suffix ); }
				async Task<Color[]> Draw( GltfGpuAsset asset, string suffix )
				{
					var instance = asset.CreateInstance( scene.SceneWorld );
					try
					{
						var pixels = await Take( suffix ); Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision && instance.CallbackErrors.Length == 0,"Fixture actual GPU draw failed." );
						var rows = instance.Readback(); Require( rows.Count == 1 && rows[0].VertexCount == 32 && rows[0].Indices.Length == 48 && rows[0].IndexDataOrigin == "gpu-readback","Literal fixture did not submit expected actual geometry." ); return pixels;
					}
					finally { await MainThread.Wait(); instance.Dispose(); await waitForActualFrames(); using var scope = scene.Push(); scene.ProcessDeletes(); }
				}
			}
		}
		Require( results.Count == ExpectedCases,"Literal fixture image inventory changed." ); return results.ToArray();
	}
	private static void Empty( Color[] pixels ) => Require( pixels is not null && pixels.Length == 384*384 && pixels.All( p => float.IsFinite( p.r ) && float.IsFinite( p.g ) && float.IsFinite( p.b ) && Math.Max( p.r,Math.Max( p.g,p.b ) ) <= .01 ),"Literal fixture empty/deleted native capture failed." );
}
