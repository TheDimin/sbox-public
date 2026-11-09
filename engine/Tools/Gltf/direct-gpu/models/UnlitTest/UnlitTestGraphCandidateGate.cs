using System;
using System.Buffers.Binary;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace Sandbox.Gltf.DirectGpu.UnlitTestValidation;

using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;
using NMatrix = System.Numerics.Matrix4x4;

/// <summary>
/// Coordinator-only candidate gate. Generation and execution are separate: a
/// reviewed generated-source hash and the current adapter's real red receipt
/// are prerequisites for anonymous shader compilation. This never changes the
/// shared adapter and never creates a native Model/Mesh.
/// </summary>
[TestClass]
public sealed partial class UnlitTestGraphCandidateGate
{
	public const string GraphSha256 = "579e22ddfedc2588ba051afd01799aaced463135fb0ef230b7413327f1d788c7";
	public static List<object> Measurements { get; } = new();
	private static string checkout;
	private static string configurationPath;
	public static void Configure( string checkoutRoot, string configPath )
	{
		checkout = Path.GetFullPath( checkoutRoot ); configurationPath = Path.GetFullPath( configPath ); Measurements.Clear();
	}

	public sealed record Generation( int Version, string GraphSha256, string GraphToolsMvid, object[] GraphTypes,
		string CandidateSourcePath, string CandidateSourceSha256, string ReferenceSourcePath, string ReferenceSourceSha256 );
	public sealed record Configuration( string AdapterRedReceiptPath, string AdapterRedReceiptSha256,
		string GenerationManifestPath, string GenerationManifestSha256, string CandidateSourceSha256,
		string ReferenceSourceSha256, string ComputeSourceSha256, string OutputDirectory,
		double MeanRgbToleranceBytes, int MaximumRgbToleranceBytes, double MinimumLitLightingChangeBytes,
		float MaximumPositionErrorInches, float MaximumNormalError, string SourceShaderPath = null, string SourceShaderSha256 = null, bool PublicSource = false );

