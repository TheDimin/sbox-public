using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using SharedSurfaceGate = Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation.SurfaceOrmCandidateGate;

namespace Sandbox.Gltf.DirectGpu.AlphaBlendModeValidation;

/// <summary>Actual provided Surface graph generation plus narrow, reviewable alpha/sidedness edits. No alternate template or shader compiler.</summary>
public static class AlphaSurfaceCandidateGate
{
	private static readonly Dictionary<string,string> GraphHashes = new()
	{
		["opaque"] = "76054a8d759c4a11698d96aeec0e072ee26f1918a7b3ee20d08b2f887c7a01d1",
		["masked"] = "438c7ed39f2a4ed0b8ba597699c4e7a3a1898c1c21410b1601c872ba5191cb2a",
		["translucent"] = "500a0bb4f18d595465914efdb2a32020c490fd6faa54f99880cf8fe23931ef2e"
	};
	/// <summary>Coordinator-only. Provided texture node construction can invoke its normal preview resource compilation. No Shader.CompileAsync here.</summary>
	public static object[] GenerateForReview( string checkoutRoot, string outputDirectory, string expectedToolsMvid )
	{
		Require( Application.IsEditor && !Game.IsPlaying && !Graphics.IsActive, "Requires verified idle editor." ); ThreadSafe.AssertIsMainThread();
		var root = Path.GetFullPath( checkoutRoot ); var output = Path.GetFullPath( outputDirectory );
		Require( output.StartsWith( Path.Combine( root, "Temp/gltf-validation/direct-gpu" ) + Path.DirectorySeparatorChar, StringComparison.Ordinal ) && (!Directory.Exists( output ) || !Directory.EnumerateFileSystemEntries( output ).Any()), "Use a fresh direct-GPU evidence directory." );
		Directory.CreateDirectory( output ); var variants = new List<object>();
		foreach ( var (mode, hash) in GraphHashes )
		{
			var relative = "research/gltf-validation/direct-gpu/models/AlphaBlendModeTest/surface_alpha_" + mode + ".shdrgrph";
			var generationDirectory = Path.Combine( output, mode + "-provided" );
			// Single provided-generator boundary shared with ORM/normal candidates.
			// It validates actual active producer/node identities and all four role
			// bindings; the original generated source remains untouched evidence.
			_ = SharedSurfaceGate.GenerateGraphForReview( root, generationDirectory, expectedToolsMvid,
				relative, hash, "[\"BaseColor\",\"MetallicRoughness\",\"Occlusion\",\"Emissive\"]" );
			var generationPath = Path.Combine( generationDirectory, "generation.json" ); var generationBytes = File.ReadAllBytes( generationPath );
			var generation = JsonSerializer.Deserialize<SharedSurfaceGate.Generation>( generationBytes );
			Require( generation.GraphSha256 == hash && generation.ToolsMvid == expectedToolsMvid, "Shared generation provenance differs from reviewed graph/producer." );
			var originalBytes = File.ReadAllBytes( generation.SourcePath ); Require( Hash( originalBytes ) == generation.SourceSha256, "Actual provided source changed after generation." );
			var generated = Encoding.UTF8.GetString( originalBytes );
			if ( mode != "opaque" ) Require( generated.Contains( "g_flGltfAlphaFactor" ) && generated.Contains( "i.vColor.a" ), "Graph lost the linear factor/vertex alpha product." );
			foreach ( var doubleSided in new[] { false, true } )
			{
				var candidate = ApplyReviewedDelta( generated, mode, doubleSided );
				var target = Path.Combine( output, mode + (doubleSided ? "-double" : "-single") + ".shader" ); File.WriteAllText( target, candidate, new UTF8Encoding( false ) );
				variants.Add( new { mode, doubleSided, graphSha256 = hash, generationManifestPath = generationPath, generationManifestSha256 = Hash( generationBytes ),
					originalPath = generation.SourcePath, originalSha256 = generation.SourceSha256, sourcePath = target, sourceSha256 = Hash( File.ReadAllBytes( target ) ) } );
			}
		}
		var manifest = new { scope = "actual provided source generation and explicit alpha delta only; not compiled, rendered, installed or material capability", toolsMvid = expectedToolsMvid, variants };
		var receipt = Path.Combine( output, "generation.json" ); File.WriteAllText( receipt, JsonSerializer.Serialize( manifest, new JsonSerializerOptions { WriteIndented = true } ) );
		return [new { Passed = true, Name = "provided_alpha_surface_sources_generated_for_review", ManifestPath = receipt, ManifestSha256 = Hash( File.ReadAllBytes( receipt ) ), Generated = manifest }];
	}

	public static string ApplyReviewedDelta( string generated, string mode, bool doubleSided )
	{
		Require( mode is "opaque" or "masked" or "translucent", "Unknown alpha specialization." );
		var ps = "#include \"common/pixel.hlsl\"";
		var states = "\n\tRenderState( CullMode, " + (doubleSided ? "NONE" : "BACK") + " );\n";
		if ( mode == "translucent" ) states += "\tRenderState( DepthEnable, true );\n\tRenderState( DepthWriteEnable, false );\n\tRenderState( BlendEnable, true );\n\tRenderState( SrcBlend, SRC_ALPHA );\n\tRenderState( DstBlend, INV_SRC_ALPHA );\n\tRenderState( BlendOp, ADD );\n\tRenderState( SrcBlendAlpha, ONE );\n\tRenderState( DstBlendAlpha, INV_SRC_ALPHA );\n\tRenderState( BlendOpAlpha, ADD );\n";
		if ( mode == "masked" ) states += "\tfloat g_flGltfAlphaCutoff < Default( 0.5 ); >;\n";
		var source = ReplaceOnce( generated, ps, ps + states );
		if ( mode == "masked" )
		{
			Require( generated.Contains( "#define S_ALPHA_TEST 1" ), "MASK source lost the depth alpha-test specialization." );
			// Preserve the provided alpha-test pass routing, but feed full opacity
			// to its alpha-to-coverage helper only AFTER the exact glTF clip.
			source = ReplaceOnce( source, "m.Opacity = saturate( m.Opacity );", "m.Opacity = saturate( m.Opacity );\n\t\tclip( m.Opacity - g_flGltfAlphaCutoff );\n\t\tm.Opacity = 1.0;" );
		}
		if ( doubleSided ) source = ReplaceOnce( source, "m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );", "m.Normal = TransformNormal( m.Normal, i.vNormalWs, i.vTangentUWs, i.vTangentVWs );\n\t\tif ( !i.vFrontFacing ) m.Normal = -m.Normal;" );
		return source;
	}
	private static string ReplaceOnce( string source, string find, string replacement )
	{
		var offset = source.IndexOf( find, StringComparison.Ordinal ); Require( offset >= 0 && source.IndexOf( find, offset + find.Length, StringComparison.Ordinal ) < 0, "Provided surface hook missing or ambiguous: " + find );
		return source[..offset] + replacement + source[(offset + find.Length)..];
	}
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
	private static void Require( bool condition, string message ) => AlphaBlendModeCoreTests.Require( condition, message );
}
