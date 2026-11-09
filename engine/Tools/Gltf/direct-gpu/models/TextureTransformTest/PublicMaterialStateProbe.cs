using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.TextureTransformValidation;

/// <summary>Public stock-material face/depth diagnostics in a private native scene.</summary>
internal static class PublicMaterialStateProbe
{
	private sealed record Observation( string Name, string Image, string Hash, int Coverage, int[] SubmittedMaterials, string[] Shaders, bool[] Overrides, Color[] Pixels );
	private sealed record ImageDifference( int Changed, double Maximum );
	public static async Task<object[]> Run( string checkoutRoot )
	{
		await MainThread.Wait();
		if ( !Application.IsEditor || Game.IsPlaying || Graphics.IsActive ) throw new InvalidOperationException( "Requires idle editor main thread." );
		var output = Path.Combine( checkoutRoot, "Temp/gltf-validation/direct-gpu/public-material-state", Guid.NewGuid().ToString( "N" ) );
		var directory = Path.Combine( Path.GetTempPath(), "gltf_material_state_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output ); Directory.CreateDirectory( directory );
		var aggregate = FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
		LocalFileSystem mount = null; GltfGpuTriangleGateTests.NativeCapture capture = null; var models = new List<Model>(); ModelRenderer renderer = null; SceneObject sphere = null;
		try
		{
			mount = new LocalFileSystem( directory ); aggregate.Mount( mount ); capture = new GltfGpuTriangleGateTests.NativeCapture( output );
			var camera = capture.Camera; camera.DebugMode = SceneCameraDebugMode.NormalMap;
			var empty = await capture.Take( "empty_before" ); Require( empty.Coverage == 0, "Private normal-view scene is not empty." );
			var front = await Draw( "single_front", false, false, "front" ); var back = await Draw( "single_back", true, false, "front" );
			var doubleFront = await Draw( "double_front", false, true, "front" ); var doubleBack = await Draw( "double_back", true, true, "front" );
			var overridden = await Draw( "front_explicit_stock_override", false, false, "front", useOverride: true );
			var frontVsBackNormal = Difference( doubleFront.Pixels, doubleBack.Pixels );
			var facePassed = front.Coverage > 256 && back.Coverage == 0 && doubleFront.Coverage > 256 && doubleBack.Coverage > 256 && overridden.Coverage > 256 && Math.Abs( doubleFront.Coverage - doubleBack.Coverage ) <= 16 && frontVsBackNormal.Changed >= 256;
			camera.DebugMode = SceneCameraDebugMode.Albedo;
			var near = await Draw( "depth_near_reference", false, false, "near" ); var far = await Draw( "depth_far_reference", false, false, "far" );
			var nearFirst = await Draw( "depth_near_then_far", false, false, "near_far" ); var farFirst = await Draw( "depth_far_then_near", false, false, "far_near" );
			var distinguish = Difference( near.Pixels, far.Pixels ); var nearFirstError = Difference( near.Pixels, nearFirst.Pixels ); var farFirstError = Difference( near.Pixels, farFirst.Pixels );
			var depthPassed = near.Coverage > 256 && far.Coverage > 256 && distinguish.Changed > 256 && nearFirstError.Changed <= 16 && farFirstError.Changed <= 16 &&
				nearFirst.SubmittedMaterials.SequenceEqual( new[] { 0, 1 } ) && farFirst.SubmittedMaterials.SequenceEqual( new[] { 1, 0 } );
			var stock = Material.Load( "materials/default/white.vmat" ); Require( stock is { IsValid: true, IsError: false }, "Stock vmdl material is unavailable." );
			sphere = new SceneObject( capture.Scene.SceneWorld, Model.Sphere, new Transform( new Vector3( 0, 20, 20 ), Rotation.Identity, 1 ) ); sphere.SetMaterialOverride( stock );
			var sphereFrame = await capture.Take( "stock_vmdl" ); sphere.Delete(); sphere = null; using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
			var deleted = await capture.Take( "empty_after_vmdl" ); var vmdlPassed = sphereFrame.Coverage > 256 && deleted.Coverage == 0 && !stock.ShaderName.Contains( "gltf_stock_sampler", StringComparison.OrdinalIgnoreCase );
			return new object[] {
				new { Passed = facePassed, Case = "public_front_back_and_double_sided_flat_normals", front = Summary( front ), back = Summary( back ), doubleFront = Summary( doubleFront ), doubleBack = Summary( doubleBack ), explicitStockOverride = Summary( overridden ), normalDifference = frontVsBackNormal, scope = "Normal-view raster coverage, flat-normal reversal and a submitted explicit stock override on one triangle; not mapped-normal parity or raw transformed-UV validation." },
				new { Passed = depthPassed, Case = "public_depth_both_draw_orders", near = Summary( near ), far = Summary( far ), nearFirst = Summary( nearFirst ), farFirst = Summary( farFirst ), distinguish, nearFirstError, farFirstError, scope = "Two overlapping opaque triangles, two source draw orders, and separate near/far color references." },
				new { Passed = vmdlPassed, Case = "stock_vmdl_positive_and_deletion", model = "models/dev/sphere.vmdl", shader = stock.ShaderName, Image = sphereFrame.Path, sphereFrame.Sha256, sphereFrame.Coverage, deleted = deleted.Path, deletedSha256 = deleted.Sha256, scope = "Independent existing stock vmdl and material remain drawable; not before/after vmdl parity." }
			};

			async Task<Observation> Draw( string name, bool reverse, bool doubleSided, string ordering, bool useOverride = false )
			{
				var resource = "gltf_material_state_" + Guid.NewGuid().ToString( "N" ) + ".gltf";
				File.WriteAllText( Path.Combine( directory, resource ), Fixture( reverse, doubleSided, ordering ) );
				var model = Model.Load( resource ); models.Add( model ); Require( model is { IsValid: true, IsError: false } && model.GltfResource is not null && model.native.IsNull, "Public glTF Model.Load failed." );
				using ( capture.Scene.Push() ) { renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false ); renderer.WorldTransform = Transform.Zero; renderer.Model = model; if ( useOverride ) renderer.MaterialOverride = Material.Load( "materials/default/white.vmat" ); renderer.Enabled = true; capture.Scene.EditorTick( 0, 0 ); }
				await capture.WaitFrames(); var instance = renderer.GltfInstance ?? throw new InvalidOperationException( "Public renderer has no glTF instance." );
				var frame = await capture.Take( name ); var rows = instance.Readback().OrderBy( r => r.Node ).ToArray();
				Require( instance.DrawCalls > 0 && instance.CallbackErrors.Length == 0 && rows.Length == (ordering.Contains( '_' ) ? 2 : 1), "Public instance did not submit expected primitive rows." );
				Require( rows.All( r => r.SubmittedMaterial is { IsValid: true, IsError: false } && r.SubmittedMaterialOverride == useOverride && (useOverride ? !r.SubmittedMaterial.ShaderName.Contains( "gltf_stock_sampler", StringComparison.OrdinalIgnoreCase ) : r.SubmittedMaterial.ShaderName.Contains( "gltf_stock_sampler", StringComparison.OrdinalIgnoreCase )) ), "Actual public material or explicit override does not match the selected path." );
				var result = new Observation( name, frame.Path, frame.Sha256, frame.Coverage, rows.Select( r => r.SubmittedMaterialIndex ?? -1 ).ToArray(), rows.Select( r => r.SubmittedMaterial.ShaderName ).ToArray(), rows.Select( r => r.SubmittedMaterialOverride ).ToArray(), frame.Pixels );
				using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); renderer = null; }
				await capture.WaitFrames(); return result;
			}
		}
		finally
		{
			await MainThread.Wait();
			if ( renderer is not null && capture is not null ) using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); }
			sphere?.Delete(); capture?.Dispose(); foreach ( var model in models ) model?.Destroy();
			if ( mount is not null ) { aggregate.UnMount( mount ); mount.Dispose(); } if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}
	private static object Summary( Observation value ) => new { value.Name, value.Image, value.Hash, value.Coverage, value.SubmittedMaterials, value.Shaders, value.Overrides };
	private static ImageDifference Difference( Color[] a, Color[] b )
	{
		Require( a.Length == b.Length, "Image extents differ." ); var changed = 0; double maximum = 0;
		for ( var i = 0; i < a.Length; i++ ) { var d = Math.Max( Math.Abs( a[i].r-b[i].r ), Math.Max( Math.Abs( a[i].g-b[i].g ), Math.Abs( a[i].b-b[i].b ) ) ); maximum = Math.Max( maximum, d ); if ( d > 4f/255f ) changed++; }
		return new( changed, maximum );
	}
	private static string Fixture( bool reverse, bool doubleSided, string ordering )
	{
		var bytes = new byte[78]; var positions = new float[] { 0,0,0, 1,0,0, 0,1,0 }; var normals = new float[] { 0,0,1, 0,0,1, 0,0,1 };
		for ( var i = 0; i < positions.Length; i++ ) { BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( i*4 ), positions[i] ); BinaryPrimitives.WriteSingleLittleEndian( bytes.AsSpan( 36+i*4 ), normals[i] ); }
		ushort[] indices = reverse ? [0,2,1] : [0,1,2]; for ( var i = 0; i < 3; i++ ) BinaryPrimitives.WriteUInt16LittleEndian( bytes.AsSpan( 72+i*2 ), indices[i] );
		var parts = ordering switch { "near" => new[] { 0 }, "far" => new[] { 1 }, "near_far" => new[] { 0,1 }, "far_near" => new[] { 1,0 }, _ => new[] { 0 } };
		var nodes = parts.Select( material => new { mesh = material, translation = new[] { 0f, 0f, material == 0 ? .25f : 0f } } ).ToArray();
		var data = new { asset = new { version = "2.0" }, buffers = new[] { new { uri = "data:application/octet-stream;base64," + Convert.ToBase64String( bytes ), byteLength = bytes.Length } },
			bufferViews = new[] { new { buffer = 0, byteOffset = 0, byteLength = 36 }, new { buffer = 0, byteOffset = 36, byteLength = 36 }, new { buffer = 0, byteOffset = 72, byteLength = 6 } },
			accessors = new object[] { new { bufferView = 0, componentType = 5126, count = 3, type = "VEC3", min = new[] { 0,0,0 }, max = new[] { 1,1,0 } }, new { bufferView = 1, componentType = 5126, count = 3, type = "VEC3" }, new { bufferView = 2, componentType = 5123, count = 3, type = "SCALAR" } },
			materials = new[] { new { doubleSided, pbrMetallicRoughness = new { baseColorFactor = new[] { 1,0,0,1 }, metallicFactor = 0, roughnessFactor = 1 } }, new { doubleSided, pbrMetallicRoughness = new { baseColorFactor = new[] { 0,1,0,1 }, metallicFactor = 0, roughnessFactor = 1 } } },
			meshes = new[] { new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2, material = 0 } } }, new { primitives = new[] { new { attributes = new { POSITION = 0, NORMAL = 1 }, indices = 2, material = 1 } } } }, nodes, scenes = new[] { new { nodes = Enumerable.Range( 0, nodes.Length ).ToArray() } }, scene = 0 };
		return JsonSerializer.Serialize( data );
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
