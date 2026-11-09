using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.NormalTangentMirrorValidation;
using static NormalTangentMirrorReference;

/// <summary>
/// Independent raw-source geometry/frame image reference with shared material.
/// This proves frame/material binding only: it cannot prove the shared material's
/// RGB sampling/normal equation. Literal normal pixel controls remain separate.
/// </summary>
internal static class NormalTangentMirrorImageProbe
{
	public const int ExpectedCases = 16;
	public sealed record Measurement( int VisiblePixels, double SilhouetteIoU, double MeanRgbError, double MaximumRgbError, int WrongHandednessPixels, double WrongHandednessMeanRgbError );
	public sealed record Result( string Form, string Placement, bool Back, int Captures, NormalTangentMirrorGpuProbe.Measurement Readback, Measurement Image, bool Passed, string Error );
	public static async Task<Result[]> Run( string checkoutRoot, Scene scene, CameraComponent camera, Material compute, Func<Task> waitForActualFrames, Func<string,Task<Color[]>> capture )
	{
		Require( ReferenceEquals( camera.Scene,scene ),"Coordinator camera must belong to private scene." ); var reference = new NormalTangentMirrorReference( checkoutRoot ); var results = new List<Result>();
		foreach ( var form in reference.Forms )
		{
			// Deliberately no fallback material: unsupported source normal/occlusion
			// must stay a material gate failure, never a diagnostic fidelity pass.
			using var document = form.Read(); using var asset = GltfGpuAsset.Create( document,compute );
			Require( asset.GetMaterials().Count == 1,"Actual source has one material." ); var material = asset.GetMaterials()[0];
			foreach ( var placement in new[] { ("identity",Matrix4x4.Identity),("reflected_affine",Mirrored) } ) foreach ( var back in new[] { false,true } )
			{
				GltfGpuInstance instance = null; var captures = 0; NormalTangentMirrorGpuProbe.Measurement readback = null; Measurement image = null; string failure = null;
				var label = form.Name.Replace( '/','_' ) + "_" + placement.Item1 + (back ? "_back" : "_front");
				try
				{
					await MainThread.Wait(); Configure( camera,reference,placement.Item2,back ); RequireEmpty( await Take( "empty_before" ) );
					instance = asset.CreateInstance( scene.SceneWorld ); instance.Placement = placement.Item2; var actual = await Take( "actual_gpu" );
					Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision && instance.CallbackErrors.Length == 0,"Actual current draw failed." ); readback = NormalTangentMirrorGpuProbe.Assert( reference,instance.Readback(),placement.Item2 );
					var calls = instance.DrawCalls; instance.Dispose(); await waitForActualFrames(); Deletes(); Require( instance.DrawCalls == calls,"Deleted actual instance continued drawing." ); RequireEmpty( await Take( "empty_after_actual" ) );
					Color[] expected,negative;
					using ( var draw = new ReferenceDraw( scene.SceneWorld,reference,placement.Item2,material,false ) ) { expected = await Take( "independent_authored_frame" ); Require( draw.DrawCalls > 0 && draw.Errors.Length == 0,"Literal reference draw failed." ); }
					await waitForActualFrames(); Deletes(); RequireEmpty( await Take( "empty_after_reference" ) );
					using ( var draw = new ReferenceDraw( scene.SceneWorld,reference,placement.Item2,material,true ) ) { negative = await Take( "deliberately_ignored_authored_w" ); Require( draw.DrawCalls > 0 && draw.Errors.Length == 0,"Wrong-W control failed to draw." ); }
					await waitForActualFrames(); Deletes(); RequireEmpty( await Take( "empty_after_negative" ) ); image = Compare( expected,actual,negative );
				}
				catch ( Exception e ) { failure = e.ToString(); }
				finally { await MainThread.Wait(); instance?.Dispose(); Deletes(); }
				results.Add( new( form.Name,placement.Item1,back,captures,readback,image,failure is null,failure ) );
				async Task<Color[]> Take( string name ) { captures++; return await capture( label + "_" + name ); }
				void Deletes() { using var scope = scene.Push(); scene.ProcessDeletes(); }
			}
		}
		Require( results.Count == ExpectedCases,"Image case inventory changed." ); return results.ToArray();
	}
	private static void Configure( CameraComponent camera, NormalTangentMirrorReference reference, Matrix4x4 placement, bool back )
	{
		var points = Enumerable.Range( 0,2770 ).Select( i => reference.Position( i,placement ) ).ToArray();
		var min = Enumerable.Range( 0,3 ).Select( c => points.Min( p => p[c] ) ).ToArray(); var max = Enumerable.Range( 0,3 ).Select( c => points.Max( p => p[c] ) ).ToArray();
		var center = new global::Vector3( (float)((min[0]+max[0])/2),(float)((min[1]+max[1])/2),(float)((min[2]+max[2])/2) );
		camera.WorldPosition = center + global::Vector3.Forward * (back ? -1500 : 1500); camera.WorldRotation = Rotation.LookAt( center-camera.WorldPosition,global::Vector3.Up );
		camera.Orthographic = true; camera.OrthographicHeight = (float)Math.Max( max[1]-min[1],max[2]-min[2] )*1.2f; camera.ZNear = 1; camera.ZFar = 5000; camera.CustomSize = new Vector2( 384,384 ); camera.BackgroundColor = Color.Black; camera.ClearFlags = ClearFlags.All;
		camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal;
	}
	private static bool Visible( Color p ) => Math.Max( p.r,Math.Max( p.g,p.b ) ) > .01;
	private static void Validate( Color[] image ) { Require( image is not null && image.Length == 384*384,"Unexpected native capture dimensions." ); Require( image.All( p => float.IsFinite( p.r ) && float.IsFinite( p.g ) && float.IsFinite( p.b ) ),"Nonfinite captured RGB." ); }
	private static void RequireEmpty( Color[] image ) { Validate( image ); Require( !image.Any( Visible ),"Native empty/deleted capture contains geometry." ); }
	internal static Measurement Compare( Color[] expected, Color[] actual, Color[] wrongW )
	{
		Validate( expected ); Validate( actual ); Validate( wrongW ); var union = 0; var intersection = 0; var changed = 0; var sum = 0d; var maximum = 0d; var badSum = 0d;
		for ( var i = 0; i < expected.Length; i++ )
		{
			var e = expected[i]; var a = actual[i]; var b = wrongW[i]; if ( Visible( e ) || Visible( a ) ) union++; if ( Visible( e ) && Visible( a ) ) intersection++;
			var errors = new[] { Math.Abs( (double)e.r-a.r ),Math.Abs( (double)e.g-a.g ),Math.Abs( (double)e.b-a.b ) }; var bad = new[] { Math.Abs( (double)e.r-b.r ),Math.Abs( (double)e.g-b.g ),Math.Abs( (double)e.b-b.b ) };
			sum += errors.Sum(); maximum = Math.Max( maximum,errors.Max() ); badSum += bad.Sum(); if ( bad.Max() > .02 ) changed++;
		}
		Require( intersection > 1024 && union > 0,"Reference/actual images contain insufficient geometry." ); var iou = (double)intersection/union; var mean = sum/(expected.Length*3); var badMean = badSum/(expected.Length*3);
		Require( iou >= .995 && mean <= .002 && maximum <= .03,"Authored frame rendered image differs from independent raw-source geometry/reference." );
		Require( changed >= 16 && badMean > mean+.00005,"Wrong tangent-W image control is not sensitive; do not accept a flat normal/material that ignores authored frame." );
		return new( intersection,iou,mean,maximum,changed,badMean );
	}
	private sealed class ReferenceDraw : IDisposable
	{
		private readonly SceneCustomObject sceneObject; private readonly ConcurrentQueue<string> errors = new();
		public int DrawCalls { get; private set; }
		public string[] Errors => errors.ToArray();
		public ReferenceDraw( SceneWorld world, NormalTangentMirrorReference reference, Matrix4x4 placement, Material material, bool ignoreW )
		{
			var vertices = new Vertex[15720]; var min = new global::Vector3( float.PositiveInfinity ); var max = new global::Vector3( float.NegativeInfinity );
			for ( var i = 0; i < vertices.Length; i++ )
			{
				var index = Det( placement ) < 0 && i%3 != 0 ? i+(i%3 == 1 ? 1 : -1) : i; var source = (int)reference.Values( 0,index )[0]; var p = reference.Position( source,placement ); var n = reference.Normal( source,placement ); var t = reference.Tangent( source,placement ); var uv = reference.Values( 4,source );
				var position = new global::Vector3( (float)p[0],(float)p[1],(float)p[2] ); min = global::Vector3.Min( min,position ); max = global::Vector3.Max( max,position );
				vertices[i] = new Vertex( position ) { Normal = new global::Vector3( (float)n[0],(float)n[1],(float)n[2] ),Tangent = new global::Vector4( (float)t[0],(float)t[1],(float)t[2],ignoreW ? (Det( placement ) < 0 ? -1 : 1) : (float)t[3] ),TexCoord0 = new global::Vector4( (float)uv[0],(float)uv[1],0,0 ),Color = Color32.White };
			}
			sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false; sceneObject.Bounds = new BBox( min-.01f,max+.01f );
			sceneObject.RenderOverride = _ => { try { DrawCalls++; Graphics.Draw( vertices.AsSpan(),vertices.Length,material ); } catch ( Exception e ) { if ( errors.Count < 8 ) errors.Enqueue( e.ToString() ); } };
		}
		public void Dispose() { sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); }
	}
}
