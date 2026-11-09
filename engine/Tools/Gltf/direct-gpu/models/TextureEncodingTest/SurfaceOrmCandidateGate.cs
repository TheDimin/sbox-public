using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

/// <summary>
/// Uses the actual active provided graph generator, then a separate hash-gated
/// template session for coordinator-owned controls. Generation never grants
/// permission to select a default template or advertise GPU/material support.
/// </summary>
public static class SurfaceOrmCandidateGate
{
	public const string GraphSha256 = "1f2860b53fe1c3a377515d915e19bcf12b872a12ae94ec5fa84e9a6436fcbb0e";
	private static readonly string[] Roles = ["BaseColor", "MetallicRoughness", "Occlusion", "Emissive"];
	public sealed record Generation( string GraphSha256, string ToolsMvid, object[] Producers, string SourcePath, string SourceSha256, int NodeCount, string Scope );

	/// <summary>
	/// Run under the coordinator's resource guard. Provided TextureSampler nodes
	/// can compile their default preview texture during construction; that is an
	/// existing tools side effect, not a worker-authorized compiler invocation.
	/// This method never invokes Shader.CompileAsync or creates a Model/Mesh.
	/// </summary>
	public static object[] GenerateForReview( string checkoutRoot, string outputDirectory, string expectedToolsMvid )
		=> GenerateGraphForReview( checkoutRoot, outputDirectory, expectedToolsMvid,
			"research/gltf-validation/direct-gpu/models/TextureEncodingTest/surface_orm_candidate.shdrgrph", GraphSha256, JsonSerializer.Serialize( Roles ) );

