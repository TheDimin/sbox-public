using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AnisotropyBarnLampValidation;

[TestClass]
internal sealed class PublicFallbackTests
{
	private static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; Measurements.Clear(); }

	[TestMethod]
	[DataRow( "glTF", false )]
	[DataRow( "glTF", true )]
	[DataRow( "glTF-Binary", false )]
	[DataRow( "glTF-Binary", true )]
	[DataRow( "glTF-KTX-BasisU", false )]
	[DataRow( "glTF-KTX-BasisU", true )]
	public async Task PublicDefaultRouteRendersAllThreeAuthoredMeshes( string variant, bool canonical )
	{
		await MainThread.Wait();
		var sourceDirectory = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp", variant );
		var extension = variant == "glTF-Binary" ? ".glb" : ".gltf";
		var source = File.ReadAllBytes( Path.Combine( sourceDirectory, "AnisotropyBarnLamp" + extension ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( sourceDirectory, uri ) );
		var bytes = canonical ? GltfCompiler.Compile( source, Resolve, GltfModelLoader.SupportedExtensions ) : source;
		using var document = GltfModelLoader.ReadDocument( bytes, Resolve );
		var id = Guid.NewGuid().ToString( "N" );
		var directory = Path.Combine( Path.GetTempPath(), "barn_lamp_" + id );
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/AnisotropyBarnLamp/public-baseline", id );
		Directory.CreateDirectory( directory );
		Directory.CreateDirectory( output );
		var filename = "lamp_" + id + (canonical ? ".glb_c" : extension);
		File.WriteAllBytes( Path.Combine( directory, filename ), bytes );
		foreach ( var input in Directory.EnumerateFiles( sourceDirectory ) )
		{
			var suffix = Path.GetExtension( input );
			if ( suffix is ".png" or ".ktx2" || (!canonical && suffix == ".bin") )
				File.Copy( input, Path.Combine( directory, Path.GetFileName( input ) ) );
		}

		var unsupported = document.UnsupportedOptionalExtensions.OrderBy( x => x, StringComparer.Ordinal ).ToArray();
		Measurements.Add( new
		{
			phase = "public_load_requested", variant, canonical, output,
			source_sha256 = Hash( source ), input_sha256 = Hash( bytes ), unsupported_optional_extensions = unsupported
		} );
		LocalFileSystem mount = null;
		Model model = null;
		ModelRenderer renderer = null;
		using var capture = new BarnLampCapture( root, output );
		try
		{
			mount = new LocalFileSystem( directory );
			FileSystem.Mounted.Mount( mount );
			model = Model.Load( filename );
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.MeshCount == 3, "Expected three managed public model meshes." );
			Assert.AreEqual( 3, model.Materials.Length );
			var empty = await capture.Take( "empty_before" );
			Assert.AreEqual( 0, empty.Coverage );

			using ( capture.Scene.Push() )
			{
				renderer = capture.Scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.Model = model;
				renderer.Enabled = true;
				capture.Scene.EditorTick( 0, 0 );
			}
			var instance = renderer.GltfInstance;
			Assert.IsNotNull( instance, "Default ModelRenderer did not create a managed instance." );
			await capture.Settle();
			var frame = await capture.Take( "default" );
			Assert.IsTrue( frame.Coverage > 500, "Default public material capture is empty or nearly empty." );
			Assert.AreEqual( 0, instance.CallbackErrors.Length, string.Join( "\n", instance.CallbackErrors ) );
			var rows = instance.Readback().OrderBy( x => x.Mesh ).ToArray();
			CollectionAssert.AreEqual( new[] { 6803, 140, 769 }, rows.Select( x => x.VertexCount ).ToArray() );
			CollectionAssert.AreEqual( new[] { 25257, 840, 4512 }, rows.Select( x => x.Indices.Length ).ToArray() );
			Assert.IsTrue( rows.All( x => x.IndexDataOrigin == "gpu-readback" && x.DrawCalls > 0 && x.SubmittedRevision == instance.PoseRevision ) );
			DeleteRenderer( renderer, capture.Scene );
			renderer = null;
			var deleted = await capture.Take( "empty_after" );
			Assert.AreEqual( 0, deleted.Coverage );
			Measurements.Add( new
			{
				phase = "public_default_render", variant, canonical, input_sha256 = Hash( bytes ), unsupported_optional_extensions = unsupported,
				captures = new[] { empty, frame, deleted }.Select( x => new { x.Path, x.Sha256, x.Coverage } ).ToArray(), camera = capture.ObservedPresentation(),
				draws = rows.Select( x => new { x.Mesh, x.VertexCount, indices = x.Indices.Length, x.IndexDataOrigin, x.DrawCalls } ).ToArray(),
				scope = "Actual ordinary ModelRenderer and default materials. Fitted reference camera and native stock cubemap; no Khronos reference lighting or optional material-extension fidelity claim."
			} );
		}
		finally
		{
			await MainThread.Wait();
			try
			{
				if ( renderer is not null ) DeleteRenderer( renderer, capture.Scene );
				model?.Destroy();
			}
			finally
			{
				try { if ( mount is not null ) FileSystem.Mounted.UnMount( mount ); }
				finally { mount?.Dispose(); Directory.Delete( directory, true ); }
			}
		}
	}

	private static void DeleteRenderer( ModelRenderer renderer, Scene scene )
	{
		using var scope = scene.Push();
		renderer.GameObject.Destroy();
		scene.EditorTick( 0, 0 );
		scene.ProcessDeletes();
	}

	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