	/// <summary>
	/// Invoke only in the coordinator's verified idle editor. Calls the actually
	/// loaded graph implementation; never invokes a replacement graph compiler.
	/// Writes research artifacts only and performs no Shader.CompileAsync call.
	/// </summary>
	public static Generation GenerateCandidate( string checkoutRoot, string outputDirectory, string expectedLoadedToolsMvid )
	{
		RequireIdle();
		var root = Path.GetFullPath( checkoutRoot ); var output = FreshOutput( root, outputDirectory );
		var graphPath = Path.Combine( root, "research/gltf-validation/direct-gpu/models/UnlitTest/unlit_surface_candidate.shdrgrph" );
		var graphBytes = ReadVerified( graphPath, GraphSha256 ); var graphJson = JsonNode.Parse( graphBytes )!.AsObject();
		// Hotload history can retain multiple assemblies with identical names.
		// Select the active editor registry's actual CLR types, never a historical
		// AppDomain assembly chosen by name or arbitrary enumeration order.
		var graphType = ActiveEditorType( "Editor.ShaderGraph.ShaderGraph" );
		var compilerType = ActiveEditorType( "Editor.ShaderGraph.GraphCompiler" );
		var tools = compilerType.Assembly;
		Require( tools.ManifestModule.ModuleVersionId.ToString() == expectedLoadedToolsMvid && graphType.Assembly == tools,
			"Active graph/compiler producer differs from the coordinator's expected current MVID." );
		var identities = new List<object>();
		string Generate( JsonObject data, string[] expectedTypes )
		{
			var graph = Activator.CreateInstance( graphType );
			graphType.GetMethod( "Deserialize", new[] { typeof( string ), typeof( string ) } )!.Invoke( graph, new object[] { data.ToJsonString(), null } );
			var nodes = ((IEnumerable)graphType.GetProperty( "Nodes" )!.GetValue( graph )!).Cast<object>().ToArray();
			Require( nodes.Select( node => node.GetType().Name ).Order().SequenceEqual( expectedTypes.Order() ), "Graph did not resolve exactly the audited provided node types." );
			Require( nodes.All( node => node.GetType().Namespace is "Editor.ShaderGraph" or "Editor.ShaderGraph.Nodes" ), "Unexpected graph implementation namespace." );
			Require( nodes.All( node => ActiveEditorType( node.GetType().FullName! ) == node.GetType() ), "Graph resolved a historical node type outside the active editor registry." );
			identities.AddRange( nodes.Select( node => AssemblyIdentity( node.GetType() ) ) );
			var compiler = Activator.CreateInstance( compilerType, graph, false );
			var source = (string)compilerType.GetMethod( "Generate" )!.Invoke( compiler, null );
			var errors = ((IEnumerable)compilerType.GetProperty( "Errors" )!.GetValue( compiler )!).Cast<object>().ToArray();
			Require( errors.Length == 0 && !string.IsNullOrWhiteSpace( source ), "Loaded graph generator failed: " + JsonSerializer.Serialize( errors ) );
			Require( source.Contains( "Forward();", StringComparison.Ordinal ) && source.Contains( "Depth();", StringComparison.Ordinal )
				&& source.Contains( "ProcessVertex( v )", StringComparison.Ordinal ) && source.Contains( "FinalizeVertex( i )", StringComparison.Ordinal )
				&& !source.Contains( "return ShadingModelStandard::Shade", StringComparison.Ordinal )
				&& !source.Contains( "Texture2D g_t", StringComparison.Ordinal ), "Generated untextured surface does not match the reviewed route." );
			return source;
		}
		var candidate = Generate( graphJson, new[] { "Float3", "VertexColor", "Multiply", "Result" } );
		Require( candidate.Contains( "g_vGltfBaseColor", StringComparison.Ordinal ) && candidate.Contains( "i.vColor.rgb", StringComparison.Ordinal ), "Candidate lost its linear uniform or vertex RGB input." );
		// Independent numeric reference: no factor uniform or multiply node. Its
		// FLOAT32 vertex RGB is the literal CPU product factor * authored color.
		var referenceJson = graphJson.DeepClone().AsObject();
		referenceJson["Description"] = "UnlitTest independent literal FLOAT32 color reference";
		referenceJson["nodes"] = JsonNode.Parse( """
			[{"_class":"VertexColor","Identifier":"reference_color"},
			 {"_class":"Result","Identifier":"reference_surface","Albedo":{"Identifier":"reference_color","Output":"RGB"},"Opacity":{},"DefaultOpacity":1}]
			""" );
		var reference = Generate( referenceJson, new[] { "VertexColor", "Result" } );
		Require( !reference.Contains( "g_vGltfBaseColor", StringComparison.Ordinal ) && reference.Contains( "i.vColor.rgb", StringComparison.Ordinal ), "Reference must not share the candidate factor uniform." );
		var candidatePath = Path.Combine( output, "provided-unlit-candidate.shader" ); var referencePath = Path.Combine( output, "literal-color-reference.shader" );
		File.WriteAllText( candidatePath, candidate, new UTF8Encoding( false ) ); File.WriteAllText( referencePath, reference, new UTF8Encoding( false ) );
		identities.Add( AssemblyIdentity( graphType ) ); identities.Add( AssemblyIdentity( compilerType ) );
		var manifest = new Generation( 1, GraphSha256, tools.ManifestModule.ModuleVersionId.ToString(), identities.ToArray(), candidatePath, HashFile( candidatePath ), referencePath, HashFile( referencePath ) );
		File.WriteAllText( Path.Combine( output, "generation.json" ), JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } ) );
		return manifest;
	}
	/// <summary>One static-results row for the existing coordinator helper runner.</summary>
	public static object[] GenerateForReview( string checkoutRoot, string outputDirectory, string expectedLoadedToolsMvid )
	{
		var generated = GenerateCandidate( checkoutRoot, outputDirectory, expectedLoadedToolsMvid );
		var path = Path.Combine( Path.GetFullPath( outputDirectory ), "generation.json" );
		return new object[] { new { Passed = true, Name = "actual_loaded_graph_sources_generated_for_review", ManifestPath = path, ManifestSha256 = HashFile( path ), Generated = generated,
			Scope = "Actual provided graph/compiler text generation only; no shader compilation or draw executed." } };
	}

	[TestMethod]
	public async Task ReviewedProvidedGraphHas3DUnlitColorAndLightingBehavior()
	{
		RequireIdle(); Require( checkout is not null && configurationPath is not null, "Configure the separate candidate gate before running." );
		var configBytes = File.ReadAllBytes( configurationPath ); var configHash = Hash( configBytes );
		var config = JsonSerializer.Deserialize<Configuration>( configBytes )!;
		Require( config.MeanRgbToleranceBytes is >= 0 and <= 1 && config.MaximumRgbToleranceBytes is >= 0 and <= 3
			&& config.MinimumLitLightingChangeBytes >= 8 && double.IsFinite( config.MinimumLitLightingChangeBytes )
			&& config.MaximumPositionErrorInches is >= 0 and <= .0002f && config.MaximumNormalError is >= 0 and <= .00001f, "Candidate thresholds exceed the reviewed bounds." );
		VerifyCurrentAdapterRed( config );
		var generation = JsonSerializer.Deserialize<Generation>( ReadVerified( config.GenerationManifestPath, config.GenerationManifestSha256 ) )!;
		Require( generation.Version == 1 && generation.GraphSha256 == GraphSha256 && generation.CandidateSourceSha256 == config.CandidateSourceSha256
			&& generation.ReferenceSourceSha256 == config.ReferenceSourceSha256, "Generation manifest differs from reviewed source hashes." );
		Require( ActiveEditorType( "Editor.ShaderGraph.GraphCompiler" ).Assembly.ManifestModule.ModuleVersionId.ToString() == generation.GraphToolsMvid
			&& ActiveEditorType( "Editor.ShaderGraph.ShaderGraph" ).Assembly.ManifestModule.ModuleVersionId.ToString() == generation.GraphToolsMvid,
			"Generated graph's producer is no longer the active editor graph/compiler." );
		var candidateBytes = config.SourceShaderPath is null ? ReadVerified( generation.CandidateSourcePath, config.CandidateSourceSha256 ) : ReadVerified( config.SourceShaderPath, config.SourceShaderSha256 );
		var referenceBytes = ReadVerified( generation.ReferenceSourcePath, config.ReferenceSourceSha256 );
		var computeBytes = ReadVerified( Path.Combine( checkout, "engine/Tools/Gltf/direct-gpu/shaders/gltf_direct_deform.shader" ), config.ComputeSourceSha256 );
		var output = FreshOutput( checkout, config.OutputDirectory );
		File.WriteAllBytes( Path.Combine( output, "configuration.json" ), configBytes );
		var shaders = new List<Shader>(); var materials = new List<Material>();
		CandidateCapture capture = null;
		try
		{
			async Task<Material> Compile( string label, byte[] bytes )
			{
				File.WriteAllBytes( Path.Combine( output, label + ".shader" ), bytes );
				Measurements.Add( new { phase = "anonymous_shader_requested", label, sha256 = Hash( bytes ) } );
				using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
				var shader = await Shader.CompileAsync( Encoding.UTF8.GetString( bytes ), timeout.Token ); await MainThread.Wait();
				if ( shader is not null ) shaders.Add( shader ); Require( shader is { IsValid: true, IsAnonymous: true }, "Candidate shader did not compile/load: " + label );
				var material = Material.Create( "unlit_candidate_" + label + "_" + Guid.NewGuid().ToString( "N" ), shader );
				if ( material is not null ) materials.Add( material ); Require( material is { IsValid: true, IsError: false } && ReferenceEquals( material.Shader, shader ), "Material lost anonymous shader identity." );
				Measurements.Add( new { phase = "anonymous_shader_loaded", label, sha256 = Hash( bytes ), shader.ResourcePath, shader.IsAnonymous, material.ShaderName, schema = shader.Schema } );
				return material;
			}
			var candidate = await Compile( "candidate", candidateBytes );
			var reference = await Compile( "reference", referenceBytes );
			var compute = await Compile( "compute", computeBytes );
			Require( candidate.Shader.Schema.Variables.Any( variable => variable.Name == "g_vGltfBaseColor" ), "Compiled candidate lacks the reviewed factor uniform." );
			var candidateMaterials = new Material[2]; var litMaterials = new Material[2];
			for ( var material = 0; material < 2; material++ )
			{
				candidateMaterials[material] = candidate.CreateCopy(); materials.Add( candidateMaterials[material] );
				SetFactor( candidateMaterials[material], UnlitTestReference.BaseColors[material] );
				litMaterials[material] = Material.Load( "materials/default/white.vmat" ).CreateCopy(); materials.Add( litMaterials[material] );
				var factor = UnlitTestReference.BaseColors[material];
				litMaterials[material].Set( "g_vColorTint", new global::Vector4( Srgb( factor.X ), Srgb( factor.Y ), Srgb( factor.Z ), 1 ) );
				litMaterials[material].Set( "g_flMetalness", 0 ); litMaterials[material].SetFeature( "F_RENDER_BACKFACES", 0 );
			}
			capture = new CandidateCapture( output );
			AssertEmpty( await capture.Take( "empty_before" ) );
			await TriangleControls( capture, candidateMaterials, reference, config );
			var completedForms = 0;
			foreach ( var colorCase in new[] { (Name: "white", Color: NVector4.One), (Name: "nonwhite_u8", Color: new NVector4( 64f / 255f, 128f / 255f, 192f / 255f, 1 )) } )
			{
				var masks = new IReadOnlyList<int>[2];
				for ( var node = 0; node < 2; node++ )
				{
					using ( var literal = new LiteralDraw( capture.Scene.SceneWorld, new[] { ModelDraw( node, colorCase.Color, reference, bakeFactor: true ) } ) )
						masks[node] = Interior( await capture.Take( colorCase.Name + "_mask_" + node ), 3 );
					await capture.FlushDeletes(); AssertEmpty( await capture.Take( colorCase.Name + "_mask_" + node + "_deleted" ) );
				}
				CandidateCapture.Frame expected;
				using ( var literal = new LiteralDraw( capture.Scene.SceneWorld, Enumerable.Range( 0, 2 ).Select( node => ModelDraw( node, colorCase.Color, reference, bakeFactor: true ) ).ToArray() ) )
				{
					expected = await capture.Take( colorCase.Name + "_literal_reference" ); Require( literal.DrawCalls > 0 && literal.Errors.Length == 0, "Independent reference did not submit." );
				}
				await capture.FlushDeletes(); AssertEmpty( await capture.Take( colorCase.Name + "_reference_deleted" ) );
				CandidateCapture.Frame darkLit, brightLit;
				using ( var lit = new LiteralDraw( capture.Scene.SceneWorld, Enumerable.Range( 0, 2 ).Select( node => ModelDraw( node, NVector4.One, litMaterials[node], bakeFactor: false ) ).ToArray() ) )
				{
					capture.Light( false ); darkLit = await capture.Take( colorCase.Name + "_lit_control_dark" );
					capture.Light( true ); brightLit = await capture.Take( colorCase.Name + "_lit_control_bright" );
					Require( lit.DrawCalls > 0 && lit.Errors.Length == 0, "Lit negative control did not submit." );
				}
				await capture.FlushDeletes(); AssertEmpty( await capture.Take( colorCase.Name + "_lit_deleted" ) );
				var forms = colorCase.Name == "white" ? UnlitTestReference.OpenForms( checkout ) : NonwhiteForms( checkout );
				foreach ( var form in forms )
				{
					using var document = form.Read();
					using var asset = config.SourceShaderPath is null
						? GltfGpuAsset.Create( document, compute, index => candidateMaterials[index ?? throw new InvalidOperationException( "Expected authored material binding." )] )
						: GltfGpuAsset.Create( document, compute, _ => null );
					if ( config.SourceShaderPath is not null ) asset.ConfigureProjectedSourceMaterials( 1024 * 1024, (_, _) => candidate, global::Sandbox.GltfUnlitMaterialPlan.Features.LinearFactor | global::Sandbox.GltfUnlitMaterialPlan.Features.VertexRgb );
					var activeMaterials = config.SourceShaderPath is null ? candidateMaterials : asset.GetMaterials().ToArray();
					var instance = asset.CreateInstance( capture.Scene.SceneWorld ); var beforeDelete = 0;
					try
					{
						capture.Light( false ); var dark = await capture.Take( SafeName( form.Name ) + "_" + colorCase.Name + "_candidate_dark" );
						capture.Light( true ); var bright = await capture.Take( SafeName( form.Name ) + "_" + colorCase.Name + "_candidate_bright" );
						var gpu = CheckGpu( instance, colorCase.Color, config );
						var image = UnlitTestImageChecks.Measure( expected.Rgba, dark.Rgba, bright.Rgba, darkLit.Rgba, brightLit.Rgba, masks );
						Measurements.Add( new { phase = "candidate_model", form = form.Name, color_case = colorCase.Name, material_scope = config.SourceShaderPath is null ? "explicit candidate resolver" : "actual installed unlit source adapter + projected materials; public loader pending", gpu, image,
							dark_image = dark.Path, bright_image = bright.Path, reference_image = expected.Path, scope = "Linux untextured opaque candidate, not full extension or Model.Load acceptance." } );
						UnlitTestImageChecks.RequireWithin( image, config.MeanRgbToleranceBytes, config.MaximumRgbToleranceBytes, config.MeanRgbToleranceBytes, config.MaximumRgbToleranceBytes, config.MinimumLitLightingChangeBytes );
						CompareCoverage( expected, dark ); CompareCoverage( expected, bright );
						if ( colorCase.Name == "white" && form.Name == "glTF/source" )
						{
							var original = UnlitTestReference.BaseColors[0]; SetFactor( activeMaterials[0], new( original.X, Srgb( original.Y ), original.Z, 1 ) );
							var wrong = await capture.Take( "wrong_srgb_factor_negative" );
							var negative = UnlitTestImageChecks.Measure( expected.Rgba, wrong.Rgba, wrong.Rgba, darkLit.Rgba, brightLit.Rgba, masks );
							var rejected = false; try { UnlitTestImageChecks.RequireWithin( negative, config.MeanRgbToleranceBytes, config.MaximumRgbToleranceBytes, config.MeanRgbToleranceBytes, config.MaximumRgbToleranceBytes, config.MinimumLitLightingChangeBytes ); }
							catch ( InvalidOperationException ) { rejected = true; }
							Require( rejected, "Independent constant-color reference failed to reject an encoded-space factor." );
							SetFactor( activeMaterials[0], original ); var restored = await capture.Take( "linear_factor_restored" ); Compare( bright, restored, masks.SelectMany( mask => mask ).ToArray(), config );
							Measurements.Add( new { phase = "encoded_factor_negative_control", rejected, negative } );
						}
						beforeDelete = instance.DrawCalls;
					}
					finally { instance.Dispose(); }
					await capture.FlushDeletes(); AssertEmpty( await capture.Take( SafeName( form.Name ) + "_" + colorCase.Name + "_candidate_deleted" ) );
					Require( instance.DrawCalls == beforeDelete && instance.CallbackErrors.Length == 0, "Deleted candidate continued drawing or left callback errors." );
					completedForms++;
				}
			}
			Require( completedForms == 6, "Candidate form inventory changed or did not finish." );
			Require( HashFile( configurationPath ) == configHash && HashFile( generation.CandidateSourcePath ) == config.CandidateSourceSha256 && HashFile( generation.ReferenceSourcePath ) == config.ReferenceSourceSha256, "Candidate execution inputs changed during the run." );
			Measurements.Add( new { phase = "complete", passed = true, output, model_forms = completedForms, scope = "Provided Surface/Unlit candidate proof only. Shared adapter and production loading are unchanged." } );
		}
		finally
		{
			await MainThread.Wait(); capture?.Dispose();
			for ( var index = materials.Count - 1; index >= 0; index-- ) materials[index]?.Destroy();
			for ( var index = shaders.Count - 1; index >= 0; index-- ) shaders[index]?.Destroy();
		}
	}

	private static void SetFactor( Material material, NVector4 factor )
	{
		Require( material is { IsValid: true, IsError: false }, "Candidate material copy is invalid." );
		material.Set( "g_vGltfBaseColor", new global::Vector4( factor.X, factor.Y, factor.Z, 1 ) );
		var actual = material.GetVector4( "g_vGltfBaseColor" );
		Require( actual.x == factor.X && actual.y == factor.Y && actual.z == factor.Z, "Candidate uniform round-trip changed the linear factor." );
	}
	private static float Srgb( float value ) => value <= .0031308f ? 12.92f * value : 1.055f * MathF.Pow( value, 1 / 2.4f ) - .055f;
	private static string SafeName( string value ) => value.Replace( '/', '_' );
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static string HashFile( string path ) => Hash( File.ReadAllBytes( path ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
	private static byte[] ReadVerified( string path, string hash )
	{
		var info = new FileInfo( path ); Require( info.Exists && info.Length is > 0 and < 4 * 1024 * 1024, "Missing/oversized frozen candidate input: " + path );
		var bytes = File.ReadAllBytes( path ); Require( Hash( bytes ) == hash, "Candidate input differs from reviewed hash: " + path ); return bytes;
	}
	private static string OutputPath( string root, string path )
	{
		var output = Path.GetFullPath( path ); var allowed = Path.Combine( root, "Temp/gltf-validation/direct-gpu" ) + Path.DirectorySeparatorChar;
		Require( output.StartsWith( allowed, StringComparison.Ordinal ), "Candidate outputs must stay inside the coordinator's direct-GPU temporary directory." ); return output;
	}
	private static string FreshOutput( string root, string path )
	{
		var output = OutputPath( root, path ); Require( !Directory.Exists( output ) || !Directory.EnumerateFileSystemEntries( output ).Any(), "Use a fresh candidate output directory; previous evidence must not be overwritten." );
		Directory.CreateDirectory( output ); return output;
	}
	private static object AssemblyIdentity( Type type )
	{
		var assembly = type.Assembly; var location = assembly.Location;
		return new { type = type.FullName, assembly = assembly.FullName, mvid = assembly.ManifestModule.ModuleVersionId.ToString(), location, sha256 = string.IsNullOrEmpty( location ) || !File.Exists( location ) ? null : HashFile( location ) };
	}
	private static Type ActiveEditorType( string fullName )
	{
		var globals = typeof( Editor.SceneRenderingWidget ).Assembly.GetType( "Sandbox.Internal.GlobalToolsNamespace", true );
		var library = globals.GetProperty( "EditorTypeLibrary", BindingFlags.Public | BindingFlags.Static )!.GetValue( null );
		Require( library is not null, "The active editor type library is unavailable." );
		var lookup = library.GetType().GetMethods( BindingFlags.Public | BindingFlags.Instance ).Single( method => method.Name == "GetType"
			&& !method.IsGenericMethod && !method.ContainsGenericParameters
			&& method.GetParameters().Select( parameter => parameter.ParameterType ).SequenceEqual( new[] { typeof( string ), typeof( bool ) } ) );
		var description = lookup.Invoke( library, new object[] { fullName, true } );
		var type = description?.GetType().GetProperty( "TargetType" )?.GetValue( description ) as Type;
		Require( type is not null && type.FullName == fullName, "The active editor type registry does not contain " + fullName ); return type;
	}
	private static void RequireIdle()
	{
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires the coordinator's idle editor main thread." );
		ThreadSafe.AssertIsMainThread(); CandidateCapture.RequireNative();
	}
	private static void VerifyCurrentAdapterRed( Configuration config )
	{
		using var receipt = JsonDocument.Parse( ReadVerified( config.AdapterRedReceiptPath, config.AdapterRedReceiptSha256 ) ); var root = receipt.RootElement;
		Require( root.GetProperty( "phase" ).GetString() == "complete" && root.GetProperty( "engine_mvid" ).GetString() == typeof( Model ).Assembly.ManifestModule.ModuleVersionId.ToString()
			&& root.GetProperty( "gltf_mvid" ).GetString() == typeof( GltfReader ).Assembly.ManifestModule.ModuleVersionId.ToString(), "Retain actual current-producer material failure before the candidate proof." );
		var failures = root.GetProperty( "tests" ).EnumerateArray().Where( row => row.TryGetProperty( "result", out var result ) && result.TryGetProperty( "Name", out var name ) && name.GetString() == "default_stock_material_preparation_accepts_required_unlit" ).Select( row => row.GetProperty( "result" ) ).ToArray();
		Require( failures.Length == 4 && failures.All( row => !row.GetProperty( "Passed" ).GetBoolean() && row.GetProperty( "Error" ).GetString()!.Contains( "unlit", StringComparison.OrdinalIgnoreCase ) ), "The four actual default-unlit failure rows are absent or no longer red." );
		Measurements.Add( new { phase = "current_default_material_red_retained", path = config.AdapterRedReceiptPath, sha256 = config.AdapterRedReceiptSha256, failed_forms = failures.Select( row => row.GetProperty( "Form" ).GetString() ).ToArray() } );
	}

	private static UnlitTestReference.Form[] NonwhiteForms( string root )
	{
		var original = UnlitTestReference.ResolvePinnedBinary( root, "UnlitTest.bin" ); var bytes = new byte[original.Length + 96 * 4]; original.CopyTo( bytes, 0 );
		for ( var vertex = 0; vertex < 96; vertex++ )
		{
			bytes[original.Length + vertex * 4] = 64; bytes[original.Length + vertex * 4 + 1] = 128;
			bytes[original.Length + vertex * 4 + 2] = 192; bytes[original.Length + vertex * 4 + 3] = 255;
		}
		var source = UnlitTestReference.SourceJson( root ); source["buffers"]![0]!["byteLength"] = bytes.Length;
		var views = source["bufferViews"]!.AsArray(); var colorView = views.Count;
		views.Add( JsonNode.Parse( $"{{\"buffer\":0,\"byteOffset\":{original.Length},\"byteLength\":384}}" ) );
		var accessors = source["accessors"]!.AsArray(); var colorAccessor = accessors.Count;
		accessors.Add( JsonNode.Parse( $"{{\"bufferView\":{colorView},\"componentType\":5121,\"normalized\":true,\"count\":96,\"type\":\"VEC4\"}}" ) );
		for ( var mesh = 0; mesh < 2; mesh++ ) source["meshes"]![mesh]!["primitives"]![0]!["attributes"]!["COLOR_0"] = colorAccessor;
		var json = Encoding.UTF8.GetBytes( source.ToJsonString() );
		byte[] Resolve( string uri ) => uri == "UnlitTest.bin" ? bytes : throw new InvalidOperationException( "Unexpected derived-fixture dependency: " + uri );
		return new[]
		{
			new UnlitTestReference.Form { Name = "derived_nonwhite_COLOR_0/source", Bytes = json, Resolve = Resolve },
			new UnlitTestReference.Form { Name = "derived_nonwhite_COLOR_0/compiled_glb", Compiled = true, Bytes = GltfCompiler.Compile( json, Resolve, UnlitTestReference.Selected ), Resolve = Resolve }
		};
	}

	private static object CheckGpu( GltfGpuInstance instance, NVector4 sourceColor, Configuration config )
	{
		Require( instance.DrawCalls > 0 && instance.PoseRevision == instance.SubmittedRevision && instance.CallbackErrors.Length == 0, "Candidate has not submitted its current raw GPU pose." );
		var rows = instance.Readback(); Require( rows.All( row => row.IndexDataOrigin == "gpu-readback" ), "Candidate index comparison needs actual bound GPU bytes." );
		var geometry = UnlitTestGpuChecks.Measure( rows, NMatrix.Identity );
		Require( geometry.Finite && geometry.TopologyMatches && geometry.Nodes == 2 && geometry.Vertices == 192 && geometry.Indices == 264
			&& geometry.MaximumPositionErrorInches <= config.MaximumPositionErrorInches && geometry.MaximumNormalError <= config.MaximumNormalError
			&& geometry.MaximumGeneratedUvError == 0, "Candidate GPU geometry differs from independent literal positions/normals/topology." );
		var maximumColor = 0f;
		foreach ( var row in rows )
		{
			var field = row.Fields.Single( value => value.Semantic == "COLOR" && value.SemanticIndex == 0 );
			for ( var vertex = 0; vertex < row.VertexCount; vertex++ ) for ( var component = 0; component < 4; component++ )
			{
				var expected = component switch { 0 => sourceColor.X, 1 => sourceColor.Y, 2 => sourceColor.Z, _ => sourceColor.W };
				var actual = BinaryPrimitives.ReadSingleLittleEndian( row.Bytes.AsSpan( vertex * row.VertexStride + field.Offset + component * 4, 4 ) );
				Require( float.IsFinite( actual ), "Nonfinite candidate source color." ); maximumColor = MathF.Max( maximumColor, MathF.Abs( actual - expected ) );
			}
		}
		Require( maximumColor <= 4 * 1.1920928955078125e-7f, "Candidate COLOR_0 normalization disagrees with the literal U8 input." );
		return new { geometry, maximum_source_color_error = maximumColor, draws = instance.DrawCalls,
			rows = rows.Select( row => new { row.Node, row.Mesh, row.Primitive, row.VertexCount, row.VertexStride, row.Fields, row.IndexDataOrigin, bytes_sha256 = Hash( row.Bytes ), indices = row.Indices } ).ToArray(),
			scope = "Actual GPU geometry/color/index bytes. SourceVertices is CPU provenance metadata; geometry.MaximumGeneratedColorError is only the older white-default diagnostic, not this derived fixture's color acceptance." };
	}

	[StructLayout( LayoutKind.Sequential )]
	private struct ReferenceVertex
	{
		[VertexLayout.Position] public global::Vector3 Position;
		[VertexLayout.Color] public global::Vector4 Color;
		[VertexLayout.Normal] public global::Vector3 Normal;
		[VertexLayout.TexCoord] public global::Vector4 TexCoord;
		[VertexLayout.Tangent] public global::Vector4 Tangent;
	}
	private sealed record Draw( ReferenceVertex[] Vertices, Material Material );
	private static Draw ModelDraw( int node, NVector4 color, Material material, bool bakeFactor )
	{
		var output = new ReferenceVertex[UnlitTestReference.Indices.Length]; var product = bakeFactor ? color * UnlitTestReference.BaseColors[node] : color;
		for ( var corner = 0; corner < output.Length; corner++ )
		{
			var source = checked((int)UnlitTestReference.Indices[corner]); var p = UnlitTestReference.EnginePosition( node, source ); var n = UnlitTestReference.EngineNormal( source );
			output[corner] = LiteralVertex( new global::Vector3( p.X, p.Y, p.Z ), new global::Vector3( n.X, n.Y, n.Z ), product );
		}
		return new( output, material );
	}
	private static ReferenceVertex LiteralVertex( global::Vector3 p, global::Vector3 n, NVector4 color ) => new()
	{
		Position = p, Normal = n, Color = new global::Vector4( color.X, color.Y, color.Z, 1 ),
		Tangent = new global::Vector4( 0, 1, 0, 1 ), TexCoord = global::Vector4.Zero
	};
	private static Draw TriangleDraw( float depth, Material material, NVector4 color, global::Vector3 normal, bool reversed = false )
	{
		var vertices = new[]
		{
			LiteralVertex( new global::Vector3( depth, -30, -30 ), normal, color ),
			LiteralVertex( new global::Vector3( depth, 30, -30 ), normal, color ),
			LiteralVertex( new global::Vector3( depth, 0, 30 ), normal, color )
		};
		if ( reversed ) (vertices[1], vertices[2]) = (vertices[2], vertices[1]);
		return new( vertices, material );
	}
	private static async Task TriangleControls( CandidateCapture capture, Material[] candidates, Material reference, Configuration config )
	{
		async Task<CandidateCapture.Frame> Take( string label, Draw[] draws )
		{
			CandidateCapture.Frame frame; LiteralDraw literal;
			using ( literal = new LiteralDraw( capture.Scene.SceneWorld, draws ) )
			{
				frame = await capture.Take( label ); Require( literal.DrawCalls > 0 && literal.Errors.Length == 0, "Literal candidate control did not draw: " + label );
			}
			var calls = literal.DrawCalls; await capture.FlushDeletes(); AssertEmpty( await capture.Take( label + "_deleted" ) );
			Require( literal.DrawCalls == calls, "Deleted literal candidate callback continued running." ); return frame;
		}
		var expected = await Take( "triangle_literal_orange", new[] { TriangleDraw( 0, reference, UnlitTestReference.BaseColors[0], global::Vector3.Forward ) } );
		var mask = Interior( expected, 3, 64 );
		var front = await Take( "triangle_candidate_front", new[] { TriangleDraw( 0, candidates[0], NVector4.One, global::Vector3.Forward ) } );
		Compare( expected, front, mask, config ); CompareCoverage( expected, front );
		var differentNormal = await Take( "triangle_candidate_different_normal", new[] { TriangleDraw( 0, candidates[0], NVector4.One, global::Vector3.Left ) } );
		Compare( expected, differentNormal, mask, config ); CompareCoverage( expected, differentNormal );
		AssertEmpty( await Take( "triangle_candidate_reversed", new[] { TriangleDraw( 0, candidates[0], NVector4.One, global::Vector3.Forward, reversed: true ) } ) );
		var expectedNear = await Take( "triangle_literal_near_blue", new[] { TriangleDraw( 10, reference, UnlitTestReference.BaseColors[1], global::Vector3.Forward ) } );
		var near = TriangleDraw( 10, candidates[1], NVector4.One, global::Vector3.Forward ); var far = TriangleDraw( 0, candidates[0], NVector4.One, global::Vector3.Forward );
		foreach ( var pair in new[] { (Name: "far_then_near", Draws: new[] { far, near }), (Name: "near_then_far", Draws: new[] { near, far }) } )
		{
			var actual = await Take( "triangle_depth_" + pair.Name, pair.Draws ); Compare( expectedNear, actual, Interior( expectedNear, 3, 64 ), config ); CompareCoverage( expectedNear, actual );
		}
		Measurements.Add( new { phase = "literal_3d_controls", passed = true, controls = new[] { "front_positive", "normal_invariance", "reversed_empty", "near_blue_wins_both_submission_orders", "deleted_callbacks_stop" } } );
	}
	private static IReadOnlyList<int> Interior( CandidateCapture.Frame reference, int radius, int minimum = 512 )
	{
		var mask = new List<int>();
		for ( var y = radius; y < reference.Height - radius; y++ ) for ( var x = radius; x < reference.Width - radius; x++ )
		{
			var inside = true;
			for ( var dy = -radius; dy <= radius && inside; dy++ ) for ( var dx = -radius; dx <= radius; dx++ )
				if ( !CandidateCapture.Foreground( reference.Pixels[(y + dy) * reference.Width + x + dx] ) ) { inside = false; break; }
			if ( inside ) mask.Add( y * reference.Width + x );
		}
		Require( mask.Count >= minimum, $"Independent reference interior has {mask.Count}, needs {minimum} pixels." ); return mask;
	}
	private static void Compare( CandidateCapture.Frame expected, CandidateCapture.Frame actual, IReadOnlyList<int> mask, Configuration config )
	{
		Require( expected.Width == actual.Width && expected.Height == actual.Height && mask.Count > 0, "Matched candidate/reference dimensions or mask are invalid." );
		long sum = 0; var maximum = 0;
		foreach ( var pixel in mask ) for ( var channel = 0; channel < 3; channel++ )
		{
			var difference = Math.Abs( expected.Rgba[pixel * 4 + channel] - actual.Rgba[pixel * 4 + channel] ); sum += difference; maximum = Math.Max( maximum, difference );
		}
		var mean = sum / (double)(mask.Count * 3);
		Measurements.Add( new { phase = "literal_image_comparison", expected = expected.Path, actual = actual.Path, interior_pixels = mask.Count, mean_rgb_bytes = mean, maximum_rgb_bytes = maximum } );
		Require( mean <= config.MeanRgbToleranceBytes && maximum <= config.MaximumRgbToleranceBytes, "Candidate differs from independent literal-color reference." );
	}
	private static void CompareCoverage( CandidateCapture.Frame expected, CandidateCapture.Frame actual )
	{
		Require( expected.Pixels.Length == actual.Pixels.Length, "Candidate coverage frame sizes differ." ); var intersection = 0; var union = 0;
		for ( var pixel = 0; pixel < expected.Pixels.Length; pixel++ )
		{
			var a = CandidateCapture.Foreground( expected.Pixels[pixel] ); var b = CandidateCapture.Foreground( actual.Pixels[pixel] );
			if ( a && b ) intersection++; if ( a || b ) union++;
		}
		var iou = union == 0 ? 0 : intersection / (double)union;
		Measurements.Add( new { phase = "literal_coverage_comparison", expected = expected.Path, actual = actual.Path, intersection, union, iou } ); Require( iou >= .99, "Candidate world projection/coverage differs from literal geometry." );
	}
	private static void AssertEmpty( CandidateCapture.Frame frame ) => Require( frame.Coverage == 0, "Fresh deleted/empty candidate scene retained pixels: " + frame.Path );

	private sealed class LiteralDraw : IDisposable
	{
		private readonly SceneCustomObject sceneObject;
		private readonly ConcurrentQueue<string> errors = new();
		public int DrawCalls { get; private set; }
		public string[] Errors => errors.ToArray();
		public LiteralDraw( SceneWorld world, Draw[] draws )
		{
			sceneObject = new SceneCustomObject( world ) { Bounds = BBox.FromPoints( draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ) ) };
			sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
			sceneObject.RenderOverride = _ =>
			{
				try { DrawCalls++; foreach ( var draw in draws ) Graphics.Draw<ReferenceVertex>( draw.Vertices.AsSpan(), draw.Vertices.Length, draw.Material ); }
				catch ( Exception error ) { errors.Enqueue( error.ToString() ); }
			};
		}
		public void Dispose() { sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); }
	}

	private sealed class CandidateCapture : IDisposable
	{
		public sealed record Frame( global::Color[] Pixels, byte[] Rgba, int Coverage, string Path, string Sha256, int Width, int Height );
		public readonly Scene Scene;
		private readonly CameraComponent camera;
		private readonly ScenePointLight light;
		private readonly Editor.SceneRenderingWidget widget;
		private readonly string output;
		private readonly Action onFrame;
		private int frames;
		private const int Size = 192;
		public CandidateCapture( string output )
		{
			this.output = output; RequireNative(); Scene = Scene.CreateEditorScene();
			using ( Scene.Push() )
			{
				camera = Scene.CreateObject().Components.Create<CameraComponent>();
				camera.WorldPosition = new global::Vector3( 200, 0, 0 ); camera.WorldRotation = Rotation.FromYaw( 180 );
				camera.Orthographic = true; camera.OrthographicHeight = 220; camera.ZNear = 1; camera.ZFar = 1000;
				camera.CustomSize = new global::Vector2( Size, Size ); camera.BackgroundColor = global::Color.Black; camera.ClearFlags = ClearFlags.All;
				camera.EnablePostProcessing = true; camera.DebugMode = SceneCameraDebugMode.Normal; LockExposure();
			}
			light = new ScenePointLight( Scene.SceneWorld, new global::Vector3( 200, -30, 60 ), 1000, global::Color.White * 8 );
			widget = new Editor.SceneRenderingWidget { Scene = Scene, Camera = camera, WindowTitle = "UnlitTest provided graph candidate", Size = new global::Vector2( 384, 384 ) };
			onFrame = () => { frames++; LockExposure(); }; widget.OnPreFrame += onFrame; widget.Show();
		}
		public static void RequireNative()
		{
			var type = typeof( Scene ).Assembly.GetType( "Sandbox.Rendering.ManagedSceneRendering", true );
			var property = type.GetProperty( "Enabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic );
			Require( !(bool)property!.GetValue( null )!, "Candidate gate keeps the existing native renderer." );
		}
		private void LockExposure()
		{
			camera.AutoExposure.Enabled = false; camera.AutoExposure.MinimumExposure = 1; camera.AutoExposure.MaximumExposure = 1; camera.AutoExposure.Compensation = 0;
			if ( camera.SceneCamera is not null ) camera.SceneCamera.Tonemap.Enabled = false;
		}
		public void SetHeight( float height ) { camera.OrthographicHeight = height; }
		public void Light( bool bright ) { RequireIdle(); light.LightColor = global::Color.White * (bright ? 8 : 0); }
		private async Task WaitFrames()
		{
			await MainThread.Wait(); RequireIdle(); var first = frames; var timer = Stopwatch.StartNew();
			while ( frames < first + 3 )
			{
				Require( timer.Elapsed < TimeSpan.FromSeconds( 10 ), "Visible candidate preview stopped receiving real frames." ); await Task.Delay( 20 ); await MainThread.Wait(); RequireIdle();
			}
		}
		public async Task FlushDeletes() { using ( Scene.Push() ) Scene.ProcessDeletes(); await WaitFrames(); }
		public async Task<Frame> Take( string label )
		{
			await WaitFrames();
			using ( Scene.Push() ) { Scene.PreCameraRender(); LockExposure(); camera.InitializeRendering( false ); LockExposure(); }
			Require( !camera.SceneCamera.Tonemap.Enabled && camera.CustomSize == new global::Vector2( Size, Size ), "Candidate capture lost its locked exposure or exact dimensions." );
			using var target = Texture.CreateRenderTarget().WithSize( Size, Size ).WithFormat( ImageFormat.RGBA8888 ).WithInitialColor( global::Color.Magenta ).Create( "unlit_candidate_" + Guid.NewGuid().ToString( "N" ) );
			bool submitted; using ( Scene.Push() ) { target.Clear( global::Color.Magenta ); submitted = camera.RenderToTexture( target ); }
			Require( submitted, "Native candidate camera rejected capture." ); await WaitFrames(); using var bitmap = target.GetBitmap(); Require( bitmap is not null, "Candidate target readback is absent." );
			var pixels = bitmap.GetPixels(); Require( pixels.Length == Size * Size, "Candidate target extent changed." );
			var png = bitmap.ToPng(); var path = Path.Combine( output, label + ".png" ); File.WriteAllBytes( path, png ); var coverage = pixels.Count( Foreground );
			var rgba = new byte[pixels.Length * 4];
			for ( var pixel = 0; pixel < pixels.Length; pixel++ )
			{
				rgba[pixel * 4] = Byte( pixels[pixel].r ); rgba[pixel * 4 + 1] = Byte( pixels[pixel].g ); rgba[pixel * 4 + 2] = Byte( pixels[pixel].b ); rgba[pixel * 4 + 3] = Byte( pixels[pixel].a );
			}
			Measurements.Add( new { phase = "capture", label, path, sha256 = Hash( png ), coverage, width = Size, height = Size, engine_frame = Application.FrameCount, widget_frames = frames,
				camera_position = camera.WorldPosition.ToString(), camera_rotation = camera.WorldRotation.ToString(), camera.OrthographicHeight, camera.DebugMode, camera.EnablePostProcessing,
				auto_exposure_enabled = camera.AutoExposure.Enabled, native_exposure_enabled = camera.SceneCamera.Tonemap.Enabled, light_position = light.Position.ToString(), light_color = light.LightColor.ToString() } );
			return new( pixels, rgba, coverage, path, Hash( png ), Size, Size );
		}
		private static byte Byte( float value ) { Require( float.IsFinite( value ), "Nonfinite native bitmap pixel." ); return (byte)Math.Clamp( (int)Math.Round( value * 255 ), 0, 255 ); }
		public static bool Foreground( global::Color value ) => value.r > .03f || value.g > .03f || value.b > .03f;
		public void Dispose()
		{
			if ( widget.IsValid ) { widget.OnPreFrame -= onFrame; widget.Close(); widget.Destroy(); }
			if ( light.IsValid() ) light.Delete(); Scene.Destroy();
		}
	}
}
