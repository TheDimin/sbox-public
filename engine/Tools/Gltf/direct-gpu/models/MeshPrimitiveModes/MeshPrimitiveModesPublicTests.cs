using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using NMatrix = System.Numerics.Matrix4x4;
using static Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation.MeshPrimitiveModesReference;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

[TestClass]
internal sealed class MeshPrimitiveModesPublicTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task PublicWholeSceneAndBoundIndexControls( bool compiled )
	{
		await MainThread.Wait();
		var root = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/MeshPrimitiveModes/glTF" );
		var source = File.ReadAllBytes( Path.Combine( root, "MeshPrimitiveModes.gltf" ) );
		var bin = File.ReadAllBytes( Path.Combine( root, "buffer.bin" ) );
		Require( Hash( source ) == "2a8b3209c25e2fca5105539136a05f42d170767f0eb16148cd3825758e5ee977" && Hash( bin ) == "4369522e5929bed650242fea069c687aa2ea1230caa61c378610b2154c1048d7", "Pinned input changed." );
		byte[] Resolve( string uri ) { Require( uri == "buffer.bin", "Unexpected dependency." ); return bin; }
		var bytes = compiled ? GltfCompiler.Compile( source, Resolve ) : source;
		var form = new Inputs( checkout ).Get( compiled ? "glTF/compiled_glb" : "glTF/source" );
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/MeshPrimitiveModes/public", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		var directory = Path.Combine( Path.GetTempPath(), "gltf_modes_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( directory );
		var name = "modes_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
		File.WriteAllBytes( Path.Combine( directory, name ), bytes ); if ( !compiled ) File.WriteAllBytes( Path.Combine( directory, "buffer.bin" ), bin );
		LocalFileSystem mount = null; Model model = null; ModelRenderer renderer = null;
		var references = new List<MeshPrimitiveModesReferenceScene>();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, new Vector3( 500, 0, 0 ), Vector3.Zero, 360 );
		var frames = new List<object>();
		try
		{
			mount = new LocalFileSystem( directory ); FileSystem.Mounted.Mount( mount );
			var empty = await Take( "empty_before" ); MeshPrimitiveModesImageChecks.RequireEmpty( empty.Pixels );
			model = Model.Load( name ); Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public load failed or created native model." );
			Require( model.GltfResource.Asset.MeshCount == 7 && model.GltfSelectedScene == 0, "Selected scene lost primitive instances." );
			var material = model.Materials.Single(); Require( material is { IsValid: true, IsError: false }, "Default material unavailable." );
			for ( int mode = 0; mode < 7; mode++ ) references.Add( new( capture.Scene.SceneWorld, mode, NMatrix.Identity, material ) );
			var reference = await Take( "independent_literal_reference" ); Require( reference.Coverage > 0 && references.All( r => r.DrawCalls > 0 && r.CallbackErrors.Length == 0 ), "Reference primitives did not submit visibly." );
			ClearReferences(); MeshPrimitiveModesImageChecks.RequireEmpty( (await Take( "empty_after_reference" )).Pixels );
			for ( int mode = 0; mode < 7; mode++ ) references.Add( new( capture.Scene.SceneWorld, mode, NMatrix.Identity, material, true ) );
			var negative = await Take( "wrong_topology_reference" ); ClearReferences(); MeshPrimitiveModesImageChecks.RequireEmpty( (await Take( "empty_after_negative" )).Pixels );
			using ( capture.Scene.Push() ) { renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false ); renderer.Model = model; renderer.WorldTransform = Transform.Zero; renderer.Enabled = true; capture.Scene.EditorTick( 0, 0 ); }
			var instance = renderer.GltfInstance; Require( instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null, "Public renderer did not use managed GPU buffers." );
			var actual = await Take( "public_source_material" );
			Require( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision && instance.CallbackErrors.Length == 0, "Public draw failed: " + string.Join( "\n", instance.CallbackErrors ) );
			var snapshots = instance.Readback(); Require( snapshots.Count == 7, "Expected seven actual GPU snapshots." );
			var readback = JsonSerializer.SerializeToUtf8Bytes( snapshots.Select( x => new { x.Node, x.Mesh, x.Primitive, x.SourceMode, x.SubmittedPrimitiveType, x.DrawCalls, x.VertexCount, x.VertexStride, x.Fields, x.Bytes, x.SourceVertices, x.Indices, x.IndexDataOrigin } ).ToArray() );
			var readbackPath = Path.Combine( output, "gpu-readback.json" ); File.WriteAllBytes( readbackPath, readback );
			Measurements.Add( new { compiled, input_sha256 = Hash( bytes ), readbackPath, readback_sha256 = Hash( readback ), frames, material = material.ShaderName } );
			foreach ( var row in snapshots )
			{
				var expected = row.Node == 0 ? Graphics.PrimitiveType.Points : row.Node < 4 ? Graphics.PrimitiveType.Lines : Graphics.PrimitiveType.Triangles;
				Require( row.SourceMode == row.Node && row.SubmittedPrimitiveType == expected && row.DrawCalls > 0, "Wrong submitted topology." );
				MeshPrimitiveModesGpuChecks.AcceptNumeric( MeshPrimitiveModesGpuChecks.Measure( form, row.Node, NMatrix.Identity, [row] ) );
			}
			var image = MeshPrimitiveModesImageChecks.Measure( -1, actual.Pixels, reference.Pixels, negative.Pixels );
			Measurements.Add( new { compiled, image, frames } );
			var draws = ((IEnumerable)typeof( GltfGpuInstance ).GetField( "draws", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( instance )).Cast<object>().ToArray();
			Require( draws.Length == 7, "Expected seven actual draw buffers." );
			foreach ( var draw in draws )
			{
				var indices = (GpuBuffer)draw.GetType().GetField( "Indices", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( draw );
				// Match buffer to a unique original index sequence by direct GPU bytes.
				var nodeRecord = (GltfGpuAsset.Node)draw.GetType().GetField( "Node", BindingFlags.Instance | BindingFlags.NonPublic ).GetValue( draw );
				var node = nodeRecord.Index;
				var row = snapshots.Single( x => x.Node == node ); var original = row.Indices.ToArray(); var bad = original.ToArray(); bad[0] = bad[0] == 0 ? 1u : 0u;
				try { indices.SetData<uint>( bad ); await capture.WaitFrames(); var changed = instance.Readback().Single( x => x.Node == node ); Require( changed.Indices.SequenceEqual( bad ), "Actual bound index mutation not observed." ); bool rejected = false; try { MeshPrimitiveModesGpuChecks.Measure( form, node, NMatrix.Identity, [changed] ); } catch ( InvalidDataException ) { rejected = true; } Require( rejected, "Checker accepted corrupted bound indices." ); Measurements.Add( new { compiled, node, actual_gpu_index_negative = bad, rejected } ); }
				finally { indices.SetData<uint>( original ); await capture.WaitFrames(); }
				MeshPrimitiveModesGpuChecks.AcceptNumeric( MeshPrimitiveModesGpuChecks.Measure( form, node, NMatrix.Identity, [instance.Readback().Single( x => x.Node == node )] ) );
			}
			MeshPrimitiveModesImageChecks.AcceptExactGeometry( image );
			DestroyRenderer(); var calls = instance.DrawCalls; MeshPrimitiveModesImageChecks.RequireEmpty( (await Take( "empty_after_public_delete" )).Pixels ); Require( calls == instance.DrawCalls, "Deleted renderer still draws." );
			Measurements.Add( new { compiled, completed = true, frames, scope = "Public Model.Load and ModelRenderer; source material, GPU output and actual bound indices; independent literal topology and deletion controls. Upstream visual review remains." } );
		}
		finally
		{
			await MainThread.Wait(); ClearReferences(); DestroyRenderer(); if ( model is not null && model.ResourcePath == Resource.FixPath( name ) ) model.Destroy();
			if ( mount is not null ) { FileSystem.Mounted.UnMount( mount ); mount.Dispose(); } Directory.Delete( directory, true );
			Measurements.Add( new { compiled, frames, output } );
		}
		async Task<GltfGpuTriangleGateTests.NativeCapture.Frame> Take( string label ) { var f = await capture.Take( label ); frames.Add( new { label, f.Path, f.Sha256, f.Coverage, f.Width, f.Height, f.CameraStateJson } ); return f; }
		void ClearReferences() { foreach ( var r in references ) r.Dispose(); references.Clear(); using var scope = capture.Scene.Push(); capture.Scene.ProcessDeletes(); }
		void DestroyRenderer() { if ( renderer is null ) return; using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); renderer = null; }
	}
}
