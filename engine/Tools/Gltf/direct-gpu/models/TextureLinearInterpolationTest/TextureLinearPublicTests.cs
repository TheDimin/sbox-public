using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureLinearValidation;

[TestClass]
internal sealed class TextureLinearPublicTests
{
	private static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; Measurements.Clear(); }
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task ActualSourceEmissiveInterpolationPublicRendererAndCleanup( bool compiled )
	{
		await MainThread.Wait(); Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive );
		using var reference = new TextureLinearReference( root );
		var canonical = GltfCompiler.Compile( reference.Source );
		string canonicalPath = Path.Combine( root, "Temp/gltf-validation/direct-gpu/TextureLinearInterpolationTest/canonical/TextureLinearInterpolationTest.glb_c" );
		Directory.CreateDirectory( Path.GetDirectoryName( canonicalPath ) );
		if ( File.Exists( canonicalPath ) ) CollectionAssert.AreEqual( File.ReadAllBytes( canonicalPath ), canonical ); else File.WriteAllBytes( canonicalPath, canonical );
		byte[] input = compiled ? canonical : reference.Source;
		string output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/TextureLinearInterpolationTest/public", Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( output );
		string directory = Path.Combine( Path.GetTempPath(), "gltf_linear_" + Guid.NewGuid().ToString( "N" ) ); Directory.CreateDirectory( directory );
		string name = "linear_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".glb"); File.WriteAllBytes( Path.Combine( directory, name ), input );
		LocalFileSystem mount = null; Model model = null; ModelRenderer renderer = null; Shader shader = null; Material probe = null;
		GltfGpuTriangleGateTests.Measurements.Clear();
		using var capture = new GltfGpuTriangleGateTests.NativeCapture( output, new Vector3( 350, 0, 10 ), new Vector3( 0, 0, 10 ), 270 );
		capture.Camera.EnablePostProcessing = false;
		try
		{
			mount = new LocalFileSystem( directory ); FileSystem.Mounted.Mount( mount );
			model = Model.Load( name ); Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null ); Assert.AreEqual( 3, model.MeshCount );
			Assert.AreEqual( 0, (await capture.Take( "empty_before" )).Coverage );
			using ( capture.Scene.Push() ) { renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false ); renderer.Model = model; renderer.Enabled = true; capture.Scene.EditorTick( 0, 0 ); }
			var instance = renderer.GltfInstance; Assert.IsNotNull( instance ); Assert.IsTrue( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null );
			Assert.AreEqual( 3, model.Materials.Length );
			var material = model.Materials[1];
			var texture = material.GetTexture( "g_tGltfEmissiveTexture" ); Assert.IsTrue( texture is { IsValid: true, IsError: false } );
			Assert.AreEqual( 2, texture.Width ); Assert.AreEqual( 1, texture.Height );
			var bytes = File.ReadAllBytes( Path.Combine( root, "engine/Tools/Gltf/direct-gpu/models/TextureLinearInterpolationTest/linear_probe.shader" ) );
			using ( var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) ) ) shader = await Shader.CompileAsync( System.Text.Encoding.UTF8.GetString( bytes ), timeout.Token );
			await MainThread.Wait(); Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } ); probe = Material.Create( "linear_probe_" + Guid.NewGuid().ToString( "N" ), shader );
			var dimensions = Sample( probe, material, texture, new Vector2( .5f, 0 ), .125f, 0 );
			CollectionAssert.AreEqual( new[] { 2f, 1f, 1f, 1f }, dimensions );
			foreach ( float footprint in new[] { .125f, 4f } )
			foreach ( float u in new[] { .5f, 1.5f, -.5f } )
			{
				var actual = Sample( probe, material, texture, new Vector2( u, 0 ), footprint, 1 );
				Check( "actual_bound_emissive_sampler", new[] { 0f, .5f, 0f, 1f }, actual, .002f, new { footprint, u } );
			}
			var wrongTransfer = Sample( probe, material, texture, new Vector2( .5f, 0 ), .125f, 2 );
			Check( "wrong_filter_then_decode_negative", new[] { 0f, (float)TextureLinearReference.Decode( .5 ), 0f, 1f }, wrongTransfer, .002f, null );
			Assert.IsTrue( Math.Abs( wrongTransfer[1] - .5f ) > .28f );
			var point = Sample( probe, material, texture, new Vector2( .5f, 0 ), .125f, 3 ); Assert.IsTrue( Math.Abs( point[1] - .5f ) > .49f );
			var sourcePixels = new byte[8]; texture.GetPixels( (0, 0, 2, 1), 0, 0, sourcePixels.AsSpan(), ImageFormat.RGBA8888 ); CollectionAssert.AreEqual( new byte[] { 0, 0, 0, 255, 0, 255, 0, 255 }, sourcePixels );
			var actualFrame = await capture.Take( "actual_public_source_material" ); Assert.IsTrue( actualFrame.Coverage > 500 );
			Assert.AreEqual( 0, instance.CallbackErrors.Length, string.Join( "\n", instance.CallbackErrors ) ); Assert.IsTrue( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision );
			var rows = instance.Readback(); Assert.AreEqual( 3, rows.Count ); float maxPosition = 0, maxUv = 0; int indices = 0, vertices = 0;
			foreach ( var row in rows )
			{
				Assert.AreEqual( "gpu-readback", row.IndexDataOrigin ); var primitive = reference.Root.GetProperty( "meshes" )[row.Mesh].GetProperty( "primitives" )[0];
				var attrs = primitive.GetProperty( "attributes" ); int pa = attrs.GetProperty( "POSITION" ).GetInt32(); int ia = primitive.GetProperty( "indices" ).GetInt32();
				var node = reference.Root.GetProperty( "nodes" )[row.Node]; var translation = node.GetProperty( "translation" ).EnumerateArray().Select( x => x.GetSingle() ).ToArray();
				var scale = node.TryGetProperty( "scale", out var s ) ? s.EnumerateArray().Select( x => x.GetSingle() ).ToArray() : new[] { 1f, 1f, 1f };
				var field = row.Fields.Single( x => x.Semantic == "POSITION" );
				for ( int v = 0; v < row.VertexCount; v++ )
				{
					var p = reference.Float( pa, (int)row.SourceVertices[v] ); var transformed = p.Select( (x, c) => x * scale[c] + translation[c] ).ToArray();
					var expected = new[] { transformed[2] / .0254f, transformed[0] / .0254f, transformed[1] / .0254f };
					for ( int c = 0; c < 3; c++ ) maxPosition = Math.Max( maxPosition, Math.Abs( expected[c] - BitConverter.ToSingle( row.Bytes, v * row.VertexStride + field.Offset + c * 4 ) ) );
					if ( row.Mesh == 1 )
					{
						var uv = row.Fields.First( x => x.Semantic == "TEXCOORD" && x.SemanticIndex == 0 );
						maxUv = Math.Max( maxUv, Math.Abs( .5f - BitConverter.ToSingle( row.Bytes, v * row.VertexStride + uv.Offset ) ) );
						maxUv = Math.Max( maxUv, Math.Abs( BitConverter.ToSingle( row.Bytes, v * row.VertexStride + uv.Offset + 4 ) ) );
					}
				}
				Assert.AreEqual( reference.Root.GetProperty( "accessors" )[ia].GetProperty( "count" ).GetInt32(), row.Indices.Length );
				for ( int i = 0; i < row.Indices.Length; i++ ) Assert.AreEqual( reference.Index( ia, i ), row.SourceVertices[(int)row.Indices[i]] );
				indices += row.Indices.Length; vertices += row.VertexCount;
			}
			Assert.IsTrue( maxPosition <= .00003f && maxUv == 0, $"GPU position error {maxPosition}, UV error {maxUv}." );
			// Compare the two independent authored witnesses in the final native image.
			var spherePixels = actualFrame.Pixels.Select( (p, i) => new { p, x = i % actualFrame.Width } ).Where( x => x.p.g > .1f && x.p.r < .02f && x.p.b < .02f ).ToArray();
			float[] means = new[] { false, true }.Select( right => { var group = spherePixels.Where( p => (p.x >= actualFrame.Width / 2) == right ).ToArray(); Assert.IsTrue( group.Length > 200 ); return group.Average( x => x.p.g ); } ).ToArray();
			Assert.IsTrue( Math.Abs( means[0] - means[1] ) <= 2f / 255, $"Authored spheres differ: {means[0]}, {means[1]}." );
			Measurements.Add( new { form = compiled ? "canonical glb_c" : "upstream GLB", source_sha256 = TextureLinearReference.SourceHash, input_sha256 = TextureLinearReference.Hash( input ), canonical_path = canonicalPath, canonical_sha256 = TextureLinearReference.Hash( canonical ), output, actualFrame.Path, actualFrame.Sha256, actualFrame.CameraStateJson, texture_dimensions = dimensions, texture_pixels = sourcePixels, numeric_probe_shader_sha256 = TextureLinearReference.Hash( bytes ), wrong_transfer = wrongTransfer, wrong_point = point, sphere_green_means = means, vertices, indices, max_position_error_inches = maxPosition, max_uv_error = maxUv, material_shaders = model.Materials.Select( m => m.ShaderName ).ToArray(), scope = "Actual public Model.Load/ModelRenderer default source materials, bound sampler and native texture float readback. Inferred native camera; final upstream visual approval is separate." } );
			var retained = instance;
			using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); } renderer = null; int calls = retained.DrawCalls;
			Assert.AreEqual( 0, (await capture.Take( "empty_after_delete" )).Coverage ); Assert.AreEqual( calls, retained.DrawCalls ); Assert.IsTrue( model.IsValid );
		}
		finally
		{
			await MainThread.Wait();
			try { if ( renderer is not null ) using ( capture.Scene.Push() ) { renderer.GameObject.Destroy(); capture.Scene.EditorTick( 0, 0 ); capture.Scene.ProcessDeletes(); } if ( model is not null && model.ResourcePath == Resource.FixPath( name ) ) model.Destroy(); }
			finally { probe?.Destroy(); shader?.Destroy(); if ( mount is not null ) { FileSystem.Mounted.UnMount( mount ); mount.Dispose(); } Directory.Delete( directory, true ); Measurements.Add( new { compiled, captures = GltfGpuTriangleGateTests.Measurements.ToArray() } ); }
		}
	}
	private static float[] Sample( Material probe, Material actualMaterial, Texture texture, Vector2 uv, float footprint, int mode )
	{
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Magenta ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target ); scope.Context.Clear( Color.Magenta, true, false, false ); actualMaterial.Attributes.MergeTo( Graphics.Attributes );
			Graphics.Attributes.Set( "ProbeTexture", texture ); Graphics.Attributes.Set( "ProbeUv", uv ); Graphics.Attributes.Set( "ProbeFootprint", footprint ); Graphics.Attributes.Set( "ProbeMode", mode ); Graphics.Blit( probe );
		}
		Graphics.FlushGPU(); var pixels = new float[4]; target.GetPixels( (0, 0, 1, 1), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F ); return pixels;
	}
	private static void Check( string name, float[] expected, float[] actual, float tolerance, object request )
	{
		float error = expected.Zip( actual, (a, b) => Math.Abs( a - b ) ).Max(); Measurements.Add( new { name, expected, actual, tolerance, error, request } );
		Assert.IsTrue( float.IsFinite( error ) && error <= tolerance, $"{name} error {error}." );
	}
}
