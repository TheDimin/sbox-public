using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using UnlitGate = Sandbox.Gltf.DirectGpu.UnlitTestValidation.UnlitTestGraphCandidateGate;

/// <summary>
/// Samples the actual reviewed Lit template through PrepareSource/AcquireSource
/// and raw GPU draws. Expected RGB comes from literal CPU values in a separately
/// generated Unlit vertex-color shader. This never installs a default template.
/// </summary>
[TestClass]
public sealed class SourceOrmEmissionGpuTests
{
	public sealed record Configuration( string SurfaceManifestPath, string SurfaceManifestSha256, string SurfaceSourceSha256,
		string ReferenceManifestPath, string ReferenceManifestSha256, string ReferenceSourceSha256, string OutputDirectory );
	public static List<object> Measurements { get; } = new();
	private static string root, configPath;
	public static void Configure( string checkoutRoot, string configurationPath ) { root = Path.GetFullPath( checkoutRoot ); configPath = Path.GetFullPath( configurationPath ); Measurements.Clear(); }
	private sealed record Comparison( string Name, int InteriorPixels, double MeanRgbBytes, int MaximumRgbBytes, double CoverageIou, string ExpectedPath, string ActualPath );

	[TestMethod]
	public async Task ReviewedLitShaderSamplesDefaultAndSourceEmissionAgainstIndependentLiteralRgb()
	{
		RequireIdle(); Measurements.Clear();
		var configurationBytes = File.ReadAllBytes( configPath ); var configurationHash = Hash( configurationBytes );
		var config = JsonSerializer.Deserialize<Configuration>( configurationBytes );
		var surface = JsonSerializer.Deserialize<SurfaceOrmCandidateGate.Generation>( ReadVerified( config.SurfaceManifestPath, config.SurfaceManifestSha256 ) );
		var reference = JsonSerializer.Deserialize<UnlitGate.Generation>( ReadVerified( config.ReferenceManifestPath, config.ReferenceManifestSha256 ) );
		Require( surface.SourceSha256 == config.SurfaceSourceSha256 && reference.ReferenceSourceSha256 == config.ReferenceSourceSha256
			&& reference.GraphSha256 == UnlitGate.GraphSha256 && reference.GraphToolsMvid == surface.ToolsMvid, "Independent reference and Lit source must be reviewed products of the same actual active graph producer." );
		var referenceSource = ReadVerified( reference.ReferenceSourcePath, config.ReferenceSourceSha256 );
		var referenceText = Encoding.UTF8.GetString( referenceSource );
		Require( referenceText.Contains( "i.vColor.rgb", StringComparison.Ordinal ) && referenceText.Contains( "return float4( l_0, 1 )", StringComparison.Ordinal )
			&& !referenceText.Contains( "Texture2D", StringComparison.Ordinal ) && !referenceText.Contains( "g_vGltf", StringComparison.Ordinal ) && !referenceText.Contains( "ShadingModelStandard::Shade", StringComparison.Ordinal ), "Independent reference lost its raw literal vertex-RGB route." );
		var output = Path.GetFullPath( config.OutputDirectory );
		Require( output.StartsWith( Path.Combine( root, "Temp/gltf-validation/direct-gpu" ) + Path.DirectorySeparatorChar, StringComparison.Ordinal )
			&& (!Directory.Exists( output ) || !Directory.EnumerateFileSystemEntries( output ).Any()), "Use a fresh output directory inside direct-GPU Temp evidence." );
		Directory.CreateDirectory( output ); File.WriteAllBytes( Path.Combine( output, "configuration.json" ), configurationBytes );
		File.WriteAllBytes( Path.Combine( output, "literal-color-reference.shader" ), referenceSource );
		var pinned = TextureEncodingReference.Open( root );
		GltfMaterialLease<Material> surfaceSession = null; Shader referenceShader = null; Material referenceMaterial = null; Capture capture = null;
		Exception failure = null;
		try
		{
			surfaceSession = await SurfaceOrmCandidateGate.AcquireReviewedTemplate( config.SurfaceManifestPath, config.SurfaceManifestSha256, config.SurfaceSourceSha256 );
			using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			referenceShader = await Shader.CompileAsync( referenceText, timeout.Token ); await MainThread.Wait();
			Require( referenceShader is { IsValid: true, IsAnonymous: true }, "Independent literal reference did not compile anonymously." );
			referenceMaterial = Material.Create( "source_orm_literal_reference_" + Guid.NewGuid().ToString( "N" ), referenceShader );
			Require( referenceMaterial is { IsValid: true, IsError: false }, "Independent literal reference material is invalid." );
			Measurements.Add( new { phase = "reviewed_shaders_loaded", surface_source_sha256 = config.SurfaceSourceSha256, reference_source_sha256 = config.ReferenceSourceSha256,
				surface_shader = surfaceSession.Value.Shader.ResourcePath, reference_shader = referenceShader.ResourcePath,
				scope = "Explicit reviewed template only; original GetTexture introspection reds are retained and no fallback texture is injected." } );
			capture = new Capture( output ); capture.Light( false );
			Empty( await capture.Take( "empty_before" ) );
			var comparisons = new List<Comparison>(); Comparison negative = null; var cases = 0; var retiredDraws = new List<Draw>();
			foreach ( var image in new[] { -1, 3, 4, 5 } )
			{
				var factor = image < 0 ? new[] { .25f, .5f, .75f } : new[] { .5f, .75f, .625f };
				var expected = image < 0 ? factor : new[] { 0f, (float)TextureEncodingReference.Linear136 * factor[1], factor[2] };
				var sourceBytes = Fixture( image < 0 ? null : pinned.Images[image], factor );
				foreach ( var portable in new[] { false, true } )
				{
					var bytes = portable ? GltfCompiler.Compile( sourceBytes ) : sourceBytes;
					using var document = GltfReader.Read( bytes ); var material = document.ReadMaterial( 0 );
					var prepared = GltfStockMaterialAdapter.PrepareSource( document, material );
					var uv = GltfMaterialUvPlan.Create( material, document.MaterializePrimitiveUpload( 0, 0 ) );
					using var lease = GltfStockMaterialAdapter.AcquireSource( prepared, surfaceSession.Value, uv );
					var name = (image < 0 ? "omitted_emissive_texture" : "pinned_emissive_image_" + image) + (portable ? "_portable" : "_source");
					Measurements.Add( new { phase = "actual_source_material", name, source_sha256 = Hash( sourceBytes ), artifact_sha256 = Hash( bytes ), resources = prepared.Resources.Count,
						expected_linear_rgb = expected, factor, texture_handle_visible = lease.Value.GetTexture( "g_tGltfEmissiveTexture" ) is { IsValid: true },
						scope = "Minimal material derivative uses exact pinned encoded PNG bytes; source/portable agreement alone is not the color oracle." } );
					Frame expectedFrame;
					using ( var draw = new Draw( capture.Scene.SceneWorld, referenceMaterial, expected ) )
					{
						expectedFrame = await capture.Take( name + "_literal_reference" ); draw.RequireSubmitted();
						retiredDraws.Add( draw );
					}
					await capture.FlushDeletes();
					Frame actualFrame;
					using ( var draw = new Draw( capture.Scene.SceneWorld, lease.Value, new[] { 1f, 1f, 1f } ) )
					{
						actualFrame = await capture.Take( name + "_actual_lit_emission" ); draw.RequireSubmitted();
						retiredDraws.Add( draw );
					}
					await capture.FlushDeletes();
					comparisons.Add( Compare( name, expectedFrame, actualFrame ) );
					if ( image == 3 && !portable )
					{
						// A different program with literal encoded-space green must be
						// rejected; neither image is generated by the Lit implementation.
						using ( var draw = new Draw( capture.Scene.SceneWorld, referenceMaterial, new[] { 0f, 136f / 255 * factor[1], factor[2] } ) )
						{
							negative = Compare( "wrong_encoded_space_green_negative", await capture.Take( name + "_wrong_encoded_reference" ), actualFrame ); draw.RequireSubmitted();
							retiredDraws.Add( draw );
						}
						await capture.FlushDeletes();
					}
					cases++;
				}
			}
			Empty( await capture.Take( "deleted_empty" ) );
			foreach ( var draw in retiredDraws ) draw.RequireStopped();
			Require( cases == 8 && comparisons.Count == 8, "Expected omitted texture plus three exact pinned profiles, each source and portable." );
			// Keep all eight actual image comparisons even when the omitted-role
			// shader default is genuinely wrong. No introspection assumption gates
			// GPU execution or changes a texture/default resource to make it pass.
			var bad = comparisons.Where( value => value.InteriorPixels < 64 || value.MeanRgbBytes > 1 || value.MaximumRgbBytes > 3 || value.CoverageIou < .99 ).ToArray();
			Require( bad.Length == 0, "Actual emission differs from independent literal RGB: " + JsonSerializer.Serialize( bad ) );
			Require( negative is not null && (negative.MeanRgbBytes > 8 || negative.MaximumRgbBytes > 8), "Independent wrong-transfer color must fail the same image gate observably." );
			Require( Hash( File.ReadAllBytes( configPath ) ) == configurationHash, "Configuration changed during execution." );
			Measurements.Add( new { phase = "complete", comparisons = 8, passed = true, scope = "Actual explicit source-template emission/default texel proof. No PBR lighting, sampler matrix, normal, alpha, whole-model or default Model.Load capability claim." } );
		}
		catch ( Exception error ) { failure = error; throw; }
		finally
		{
			await MainThread.Wait(); Action[] cleanup = [() => capture?.Dispose(), () => referenceMaterial?.Destroy(), () => referenceShader?.Destroy(), () => surfaceSession?.Dispose()];
			if ( failure is null ) GltfGpuCleanup.ReleaseAll( cleanup ); else GltfGpuCleanup.Rollback( failure, cleanup );
		}
	}

