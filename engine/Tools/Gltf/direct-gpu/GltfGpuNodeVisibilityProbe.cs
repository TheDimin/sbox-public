using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Opt-in GPU probe using the coordinator's owned scene, material and capture.
/// It checks actual draw counters and pixels; source-material and light visibility
/// are separate gates. Never used to replace the public Model.Load test.
/// </summary>
internal static class GltfGpuNodeVisibilityProbe
{
	public static async Task Run( SceneWorld world, Material compute, Material material, Func<Task> waitFrames, Func<string, Task<Color[]>> capture )
	{
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( compute ); ArgumentNullException.ThrowIfNull( material );
		ArgumentNullException.ThrowIfNull( waitFrames ); ArgumentNullException.ThrowIfNull( capture );
		using var document = Fixture();
		using var asset = GltfGpuAsset.Create( document, compute, _ => material );
		using var first = asset.CreateInstance( world );
		using var second = asset.CreateInstance( world );
		second.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = false, [2] = false } );
		Assert.IsTrue( Coverage( await capture( "visibility_initial_sibling" ) ) > 64 );
		await waitFrames();
		var initial = first.Readback().ToDictionary( row => row.Node );
		Assert.AreEqual( 2, initial.Count ); Assert.IsFalse( initial[1].Visible ); Assert.IsTrue( initial[2].Visible );
		Assert.AreEqual( -1L, initial[1].SubmittedRevision, "An initially hidden row must never claim a draw." );
		Assert.IsNull( initial[1].Bytes ); Assert.IsNull( initial[1].Indices ); Assert.IsNull( initial[1].IndexDataOrigin );
		Assert.AreEqual( first.PoseRevision, initial[2].SubmittedRevision );

		first.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = false, [2] = false } );
		Assert.AreEqual( 0, Coverage( await capture( "visibility_all_hidden" ) ) );
		var hiddenCalls = first.DrawCalls + second.DrawCalls;
		await waitFrames();
		Assert.AreEqual( hiddenCalls, first.DrawCalls + second.DrawCalls, "Hidden rows must issue no additional draws." );
		Assert.IsTrue( first.Readback().All( row => !row.Visible && row.Bytes is null && row.Indices is null && row.IndexDataOrigin is null ) );

		first.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = true, [2] = false } );
		Assert.IsTrue( Coverage( await capture( "visibility_revealed_child" ) ) > 64 );
		var revealed = first.Readback().ToDictionary( row => row.Node );
		Assert.IsTrue( revealed[1].Visible ); Assert.IsFalse( revealed[2].Visible );
		Assert.AreEqual( first.PoseRevision, revealed[1].ComputedRevision );
		Assert.AreEqual( first.PoseRevision, revealed[1].SubmittedRevision, "Reveal must submit even without a pose change." );
		Assert.IsTrue( second.Readback().All( row => !row.Visible ), "Instance overrides must be independent." );
		Assert.IsTrue( first.CallbackErrors.Length == 0 && second.CallbackErrors.Length == 0 );

		second.SetNodeVisibilityOverrides( new Dictionary<int, bool> { [0] = false, [2] = true } );
		Assert.IsTrue( Coverage( await capture( "visibility_both_instances" ) ) > 64 );
		first.Dispose();
		Assert.IsTrue( Coverage( await capture( "visibility_remaining_instance" ) ) > 64, "Disposing one instance must not remove the other." );
		Assert.IsTrue( second.Readback().Any( row => row.Visible && row.SubmittedRevision == second.PoseRevision ) );
		second.Dispose();
		Assert.AreEqual( 0, Coverage( await capture( "visibility_after_disposal" ) ), "Disposed instances must leave no visible geometry." );
	}

	private static int Coverage( Color[] pixels ) => pixels.Count( p => p.r > .03f || p.g > .03f || p.b > .03f );

	private static GltfDocument Fixture()
	{
		Assert.IsTrue( BitConverter.IsLittleEndian );
		float[] vertices = [-.4f, -.4f, 0, .4f, -.4f, 0, 0, .4f, 0];
		var bytes = new byte[vertices.Length * sizeof(float)]; Buffer.BlockCopy( vertices, 0, bytes, 0, bytes.Length );
		var encoded = Convert.ToBase64String( bytes );
		var json = $$$"""
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_node_visibility"],"scene":0,
			 "buffers":[{"uri":"data:application/octet-stream;base64,{{{encoded}}}","byteLength":36}],
			 "bufferViews":[{"buffer":0,"byteLength":36}],
			 "accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.4,-0.4,0],"max":[0.4,0.4,0]}],
			 "meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],
			 "nodes":[{"children":[1],"extensions":{"KHR_node_visibility":{"visible":false}}},
			 {"mesh":0,"translation":[-0.7,0,0],"extensions":{"KHR_node_visibility":{"visible":true}}},
			 {"mesh":0,"translation":[0.7,0,0]}],"scenes":[{"nodes":[0,2]}]}
			""";
		return GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_node_visibility" } );
	}
}
