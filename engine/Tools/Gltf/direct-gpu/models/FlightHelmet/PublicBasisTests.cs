using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Gltf.DirectGpu.FlightHelmetValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

[TestClass]
internal sealed class PublicBasisTests
{
	private const string Basis = "glTF-KTX-BasisU";
	private const string Png = "glTF";
	internal const string Canonical = "canonical";
	private const string Name = "FlightHelmet.gltf";
	private const string BasisExtension = "KHR_texture_basisu";
	private static readonly IReadOnlySet<string> Extensions = new HashSet<string> { BasisExtension };
	private static readonly string[] Roles = ["BaseColor", "Normal", "MetallicRoughness", "Occlusion"];
	private static readonly string[] Materials = ["HoseMat", "RubberWoodMat", "GlassPlasticMat", "MetalPartsMat", "LeatherPartsMat", "LensesMat"];
	private static string checkout;
	private static string corpus;
	private static string evidence;

	public static List<object> Measurements { get; } = new();

	public static void Configure( string root, string output, string auditHash, string pngReferenceHash )
	{
		checkout = root;
		corpus = Path.Combine( checkout, "Temp/gltf-reference/glTF-Sample-Assets/Models/FlightHelmet" );
		evidence = output;
		Directory.CreateDirectory( evidence );
		var auditBytes = File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/FlightHelmet/source-audit.json" ) );
		Assert.AreEqual( auditHash, Hash( auditBytes ) );
		Assert.AreEqual( pngReferenceHash, Hash( File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/FlightHelmet/png-reference.json" ) ) ) );
		using var audit = JsonDocument.Parse( auditBytes );
		foreach ( var file in audit.RootElement.GetProperty( "files" ).EnumerateArray() )
		{
			var path = file.GetProperty( "path" ).GetString();
			Assert.AreEqual( file.GetProperty( "sha256" ).GetString(), Hash( File.ReadAllBytes( Path.Combine( corpus, path ) ) ), path );
		}
		Measurements.Clear();
	}

	[TestMethod]
	[DataRow( Basis )]
	[DataRow( Canonical )]
	[DataRow( Png )]
	public Task PublicModelAndBoundTextureReadbacks( string form ) => VerifyPublicModel( form );

	internal static async Task VerifyPublicModel( string form, long? embeddedBudget = null )
	{
		await MainThread.Wait();
		var compiled = form == Canonical;
		var isBasis = form != Png;
		var sourceDirectory = Path.Combine( corpus, isBasis ? Basis : Png );
		var source = File.ReadAllBytes( Path.Combine( sourceDirectory, Name ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( sourceDirectory, uri ) );
		var input = source;
		if ( compiled && embeddedBudget is long budget )
		{
			var refusal = Assert.ThrowsException<GltfException>( () => GltfCompiler.CompileWithEmbeddedImages( source, Resolve, Extensions ) );
			Assert.AreEqual( "Image packaging budget exceeded.", refusal.Message );
			Measurements.Add( new { form, defaultBudgetBytes = 256L * 1024 * 1024, defaultRefusal = refusal.Message, explicitBudgetBytes = budget } );
			input = GltfCompiler.CompileWithEmbeddedImages( source, Resolve, Extensions, budget );
		}
		else if ( compiled ) input = GltfCompiler.CompileWithEmbeddedImages( source, Resolve, Extensions );
		var inputHash = Hash( input );
		var prefix = "gltf_flighthelmet_" + Guid.NewGuid().ToString( "N" );
		var directory = Path.Combine( Path.GetTempPath(), prefix );
		var logicalName = prefix + (compiled ? ".glb" : ".gltf");
		var output = Path.Combine( evidence, form );
		Directory.CreateDirectory( output );
		Directory.CreateDirectory( directory );
		LocalFileSystem mount = null;
		Model model = null;
		ModelRenderer renderer = null;
		GltfGpuTriangleGateTests.NativeCapture capture = null;
		var aggregate = FileSystem.Mounted;
		try
		{
			using var document = GltfReader.Read( input, compiled ? null : Resolve, Extensions );
			CollectionAssert.AreEqual( new[] { "KHR_materials_transmission" }, document.UnsupportedOptionalExtensions.ToArray() );
			if ( compiled )
			{
				// Verify standard GLB and that every image and buffer is self-contained.
				// The only file mounted for the public cold load is this GLB.
				Assert.AreEqual( "glTF", System.Text.Encoding.ASCII.GetString( input, 0, 4 ) );
				Assert.IsTrue( document.Root.GetProperty( "buffers" ).EnumerateArray().All( item => !item.TryGetProperty( "uri", out _ ) ) );
				foreach ( var image in document.Root.GetProperty( "images" ).EnumerateArray() )
				{
					Assert.IsTrue( image.TryGetProperty( "bufferView", out _ ) && !image.TryGetProperty( "uri", out _ ) );
					Assert.AreEqual( "image/ktx2", image.GetProperty( "mimeType" ).GetString() );
				}
				using var external = GltfReader.Read( source, Resolve, Extensions );
				for ( int image = 0; image < 15; image++ )
					Assert.AreEqual( Hash( external.ReadImage( image ).Bytes ), Hash( document.ReadImage( image ).Bytes ), $"Embedded KTX2 image {image}" );
			}
			else
			{
				foreach ( var path in Directory.EnumerateFiles( sourceDirectory ).Where( path => Path.GetFileName( path ) != Name ) )
					File.Copy( path, Path.Combine( directory, Path.GetFileName( path ) ) );
			}
			File.WriteAllBytes( Path.Combine( directory, compiled ? logicalName + "_c" : logicalName ), input );
			if ( compiled ) Assert.AreEqual( 1, Directory.GetFiles( directory ).Length );
			mount = new LocalFileSystem( directory );
			aggregate.Mount( mount );

			var timer = Stopwatch.StartNew();
			model = Model.Load( logicalName );
			Measurements.Add( new { form, inputHash, logicalName, loadMilliseconds = timer.ElapsedMilliseconds,
				valid = model?.IsValid, error = model?.IsError, publicResource = model?.ResourcePath } );
			Assert.IsTrue( model is { IsValid: true, IsError: false } && model.native.IsNull && model.GltfResource is not null,
				$"Public Model.Load rejected {logicalName}. Inspect the editor console for the exception." );
			Assert.AreEqual( Resource.FixPath( logicalName ), model.ResourcePath );
			Assert.AreEqual( 6, model.MeshCount );
			var materials = model.Materials.ToArray();
			Assert.AreEqual( 6, materials.Length );
			Assert.IsTrue( materials.All( material => material is { IsValid: true, IsError: false } ) );
			Assert.AreEqual( 1, materials[0].GetFeature( "F_RENDER_BACKFACES" ) );
			Assert.IsTrue( materials.Skip( 1 ).All( material => material.GetFeature( "F_RENDER_BACKFACES" ) == 0 ) );

			capture = new GltfGpuTriangleGateTests.NativeCapture( output, new Vector3( 40, -24, 23 ), new Vector3( 0, 0, 13 ) );
			capture.Camera.Orthographic = false;
			capture.Camera.FovAxis = CameraComponent.Axis.Vertical;
			capture.Camera.FieldOfView = 47;
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
			Assert.IsNotNull( instance );
			Assert.IsTrue( renderer.SceneObject is SceneCustomObject && renderer.SceneObject.Model is null );
			var initial = await capture.Take( "source_material_initial" );
			Assert.IsTrue( initial.Coverage > 100, "No source-material pixels." );
			Assert.AreEqual( 0, instance.CallbackErrors.Length, string.Join( "\n", instance.CallbackErrors ) );
			Assert.IsTrue( instance.DrawCalls > 0 && instance.SubmittedRevision == instance.PoseRevision );
			CheckGeometry( instance );
			await CheckTextures( form, document, materials, capture );
			var actual = await capture.Take( "source_material_settled" );

			using ( capture.Scene.Push() ) renderer.MaterialOverride = capture.Material;
			var white = await capture.Take( "white_material_control" );
			var changed = actual.Pixels.Zip( white.Pixels, (a, b) =>
				Math.Abs( a.r - b.r ) > .02f || Math.Abs( a.g - b.g ) > .02f || Math.Abs( a.b - b.b ) > .02f ).Count( value => value );
			Assert.IsTrue( white.Coverage > 100 && changed > 100, "Source material should visibly affect public geometry." );
			RemoveRenderer( capture.Scene, renderer );
			renderer = null;
			var calls = instance.DrawCalls;
			Assert.AreEqual( 0, (await capture.Take( "empty_after" )).Coverage );
			Assert.AreEqual( calls, instance.DrawCalls );

			string canonicalPath = null;
			if ( compiled )
			{
				canonicalPath = Path.Combine( output, "FlightHelmet.glb_c" );
				File.WriteAllBytes( canonicalPath, input );
			}
			Measurements.Add( new { form, inputHash, canonicalPath, shaderNames = materials.Select( m => m.ShaderName ).ToArray(),
				initialPath = initial.Path, capturePath = actual.Path, captureSha256 = actual.Sha256, actual.Width, actual.Height,
				actual.Coverage, whiteControlChangedPixels = changed, gpuDrawRows = 6,
				optionalFallback = "OPAQUE visor; KHR_materials_transmission not supported by the loaded public route",
				recipeScope = "Inferred perspective and diagnostic point light; upstream screenshot lighting and exact camera unknown." } );
		}
		finally
		{
			await MainThread.Wait();
			try
			{
				if ( renderer is not null ) RemoveRenderer( capture.Scene, renderer );
				capture?.Dispose();
				if ( model?.ResourcePath == Resource.FixPath( logicalName ) && !ReferenceEquals( model, Model.Error ) ) model.Destroy();
			}
			finally
			{
				if ( mount is not null ) { aggregate.UnMount( mount ); mount.Dispose(); }
				Directory.Delete( directory, true );
			}
		}
	}

	private static void CheckGeometry( GltfGpuInstance instance )
	{
		var rows = instance.Readback();
		Assert.AreEqual( 6, rows.Count );
		Assert.AreEqual( 94722 * 3, rows.Sum( row => row.Indices.Length ) );
		Assert.IsTrue( rows.All( row => row.IndexDataOrigin == "gpu-readback" && row.DrawCalls > 0 &&
			row.Fields.Any( field => field.Semantic == "NORMAL" ) && row.Fields.Any( field => field.Semantic == "TANGENT" ) &&
			row.Fields.Any( field => field.Semantic == "TEXCOORD" ) ) );
		float[] minimum = [float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity];
		float[] maximum = [float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity];
		foreach ( var row in rows )
		{
			var position = row.Fields.Single( field => field.Semantic == "POSITION" );
			Assert.IsTrue( row.Indices.All( index => index < row.VertexCount ) );
			for ( var vertex = 0; vertex < row.VertexCount; vertex++ )
				for ( var axis = 0; axis < 3; axis++ )
				{
					var coordinate = BitConverter.ToSingle( row.Bytes, vertex * row.VertexStride + position.Offset + axis * sizeof( float ) );
					Assert.IsTrue( float.IsFinite( coordinate ) );
					minimum[axis] = Math.Min( minimum[axis], coordinate );
					maximum[axis] = Math.Max( maximum[axis], coordinate );
				}
		}
		// Independent pinned accessor/node bounds in engine inches from the corpus manifest.
		float[] expectedMinimum = [-6.753771f, -8.017430f, -.000843f];
		float[] expectedMaximum = [7.875164f, 6.414986f, 28.188994f];
		for ( var axis = 0; axis < 3; axis++ )
		{
			Assert.AreEqual( expectedMinimum[axis], minimum[axis], .02f, $"minimum axis {axis}" );
			Assert.AreEqual( expectedMaximum[axis], maximum[axis], .02f, $"maximum axis {axis}" );
		}
	}

	private static async Task CheckTextures( string form, GltfDocument document, Material[] materials, GltfGpuTriangleGateTests.NativeCapture capture )
	{
		var isBasis = form != Png;
		var images = new Dictionary<int, (Texture Texture, string Role, bool IsSrgb)>();
		for ( var slot = 0; slot < Materials.Length; slot++ )
		{
			var material = document.ReadMaterial( slot );
			var shaderMaterial = materials[slot];
			var materialName = document.Root.GetProperty( "materials" )[slot].GetProperty( "name" ).GetString();
			Assert.AreEqual( Materials[slot], materialName );
			Assert.AreEqual( "OPAQUE", material.AlphaMode );
			Assert.IsFalse( shaderMaterial.Flags.IsAlphaTest || shaderMaterial.Flags.IsTranslucent, materialName );
			var slots = new[] { material.BaseColorTexture, material.NormalTexture, material.MetallicRoughnessTexture, material.OcclusionTexture };
			for ( var i = 0; i < Roles.Length; i++ )
			{
				var binding = document.ReadTexture( slots[i].Index );
				var index = binding.Image.Value;
				var sampler = document.ReadSampler( binding.Sampler.Value );
				Assert.AreEqual( 9729, sampler.MagFilter );
				Assert.AreEqual( 9987, sampler.MinFilter );
				Assert.AreEqual( 10497, sampler.WrapS );
				Assert.AreEqual( 10497, sampler.WrapT );
				var texture = shaderMaterial.GetTexture( "g_tGltf" + Roles[i] + "Texture" );
				Assert.IsTrue( texture is { IsValid: true, IsError: false }, $"{materialName} {Roles[i]}" );
				Assert.AreEqual( slot == 5 ? 1024 : 2048, texture.Width );
				Assert.AreEqual( texture.Width, texture.Height );
				Assert.AreEqual( slot == 5 ? 11 : 12, texture.Mips );
				if ( !images.TryGetValue( index, out var existing ) )
					images.Add( index, (texture, Roles[i], i == 0) );
				else Assert.AreEqual( existing.Texture, texture, $"{materialName} {Roles[i]} must share the same bound source image" );
			}
		}
		Assert.AreEqual( 15, images.Count );
		using var reference = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( checkout, "research/gltf-validation/direct-gpu/models/FlightHelmet/png-reference.json" ) ) );
		var pointsByFile = reference.RootElement;
		foreach ( var (index, (texture, role, srgb)) in images.OrderBy( pair => pair.Key ) )
		{
			var image = document.ReadImage( index );
			var uri = document.Root.GetProperty( "images" )[index].TryGetProperty( "uri", out var imageUri ) ? imageUri.GetString() : null;
			var canonicalName = uri is not null ? Path.GetFileNameWithoutExtension( uri ) :
				Path.GetFileNameWithoutExtension( PinnedImageUri( index ) );
			var points = pointsByFile.GetProperty( canonicalName );
			GltfKtxImage decoded = null;
			if ( isBasis )
			{
				var plan = GltfBasisTexturePlan.Create( image, srgb, true );
				Assert.AreEqual( srgb ? GltfBasisSourceCodec.Etc1S : GltfBasisSourceCodec.Uastc, plan.SourceCodec );
				Assert.AreEqual( srgb ? 1u : 2u, plan.SupercompressionScheme );
				Assert.AreEqual( srgb ? (byte)1 : (byte)0, plan.ColorPrimaries );
				Assert.AreEqual( texture.Mips, plan.Levels.Count );
				decoded = plan.DecodeRgba32();
				Assert.AreEqual( srgb, decoded.IsSrgb );
			}
			Color32[] full = null;
			var wait = Stopwatch.StartNew();
			int firstResidentPixels = 0;
			do
			{
				foreach ( var binding in images.Values ) binding.Texture.MarkUsed( binding.Texture.Width );
				await capture.WaitFrames();
				full = texture.GetPixels();
				if ( firstResidentPixels == 0 ) firstResidentPixels = full.Length;
			} while ( full.Length != texture.Width * texture.Height && wait.Elapsed < TimeSpan.FromSeconds( 30 ) );
			Assert.AreEqual( texture.Width * texture.Height, full.Length, $"{form} image {index} full residency" );
			var landmarks = points.EnumerateArray().Select( point =>
			{
				int x = point[0].GetInt32(), y = point[1].GetInt32();
				var pixel = full[y * texture.Width + x];
				return new { x, y, gpu = new[] { pixel.r, pixel.g, pixel.b, pixel.a }.Select( channel => (int)channel ).ToArray(),
					png = point.EnumerateArray().Skip( 2 ).Select( channel => channel.GetInt32() ).ToArray() };
			} ).ToArray();
			var verifiedMips = 0;
			if ( decoded is not null )
			{
				for ( var mip = 0; mip < decoded.Levels.Count; mip++ )
				{
					var pixels = mip == 0 ? full : texture.GetPixels( mip );
					var expected = decoded.Levels[mip].Pixels;
					Assert.IsTrue( MemoryMarshal.AsBytes( pixels.AsSpan() ).SequenceEqual( expected ), $"{form} image {index} mip {mip} GPU bytes differ from decoded authored KTX2 bytes." );
					verifiedMips++;
				}
				Assert.IsTrue( full.All( pixel => pixel.a == byte.MaxValue ), $"{form} image {index} authored RGB KTX2 must decode opaque alpha." );
			}
			Measurements.Add( new { form, index, role, srgb, canonicalName, firstResidentPixels, fullPixels = full.Length, waitedSeconds = wait.Elapsed.TotalSeconds,
				verifiedMips, width = texture.Width, height = texture.Height, landmarks } );
		}
	}

	private static string PinnedImageUri( int index )
	{
		using var document = JsonDocument.Parse( File.ReadAllBytes( Path.Combine( corpus, Basis, Name ) ) );
		return document.RootElement.GetProperty( "images" )[index].GetProperty( "uri" ).GetString();
	}

	private static void RemoveRenderer( Scene scene, ModelRenderer renderer )
	{
		using var scope = scene.Push();
		renderer.GameObject.Destroy();
		scene.EditorTick( 0, 0 );
		scene.ProcessDeletes();
	}

	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}

[TestClass]
internal sealed class CanonicalBasisTests
{
	public static List<object> Measurements => PublicBasisTests.Measurements;
	public static void Configure( string root, string output, string auditHash, string pngReferenceHash ) =>
		PublicBasisTests.Configure( root, output, auditHash, pngReferenceHash );

	[TestMethod]
	public Task DefaultBudgetRefusesButBoundedStandaloneArtifactLoadsThroughPublicModel() =>
		PublicBasisTests.VerifyPublicModel( PublicBasisTests.Canonical, 384L * 1024 * 1024 );
}
