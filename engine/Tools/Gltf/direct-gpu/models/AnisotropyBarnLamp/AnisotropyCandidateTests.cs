using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.AnisotropyBarnLampValidation;

[TestClass]
internal sealed class AnisotropyCandidateTests
{
	private static string root;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string checkout ) { root = checkout; Measurements.Clear(); }
	private const string ShaderSha = "fc1a1f93ac5bf498f52df268468e1b0fb31af9b23cede2383bcb2febf94f4771";
	private const string OracleSha = "ade72e44aa8768854b8c397e6ec38b91d72acb22241aeb42e069ca912e9f9b17";

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task AuthoredCopperDirectionAndStrengthAreIndependentInNativeLight( bool canonical )
	{
		await MainThread.Wait();
		var sourceDirectory = Path.Combine( root, "Temp/gltf-reference/glTF-Sample-Assets/Models/AnisotropyBarnLamp/glTF" );
		var owned = Path.Combine( root, "research/gltf-validation/direct-gpu/models/AnisotropyBarnLamp" );
		var source = File.ReadAllBytes( Path.Combine( sourceDirectory, "AnisotropyBarnLamp.gltf" ) );
		byte[] Resolve( string uri ) => File.ReadAllBytes( Path.Combine( sourceDirectory, uri ) );
		var bytes = canonical ? GltfCompiler.Compile( source, Resolve, GltfModelLoader.SupportedExtensions ) : source;
		using var document = GltfModelLoader.ReadDocument( bytes, Resolve );
		var oracleBytes = File.ReadAllBytes( Path.Combine( owned, "anisotropy-landmarks.json" ) );
		Assert.AreEqual( OracleSha, Hash( oracleBytes ) );
		using var oracle = JsonDocument.Parse( oracleBytes );
		var authored = document.Root.GetProperty( "materials" )[0].GetProperty( "extensions" ).GetProperty( "KHR_materials_anisotropy" );
		var textureInfo = authored.GetProperty( "anisotropyTexture" );
		var texture = document.ReadTexture( textureInfo.GetProperty( "index" ).GetInt32() );
		Assert.AreEqual( 1f, authored.GetProperty( "anisotropyStrength" ).GetSingle() );
		Assert.AreEqual( 0f, authored.GetProperty( "anisotropyRotation" ).GetSingle() );
		Assert.IsFalse( textureInfo.TryGetProperty( "texCoord", out _ ), "The pinned lamp uses default UV0." );
		var encoded = document.ReadImage( texture.Image.Value );
		Assert.AreEqual( oracle.RootElement.GetProperty( "sourceAnisotropyPngSha256" ).GetString(), Hash( encoded.Bytes ) );
		var shaderBytes = File.ReadAllBytes( Path.Combine( owned, "gltf_anisotropy_candidate.shader" ) );
		Assert.AreEqual( ShaderSha, Hash( shaderBytes ) );
		var id = Guid.NewGuid().ToString( "N" );
		var output = Path.Combine( root, "Temp/gltf-validation/direct-gpu/AnisotropyBarnLamp/anisotropy-candidate", id );
		Directory.CreateDirectory( output );
		Measurements.Add( new { phase = "anonymous_shader_requested", canonical, source_sha256 = Hash( source ), input_sha256 = Hash( bytes ), shader_sha256 = ShaderSha, oracle_sha256 = OracleSha, output } );
		Shader shader = null;
		Material template = null;
		Material coreTemplate = null;
		GltfMaterialLease<Texture> directionalMap = null;
		GltfGpuAsset asset = null;
		GltfGpuInstance instance = null;
		BarnLampCapture capture = null;
		try
		{
			using ( var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) ) )
				shader = await Shader.CompileAsync( Encoding.UTF8.GetString( shaderBytes ), timeout.Token );
			await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true, IsAnonymous: true } );
			template = Material.Create( "gltf_barn_anisotropy_" + id, shader );
			Assert.IsTrue( template is { IsValid: true, IsError: false } );
			foreach ( var parameter in new[] { "g_tGltfAnisotropyTexture", "g_flGltfAnisotropyStrength", "g_vGltfAnisotropyRotation", "g_flGltfHasAnisotropyTexture",
				"g_flGltfAnisotropyUvSlot", "g_vGltfAnisotropyUvRowU", "g_vGltfAnisotropyUvRowV", "g_flGltfProbeZeroBlue", "g_flGltfProbeFlatRg" } )
				Assert.IsTrue( template.HasParameter( parameter ), $"Missing native candidate material parameter: {parameter}." );
			coreTemplate = Material.FromShader( "shaders/gltf_surface_five_uv.shader" );
			Assert.IsTrue( coreTemplate is { IsValid: true, IsError: false } );
			directionalMap = GltfNativeTextureCache.Acquire( GltfTextureCompiler.CompileWithMipmaps( encoded, false ) );
			Assert.IsTrue( directionalMap.Value is { IsValid: true, IsError: false, Width: 2048, Height: 2048, Mips: 12 } );
			asset = GltfGpuAsset.PrepareProjected( document, GltfManagedModelResource.ComputeShaderPath, 256L * 1024 * 1024,
				(_, plan) => plan.Mesh == 0 ? template : coreTemplate, GltfUnlitMaterialPlan.Features.All, legacyLit: true, sourceLitFeatureSidedness: true );

			capture = new BarnLampCapture( root, output, directionalControl: true );
			var empty = await capture.Take( "empty_before" );
			Assert.AreEqual( 0, empty.Coverage );
			instance = asset.CreateInstance( capture.Scene.SceneWorld );
			var copper = asset.GetMaterials()[0];
			Assert.AreEqual( template.ShaderName, copper.ShaderName );
			Assert.IsTrue( copper.Set( "g_tGltfAnisotropyTexture", directionalMap.Value ) );
			copper.Attributes.Set( "GltfAnisotropySampler", GltfSamplerMapping.Map( document.ReadSampler( texture.Sampler.Value ), 9987, 9729 ).State );
			copper.Set( "g_flGltfHasAnisotropyTexture", 1 );
			copper.Set( "g_flGltfAnisotropyUvSlot", 0 );
			copper.Set( "g_vGltfAnisotropyUvRowU", new Vector3( 1, 0, 0 ) );
			copper.Set( "g_vGltfAnisotropyUvRowV", new Vector3( 0, 1, 0 ) );
			await capture.Settle();
			var actual = await Draw( "authored", 1, new Vector2( 1, 0 ), false, false );
			var repeat = await Draw( "authored_repeat", 1, new Vector2( 1, 0 ), false, false );
			var rotated = await Draw( "direction_rotated_90", 1, new Vector2( 0, 1 ), false, false );
			var flatRg = await Draw( "constant_direction_authored_blue", 1, new Vector2( 1, 0 ), false, true );
			var strengthOff = await Draw( "uniform_strength_zero", 0, new Vector2( 1, 0 ), false, false );
			var blueOff = await Draw( "texture_blue_zero", 1, new Vector2( 1, 0 ), true, false );
			var restored = await Draw( "authored_restored", 1, new Vector2( 1, 0 ), false, false );
			var regions = oracle.RootElement.GetProperty( "landmarks" ).EnumerateObject().Select( site =>
			{
				var p = site.Value.GetProperty( "referencePixel" );
				int x = (int)Math.Round( p[0].GetInt32() * 704d / 1055 ), y = (int)Math.Round( p[1].GetInt32() * 802d / 1202 );
				var area = (from dy in Enumerable.Range( -4, 9 ) from dx in Enumerable.Range( -4, 9 ) select (y + dy) * 704 + x + dx).ToArray();
				return new
				{
					site.Name, x, y, authored_blue = site.Value.GetProperty( "controls" ).GetProperty( "authored" ).GetProperty( "strength" ).GetSingle(),
					rotated_mean = area.Average( i => PixelDelta( actual.Pixels[i], rotated.Pixels[i] ) ),
					flattened_mean = area.Average( i => PixelDelta( actual.Pixels[i], flatRg.Pixels[i] ) ),
					strength_off_mean = area.Average( i => PixelDelta( actual.Pixels[i], strengthOff.Pixels[i] ) ),
					repeat_mean = area.Average( i => PixelDelta( actual.Pixels[i], repeat.Pixels[i] ) ),
				};
			} ).ToArray();
			var noise = Changed( actual, repeat );
			var directionSignal = Changed( actual, rotated );
			var rgSignal = Changed( actual, flatRg );
			var blueSignal = Changed( actual, blueOff );
			var equivalent = Changed( strengthOff, blueOff );
			var reset = Changed( actual, restored );
			var presentation = capture.ObservedPresentation();
			var rows = instance.Readback();
			Assert.AreEqual( 3, rows.Count );
			Assert.IsTrue( rows.All( x => x.IndexDataOrigin == "gpu-readback" && x.DrawCalls > 0 && x.SubmittedRevision == instance.PoseRevision ) );
			instance.Dispose();
			instance = null;
			using ( capture.Scene.Push() ) capture.Scene.ProcessDeletes();
			var deleted = await capture.Take( "empty_after" );
			Measurements.Add( new
			{
				phase = "anisotropy_candidate_pixels", canonical, source_sha256 = Hash( source ), input_sha256 = Hash( bytes ), shader_sha256 = ShaderSha, oracle_sha256 = OracleSha,
				captures = new[] { empty, actual, repeat, rotated, flatRg, strengthOff, blueOff, restored, deleted }.Select( x => new { x.Path, x.Sha256, x.Coverage } ).ToArray(),
				presentation, noise, directionSignal, rgSignal, blueSignal, equivalent, reset, regions,
				scope = "Anonymous candidate with real lamp core materials and a borrowed anisotropy binding; one native point light. Public default selection, stock IBL approximation, other optional material extensions and reference appearance remain separate."
			} );
			Assert.AreEqual( 0, deleted.Coverage );
			Assert.IsTrue( equivalent <= Math.Max( 25, 2 * noise ), "Uniform zero and blue zero must produce the same isotropic capture." );
			Assert.IsTrue( reset <= Math.Max( 25, 2 * noise ), "Restored authored inputs must reproduce the initial capture." );
			Assert.IsTrue( new[] { directionSignal, rgSignal, blueSignal }.All( x => x > Math.Max( 100, 5 * noise ) ),
				"Rotation, sampled RG, and sampled B must each change more than same-input variation." );
			Assert.IsTrue( regions.Where( x => x.authored_blue > .9f ).Count( x => x.rotated_mean > x.repeat_mean + .01 ) >= 2,
				"At least two authored high-strength copper landmarks must respond to a 90-degree direction rotation." );

			async Task<BarnLampCapture.Frame> Draw( string label, float amount, Vector2 rotation, bool zeroBlue, bool constantDirection )
			{
				await MainThread.Wait();
				copper.Set( "g_flGltfAnisotropyStrength", amount );
				copper.Set( "g_vGltfAnisotropyRotation", rotation );
				copper.Set( "g_flGltfProbeZeroBlue", zeroBlue );
				copper.Set( "g_flGltfProbeFlatRg", constantDirection );
				var frame = await capture.Take( label );
				Assert.IsTrue( frame.Coverage > 500 );
				Assert.AreEqual( 0, instance.CallbackErrors.Length, string.Join( "\n", instance.CallbackErrors ) );
				return frame;
			}
		}
		finally
		{
			await MainThread.Wait();
			instance?.Dispose();
			capture?.Dispose();
			asset?.Dispose();
			directionalMap?.Dispose();
			template?.Destroy();
			shader?.Destroy();
		}
	}

	private static int Changed( BarnLampCapture.Frame a, BarnLampCapture.Frame b ) => a.Pixels.Zip( b.Pixels, PixelDelta ).Count( x => x > .03f );
	private static float PixelDelta( Color a, Color b ) => Math.Max( Math.Abs( a.r - b.r ), Math.Max( Math.Abs( a.g - b.g ), Math.Abs( a.b - b.b ) ) );
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
