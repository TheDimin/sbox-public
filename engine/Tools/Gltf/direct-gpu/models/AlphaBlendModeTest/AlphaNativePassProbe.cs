using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>
/// Actual native callback/pass/sort observation, independent of the glTF backend.
/// Uses SceneCustomObject and literal Graphics.Draw only, never Model/Mesh.
/// The coordinator owns private scene, actual native camera captures and materials.
/// A routing pass does not establish alpha shader or public renderer integration.
/// </summary>
public static class AlphaNativePassProbe
{
	public sealed record Callback( long Sequence, string Phase, ulong Frame, int Object, string Layer,
		int? CameraId, int? ViewId, string MaterialModeIdentity, string CameraPosition, float BoundsDistance );
	public sealed record Case( string Name, int Captures, Callback[] Callbacks, int ForegroundPixels,
		bool Passed, string Error, object Observation );
	public const int ExpectedCases = 8;

	public static async Task<Case[]> Run( Scene scene, CameraComponent camera, Material opaqueMaterial, Material blendMaterial,
		Func<Task> waitForActualFrames, Func<string,Task<Color[]>> capture )
	{
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && ReferenceEquals( scene,camera.Scene ), "Requires the verified idle editor and private native scene/camera." );
		RequireNative();
		Require( opaqueMaterial is { IsValid:true,IsError:false } && blendMaterial is { IsValid:true,IsError:false }, "Borrowed audit materials must be valid." );
		Require( blendMaterial.native.GetIntAttributeOrDefault("translucent",0)!=0, "Blend control material must be natively classified translucent; shader filename/metadata supplied by caller is insufficient." );
		var result=new List<Case>();var events=new ConcurrentQueue<Callback>();var callbackErrors=new ConcurrentQueue<string>();long sequence=0;var phase="setup";
		var owned=new List<SceneCustomObject>();
		try
		{
			Configure(1);
			await RunCase("opaque_only_native_layers",async()=>
			{
				Add(0,0,true,false,opaqueMaterial);var image=await Take("opaque");var rows=Rows();
				Require(rows.Any(r=>r.Layer==nameof(SceneLayerType.Opaque)),"Opaque object did not enter native opaque pass.");
				Require(rows.All(r=>r.Layer!=nameof(SceneLayerType.Translucent)),"Opaque-only object entered native translucent pass.");
				Require(Foreground(image)>0,"Positive literal opaque control rendered no pixels.");return (image,(object)new{route="opaque",depthObserved=rows.Any(r=>r.Layer==nameof(SceneLayerType.DepthPrepass))});
			});
			await RunCase("blend_only_native_layers",async()=>
			{
				Add(0,0,false,true,blendMaterial);var image=await Take("blend");var rows=Rows();
				Require(rows.Any(r=>r.Layer==nameof(SceneLayerType.Translucent)),"Translucent-only object did not enter native translucent pass.");
				Require(rows.All(r=>r.Layer!=nameof(SceneLayerType.Opaque)&&r.Layer!=nameof(SceneLayerType.DepthPrepass)),"Translucent-only object entered an opaque/depth pass.");
				Require(Foreground(image)>0,"Positive literal blend control rendered no pixels.");return (image,(object)new{route="translucent"});
			});
			foreach(var cameraSign in new[]{1,-1})foreach(var reverse in new[]{false,true})
			await RunCase("separate_blend_sorted_camera_"+cameraSign+"_creation_"+(reverse?"reverse":"forward"),async()=>
			{
				Configure(cameraSign);foreach(var id in reverse?new[]{1,0}:new[]{0,1})Add(id,id==0?-20:20,false,true,blendMaterial);
				var image=await Take("separate_sorted");var rows=Rows().Where(r=>r.Layer==nameof(SceneLayerType.Translucent)).ToArray();
				var far=cameraSign==1?0:1;var near=1-far;
				Require(rows.Length>0,"No native translucent callbacks observed.");
				var views=rows.GroupBy(r=>(r.Frame,r.CameraId,r.ViewId)).ToArray();
				Require(views.All(v=>v.Any(r=>r.Object==far)&&v.Any(r=>r.Object==near)),"An observed native view is missing one separated blend object.");
				foreach(var view in views)
				{
					var order=view.OrderBy(r=>r.Sequence).Select(r=>r.Object).ToArray();
					Require(order.Length%2==0,"Unexpected unequal callback inventory for two visible blend objects.");
					for(var i=0;i<order.Length;i+=2)Require(order[i]==far&&order[i+1]==near,"Native separated blend callbacks were not far-before-near independently of creation order.");
				}
				return(image,(object)new{cameraSign,reverse,expectedFar=far,expectedNear=near,viewGroups=views.Length});
			});
			await RunCase("child_flags_are_measured_not_assumed",async()=>
			{
				Configure(1);var parent=Add(0,-40,true,false,opaqueMaterial);var child=Add(1,20,false,true,blendMaterial);parent.AddChild("alpha_probe",child);
				parent.ColorTint=new Color(.25f,.5f,.75f,.5f);parent.RenderingEnabled=false;parent.Flags.ExcludeGameLayer=true;
				var image=await Take("parent_disabled_child_observation");
				// Record the actual inheritance behavior without claiming any policy
				// until reviewed. Both child callback presence and flags are evidence.
				return(image,(object)new{parentEnabled=parent.RenderingEnabled,childEnabled=child.RenderingEnabled,parentExclude=parent.Flags.ExcludeGameLayer,childExclude=child.Flags.ExcludeGameLayer,
					parentTint=parent.ColorTint.ToString(),childTint=child.ColorTint.ToString(),childReceivedCallbacks=Rows().Any(r=>r.Object==1),childParentMatches=ReferenceEquals(child.Parent,parent)});
			});
			await RunCase("both_flags_dispatch_each_native_pass",async()=>
			{
				Configure(1);Add(0,0,true,true,opaqueMaterial);var image=await Take("both_flags");var rows=Rows();
				Require(rows.Any(r=>r.Layer==nameof(SceneLayerType.Opaque))&&rows.Any(r=>r.Layer==nameof(SceneLayerType.Translucent)),"Both classification flags did not dispatch both native passes.");
				return(image,(object)new{warning="A mixed callback must filter draws by actual layer; otherwise geometry is submitted twice."});
			});
		}
		finally{await MainThread.Wait();Cleanup();await waitForActualFrames();}
		Require(result.Count==ExpectedCases,"Native alpha routing inventory changed.");return result.ToArray();

