using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>
/// Real native-image probe driven only by the coordinator. The caller compiles
/// reviewed materials and supplies real-frame/native linear RGBA capture callbacks.
/// Internal because the actual producer backend is internal. Run is a public
/// static reflection entrypoint. No native Model/Mesh or replacement producer.
/// </summary>
internal static class AlphaBlendModeSourceProbe
{
	public sealed record Result( string Form, string MaterialScope, int Captures, int DrawCalls, AlphaBlendModeImageChecks.Measurement Pixels, bool Passed, string Error );
	public static async Task<Result[]> Run( string checkoutRoot, Scene scene, CameraComponent camera, Material compute,
		Material independentConstantOpaqueReference, Func<Task> waitForActualFrames, Func<string,Task<Color[]>> captureLinear,
		double maximumLinearRgbError, double maximumCoverageError, Func<int?,Material> reviewedCandidateResolver = null )
	{
		Require( ReferenceEquals( camera.Scene, scene ) && independentConstantOpaqueReference is { IsValid: true, IsError: false }, "Need private coordinator scene/camera and independent constant decoded-RGB opaque reference material." );
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Run only in the verified idle native editor." );
		ConfigureCamera( camera );
		var isolated = AlphaBlendModeReference.IsolatedPanels( checkoutRoot );
		var forms = new[] { isolated, new AlphaBlendModeReference.Form( "derived_isolated_authored_panels/compiled_glb", GltfCompiler.Compile( isolated.Bytes, isolated.Resolve ), isolated.Resolve ) };
		var results = new List<Result>();
		SceneDirectionalLight light = null;
		try
		{
			light = new SceneDirectionalLight( scene.SceneWorld, camera.WorldRotation, Color.White ) { ShadowsEnabled = false };
			foreach ( var form in forms )
			{
				GltfGpuAsset asset = null; GltfGpuInstance instance = null; GltfDocument document = null; var captures = 0; var calls = 0; string failure = null; AlphaBlendModeImageChecks.Measurement measured = null;
				try
				{
					var black = await Take( "empty_black", Color.Black ); var white = await Take( "empty_white", Color.White );
					var mid = await Take( "empty_half_linear_transfer_control", new Color( .5f,.5f,.5f,1 ) );
					RequireBackground( black,0 ); RequireBackground( white,1 ); RequireBackground( mid,.5 );
					Color[] expected;
					using ( var referenceDocument = LiteralPanels() )
					using ( var referenceAsset = GltfGpuAsset.Create( referenceDocument, compute, _ => independentConstantOpaqueReference ) )
					using ( var reference = referenceAsset.CreateInstance( scene.SceneWorld ) )
					{
						expected = await Take( "independent_constant_rgb_literal_panels", Color.Black ); Submitted( reference );
					}
					await waitForActualFrames(); Deletes(); RequireBackground( await Take( "reference_deleted", Color.Black ),0 );
					document = form.Read(); asset = GltfGpuAsset.Create( document, compute, reviewedCandidateResolver ); instance = asset.CreateInstance( scene.SceneWorld );
					var actualBlack = await Take( "source_black", Color.Black ); var actualWhite = await Take( "source_white", Color.White ); Submitted( instance ); calls = instance.DrawCalls;
					var samples = new List<AlphaBlendModeImageChecks.Sample>();
					for ( var panel = 0; panel < 5; panel++ ) for ( var row = 0; row < 5; row++ )
					{
						var pixel = ProbePixel( camera,panel,AlphaBlendModeReference.RampRows[row] );
						var b=actualBlack[pixel]; var w=actualWhite[pixel]; var r=expected[pixel]; var eb=black[pixel]; var ew=white[pixel];
						Require( r.r + r.g + r.b > .1, "Independent opaque reference has no positive geometry at the source probe." );
						samples.Add( new(panel,row,b.r,b.g,b.b,w.r,w.g,w.b,r.r,r.g,r.b,eb.r,eb.g,eb.b,ew.r,ew.g,ew.b) );
					}
					measured = AlphaBlendModeImageChecks.Measure( samples ); AlphaBlendModeImageChecks.RequireWithin( measured, maximumLinearRgbError, maximumCoverageError );
					instance.Dispose(); await waitForActualFrames(); Deletes(); RequireBackground( await Take( "source_deleted",Color.Black ),0 );
					Require( instance.DrawCalls == calls, "Deleted source continued submitting." );
				}
				catch ( Exception e ) { failure = e.ToString(); }
				finally { await MainThread.Wait(); instance?.Dispose(); asset?.Dispose(); document?.Dispose(); Deletes(); }
				results.Add( new(form.Name,reviewedCandidateResolver is null ? "default_source_material" : "explicit_reviewed_candidate_not_default_adapter",captures,calls,measured,failure is null,failure) );
				async Task<Color[]> Take( string suffix,Color background )
				{
					await MainThread.Wait(); camera.BackgroundColor=background; camera.AutoExposure.Enabled=false;
					if(camera.SceneCamera is not null) camera.SceneCamera.Tonemap.Enabled=false;
					await waitForActualFrames(); captures++;
					var pixels=await captureLinear(form.Name.Replace('/','_')+"_"+suffix);
					Require(pixels.Length==1600*600,"Actual linear capture dimensions differ from the frozen projection."); return pixels;
				}
			}
		}
		finally { await MainThread.Wait(); if(light.IsValid()) light.Delete(); Deletes(); }
		return results.ToArray();
		void Deletes(){using var scope=scene.Push();scene.ProcessDeletes();}
	}
	private static void Submitted(GltfGpuInstance i)=>Require(i.DrawCalls>0 && i.PoseRevision==i.SubmittedRevision && i.CallbackErrors.Length==0,"Actual source GPU submission missing: "+string.Join("\n",i.CallbackErrors));
	private static void ConfigureCamera(CameraComponent c)
	{
		c.WorldPosition=new global::Vector3(200,0,1.05f/.0254f);c.WorldRotation=Rotation.FromYaw(180);c.Orthographic=true;c.OrthographicHeight=3f/.0254f;c.ZNear=1;c.ZFar=1000;c.CustomSize=new global::Vector2(1600,600);c.ClearFlags=ClearFlags.All;c.EnablePostProcessing=true;c.DebugMode=SceneCameraDebugMode.Normal;
		c.AutoExposure.Enabled=false;c.AutoExposure.MinimumExposure=1;c.AutoExposure.MaximumExposure=1;c.AutoExposure.Compensation=0;
	}
	private static int ProbePixel(CameraComponent c,int panel,int texelY)
	{
		var u=512.5/1024;var v=(texelY+.5)/1024;
		var x=-.550000011920929+(u-.1250002235174179)/(.675000011920929-.1250002235174179)*1.100000023841858+AlphaBlendModeReference.PanelTranslations[panel];
		var y=1.9699997901916504-(v-.050000011920928955)/(.9850000143051147-.050000011920928955)*1.8699997067451477;
		var p=c.PointToScreenPixels(new global::Vector3(0,(float)(x/.0254),(float)(y/.0254)));var px=(int)Math.Floor(p.x);var py=(int)Math.Floor(p.y);
		Require(px>=0&&px<1600&&py>=0&&py<600,"Source texel projected outside capture.");return py*1600+px;
	}
	private static void RequireBackground(Color[] pixels,double expected)=>Require(pixels.All(c=>float.IsFinite(c.r)&&float.IsFinite(c.g)&&float.IsFinite(c.b)&&Math.Abs(c.r-expected)<.01&&Math.Abs(c.g-expected)<.01&&Math.Abs(c.b-expected)<.01),"Native empty/deleted/linear-transfer control failed.");
	private static GltfDocument LiteralPanels()
	{
		using var bytes=new MemoryStream();using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))
		for(var panel=0;panel<5;panel++)foreach(var p in new[]{(-.55f,.1f),(.55f,.1f),(.55f,1.97f),(-.55f,1.97f)}){writer.Write(p.Item1+AlphaBlendModeReference.PanelTranslations[panel]);writer.Write(p.Item2);writer.Write(0f);}
		var normalOffset=(int)bytes.Length;using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))for(var i=0;i<20;i++){writer.Write(0f);writer.Write(0f);writer.Write(1f);}
		var indexOffset=(int)bytes.Length;using(var writer=new BinaryWriter(bytes,System.Text.Encoding.UTF8,true))for(var panel=0;panel<5;panel++)foreach(var i in new[]{0,1,2,0,2,3})writer.Write((ushort)(panel*4+i));
		var json=new{asset=new{version="2.0"},scene=0,scenes=new[]{new{nodes=new[]{0}}},nodes=new[]{new{mesh=0}},meshes=new[]{new{primitives=new[]{new{attributes=new Dictionary<string,int>{{"POSITION",0},{"NORMAL",1}},indices=2}}}},buffers=new[]{new{byteLength=bytes.Length,uri="data:application/octet-stream;base64,"+Convert.ToBase64String(bytes.ToArray())}},bufferViews=new[]{new{buffer=0,byteOffset=0,byteLength=normalOffset},new{buffer=0,byteOffset=normalOffset,byteLength=indexOffset-normalOffset},new{buffer=0,byteOffset=indexOffset,byteLength=60}},accessors=new object[]{new{bufferView=0,componentType=5126,count=20,type="VEC3",min=new[]{-3.55,.1,0},max=new[]{3.55,1.97,0}},new{bufferView=1,componentType=5126,count=20,type="VEC3"},new{bufferView=2,componentType=5123,count=30,type="SCALAR"}}};
		return GltfReader.Read(JsonSerializer.SerializeToUtf8Bytes(json));
	}
	private static void Require(bool condition,string message)=>AlphaBlendModeCoreTests.Require(condition,message);
}