	private static byte[] Fixture( byte[] png, float[] factor )
	{
		var fixture = JsonNode.Parse( """
			{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]},{"componentType":5126,"type":"VEC2","count":3}],"meshes":[{"primitives":[{"attributes":{"POSITION":0,"TEXCOORD_0":1},"material":0}]}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0,0,0,1],"metallicFactor":1,"roughnessFactor":1}}]}
			""" )!.AsObject();
		fixture["materials"]![0]!["emissiveFactor"] = JsonSerializer.SerializeToNode( factor );
		if ( png is not null )
		{
			fixture["images"] = new JsonArray( new JsonObject { ["uri"] = "data:image/png;base64," + Convert.ToBase64String( png ) } );
			fixture["samplers"] = JsonNode.Parse( """[{"minFilter":9728,"magFilter":9728,"wrapS":33071,"wrapT":33071}]""" );
			fixture["textures"] = JsonNode.Parse( """[{"source":0,"sampler":0}]""" );
			fixture["materials"]![0]!["emissiveTexture"] = JsonNode.Parse( """{"index":0}""" );
		}
		return Encoding.UTF8.GetBytes( fixture.ToJsonString() );
	}
	private static Comparison Compare( string name, Frame expected, Frame actual )
	{
		Require( expected.Width == actual.Width && expected.Height == actual.Height && expected.Rgba.Length == actual.Rgba.Length, "Capture extents differ." );
		var width = expected.Width; var mask = new List<int>(); var intersection = 0; var union = 0;
		bool Foreground( Frame frame, int pixel ) => frame.Rgba[pixel * 4] > 8 || frame.Rgba[pixel * 4 + 1] > 8 || frame.Rgba[pixel * 4 + 2] > 8;
		for ( var p = 0; p < width * expected.Height; p++ ) { var a = Foreground( expected, p ); var b = Foreground( actual, p ); if ( a && b ) intersection++; if ( a || b ) union++; }
		for ( var y = 3; y < expected.Height - 3; y++ ) for ( var x = 3; x < width - 3; x++ )
		{
			var interior = true; for ( var dy = -3; dy <= 3 && interior; dy++ ) for ( var dx = -3; dx <= 3; dx++ ) if ( !Foreground( expected, (y + dy) * width + x + dx ) ) { interior = false; break; }
			if ( interior ) mask.Add( y * width + x );
		}
		long sum = 0; var maximum = 0;
		foreach ( var pixel in mask ) for ( var c = 0; c < 3; c++ ) { var delta = Math.Abs( expected.Rgba[pixel * 4 + c] - actual.Rgba[pixel * 4 + c] ); sum += delta; maximum = Math.Max( maximum, delta ); }
		var value = new Comparison( name, mask.Count, mask.Count == 0 ? double.MaxValue : sum / (double)(mask.Count * 3), maximum, union == 0 ? 0 : intersection / (double)union, expected.Path, actual.Path );
		Measurements.Add( new { phase = "independent_emission_image_comparison", comparison = value, mean_rgb_limit_bytes = 1, maximum_rgb_limit_bytes = 3, coverage_iou_limit = .99 } ); return value;
	}
	private static void Empty( Frame frame ) => Require( frame.Coverage == 0, "Fresh empty/deleted scene has foreground: " + frame.Path );
	private static byte[] ReadVerified( string path, string hash ) { var bytes = File.ReadAllBytes( path ); Require( bytes.Length is > 0 and < 4 * 1024 * 1024 && Hash( bytes ) == hash, "Reviewed input changed: " + path ); return bytes; }
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	private static void RequireIdle() => Require( Application.IsEditor && !Game.IsPlaying && Graphics.IsAvailable && !Graphics.IsActive && ThreadSafe.IsMainThread, "Requires the coordinator's authorized idle editor GPU session." );

