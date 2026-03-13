using System.Collections.Generic;
using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

/// <summary>
/// Engine-level loader for MODL (Model) resources.
/// Resolves the MODL → MLOD → VBUF/IBUF/VRTF chain and builds a sandbox Model.
/// Supports multiple meshes per LOD, each with its own material.
/// </summary>
public class ModlLoader( DbpfPackage package, ResourceEntry entry, IReadOnlyList<DbpfPackage> allPackages ) : ResourceLoader<SimsMount>
{
	static new Logger Log = new Logger( "Sims4-ModlLoader" );

	protected override object? Load()
	{
		if ( entry.MemSize == 0 || entry.FileSize == 0 )
			return null;

		try
		{
			var resolved = ModlModelLoader.LoadModel( package, entry, allPackages );

			if ( resolved.Lods.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: no LODs resolved" );
				return null;
			}

			// Get the best (highest detail) LOD
			var bestLod = resolved.GetBestLod();
			if ( bestLod == null || bestLod.Meshes.Count == 0 )
			{
				Log.Warning( $"MODL {entry.Key}: best LOD has no meshes" );
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
				Log.Warning( $"MODL {entry.Key}: LOD {bestLod.LodId} has {bestLod.Meshes.Count} meshes but none have valid geometry" );
				return null;
			}

			return ModlModelBuilder.Build( meshMaterials );
		}
		catch ( Exception e )
		{
			Log.Warning( $"Failed to load MODL {entry.Key}: {e.Message}" );
			return null;
		}
	}

	/// <summary>
	/// Build a sandbox Material from the resolved mesh's texture keys and shader parameters.
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

		// Map TS4 texture keys to sims4 shader texture slots
		foreach ( var (field, key) in mesh.TextureKeys )
		{
			var paramName = field switch
			{
				ShaderFieldType.DiffuseMap => "g_tDiffuse",
				ShaderFieldType.NormalMap => "g_tNormalMap",
				ShaderFieldType.SpecularMap => "g_tSpecular",
				ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
				ShaderFieldType.AlphaMap => "g_tDiffuse", // alpha baked into diffuse alpha channel
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
				}
			}
		}

		// Enable static combos based on detected features
		// Note: s&box material system handles static combos via Material.Set for bool features
		// The shader compiler picks them up from the feature flags
		if ( hasAlphaMap )
			material.Set( "F_ALPHA_TEST", true );
		if ( hasEmissive )
			material.Set( "F_EMISSIVE", true );

		return anySet ? material : null;
	}
}
