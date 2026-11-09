using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.MeshoptCubeValidation;

using static MeshoptCubeReference;
using Frame = GltfGpuTriangleGateTests.NativeCapture.Frame;

/// <summary>Public loader/component proof against the independently decoded upstream fallback.</summary>
[TestClass]
internal sealed class MeshoptCubePublicTests
{
	private static string checkout;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string root ) { checkout = root; Measurements.Clear(); }

	[TestMethod]
	[DataRow( "glTF/source" )]
	[DataRow( "glTF/compiled_glb_c" )]
	[DataRow( "glTF-Meshopt/source" )]
	[DataRow( "glTF-Meshopt/compiled_glb_c" )]
	public async Task PublicSourceMaterialRendererAndIndependentCubeOracle( string formName )
	{
		await MainThread.Wait();
		var reference = new MeshoptCubeReference( checkout );
		var form = reference.Forms.Single( row => row.Name == formName );
		var output = Path.Combine( checkout, "Temp/gltf-validation/direct-gpu/MeshoptCubeTest/public", Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( output );
		var directory = Path.Combine( Path.GetTempPath(), "gltf_meshopt_public_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		LocalFileSystem mount = null; Model model = null; ModelRenderer renderer = null; LiteralDraw literal = null;
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, new global::Vector3( 1600, 250, 370 ), new global::Vector3( 0, 250, 370 ), 1100 );
		capture.Camera.ZFar = 3000;
		var light = new SceneDirectionalLight( capture.Scene.SceneWorld, capture.Camera.WorldRotation, Color.White * 2 );
		var frames = new List<object>();
		try
		{
			foreach ( var file in Directory.EnumerateFiles( Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/MeshoptCubeTest", form.Variant ) ) )
				if ( Path.GetExtension( file ) == ".png" || (!form.Compiled && Path.GetExtension( file ) == ".bin") )
					File.Copy( file, Path.Combine( directory, Path.GetFileName( file ) ) );
			var name = "meshopt_cube_" + Guid.NewGuid().ToString( "N" ) + (form.Compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, name ), form.Bytes );
			mount = new LocalFileSystem( directory ); FileSystem.Mounted.Mount( mount );
			model = Model.Load( name );
			Require( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null, "Public Model.Load must use the actual managed backend." );
			// Diagnostic seeks must use the public resource's own generation. A
			// separately parsed input document correctly fails the ownership gate.
			var document = model.GltfResource.Asset.Document;
			Require( model.GltfResource.Asset.MeshCount == PrimitiveCount && model.GltfSelectedScene == 0, "Public selected scene lost authored node primitives." );
			Require( ReferenceEquals( model, Model.Load( name ) ), "Repeated public load lost immutable resource identity." );
			var materials = model.Materials;
			Require( materials.Length == 11 && model.GltfResource.MaterialSlotSourceIndices.SequenceEqual( Enumerable.Range( 0, 10 ).Select( i => (int?)i ).Append( null ) ) && materials.All( m => m is { IsValid: true, IsError: false } ), "Ten authored label materials and implicit cube material must realize unchanged." );
			foreach ( var material in materials.Take( 10 ) )
			{
				Require( material.GetFeature( "F_ALPHA_TEST" ) == 1 && material.GetFeature( "F_TRANSLUCENT" ) == 0 && material.GetFeature( "F_RENDER_BACKFACES" ) == 1,
					"Authored double-sided MASK labels must retain their alpha-test material specialization." );
				Require( material.HasParameter( "g_flGltfAlphaCutoff" ) && material.GetVector4( "g_flGltfAlphaCutoff" ).x == .001f && material.GetVector4( "g_vGltfBaseColor" ).w == 1,
					"The native source material must retain authored MASK cutoff .001 and base-color alpha one." );
				var textureParameter = material.HasParameter( "g_tGltfBaseColorTexture" ) ? "g_tGltfBaseColorTexture" : "g_tColor";
				var texture = material.GetTexture( textureParameter );
				Require( texture is { IsValid: true, IsError: false }, "Source label texture did not realize." );
				texture.MarkUsed( 1024 );
			}
			var empty = await Take( "empty_before" ); Empty( empty );
			literal = new LiteralDraw( capture.Scene.SceneWorld, reference, materials.ToArray() );
			var expected = await Take( "independent_fallback_literal_geometry_source_material" ); Positive( expected );
			Require( literal.DrawCalls > 0 && literal.Errors.IsEmpty, "Independent literal geometry failed to submit." );
			RemoveLiteral();
			Empty( await Take( "empty_after_reference" ) );
			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.Model = model; renderer.WorldTransform = Transform.Zero; renderer.Enabled = true; capture.Scene.EditorTick( 0, 0 );
			}
			var instance = renderer.GltfInstance;
			Require( instance is not null && renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null && ReferenceEquals( instance.SceneObject, renderer.SceneObject ), "Public renderer must own the buffer-backed custom object without a native model." );
			var actual = await Take( "actual_public_source_material_rest" ); Positive( actual );
			Require( instance.CallbackErrors.Length == 0 && instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision, "Public renderer did not submit its current revision." );
			var snapshots = instance.Readback();
			var readbackBytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes( snapshots.Select( row => new { row.Node, row.Mesh, row.Primitive, row.VertexCount, row.VertexStride, row.Fields, row.Bytes, row.SourceVertices, row.Indices, row.IndexDataOrigin, row.DrawCalls, row.ComputedRevision, row.SubmittedRevision } ).ToArray() );
			var readbackPath = Path.Combine( output, "actual_gpu_rest.json" ); File.WriteAllBytes( readbackPath, readbackBytes );
			Measurements.Add( new { form = formName, actual_gpu_readback = readbackPath, sha256 = Hash( readbackBytes ), index_origin = "actual bound GPU readback", fields = "explicit per-row descriptors, bytes, source correspondence metadata and actual indices" } );
			var numeric = MeshoptCubeGpuChecks.AssertReadback( reference, snapshots, null );
			var image = Compare( expected, actual );
			Measurements.Add( new { form = formName, output, input_sha256 = Hash( form.Bytes ), independent_reference_sha256 = Hash( File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/MeshoptCubeTest/reference.json" ) ) ), public_resource = model.ResourcePath, numeric, image,
				actual_path = actual.Path, actual_sha256 = actual.Sha256, reference_path = expected.Path, reference_sha256 = expected.Sha256,
				source_material_shaders = materials.Select( m => m.ShaderName ).ToArray(),
				source_label_material_state = materials.Take( 10 ).Select( m => new { shader = m.ShaderName, alpha_test = m.GetFeature( "F_ALPHA_TEST" ), translucent = m.GetFeature( "F_TRANSLUCENT" ), backfaces = m.GetFeature( "F_RENDER_BACKFACES" ), cutoff = m.GetVector4( "g_flGltfAlphaCutoff" ).x, factor_alpha = m.GetVector4( "g_vGltfBaseColor" ).w } ).ToArray(),
				compute_shader = model.GltfResource.Asset.ComputeMaterial?.ShaderName,
				scope = "Actual public Model.Load and ordinary ModelRenderer, all GPU positions/normals/colors/UVs and bound indices against Python fallback oracle; same-source-material independent literal geometry image. Inferred orthographic reference framing, not exact upstream camera or independent PBR material fidelity." } );
			Require( image.Iou >= .98 && image.MeanRgbByteError <= 1 && image.MaximumRgbByteError <= 8, "Actual public image differs from independent literal geometry/source bindings." );
			foreach ( var time in PoseTimes )
			{
				instance.SetPose( document.EvaluatePose( 0, time ) ); await capture.WaitFrames();
				var pose = MeshoptCubeGpuChecks.AssertReadback( reference, instance.Readback(), time );
				Measurements.Add( new { form = formName, source_time = time, actual_gpu = pose, instance.PoseRevision, instance.SubmittedRevision, instance.DrawCalls } );
			}
			// A true bound-GPU index mutation, without changing CPU topology metadata,
			// must fail the same independent checker and recover after restoration.
			var draws = (System.Collections.IEnumerable)typeof( GltfGpuInstance ).GetField( "draws", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( instance );
			var draw = draws.Cast<object>().First();
			var indices = (GpuBuffer)draw.GetType().GetField( "Indices", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic ).GetValue( draw );
			var original = new uint[reference.Indices( 0 ).Length]; indices.GetData<uint>( original );
			foreach ( var control in new[] { "reversed_winding", "changed_index" } )
			{
				var revision = instance.PoseRevision;
				try
				{
					var bad = original.ToArray();
					if ( control == "reversed_winding" ) (bad[0], bad[1]) = (bad[1], bad[0]); else bad[0] = original[2];
					indices.SetData<uint>( bad ); await capture.WaitFrames();
					var observed = instance.Readback(); var observedLabel = observed.Single( row => row.Node == 0 );
					Require( observedLabel.IndexDataOrigin == "gpu-readback" && observedLabel.Indices.SequenceEqual( bad ) && instance.PoseRevision == revision, "GPU index negative did not mutate exactly the owned bound indices." );
					var rejected = false; try { MeshoptCubeGpuChecks.AssertReadback( reference, observed, 2.25f ); } catch ( InvalidOperationException ) { rejected = true; }
					Require( rejected, "Independent checker accepted actual corrupted GPU topology/winding." );
					Measurements.Add( new { form = formName, actual_gpu_negative = control, expected = original, actual = observedLabel.Indices, observedLabel.IndexDataOrigin, rejected, revision } );
				}
				finally { indices.SetData<uint>( original ); await capture.WaitFrames(); }
				MeshoptCubeGpuChecks.AssertReadback( reference, instance.Readback(), 2.25f );
			}
			using ( capture.Scene.Push() ) renderer.MaterialOverride = capture.Material;
			var negative = await Take( "white_material_override_negative" ); Positive( negative );
			int changed = actual.Pixels.Zip( negative.Pixels, (a,b) => Math.Abs(a.r-b.r) > .02f || Math.Abs(a.g-b.g) > .02f || Math.Abs(a.b-b.b) > .02f ).Count( value => value );
			Require( changed > 64, "Source labels/colors did not differ from the white-material negative." );
			using ( capture.Scene.Push() ) renderer.MaterialOverride = null;
			instance.SetPose( document.EvaluatePose() );
			var restored = await Take( "restored_public_source_material" ); Positive( restored );
			Require( Compare( actual, restored ).MaximumRgbByteError <= 1, "Restoring the source material/pose failed." );
			var retained = instance; DestroyRenderer(); int calls = retained.DrawCalls;
			Empty( await Take( "empty_after_public_delete" ) ); Require( retained.DrawCalls == calls, "Deleted renderer continued drawing." );
			Require( model.IsValid && ReferenceEquals( model, Model.Load( name ) ), "Renderer deletion invalidated the cached model." );
			Measurements.Add( new { form = formName, completed = true, actual_gpu_pose_samples = PoseTimes.Length + 1, corrupted_actual_gpu_indices_rejected_and_restored = true, material_negative_changed_pixels = changed, cleanup_deleted_draw_stopped = true, frames } );
		}
		finally
		{
			await MainThread.Wait();
			try { RemoveLiteral(); DestroyRenderer(); model?.Destroy(); if ( light.IsValid() ) light.Delete(); }
			finally { try { if ( mount is not null ) FileSystem.Mounted.UnMount( mount ); } finally { mount?.Dispose(); Directory.Delete( directory, true ); } }
			Measurements.Add( new { form = formName, frames, native_captures = GltfGpuTriangleGateTests.Measurements.ToArray() } );
		}
		async Task<Frame> Take( string label ) { var f = await capture.Take( label ); f = f with { Pixels = f.Pixels.ToArray() }; frames.Add( new { label, f.Path, f.Sha256, f.Coverage, f.CameraStateJson, f.EngineFrame } ); return f; }
		void Empty( Frame f ) => Require( f.Coverage == 0, "Fresh empty/deletion control retained geometry." );
		void Positive( Frame f ) => Require( f.Coverage > 64 && !f.Pixels.Any( p => p.r > .8f && p.g < .2f && p.b > .8f ), "Positive capture empty or magenta sentinel present." );
		void RemoveLiteral() { literal?.Dispose(); literal = null; using var scope = capture.Scene.Push(); capture.Scene.ProcessDeletes(); }
		void DestroyRenderer() { if ( renderer is null ) return; using var scope = capture.Scene.Push(); renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); renderer = null; }
	}

	private sealed record ImageMeasurement( double Iou, double MeanRgbByteError, double MaximumRgbByteError, int DifferentPixels );
	private static ImageMeasurement Compare( Frame a, Frame b )
	{
		Require( a.CameraStateJson == b.CameraStateJson && a.Width == b.Width && a.Height == b.Height, "Reference camera/presentation changed." );
		int intersection = 0, union = 0, different = 0; double sum = 0, maximum = 0;
		for ( int i = 0; i < a.Pixels.Length; i++ )
		{
			var x = a.Pixels[i]; var y = b.Pixels[i]; bool av = GltfGpuTriangleGateTests.NativeCapture.Foreground( x ), bv = GltfGpuTriangleGateTests.NativeCapture.Foreground( y );
			if ( av && bv ) intersection++; if ( av || bv ) union++;
			var errors = new[] { Math.Abs( x.r-y.r ) * 255, Math.Abs( x.g-y.g ) * 255, Math.Abs( x.b-y.b ) * 255 };
			sum += errors.Sum(); maximum = Math.Max( maximum, errors.Max() ); if ( errors.Any( e => e > .001 ) ) different++;
		}
		return new( union == 0 ? 0 : (double)intersection / union, sum / (a.Pixels.Length * 3), maximum, different );
	}

	private sealed class LiteralDraw : IDisposable
	{
		private readonly List<SceneCustomObject> objects = new();
		public readonly ConcurrentQueue<string> Errors = new();
		public int DrawCalls;
		public LiteralDraw( SceneWorld world, MeshoptCubeReference reference, Material[] materials )
		{
			for ( int node = 0; node < NodeCount; node++ )
			{
				var primitive = reference.Primitive( node ); var attributes = primitive.GetProperty( "attributes" );
				var triangles = reference.Indices( node ).Select( source =>
				{
					var p = reference.Engine( node, (int)source, null ); var n = reference.Engine( node, (int)source, null, true );
					var normal = new global::Vector3( (float)n[0], (float)n[1], (float)n[2] );
					var tangent = global::Vector3.Cross( normal, Math.Abs( normal.x ) < .7f ? global::Vector3.Forward : global::Vector3.Left ).Normal;
					var uv = attributes.TryGetProperty( "TEXCOORD_0", out var ua ) ? reference.Value( ua.GetInt32(), (int)source ) : new double[] { 0, 0 };
					var c = attributes.TryGetProperty( "COLOR_0", out var ca ) ? reference.Value( ca.GetInt32(), (int)source ) : new double[] { 1, 1, 1, 1 };
					return new Vertex( new global::Vector3( (float)p[0], (float)p[1], (float)p[2] ) ) { Normal = normal, Tangent = new global::Vector4( tangent.x, tangent.y, tangent.z, 1 ), TexCoord0 = new global::Vector4( (float)uv[0], (float)uv[1], 0, 0 ), Color = new Color32( (byte)Math.Round(c[0]*255), (byte)Math.Round(c[1]*255), (byte)Math.Round(c[2]*255), (byte)Math.Round(c[3]*255) ) };
				} ).ToArray();
				var material = materials[primitive.TryGetProperty( "material", out var mi ) ? mi.GetInt32() : 10];
				var minimum = triangles.Select( v => v.Position ).Aggregate( (a,b) => global::Vector3.Min( a,b ) ); var maximum = triangles.Select( v => v.Position ).Aggregate( (a,b) => global::Vector3.Max( a,b ) );
				var obj = new SceneCustomObject( world ) { Bounds = new BBox( minimum - .01f, maximum + .01f ) };
				obj.Flags.IsOpaque = true; obj.Flags.IsTranslucent = false;
				obj.RenderOverride = _ => { try { DrawCalls++; Graphics.Draw( triangles.AsSpan(), triangles.Length, material ); } catch ( Exception e ) { if ( Errors.Count < 8 ) Errors.Enqueue( e.ToString() ); } };
				objects.Add( obj );
			}
		}
		public void Dispose() { foreach ( var obj in objects ) { obj.RenderOverride = null; if ( obj.IsValid() ) obj.Delete(); } objects.Clear(); }
	}
}

[TestClass]
internal sealed class MeshoptCubeIndexDiagnosticTests
{
	public static List<object> Measurements => MeshoptCubePublicTests.Measurements;
	public static void Configure( string root ) => MeshoptCubePublicTests.Configure( root );
	[TestMethod]
	public Task RetainActualPublicReadbackBeforeIndependentOracleFailure()
		=> new MeshoptCubePublicTests().PublicSourceMaterialRendererAndIndependentCubeOracle( "glTF/source" );
}