	[StructLayout( LayoutKind.Sequential )]
	private struct LiteralVertex
	{
		[VertexLayout.Position] public global::Vector3 Position;
		[VertexLayout.Color] public global::Vector4 Color;
		[VertexLayout.Normal] public global::Vector3 Normal;
		[VertexLayout.TexCoord( 0 )] public global::Vector2 Uv0;
		[VertexLayout.TexCoord( 1 )] public global::Vector2 Uv1;
		[VertexLayout.Tangent] public global::Vector4 Tangent;
	}
	private sealed class Draw : IDisposable
	{
		private readonly SceneCustomObject obj; private readonly ConcurrentQueue<string> errors = new(); private int calls; private int? retiredCalls;
		public Draw( SceneWorld world, Material material, float[] color )
		{
			var vertices = new[] { new global::Vector3( 0, -30, -30 ), new global::Vector3( 0, 30, -30 ), new global::Vector3( 0, 0, 30 ) }.Select( p => new LiteralVertex
			{ Position = p, Color = new global::Vector4( color[0], color[1], color[2], 1 ), Normal = global::Vector3.Forward, Tangent = new global::Vector4( 0, 1, 0, 1 ), Uv0 = new global::Vector2( .5f, .5f ), Uv1 = new global::Vector2( .5f, .5f ) } ).ToArray();
			obj = new SceneCustomObject( world ) { Bounds = BBox.FromPoints( vertices.Select( value => value.Position ) ) }; obj.Flags.IsOpaque = true; obj.Flags.IsTranslucent = false;
			var attributes = new RenderAttributes();
			obj.RenderOverride = _ => { try { calls++; attributes.Clear(); material.Attributes.MergeTo( attributes ); Graphics.Attributes.MergeTo( attributes ); Graphics.Draw<LiteralVertex>( vertices.AsSpan(), vertices.Length, material, attributes ); } catch ( Exception error ) { errors.Enqueue( error.ToString() ); } };
		}
		public void RequireSubmitted() => Require( calls > 0 && errors.IsEmpty, "Actual raw GPU draw was absent or failed: " + string.Join( "\n", errors ) );
		public void RequireStopped() => Require( retiredCalls.HasValue && calls == retiredCalls.Value && errors.IsEmpty, "Deleted raw GPU callback continued submitting or failed." );
		public void Dispose() { obj.RenderOverride = null; if ( obj.IsValid() ) obj.Delete(); retiredCalls = calls; }
	}
	private sealed record Frame( byte[] Rgba, int Coverage, string Path, string Sha256, int Width, int Height );
	/// <summary>Borrow the existing reviewed native presentation; no second renderer/camera implementation.</summary>
	private sealed class Capture : IDisposable
	{
		private readonly object value; private readonly Type type; public readonly Scene Scene;
		public Capture( string output )
		{
			type = typeof( UnlitGate ).GetNestedType( "CandidateCapture", BindingFlags.NonPublic ) ?? throw new InvalidOperationException( "Existing native capture contract is unavailable." );
			value = Activator.CreateInstance( type, new object[] { output } ); Scene = (Scene)type.GetField( "Scene" )!.GetValue( value );
		}
		public void Light( bool bright ) => type.GetMethod( "Light" )!.Invoke( value, new object[] { bright } );
		public async Task FlushDeletes() { await (Task)type.GetMethod( "FlushDeletes" )!.Invoke( value, null ); await MainThread.Wait(); }
		public async Task<Frame> Take( string label )
		{
			var task = (Task)type.GetMethod( "Take" )!.Invoke( value, new object[] { label } ); await task; await MainThread.Wait();
			var frame = task.GetType().GetProperty( "Result" )!.GetValue( task ); var frameType = frame.GetType();
			T Get<T>( string name ) => (T)frameType.GetProperty( name )!.GetValue( frame );
			var result = new Frame( Get<byte[]>( "Rgba" ), Get<int>( "Coverage" ), Get<string>( "Path" ), Get<string>( "Sha256" ), Get<int>( "Width" ), Get<int>( "Height" ) );
			Measurements.Add( new { phase = "borrowed_native_capture", label, result.Path, result.Sha256, result.Coverage, result.Width, result.Height, engine_frame = Application.FrameCount,
				native_state = UnlitGate.Measurements.LastOrDefault(), route = "UnlitTestGraphCandidateGate.CandidateCapture unchanged" } ); return result;
		}
		public void Dispose() => ((IDisposable)value).Dispose();
	}
}