	/// <summary>
	/// Shared generation boundary for model-owned normal/alpha augmentations of
	/// the same provided Surface/Lit family. Each caller freezes its actual graph
	/// path/hash and consumed role names; no source is rewritten after generation.
	/// This overload does not broaden the default adapter's capabilities.
	/// </summary>
	public static object[] GenerateGraphForReview( string checkoutRoot, string outputDirectory, string expectedToolsMvid,
		string graphRelativePath, string expectedGraphSha256, string consumedRoleNamesJson )
	{
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires the coordinator's idle editor boundary." ); ThreadSafe.AssertIsMainThread();
		var root = Path.GetFullPath( checkoutRoot ); var output = FreshOutput( root, outputDirectory );
		var roles = JsonSerializer.Deserialize<string[]>( consumedRoleNamesJson );
		Require( roles is { Length: > 0 and <= 5 } && roles.Distinct( StringComparer.Ordinal ).Count() == roles.Length && roles.All( role => role is "BaseColor" or "MetallicRoughness" or "Normal" or "Occlusion" or "Emissive" ), "Consumed role names must be known, nonempty and unique." );
		var graphPath = Path.GetFullPath( Path.Combine( root, graphRelativePath ) );
		Require( graphPath.StartsWith( Path.Combine( root, "research/gltf-validation/direct-gpu/models" ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ), "Provided candidate inputs must remain in their owned model folders." );
		var bytes = File.ReadAllBytes( graphPath ); Require( bytes.Length is > 0 and < 256 * 1024 && Hash( bytes ) == expectedGraphSha256, "Surface/ORM candidate graph changed from its reviewed freeze." );
		var graphJson = JsonNode.Parse( bytes )!.AsObject(); var expectedNodes = graphJson["nodes"]!.AsArray();
		var graphType = ActiveEditorType( "Editor.ShaderGraph.ShaderGraph" ); var compilerType = ActiveEditorType( "Editor.ShaderGraph.GraphCompiler" ); var samplerType = compilerType.Assembly.GetType( "Editor.ShaderGraph.Sampler", true );
		Require( graphType.Assembly == compilerType.Assembly && compilerType.Assembly.ManifestModule.ModuleVersionId.ToString() == expectedToolsMvid && samplerType.Assembly == compilerType.Assembly, "Active graph/sampler/compiler producer differs from the coordinator's frozen identity." );
		var attribute = samplerType.GetProperty( "Attribute", BindingFlags.Instance | BindingFlags.Public );
		Require( attribute is { PropertyType: not null, CanWrite: true } && attribute.PropertyType == typeof( string ), "The actual provided Sampler has no supported named typed-attribute contract; do not silently discard per-role samplers." );
		var graph = Activator.CreateInstance( graphType );
		graphType.GetMethod( "Deserialize", new[] { typeof( string ), typeof( string ) } )!.Invoke( graph, new object[] { graphJson.ToJsonString(), null } );
		var nodes = ((IEnumerable)graphType.GetProperty( "Nodes" )!.GetValue( graph )!).Cast<object>().ToArray();
		Require( nodes.Length == expectedNodes.Count && nodes.Select( node => node.GetType().Name ).Order().SequenceEqual( expectedNodes.Select( node => node!["_class"]!.GetValue<string>() ).Order() ), "The real graph did not resolve exactly the reviewed provided node inventory." );
		Require( nodes.All( node => node.GetType().Namespace is "Editor.ShaderGraph.Nodes" or "Editor.ShaderGraph" ) && nodes.All( node => ActiveEditorType( node.GetType().FullName! ) == node.GetType() ), "Graph resolved custom or historical node implementations." );
		foreach ( var role in roles )
		{
			var node = nodes.Single( node => node.GetType().Name == "TextureSampler" && (string)node.GetType().GetProperty( "Identifier" )!.GetValue( node )! == role.ToLowerInvariant() + "_texture" );
			var sampler = node.GetType().GetProperty( "Sampler" )!.GetValue( node );
			Require( (string)attribute.GetValue( sampler )! == "Gltf" + role + "Sampler", "Deserialization changed a canonical role's named sampler identity." );
		}
		var compiler = Activator.CreateInstance( compilerType, graph, false ); var source = (string)compilerType.GetMethod( "Generate" )!.Invoke( compiler, null );
		var errors = ((IEnumerable)compilerType.GetProperty( "Errors" )!.GetValue( compiler )!).Cast<object>().ToArray();
		Require( errors.Length == 0 && !string.IsNullOrWhiteSpace( source ), "Actual provided graph generation failed: " + JsonSerializer.Serialize( errors ) );
		Require( source.Contains( "Forward();", StringComparison.Ordinal ) && source.Contains( "Depth();", StringComparison.Ordinal ) && source.Contains( "ProcessVertex( v )", StringComparison.Ordinal ) && source.Contains( "FinalizeVertex( i )", StringComparison.Ordinal ) && source.Contains( "return ShadingModelStandard::Shade", StringComparison.Ordinal ), "Generated candidate lost the provided surface lighting/projection/depth route." );
		Require( source.Contains( "i.vColor.rgb", StringComparison.Ordinal ) && source.Contains( "i.vTextureCoords.xy", StringComparison.Ordinal ) && source.Contains( "i.vTextureCoords.zw", StringComparison.Ordinal ), "Generated candidate lost raw vertex color or distinct UV0/UV1 inputs." );
		foreach ( var role in roles )
		{
			Require( Regex.IsMatch( source, @"SamplerState\s+g_sSampler\d+\s*<[^>]*Attribute\s*\(\s*""Gltf" + role + @"Sampler""\s*\)", RegexOptions.CultureInvariant ), "Generated sampler declaration has no exact per-role typed attribute: " + role );
			var srgb = role is "BaseColor" or "Emissive";
			Require( Regex.IsMatch( source, @"Texture2D\s+g_tGltf" + role + @"Texture\s*<[^>]*SrgbRead\s*\(\s*" + (srgb ? "true" : "false") + @"\s*\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase ), "Generated texture declaration contradicted its canonical transfer role: " + role );
			Require( source.Contains( "g_flGltfHas" + role + "Texture", StringComparison.Ordinal ), "Generated role lacks its explicit exact0/1 texture-presence parameter: " + role );
			Require( source.Contains( "g_flGltf" + role + "Uv1", StringComparison.Ordinal ) && source.Contains( "g_vGltf" + role + "UvRowU", StringComparison.Ordinal ) && source.Contains( "g_vGltf" + role + "UvRowV", StringComparison.Ordinal ), "Generated role lacks independent selected UV and affine rows: " + role );
		}
		foreach ( var parameter in new[] { "g_vGltfBaseColor", "g_flGltfMetallicFactor", "g_flGltfRoughnessFactor", "g_flGltfOcclusionStrength", "g_vGltfEmissiveFactor" } ) Require( source.Contains( parameter, StringComparison.Ordinal ), "Generated source lost linear authored factor " + parameter );
		if ( roles.Contains( "Normal", StringComparer.Ordinal ) )
			foreach ( var parameter in new[] { "g_flGltfNormalScale", "g_flGltfHasNormalTexture", "g_flGltfDoubleSided" } ) Require( source.Contains( parameter, StringComparison.Ordinal ), "Generated normal candidate lost explicit scale/default/sidedness policy " + parameter );
		Require( !source.Contains( "g_vColorTint", StringComparison.Ordinal ) && !source.Contains( "g_flRoughnessScaleFactor", StringComparison.Ordinal ), "Provided candidate accidentally inherited the legacy packed complex-shader inputs." );
		var sourcePath = Path.Combine( output, "provided-surface-orm.shader" ); File.WriteAllText( sourcePath, source, new UTF8Encoding( false ) );
		var manifest = new Generation( expectedGraphSha256, expectedToolsMvid, nodes.Select( node => node.GetType() ).Append( graphType ).Append( compilerType ).Append( samplerType ).Distinct().Select( Identity ).ToArray(), sourcePath, Hash( File.ReadAllBytes( sourcePath ) ), nodes.Length,
			"Actual provided Surface/Lit graph generation only. UV0/UV1 source-texture candidate; no shader compilation, GPU sample, material allocation, alpha/backface correctness, final shading or default adapter capability proven." );
		var path = Path.Combine( output, "generation.json" ); File.WriteAllText( path, JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } ) );
		return [new { Passed = true, Name = "actual_provided_surface_source_orm_generated_for_review", ManifestPath = path, ManifestSha256 = Hash( File.ReadAllBytes( path ) ), Generated = manifest }];
	}

	/// <summary>
	/// Bounded coordinator-owned template session for sibling model controls.
	/// Compile only the separately reviewed generated source. Callers borrow Value
	/// and dispose every dependent material lease before disposing this session.
	/// This does not register a shipped or default production template.
	/// </summary>
	internal static async Task<GltfMaterialLease<Material>> AcquireReviewedTemplate( string generationManifestPath, string generationManifestSha256,
		string reviewedSourceSha256, string expectedGraphSha256 = GraphSha256 )
	{
		Require( Application.IsEditor && !Game.IsPlaying && Graphics.IsAvailable && !Graphics.IsActive, "Requires the coordinator's idle GPU boundary." ); ThreadSafe.AssertIsMainThread();
		var manifestBytes = File.ReadAllBytes( generationManifestPath ); Require( Hash( manifestBytes ) == generationManifestSha256, "Provided-template generation manifest changed after review." );
		var generation = JsonSerializer.Deserialize<Generation>( manifestBytes );
		Require( generation is not null && generation.GraphSha256 == expectedGraphSha256 && generation.SourceSha256 == reviewedSourceSha256
			&& ActiveEditorType( "Editor.ShaderGraph.GraphCompiler" ).Assembly.ManifestModule.ModuleVersionId.ToString() == generation.ToolsMvid, "Reviewed source no longer matches the actual active provided graph producer." );
		var bytes = File.ReadAllBytes( generation.SourcePath ); Require( Hash( bytes ) == reviewedSourceSha256, "Generated provided-template source changed after review." );
		Shader shader = null; Material material = null;
		try
		{
			using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) ); shader = await Shader.CompileAsync( Encoding.UTF8.GetString( bytes ), timeout.Token ); await MainThread.Wait();
			Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive && shader is { IsValid: true, IsAnonymous: true }, "Provided template compilation did not return a valid anonymous shader at an idle boundary." );
			material = Material.Create( "provided_surface_orm_" + Guid.NewGuid().ToString( "N" ), shader ); Require( material is { IsValid: true, IsError: false }, "Reviewed provided template material could not be created." );
			var retainedMaterial = material; var retainedShader = shader;
			return new GltfMaterialLease<Material>( material,
				() => GltfGpuCleanup.ReleaseAll( [retainedMaterial.Destroy, retainedShader.Destroy] ), () => { ThreadSafe.AssertIsMainThread(); Require( !Graphics.IsActive, "Dispose the reviewed template outside rendering." ); } );
		}
		catch ( Exception original )
		{
			await MainThread.Wait(); GltfGpuCleanup.Rollback( original, [() => material?.Destroy(), () => shader?.Destroy()] ); throw;
		}
	}

	private static Type ActiveEditorType( string fullName )
	{
		var globals = typeof( Editor.SceneRenderingWidget ).Assembly.GetType( "Sandbox.Internal.GlobalToolsNamespace", true ); var library = globals.GetProperty( "EditorTypeLibrary", BindingFlags.Public | BindingFlags.Static )!.GetValue( null );
		Require( library is not null, "Active editor type registry is unavailable." );
		var lookup = library.GetType().GetMethods( BindingFlags.Instance | BindingFlags.Public ).Single( method => method.Name == "GetType" && !method.IsGenericMethod && !method.ContainsGenericParameters && method.GetParameters() is var parameters && parameters.Length == 2 && parameters[0].ParameterType == typeof( string ) && parameters[1].ParameterType == typeof( bool ) );
		var description = lookup.Invoke( library, new object[] { fullName, true } ); var type = description?.GetType().GetProperty( "TargetType" )?.GetValue( description ) as Type;
		Require( type is not null && type.FullName == fullName, "Active editor type registry does not contain " + fullName ); return type;
	}
	private static string FreshOutput( string root, string path ) { var output = Path.GetFullPath( path ); Require( output.StartsWith( Path.Combine( root, "Temp/gltf-validation/direct-gpu" ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ), "Candidate outputs must remain in direct-GPU temporary evidence." ); Require( !Directory.Exists( output ) || !Directory.EnumerateFileSystemEntries( output ).Any(), "Use a fresh output directory; preserve previous evidence." ); Directory.CreateDirectory( output ); return output; }
	private static object Identity( Type type ) => new { type = type.FullName, assembly = type.Assembly.FullName, mvid = type.Assembly.ManifestModule.ModuleVersionId.ToString(), path = type.Assembly.Location, sha256 = string.IsNullOrEmpty( type.Assembly.Location ) ? null : Hash( File.ReadAllBytes( type.Assembly.Location ) ) };
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) { if ( !condition ) throw new InvalidOperationException( message ); }
}
