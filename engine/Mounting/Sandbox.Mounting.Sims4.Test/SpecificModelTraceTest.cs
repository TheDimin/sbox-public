using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Rcol;

namespace Sims4MountTest;

/// <summary>
/// Traces a specific white-rendering model through the full MODL → MATD → texture pipeline
/// to diagnose why BuildMaterial would return null at runtime.
/// </summary>
[TestClass]
public class SpecificModelTraceTest
{
	// The model that renders white: models/furniture/0_F44BD701CA4CC596.vmdl
	// Mount path format: {Group:X}_{Instance:X} → Group=0, Instance=0xF44BD701CA4CC596
	private const ulong TargetInstance = 0xF44BD701CA4CC596;
	private const uint TargetGroup = 0;

	/// <summary>
	/// Full trace of the MODL → MLOD → MATD → textures pipeline for the target model.
	/// Reports exactly what materials, shader entries, and texture keys are found.
	/// </summary>
	[TestMethod]
	public void TraceModel_F44BD701CA4CC596()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
				openPackages.Add( DbpfPackage.Open( path ) );

			// Find the MODL entry across all packages
			ResourceEntry? modlEntry = null;
			DbpfPackage? modlPackage = null;

			foreach ( var pkg in openPackages )
			{
				foreach ( var entry in pkg.FindAll( ResourceType.Model ) )
				{
					if ( entry.Key.Instance == TargetInstance && entry.Key.Group == TargetGroup )
					{
						modlEntry = entry;
						modlPackage = pkg;
						break;
					}
				}
				if ( modlEntry != null ) break;
			}

			if ( modlEntry == null || modlPackage == null )
			{
				Assert.Inconclusive( $"MODL 0x{TargetInstance:X} not found in any package." );
				return;
			}

			Console.WriteLine( $"=== MODL {modlEntry.Value.Key} ===" );
			Console.WriteLine( $"Package: {modlPackage}" );
			Console.WriteLine( $"MemSize: {modlEntry.Value.MemSize}, FileSize: {modlEntry.Value.FileSize}" );

			// Load with cross-package resolution
			var model = ModlModelLoader.LoadModel( modlPackage, modlEntry.Value, openPackages );

			Console.WriteLine( $"\nLODs: {model.Lods.Count}" );
			Console.WriteLine( $"AllTextureKeys: {model.AllTextureKeys.Count}" );

			foreach ( var texKey in model.AllTextureKeys )
			{
				Console.WriteLine( $"  TextureKey: {texKey}" );
				// Check if the texture exists in any package
				bool found = false;
				string foundIn = "";
				foreach ( var pkg in openPackages )
				{
					var entry = pkg.Find( texKey );
					if ( entry != null )
					{
						found = true;
						foundIn = pkg.ToString() ?? "unknown";
						break;
					}
				}
				Console.WriteLine( $"    Exists in packages: {found} {(found ? $"({foundIn})" : "")}" );
			}

			var bestLod = model.GetBestLod();
			Assert.IsNotNull( bestLod, "No best LOD found." );

			Console.WriteLine( $"\nBest LOD: {bestLod.LodId}, Meshes: {bestLod.Meshes.Count}" );

