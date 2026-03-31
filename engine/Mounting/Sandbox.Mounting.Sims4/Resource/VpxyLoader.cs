using System.Collections.Generic;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Mounting.Sims4;

/// <summary>
/// Engine-level loader for VPXY (Vertex Proxy) resources.
/// Resolves the full chain: VPXY → MODL (geometry) + MTST (materials) → sandbox Model.
///
/// VPXY is the root rendering resource in TS4. Its TGI block list references:
///   - MODL: the 3D model with LOD entries
///   - MTST: material state mapping (Default MATD with shader/texture data)
///
/// By loading through VPXY instead of directly through MODL, we get:
///   1. Proper material resolution (MTST provides MATD that MODL's internal refs often miss)
///   2. Deduplication (one mount per VPXY instead of per-MODL duplicates)
///   3. Correct LOD setup (all LODs from the MODL, not separate entries)
///
/// Supports multiple meshes per LOD, each with its own material.
/// </summary>
public class VpxyLoader( DbpfPackage package, ResourceEntry vpxyEntry ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-VpxyLoader" );

	protected override object? Load()
	{
		if ( vpxyEntry.MemSize == 0 || vpxyEntry.FileSize == 0 )
			return null;

		try
		{
			var vpxyRcol = package.GetResource<RcolContainer>( vpxyEntry );
			var vpxy = vpxyRcol.GetChunk<VpxyChunk>();
			if ( vpxy == null )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: no VpxyChunk found" );
				return null;
			}

			// Find the MODL reference in VPXY TGI blocks
			var modlKey = vpxy.FindTgi( Sims4Reader.ResourceType.Model );
			if ( modlKey == null )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: no MODL reference in TGI blocks" );
				return null;
			}

			var modlEntry = package.Find( modlKey.Value );
			if ( modlEntry == null )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: MODL {modlKey.Value} not found in package" );
				return null;
			}

			// Try to resolve the default MATD from MTST
			MaterialDefinition? externalMatd = null;
			var mtstKey = vpxy.FindTgi( Sims4Reader.ResourceType.MaterialState );
			if ( mtstKey != null )
			{
				var mtstEntry = package.Find( mtstKey.Value );
				if ( mtstEntry != null )
				{
					try
					{
						externalMatd = ModlModelLoader.ResolveDefaultMatd( package, mtstEntry.Value );
					}
					catch ( Exception e )
					{
						Log.Warning( $"VPXY {vpxyEntry.Key}: failed to resolve MTST {mtstKey.Value}: {e.Message}" );
					}
				}
			}

			// Load the model with external MATD fallback
			var resolved = ModlModelLoader.LoadModel( package, modlEntry.Value, externalMatd );

			if ( resolved.Lods.Count == 0 )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: MODL has no LODs" );
				return null;
			}

			var bestLod = resolved.GetBestLod();
			if ( bestLod == null || bestLod.Meshes.Count == 0 )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: best LOD has no meshes" );
				return null;
			}

			// Build all meshes with their per-mesh materials
			var meshMaterials = new List<(ResolvedMesh Mesh, Material? Material)>();

			foreach ( var mesh in bestLod.Meshes )
			{
				if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
					continue;

				var material = BuildMaterial( mesh );
				meshMaterials.Add( (mesh, material) );
			}

			if ( meshMaterials.Count == 0 )
			{
				Log.Warning( $"VPXY {vpxyEntry.Key}: no meshes with valid geometry" );
				return null;
			}

			return ModlModelBuilder.Build( meshMaterials );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load VPXY {vpxyEntry.Key}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Build a sandbox Material from the resolved mesh's texture keys and shader parameters.
	/// Uses the sims4 ubershader with proper TS4 normal/specular decoding.
	/// </summary>
	private Material? BuildMaterial( ResolvedMesh mesh )
	{
		if ( mesh.TextureKeys.Count == 0 && mesh.Material == null )
			return null;

		var material = Material.Create( "sims4_base", "sims4" );
		// TS4 normal maps: XY packed into ZW (blue, alpha) channels.
		// Flat normal needs ZW=128 (~0 after decode). RG unused.
		var normalMap = Texture.Create( 1, 1 ).WithData( new byte[4] { 0, 0, 128, 128 } ).Finish();

		material.Set( "g_tDiffuse", Texture.White );
		material.Set( "g_tNormalMap", normalMap );
		material.Set( "g_tSpecular", Texture.Black );
		material.Set( "g_tEmissive", Texture.Black );
		material.Set( "g_flNormalStrength", 1.0f );
		material.Set( "g_flSpecularScale", 1.0f );
		material.Set( "g_flEmissiveScale", 1.0f );
		material.Set( "g_vDiffuseTint", new Vector3( 1f, 1f, 1f ) );

		bool anySet = false;
		bool hasEmissive = false;
		bool hasAlphaMap = false;
		bool useDiffuseForAlpha = false;
		float alphaMaskThreshold = 0f;
		bool isGlass = mesh.Material != null && Sims4MaterialLoader.IsGlassShader( mesh.Material.Shader );
		float transparency = 0f;

		// Map TS4 texture keys to sims4 shader texture slots
		foreach ( var (field, key) in mesh.TextureKeys )
		{
			var paramName = field switch
			{
				ShaderFieldType.DiffuseMap => "g_tDiffuse",
				ShaderFieldType.NormalMap => "g_tNormalMap",
				ShaderFieldType.SpecularMap => "g_tSpecular",
				ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
				ShaderFieldType.AlphaMap => "g_tAlphaMap",
				_ => null,
			};

			if ( paramName == null )
				continue;

			if ( field == ShaderFieldType.EmissionMap || field == ShaderFieldType.SelfIlluminationMap )
				hasEmissive = true;
			if ( field == ShaderFieldType.AlphaMap )
				hasAlphaMap = true;

			var texturePath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
			var texture = Texture.Load( texturePath, false );
			if ( texture != null && !texture.IsError )
			{
				material.Set( paramName, texture );
				anySet = true;
			}
		}

		// Map TS4 MATD shader float/color parameters
		if ( mesh.Material != null )
		{
			foreach ( var entry in mesh.Material.ShaderEntries )
			{
				switch ( entry )
				{
					// Diffuse color tint (RGB)
					case ShaderFloat3 f3 when entry.Field == ShaderFieldType.Diffuse:
						material.Set( "g_vDiffuseTint", new Vector3( f3.X, f3.Y, f3.Z ) );
						anySet = true;
						break;

					// Emissive bloom multiplier → emissive scale
					case ShaderFloat f when entry.Field == ShaderFieldType.EmissiveBloomMultiplier
						|| entry.Field == ShaderFieldType.EmissiveLightMultiplier:
						material.Set( "g_flEmissiveScale", Math.Max( f.Value, 0f ) );
						hasEmissive = true;
						anySet = true;
						break;

					// Normal map scale → normal strength
					case ShaderFloat f when entry.Field == ShaderFieldType.NormalMapScale
						|| entry.Field == ShaderFieldType.NormalBumpScale:
						material.Set( "g_flNormalStrength", f.Value );
						anySet = true;
						break;

					// Transparency value for glass
					case ShaderFloat f when entry.Field == ShaderFieldType.Transparency:
						transparency = f.Value;
						break;

					case ShaderFloat f when entry.Field == ShaderFieldType.UseDiffuseForAlphaTest:
						useDiffuseForAlpha = f.Value > 0f;
						anySet = true;
						break;

					case ShaderFloat f when entry.Field == ShaderFieldType.AlphaMaskThreshold:
						alphaMaskThreshold = f.Value;
						break;
				}
			}
		}

		// Enable static combos based on detected features
		bool needsAlphaTest = hasAlphaMap
			|| useDiffuseForAlpha
			|| (mesh.Material != null && Sims4MaterialLoader.IsAlphaTestShader( mesh.Material.Shader ));

		if ( needsAlphaTest )
		{
			material.Set( "F_ALPHA_TEST", true );
			// Always set a sensible threshold — shader Default1(0.5) is compile-time only
			// and may not apply to runtime-created materials (leaving it at 0.0, which
			// means clip(alpha - 0.0) never fires for any alpha >= 0).
			material.Set( "g_flAlphaTestThreshold", alphaMaskThreshold > 0f ? alphaMaskThreshold : 0.5f );
			anySet = true;
		}
		if ( hasAlphaMap )
		{
			material.Set( "F_SEPARATE_ALPHA_MAP", true );
			anySet = true;
		}
		if ( hasEmissive )
			material.Set( "F_EMISSIVE", true );

		// Enable translucent rendering for glass/window materials
		if ( isGlass )
		{
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "F_RENDER_BACKFACES", true );
			float opacity = transparency > 0f ? transparency : 0.15f;
			material.Set( "g_flOpacity", opacity );
			anySet = true;
		}
		else if ( transparency > 0f )
		{
			// Non-glass materials with an explicit Transparency value
			material.Set( "F_TRANSLUCENT", true );
			material.Set( "g_flOpacity", transparency );
			anySet = true;
		}

		return anySet ? material : null;
	}
}