		SceneCustomObject Add(int id,float x,bool opaque,bool translucent,Material material)
		{
			var obj=new SceneCustomObject(scene.SceneWorld);owned.Add(obj);obj.Flags.IsOpaque=opaque;obj.Flags.IsTranslucent=translucent;obj.Flags.CastShadows=false;
			obj.Bounds=new BBox(new global::Vector3(x-.01f,-30,-30),new global::Vector3(x+.01f,30,30));
			obj.Transform=global::Transform.Zero;
			var vertices=new[]{new Vertex(new global::Vector3(x,-30,-30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(x,30,-30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,0,0,0)),new Vertex(new global::Vector3(x,30,30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(x,-30,-30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,0,0,0)),new Vertex(new global::Vector3(x,30,30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(1,1,0,0)),new Vertex(new global::Vector3(x,-30,30),global::Vector3.Forward,global::Vector3.Left,new global::Vector4(0,1,0,0))};
			obj.RenderOverride=_=>
			{
				var identity=Graphics.NativeViewIdentity;var cameraPosition=Graphics.CameraPosition;
				var delta=global::Vector3.Max((cameraPosition-obj.Bounds.Center).Abs()-obj.Bounds.Size*.5f,global::Vector3.Zero);
				events.Enqueue(new(Interlocked.Increment(ref sequence),phase,Application.FrameCount,id,Graphics.LayerType.ToString(),identity?.CameraId,identity?.ViewId,Graphics.ModeFor(material).ToString(),cameraPosition.ToString(),delta.Length));
				try { Graphics.Draw(vertices.AsSpan(),vertices.Length,material); } catch(Exception error) { if(callbackErrors.Count<16)callbackErrors.Enqueue(error.ToString()); }
			};return obj;
		}
		async Task RunCase(string name,Func<Task<(Color[] Image,object Observation)>> body)
		{
			string failure=null;Color[] image=null;object observation=null;var captures=0;phase=name;
			try{events.Clear();callbackErrors.Clear();var before=await capture(name+"_empty_before");captures++;Require(Foreground(before)==0,"Native empty-before control is not empty.");(image,observation)=await body();captures++;Require(callbackErrors.IsEmpty,"Literal native callback failed: "+string.Join("\n",callbackErrors));}
			catch(Exception e){failure=e.ToString();}
			var rows=Rows();Cleanup();await waitForActualFrames();
			try{events.Clear();var after=await capture(name+"_deleted_empty_after");captures++;Require(Foreground(after)==0,"Deleted native objects retained pixels.");Require(events.IsEmpty,"Deleted objects retained callbacks.");}catch(Exception e){failure=(failure??"")+"\nCleanup control: "+e;}
			result.Add(new(name,captures,rows,image is null?0:Foreground(image),failure is null,failure,observation));
		}
		async Task<Color[]> Take(string name){await waitForActualFrames();events.Clear();return await capture(phase+"_"+name);}
		Callback[] Rows()=>events.ToArray().OrderBy(r=>r.Sequence).ToArray();
		void Cleanup()
		{
			foreach(var obj in owned)obj.RenderOverride=null;
			foreach(var obj in owned)
				if(obj.IsValid() && obj.Parent is { } parent && parent.IsValid() && owned.Any(candidate=>ReferenceEquals(candidate,parent)))parent.RemoveChild(obj);
			foreach(var obj in owned)if(obj.IsValid())obj.Delete();
			owned.Clear();using var scope=scene.Push();scene.ProcessDeletes();
		}
		void Configure(int sign){camera.WorldPosition=new global::Vector3(sign*200,0,0);camera.WorldRotation=Rotation.LookAt(-camera.WorldPosition);camera.Orthographic=true;camera.OrthographicHeight=100;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(192,192);camera.BackgroundColor=Color.Black;camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;camera.AutoExposure.Enabled=false;if(camera.SceneCamera is not null)camera.SceneCamera.Tonemap.Enabled=false;}
	}
	private static int Foreground(Color[] pixels){Require(pixels is{Length:>0},"Native capture missing.");return pixels.Count(c=>c.r>.03f||c.g>.03f||c.b>.03f);}
	private static void RequireNative(){var type=typeof(Scene).Assembly.GetType("Sandbox.Rendering.ManagedSceneRendering",true);var property=type.GetProperty("Enabled",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.NonPublic);Require(!(bool)property.GetValue(null),"Native callback evidence cannot be supplied by the managed renderer.");}
	private static void Require(bool ok,string error){if(!ok)throw new InvalidOperationException(error);}
}
