using System;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Actual mixed-pass deformation and no-op/empty readiness regressions. No alpha-pixel or sorting claim.</summary>
internal static class AlphaMixedPassProbe
{
	public sealed record Observation(float Time,long Revision,int Vertices,double MaximumPositionError,int Callbacks);
	public static async Task<object[]> Run(Scene scene,CameraComponent camera,Material compute,Material opaque,Material blend,Func<Task> wait,Func<string,Task<Color[]>> capture,Action<object> report)
	{
		Require(!opaque.Flags.IsTranslucent&&blend.Flags.IsTranslucent,"Need actual native opaque/blend controls.");
		camera.WorldPosition=new global::Vector3(200,0,0);camera.WorldRotation=Rotation.FromYaw(180);camera.Orthographic=true;camera.OrthographicHeight=100;camera.ZNear=1;camera.ZFar=1000;camera.CustomSize=new global::Vector2(192,192);camera.BackgroundColor=Color.Black;camera.ClearFlags=ClearFlags.All;camera.EnablePostProcessing=true;camera.DebugMode=SceneCameraDebugMode.Normal;
		var result=new List<object>();var errors=new ConcurrentQueue<string>();var callbacks=new ConcurrentQueue<string>();var seen=new HashSet<bool>();var sync=new object();
		Require((await capture("mixed_empty_before")).All(c=>c.r<.03f&&c.g<.03f&&c.b<.03f),"Mixed empty-before is not empty.");
		using var document=GltfReader.Read(Fixture());using var asset=GltfGpuAsset.Create(document,compute,index=>index==0?blend:opaque);using var instance=asset.CreateInstance(scene.SceneWorld);
		instance.SceneObject.Flags.CastShadows=false;
		// Observe the installed producer inside its own reentrant submission gate.
		// This instrumentation serializes observations; uninstrumented scheduling is not proved.
		const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
		var actualRows=((System.Collections.IEnumerable)typeof(GltfGpuInstance).GetField("draws",fields)!.GetValue(instance)!).Cast<object>().ToArray();
		var rowType=actualRows[0].GetType();var rowFields=rowType.GetFields(fields).ToDictionary(f=>f.Name);
		var select=typeof(GltfGpuInstance).GetMethod("DrawsForCurrentView",fields)!;
		var transcript=new ConcurrentQueue<(long Sequence,object Value)>();long sequence=0,callbackSequence=0;int activeCallbacks=0,recordAttempts=0,droppedRecords=0;
		var activeRows=new int[actualRows.Length];var sameRowOverlaps=new ConcurrentQueue<object>();
		void Record(string kind,long callback,int? counterBefore=null,int? counterAfter=null)
		{
			if(Interlocked.Increment(ref recordAttempts)>256){Interlocked.Increment(ref droppedRecords);return;}
			var order=Interlocked.Increment(ref sequence);var graphics=Graphics.IsActive;var identity=graphics?Graphics.NativeViewIdentity:null;
			try
			{
				var selected=graphics?((System.Collections.IEnumerable)select.Invoke(instance,null)!).Cast<object>().Select(row=>Array.IndexOf(actualRows,row)).ToArray():Array.Empty<int>();
				var rows=actualRows.Select((row,index)=>
				{
					var node=(GltfGpuAsset.Node)rowFields["Node"].GetValue(row)!;var material=(Material)rowFields["EffectiveMaterial"].GetValue(row)!;
					return new{runtime_row=index,node_index=node.Index,node.Primitive.MaterialSlot,material=material.Name,shader=material.ShaderName,
						cached_translucent=(bool)rowFields["IsTranslucent"].GetValue(row)!,native_translucent=material.Flags.IsTranslucent,
						cached_alpha_test=(bool)rowFields["IsAlphaTest"].GetValue(row)!,native_alpha_test=material.Flags.IsAlphaTest,
						computed_revision=(long)rowFields["ComputedRevision"].GetValue(row)!,submitted_revision=(long)rowFields["SubmittedRevision"].GetValue(row)!,draw_calls=(int)rowFields["DrawCalls"].GetValue(row)!};
				}).ToArray();
				transcript.Enqueue((order,new{sequence=order,kind,callback,ticks=Stopwatch.GetTimestamp(),thread=Environment.CurrentManagedThreadId,frame=Application.FrameCount,
					active_callbacks=Volatile.Read(ref activeCallbacks),layer=graphics?Graphics.LayerType.ToString():null,camera=identity?.CameraId,view=identity?.ViewId,
					pose_revision=instance.PoseRevision,submitted_revision=instance.SubmittedRevision,global_draw_calls=instance.DrawCalls,counter_before=counterBefore,counter_after=counterAfter,selected,rows}));
			}
			catch(Exception exception){transcript.Enqueue((order,new{sequence=order,kind,callback,instrumentation_error=exception.ToString()}));}
		}
		var gate=typeof(GltfGpuInstance).GetField("submissionGate",fields)!.GetValue(instance)!;
		var gateRun=gate.GetType().GetMethod("Run",BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!;
		var original=instance.SceneObject.RenderOverride;
		instance.SceneObject.RenderOverride=obj=>gateRun.Invoke(gate,new object[]{(Action)(()=>
		{
			var callback=Interlocked.Increment(ref callbackSequence);Interlocked.Increment(ref activeCallbacks);Record("enter",callback);
			var layer=Graphics.LayerType;var selected=((System.Collections.IEnumerable)select.Invoke(instance,null)!).Cast<object>().ToArray();
			var ordinals=selected.Select(row=>Array.IndexOf(actualRows,row)).ToArray();
			foreach(var ordinal in ordinals)
			{
				if(Interlocked.Increment(ref activeRows[ordinal])<=1)continue;
				if(sameRowOverlaps.Count<32)sameRowOverlaps.Enqueue(new{callback,ordinal,thread=Environment.CurrentManagedThreadId,frame=Application.FrameCount,layer=layer.ToString()});
				errors.Enqueue("Same-row native callback overlap prevents this probe from attributing a per-callback row counter; retain as unresolved concurrency evidence.");
			}
			var beforeRows=selected.Select(row=>(int)rowFields["DrawCalls"].GetValue(row)!).ToArray();
			var before=instance.DrawCalls;var after=before;int[] afterRows=null;
			try{original(obj);}finally
			{
				after=instance.DrawCalls;afterRows=selected.Select(row=>(int)rowFields["DrawCalls"].GetValue(row)!).ToArray();
				foreach(var ordinal in ordinals)Interlocked.Decrement(ref activeRows[ordinal]);
				Record("exit",callback,before,after);Interlocked.Decrement(ref activeCallbacks);
			}
			var delta=after-before;
			if(layer is not(SceneLayerType.Opaque or SceneLayerType.DepthPrepass or SceneLayerType.Translucent))return;
			var expectedNode=layer==SceneLayerType.Translucent?0:1;
			if(selected.Length!=1||((GltfGpuAsset.Node)rowFields["Node"].GetValue(selected[0])!).Index!=expectedNode)
				errors.Enqueue("Actual native pass selected the wrong source row in "+layer);
			for(var row=0;row<selected.Length;row++)
			{
				var material=(Material)rowFields["EffectiveMaterial"].GetValue(selected[row])!;
				if((bool)rowFields["IsTranslucent"].GetValue(selected[row])! != (layer==SceneLayerType.Translucent)||material.Flags.IsTranslucent!=(layer==SceneLayerType.Translucent))errors.Enqueue("Actual cached/native effective material disagrees with pass in "+layer);
				if(afterRows[row]-beforeRows[row]!=1)errors.Enqueue("Expected exactly one selected-row draw in "+layer+", got "+(afterRows[row]-beforeRows[row]));
			}
			// Other native classes may complete concurrently before this wrapper's
			// bookkeeping. The producer's actual row revisions define completion.
			if(instance.SubmittedRevision==instance.PoseRevision&&actualRows.Any(row=>(long)rowFields["SubmittedRevision"].GetValue(row)! != instance.PoseRevision))errors.Enqueue("Aggregate claims complete pose while an actual required row is stale.");
			lock(sync)
			{
				callbacks.Enqueue(layer+":global_delta="+delta+":selected_deltas="+string.Join(",",afterRows.Select((value,index)=>value-beforeRows[index])));
				seen.Add(layer==SceneLayerType.Translucent);
			}
		})});
		try
		{
			foreach(var time in new[]{0f,.5f,1f})
			{
				await MainThread.Wait();lock(sync){seen.Clear();callbacks.Clear();}instance.SetPose(document.EvaluatePose(0,time));Record("pose_applied",0);
				Require(instance.SubmittedRevision!=instance.PoseRevision,"New nonempty pose was marked submitted without drawing.");
				var pixels=await capture("mixed_pose_"+time.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture));await wait();
				Require(pixels.Any(c=>c.r>.03f||c.g>.03f||c.b>.03f),"Mixed positive control rendered no pixels.");
				Require(errors.IsEmpty,"Mixed native submission: "+string.Join("\n",errors));
				Require(instance.CallbackErrors.Length==0&&instance.SubmittedRevision==instance.PoseRevision,"Both native classes did not complete current pose.");
				lock(sync)Require(seen.Count==2,"Did not observe both native pass classes.");
				var rows=instance.Readback();Require(rows.Count==2,"Expected two independently deformed rows.");double max=0;var vertices=0;
				foreach(var row in rows)
				{
					Require(row.Node is 0 or 1&&row.SubmittedRevision==instance.PoseRevision,"Wrong node/current draw revision.");
					var position=row.Fields.Single(f=>f.Semantic=="POSITION"&&f.SemanticIndex==0);
					Require(position.ComponentType==5126&&position.Components>=3,"Expected declared FLOAT3 GPU output.");
					(float X,float Y)[] corners=[(-.5f,-.5f),(.5f,-.5f),(.5f,.5f),(-.5f,-.5f),(.5f,.5f),(-.5f,.5f)];
					for(var vertex=0;vertex<row.VertexCount;vertex++)
					{
						var source=row.SourceVertices[vertex];Require(source<6,"Unexpected literal corner mapping.");var corner=corners[source];
						// Literal animation: node0 source X += .25*t; node1 source Y += .5*t.
						var expected=new[]{(row.Node==0?.1:0)/.0254,(corner.X+(row.Node==0?.25*time:0))/.0254,(corner.Y+(row.Node==1?.5*time:0))/.0254};
						for(var component=0;component<3;component++){var actual=BinaryPrimitives.ReadSingleLittleEndian(row.Bytes.AsSpan(vertex*row.VertexStride+position.Offset+component*4,4));Require(float.IsFinite(actual),"Nonfinite GPU position.");max=Math.Max(max,Math.Abs(actual-expected[component]));}vertices++;
					}
				}
				Require(max<=.0001,"Mixed animated GPU outputs are stale or wrong: "+max);result.Add(new Observation(time,instance.PoseRevision,vertices,max,callbacks.Count));
				// Same effective vector through explicit commands must preserve readiness
				// synchronously, without relying on another render to repair it.
				var revision=instance.SubmittedRevision;var calls=instance.DrawCalls;
				for(var repeat=0;repeat<3;repeat++){instance.ClearMaterialOverrides();instance.SetMaterialOverride((Material)null);instance.SetMaterialOverride(0,blend);instance.SetMaterialOverride(1,opaque);Require(instance.SubmittedRevision==revision&&instance.DrawCalls==calls&&instance.Readback().Count==2,"No-op effective overrides invalidated successful draw evidence.");}
			}
		}
		finally
		{
			await MainThread.Wait();instance.SceneObject.RenderOverride=original;
			try{report(new{phase="mixed_pass_callback_transcript",event_limit=256,dropped_records=droppedRecords,callback_count=callbackSequence,events=transcript.OrderBy(x=>x.Sequence).Select(x=>x.Value).ToArray(),same_row_overlap=sameRowOverlaps.ToArray(),assertion_errors=errors.ToArray(),layer_deltas=callbacks.ToArray()});}
			finally{instance.Dispose();using(scene.Push())scene.ProcessDeletes();}
		}
		await wait();Require((await capture("mixed_deleted_empty_after")).All(c=>c.r<.03f&&c.g<.03f&&c.b<.03f),"Deleted mixed instance retained pixels.");return result.ToArray();
	}
	public static object Empty(SceneWorld world,Material compute,Material opaque)
	{
		using var document=GltfReader.Read(Encoding.UTF8.GetBytes("{\"asset\":{\"version\":\"2.0\"},\"scene\":0,\"scenes\":[{}]}"));using var asset=GltfGpuAsset.Create(document,compute,_=>opaque);using var instance=asset.CreateInstance(world);
		for(var repeat=0;repeat<3;repeat++)
		{
			instance.SetPose(document.EvaluatePose());instance.ClearMaterialOverrides();instance.SetMaterialOverride(opaque);instance.SetMaterialOverride((Material)null);
			Require(!instance.SceneObject.Flags.IsOpaque&&!instance.SceneObject.Flags.IsTranslucent&&instance.DrawCalls==0&&instance.CallbackErrors.Length==0&&instance.SubmittedRevision==instance.PoseRevision&&instance.Readback().Count==0,"Empty readiness must be vacuous with zero draw calls and no material/pass flags.");
		}
		return new{scope="empty instance vacuous readiness; no GPU submission claimed",instance.PoseRevision,instance.SubmittedRevision,instance.DrawCalls};
	}
	private static byte[] Fixture()
	{
		var root=JsonNode.Parse(AlphaControlFixtures.All().Single(x=>x.Name=="two_separated_blend_layers_near_first").Bytes)!;root["materials"]![1]!["alphaMode"]="OPAQUE";
		using var buffer=new MemoryStream();using(var writer=new BinaryWriter(buffer,Encoding.UTF8,true))foreach(var value in new[]{0f,1f,0f,0f,0f,.25f,0f,0f,0f,0f,0f,0f,.5f,0f})writer.Write(value);
		var buffers=root["buffers"]!.AsArray();var bufferIndex=buffers.Count;buffers.Add(new JsonObject{["byteLength"]=buffer.Length,["uri"]="data:application/octet-stream;base64,"+Convert.ToBase64String(buffer.ToArray())});
		var views=root["bufferViews"]!.AsArray();var accessors=root["accessors"]!.AsArray();var first=accessors.Count;
		foreach(var (offset,length,type) in new[]{(0,8,"SCALAR"),(8,24,"VEC3"),(32,24,"VEC3")}){var view=views.Count;views.Add(new JsonObject{["buffer"]=bufferIndex,["byteOffset"]=offset,["byteLength"]=length});var accessor=new JsonObject{["bufferView"]=view,["componentType"]=5126,["count"]=2,["type"]=type};if(type=="SCALAR"){accessor["min"]=new JsonArray(0);accessor["max"]=new JsonArray(1);}accessors.Add(accessor);}
		root["animations"]=new JsonArray(new JsonObject{["samplers"]=new JsonArray(new JsonObject{["input"]=first,["output"]=first+1,["interpolation"]="LINEAR"},new JsonObject{["input"]=first,["output"]=first+2,["interpolation"]="LINEAR"}),["channels"]=new JsonArray(new JsonObject{["sampler"]=0,["target"]=new JsonObject{["node"]=0,["path"]="translation"}},new JsonObject{["sampler"]=1,["target"]=new JsonObject{["node"]=1,["path"]="translation"}})});
		return Encoding.UTF8.GetBytes(root.ToJsonString());
	}
	private static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
}