			for ( int i = 0; i < bestLod.Meshes.Count; i++ )
			{
				var mesh = bestLod.Meshes[i];
				Console.WriteLine( $"\n--- Mesh [{i}] ---" );
				Console.WriteLine( $"  NameHash: 0x{mesh.NameHash:X8}" );
				Console.WriteLine( $"  Vertices: {mesh.Vertices.Length}, Indices: {mesh.Indices.Length}" );
				Console.WriteLine( $"  Material: {(mesh.Material != null ? "RESOLVED" : "NULL")}" );

				if ( mesh.Material != null )
				{
					Console.WriteLine( $"  Shader: {mesh.Material.Shader}" );
					Console.WriteLine( $"  ShaderEntries: {mesh.Material.ShaderEntries.Count}" );

					foreach ( var se in mesh.Material.ShaderEntries )
					{
						string detail = se switch
						{
							ShaderFloat f => $"Float({f.Value})",
							ShaderFloat3 f3 => $"Float3({f3.X}, {f3.Y}, {f3.Z})",
							ShaderTextureRef tr => $"TextureRef({tr.Key})",
							ShaderTextureKey tk => $"TextureKey({tk.Key})",
							ShaderImageMapKey ik => $"ImageMapKey({ik.Key})",
							ShaderTextureIndex ti => $"TextureIndex({ti.Index})",
							_ => se.GetType().Name,
						};
						Console.WriteLine( $"    {se.Field}: {detail}" );
					}
				}

				Console.WriteLine( $"  TextureKeys: {mesh.TextureKeys.Count}" );
				foreach ( var (field, key) in mesh.TextureKeys )
				{
					Console.WriteLine( $"    {field}: {key}" );

					// Check which shader param BuildMaterial would map this to
					var paramName = field switch
					{
						ShaderFieldType.DiffuseMap => "g_tDiffuse",
						ShaderFieldType.NormalMap => "g_tNormalMap",
						ShaderFieldType.SpecularMap => "g_tSpecular",
						ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
						ShaderFieldType.AlphaMap => "g_tDiffuse (alpha)",
						_ => null,
					};
					Console.WriteLine( $"      → maps to: {paramName ?? "UNMAPPED (skipped by BuildMaterial)"}" );

					// Check if texture resource exists
					var texMountPath = $"mount://sims4/textures/{key.Group:X}_{key.Instance:X}.vtex";
					Console.WriteLine( $"      mount path: {texMountPath}" );

					bool texFound = false;
					foreach ( var pkg in openPackages )
					{
						// Check for DSTImage, RLEImage, RLEImageAlt
						foreach ( var texType in new[] { ResourceType.DstImage, ResourceType.RleImage, ResourceType.RleImageAlt } )
						{
							var texEntry = pkg.Find( new ResourceKey( texType, key.Group, key.Instance ) );
							if ( texEntry != null )
							{
								Console.WriteLine( $"      EXISTS as {texType} (MemSize={texEntry.Value.MemSize})" );
								texFound = true;
								break;
							}
						}
						if ( texFound ) break;
					}
					if ( !texFound )
						Console.WriteLine( $"      NOT FOUND in any package!" );
				}

				// Simulate BuildMaterial logic
				bool anySet = false;
				bool hasEmissive = false;
				bool hasAlphaMap = false;

				foreach ( var (field, key) in mesh.TextureKeys )
				{
					var paramName = field switch
					{
						ShaderFieldType.DiffuseMap => "g_tDiffuse",
						ShaderFieldType.NormalMap => "g_tNormalMap",
						ShaderFieldType.SpecularMap => "g_tSpecular",
						ShaderFieldType.EmissionMap or ShaderFieldType.SelfIlluminationMap => "g_tEmissive",
						ShaderFieldType.AlphaMap => "g_tDiffuse",
						_ => null,
					};

					if ( paramName == null ) continue;

					if ( field == ShaderFieldType.EmissionMap || field == ShaderFieldType.SelfIlluminationMap )
						hasEmissive = true;
					if ( field == ShaderFieldType.AlphaMap )
						hasAlphaMap = true;

					// At runtime, Texture.Load(mount path) would need to succeed
					// We can't test that here, but we know the texture exists if FindEntry found it
					bool texExists = false;
					foreach ( var pkg in openPackages )
					{
						foreach ( var texType in new[] { ResourceType.DstImage, ResourceType.RleImage, ResourceType.RleImageAlt } )
						{
							if ( pkg.Find( new ResourceKey( texType, key.Group, key.Instance ) ) != null )
							{
								texExists = true;
								break;
							}
						}
						if ( texExists ) break;
					}

					if ( texExists )
						anySet = true;
				}

				if ( mesh.Material != null )
				{
					foreach ( var se in mesh.Material.ShaderEntries )
					{
						switch ( se )
						{
							case ShaderFloat3 when se.Field == ShaderFieldType.Diffuse:
							case ShaderFloat when se.Field == ShaderFieldType.EmissiveBloomMultiplier
								|| se.Field == ShaderFieldType.EmissiveLightMultiplier:
							case ShaderFloat when se.Field == ShaderFieldType.NormalMapScale
								|| se.Field == ShaderFieldType.NormalBumpScale:
								anySet = true;
								break;
						}
					}
				}

				Console.WriteLine( $"\n  BuildMaterial simulation:" );
				Console.WriteLine( $"    anySet={anySet}, hasEmissive={hasEmissive}, hasAlphaMap={hasAlphaMap}" );
				Console.WriteLine( $"    Would return: {(anySet ? "MATERIAL" : "NULL (→ white!)")}" );
			}
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}

	/// <summary>
	/// Check how textures are mounted for this model's texture keys.
	/// Verifies that the mount path convention matches what exists in packages.
	/// </summary>
	[TestMethod]
	public void TraceModel_TextureMountPaths()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		var openPackages = new List<DbpfPackage>();
		try
		{
			foreach ( var path in packagePaths )
				openPackages.Add( DbpfPackage.Open( path ) );

			// Find the MODL
			ResourceEntry? modlEntry = null;
			DbpfPackage? modlPackage = null;
			foreach ( var pkg in openPackages )
			{
				foreach ( var entry in pkg.FindAll( ResourceType.Model ) )
				{
					if ( entry.Key.Instance == TargetInstance && entry.Key.Group == TargetGroup )
					{
						modlEntry = entry;
						modlPackage = pkg;
						break;
					}
				}
				if ( modlEntry != null ) break;
			}

			if ( modlEntry == null || modlPackage == null )
			{
				Assert.Inconclusive( $"MODL not found." );
				return;
			}

			var model = ModlModelLoader.LoadModel( modlPackage, modlEntry.Value, openPackages );
			var bestLod = model.GetBestLod();
			if ( bestLod == null )
			{
				Assert.Fail( "No LOD found." );
				return;
			}

			// Check how SimsMount mounts textures
			// From SimsMount.cs: textures are mounted as textures/{Group:X}_{Instance:X}
			// The texture types: DstImage, RleImage, RleImageAlt
			Console.WriteLine( "Texture mount path verification:" );
			Console.WriteLine( "(Checking if the texture Group:X_Instance:X matches what SimsMount would mount)\n" );

			foreach ( var mesh in bestLod.Meshes )
			{
				foreach ( var (field, key) in mesh.TextureKeys )
				{
					string mountName = $"{key.Group:X}_{key.Instance:X}";
					Console.WriteLine( $"  {field}: textures/{mountName}.vtex" );

					// Check all packages for this texture using different ResourceTypes
					foreach ( var pkg in openPackages )
					{
						foreach ( var texType in new[] { ResourceType.DstImage, ResourceType.RleImage, ResourceType.RleImageAlt } )
						{
							var texEntry = pkg.Find( new ResourceKey( texType, key.Group, key.Instance ) );
							if ( texEntry != null )
							{
								// This texture exists. Now check: would SimsMount mount it?
								// SimsMount iterates packages and mounts DstImage/RleImage/RleImageAlt entries.
								// The mount name format: {key.Group:X}_{key.Instance:X}
								// So the texture for this entry would be: textures/{texEntry.Key.Group:X}_{texEntry.Key.Instance:X}
								string texMountName = $"{texEntry.Value.Key.Group:X}_{texEntry.Value.Key.Instance:X}";
								bool matches = texMountName == mountName;
								Console.WriteLine( $"    Found as {texType} in package, mount name would be: {texMountName}, matches: {matches}" );
							}
						}
					}
				}
			}
		}
		finally
		{
			foreach ( var pkg in openPackages ) pkg.Dispose();
		}
	}
}
