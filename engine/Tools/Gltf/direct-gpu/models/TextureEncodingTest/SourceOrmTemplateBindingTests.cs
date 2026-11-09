using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureEncodingTestValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>
/// Actual provided-template default texture/uniform/cache controls. These are
/// native resource and property readbacks, not final PBR/source-image evidence.
/// The coordinator supplies the separately reviewed generated source hashes.
/// </summary>
[TestClass]
public sealed class SourceOrmTemplateBindingTests
{
	private static string manifestPath, manifestHash, sourceHash;
	public static List<object> Measurements { get; } = new();
	public static void Configure( string generationManifestPath, string generationManifestSha256, string reviewedSourceSha256 )
	{
		manifestPath = Path.GetFullPath( generationManifestPath ); manifestHash = generationManifestSha256; sourceHash = reviewedSourceSha256; Measurements.Clear();
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task AbsentTextureRolesKeepNeutralCompiledDefaultsAndExactLinearFactors( bool defaultMaterial )
	{
		Assert.IsTrue( Application.IsEditor && !Game.IsPlaying && Graphics.IsAvailable && !Graphics.IsActive && ThreadSafe.IsMainThread );
		var manifestBytes = File.ReadAllBytes( manifestPath ); Assert.AreEqual( manifestHash, Hash( manifestBytes ), "Generation manifest changed after review." );
		var manifest = JsonSerializer.Deserialize<SurfaceOrmCandidateGate.Generation>( manifestBytes );
		Assert.AreEqual( SurfaceOrmCandidateGate.GraphSha256, manifest.GraphSha256 ); Assert.AreEqual( sourceHash, manifest.SourceSha256 );
		var shaderBytes = File.ReadAllBytes( manifest.SourcePath ); Assert.AreEqual( sourceHash, Hash( shaderBytes ), "Generated shader changed after review." );
		Material template = null; GltfMaterialLease<Material> templateSession = null, first = null, second = null; Exception failure = null;
		try
		{
			templateSession = await SurfaceOrmCandidateGate.AcquireReviewedTemplate( manifestPath, manifestHash, sourceHash ); template = templateSession.Value;
			using var doc = GltfReader.Read( Encoding.UTF8.GetBytes( """
				{"asset":{"version":"2.0"},"accessors":[{"componentType":5126,"type":"VEC3","count":3,"min":[0,0,0],"max":[0,0,0]}],"meshes":[{"primitives":[{"attributes":{"POSITION":0}}]}],"materials":[{"pbrMetallicRoughness":{"baseColorFactor":[0.2,0.3,0.4,0.5],"metallicFactor":0.001,"roughnessFactor":0.7},"emissiveFactor":[0.25,0.5,0.75]}]}
				""" ) );
			var material = defaultMaterial ? null : doc.ReadMaterial( 0 ); var prepared = GltfStockMaterialAdapter.PrepareSource( doc, material );
			var uv = GltfMaterialUvPlan.Create( material, doc.MaterializePrimitiveUpload( 0, 0 ) ); Assert.AreEqual( 0, prepared.Resources.Count );
			first = GltfStockMaterialAdapter.AcquireSource( prepared, template, uv ); second = GltfStockMaterialAdapter.AcquireSource( prepared, template, uv );
			Assert.AreSame( first.Value, second.Value, "Repeated identical source inputs should share one native material lease." );
			var variables = (template.Shader.Schema?.Variables ?? new List<Shader.VariableDescription>()).Where( variable => (variable.Name ?? "").Contains( "Gltf", StringComparison.Ordinal ) || (variable.AttributeName ?? "").Contains( "Gltf", StringComparison.Ordinal ) ).Take( 64 ).ToArray();
			Measurements.Add( new { phase = "template_default_visibility_schema", default_material = defaultMaterial, source_sha256 = sourceHash,
				template.ShaderName, shader_path = template.Shader.ResourcePath, shader_anonymous = template.Shader.IsAnonymous,
				variables = variables.Select( variable => new { variable.Name, variable.AttributeName, variable.Type, variable.SourceType, variable.DefaultInputTexture, float_default = variable.FloatDefault.ToString() } ).ToArray(),
				template_first_texture = TextureState( template.FirstTexture ), copy_first_texture = TextureState( first.Value.FirstTexture ),
				scope = "Bounded schema/material parameter visibility only. Null GetTexture does not establish absent shader defaults at draw." } );
			var positive = Material.Load( "materials/default/white.vmat" );
			Measurements.Add( new { phase = "provided_native_default_material_lookup_control", valid = positive is { IsValid: true, IsError: false },
				g_tColor = positive is { IsValid: true, IsError: false } ? TextureState( positive.GetTexture( "g_tColor" ) ) : null,
				Color = positive is { IsValid: true, IsError: false } ? TextureState( positive.GetTexture( "Color" ) ) : null,
				first_texture = positive is { IsValid: true, IsError: false } ? TextureState( positive.FirstTexture ) : null,
				scope = "Borrowed existing native material; no replacement/default texture created and no shader sample claim." } );
			var defaults = new List<object>();
			foreach ( var role in new[] { "BaseColor", "MetallicRoughness", "Occlusion", "Emissive" } )
			{
				// The material owns/borrows these compiled defaults. Never dispose
				// their wrappers as if this test had created independent textures.
				var texture = first.Value.GetTexture( "g_tGltf" + role + "Texture" );
				var names = new[] { "g_tGltf" + role + "Texture", "Gltf" + role + "Texture" }
					.Concat( variables.Where( variable => (variable.Name ?? "").Contains( "Gltf" + role + "Texture", StringComparison.Ordinal ) || (variable.AttributeName ?? "").Contains( "Gltf" + role + "Texture", StringComparison.Ordinal ) ).SelectMany( variable => new[] { variable.Name, variable.AttributeName } ) )
					.Where( name => !string.IsNullOrEmpty( name ) ).Distinct( StringComparer.Ordinal ).Take( 8 ).ToArray();
				Measurements.Add( new { phase = "role_default_material_lookup", default_material = defaultMaterial, role,
					lookups = names.Select( name => new { name, template_has_parameter = template.HasParameter( name ), copy_has_parameter = first.Value.HasParameter( name ),
						template_texture = TextureState( template.GetTexture( name ) ), copy_texture = TextureState( first.Value.GetTexture( name ) ) } ).ToArray(),
					scope = "Read-only lookup diagnostics precede the retained introspection assertion; final GPU defaults remain a separate measured question." } );
				Assert.IsTrue( texture is { IsValid: true, IsError: false }, "Material.GetTexture does not expose a valid default handle for " + role + "; actual shader default sampling remains unmeasured." );
				Graphics.FlushGPU(); var pixels = texture.GetPixels(); Assert.IsTrue( pixels.Length > 0 && pixels.All( pixel => pixel.r == 255 && pixel.g == 255 && pixel.b == 255 && pixel.a == 255 ), "Absent role default must be neutral one, not an unintended emissive/metal/factor texture: " + role );
				defaults.Add( new { role, texture.Width, texture.Height, count = pixels.Length, texture.ResourcePath, observed_rgba = new[] { pixels[0].r, pixels[0].g, pixels[0].b, pixels[0].a } } );
			}
			var factor = first.Value.GetVector4( "g_vGltfBaseColor" );
			Assert.AreEqual( defaultMaterial ? 1f : .2f, factor.x ); Assert.AreEqual( defaultMaterial ? 1f : .3f, factor.y ); Assert.AreEqual( defaultMaterial ? 1f : .4f, factor.z );
			var emission = first.Value.GetVector4( "g_vGltfEmissiveFactor" ); Assert.AreEqual( defaultMaterial ? 0f : .25f, emission.x ); Assert.AreEqual( defaultMaterial ? 0f : .5f, emission.y ); Assert.AreEqual( defaultMaterial ? 0f : .75f, emission.z );
			Assert.AreEqual( defaultMaterial ? 1f : .001f, first.Value.GetVector4( "g_flGltfMetallicFactor" ).x );
			Assert.AreEqual( defaultMaterial ? 1f : .7f, first.Value.GetVector4( "g_flGltfRoughnessFactor" ).x );
			Assert.AreEqual( 1f, first.Value.GetVector4( "g_flGltfOcclusionStrength" ).x );
			first.Dispose(); first = null; Assert.IsTrue( second.Value.IsValid && template.IsValid, "Disposing one lease must preserve its peer and borrowed template." );
			Measurements.Add( new { default_material = defaultMaterial, source_sha256 = sourceHash, defaults, factor, emission, expected_metallic = defaultMaterial ? 1f : .001f,
				expected_roughness = defaultMaterial ? 1f : .7f, expected_emissive = defaultMaterial ? new[] { 0f, 0f, 0f } : new[] { .25f, .5f, .75f }, scope = "Actual compiled default texture GPU byte readbacks plus material uniform/cache/lease evidence. No final shader sample or PBR image acceptance." } );
		}
		catch ( Exception error ) { failure = error; throw; }
		finally
		{
			await MainThread.Wait(); Action[] cleanup = [() => second?.Dispose(), () => first?.Dispose(), () => templateSession?.Dispose()];
			if ( failure is null ) GltfGpuCleanup.ReleaseAll( cleanup ); else GltfGpuCleanup.Rollback( failure, cleanup );
		}
	}
	private static object TextureState( Texture texture ) => texture is { IsValid: true }
		? new { exists = true, valid = true, error = texture.IsError, width = texture.Width, height = texture.Height, path = texture.ResourcePath }
		: new { exists = texture is not null, valid = false, error = false, width = 0, height = 0, path = (string)null };
	private static string Hash( byte[] bytes ) => Convert.ToHexStringLower( SHA256.HashData( bytes ) );
}
