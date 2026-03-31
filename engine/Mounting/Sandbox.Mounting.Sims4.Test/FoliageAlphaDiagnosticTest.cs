using Sims4Reader;
using Sims4Reader.Image;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Diagnostic tests to investigate missing alpha cutouts on plant/foliage materials.
/// Inspects the OBJD → MODL → MATD pipeline for foliage objects and dumps:
///   - Shader type
///   - Diffuse texture format (DST1/DXT1 vs DST5/DXT5 vs RLE)
///   - AlphaMap presence and format
///   - UseDiffuseForAlphaTest flag
///   - Whether texture resource types are among the 3 mounted types
///
/// Run these tests, check Console output, and set breakpoints on specific textures.
/// </summary>
[TestClass]
public class FoliageAlphaDiagnosticTest
{
	/// <summary>
	/// Mounted image types — only these 3 are registered as textures in SimsMount.
	/// </summary>
	private static readonly HashSet<ResourceType> MountedImageTypes = new()
	{
		ResourceType.DstImage,
		ResourceType.RleImage,
		ResourceType.RleImageAlt,
	};

	/// <summary>
	/// Glass/translucent shader types (mirrors Sims4MaterialLoader.IsGlassShader).
	/// </summary>
	private static bool IsGlassShader( ShaderType shader )
	{
		return shader is ShaderType.GlassForObjects
			or ShaderType.GlassForObjectsTranslucent
			or ShaderType.GlassForFences
			or ShaderType.GlassForPortals
			or ShaderType.GlassForRabbitHoles
			or ShaderType.SimGlass
			or ShaderType.BuildingWindow
			or ShaderType.PhongAlpha;
	}

	/// <summary>
	/// Alpha-test shader types (mirrors Sims4MaterialLoader.IsAlphaTestShader).
	/// </summary>
	private static bool IsAlphaTestShader( ShaderType shader )
	{
		if ( IsGlassShader( shader ) )
			return false;

		return shader is not (
			ShaderType.None
			or ShaderType.ShadowMap
			or ShaderType.DropShadow
			or ShaderType.Plumbob
			or ShaderType.Blueprint
			or ShaderType.PreviewWallsAndFloors
			or ShaderType.ImpostorWater
			or ShaderType.StandingWater
			or ShaderType.BasinWater
			or ShaderType.Subtractive
			or ShaderType.Additive
			or ShaderType.ParticleAnim
			or ShaderType.ParticleJet
		);
	}

	/// <summary>
	/// Search all packages for OBJD entries matching plant/foliage/rug keywords,
	/// follow the OBJD → MODL chain, and dump material + texture diagnostics for each mesh.
	/// </summary>
	[TestMethod]
	public void DiagnosePlantAndRugAlpha()
	{
		var keywords = new[] { "plant", "orchid", "shrub", "flower", "foliage", "tree", "fern", "ivy", "vine", "rug" };
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
			packagePaths = new List<string> { TestHelper.GetPackagePath() };

		int objectsInspected = 0;
		int meshesInspected = 0;
		int issuesFound = 0;

		foreach ( var packagePath in packagePaths )
		{
			using var package = DbpfPackage.Open( packagePath );

			foreach ( var objdEntry in package.FindAll( ResourceType.ObjectDefinition ) )
			{
				if ( objdEntry.MemSize == 0 || objdEntry.FileSize == 0 )
					continue;

				ObjectDefinitionResource objd;
				try
				{
					objd = package.GetResource<ObjectDefinitionResource>( objdEntry );
					if ( !objd.IsFullyParsed )
						continue;
				}
				catch { continue; }

				// Match keywords
				var name = objd.Name ?? "";
				bool matches = false;
				foreach ( var kw in keywords )
				{
					if ( name.Contains( kw, StringComparison.OrdinalIgnoreCase ) )
					{
						matches = true;
						break;
					}
				}
				if ( !matches )
					continue;

				objectsInspected++;

				foreach ( var modelKey in objd.Models )
				{
					if ( (uint)modelKey.Type != (uint)ResourceType.Model || modelKey.Instance == 0 )
						continue;

					var modlEntry = package.Find( modelKey );
					if ( modlEntry == null )
						continue;

					ResolvedModel model;
					try
					{
						model = ModlModelLoader.LoadModel( package, modlEntry.Value );
					}
					catch ( Exception ex )
					{
						Console.WriteLine( $"[WARN] Failed to load MODL {modelKey} for '{name}': {ex.Message}" );
						continue;
					}

					var bestLod = model.GetBestLod();
					if ( bestLod == null )
						continue;

					for ( int mi = 0; mi < bestLod.Meshes.Count; mi++ )
					{
						var mesh = bestLod.Meshes[mi];
						meshesInspected++;

						var matd = mesh.Material;
						if ( matd == null )
						{
							Console.WriteLine( $"  [{name}] mesh#{mi}: NO MATERIAL" );
							continue;
						}

						var diag = AnalyzeMesh( package, name, mi, mesh );
						Console.WriteLine( diag.Summary );

						if ( diag.HasIssue )
							issuesFound++;
					}
				}
			}
		}

		Console.WriteLine( $"\n=== SUMMARY ===" );
		Console.WriteLine( $"Objects inspected: {objectsInspected}" );
		Console.WriteLine( $"Meshes inspected:  {meshesInspected}" );
		Console.WriteLine( $"Issues found:      {issuesFound}" );

		Assert.IsTrue( objectsInspected > 0, "No plant/foliage/rug OBJDs found. Check package paths." );
	}

	/// <summary>
	/// Focused test: inspect MODL entries that use foliage-like shaders.
	/// Set a breakpoint inside the loop to step through individual textures.
	/// </summary>
	[TestMethod]
	public void DiagnoseSpecificBrokenModels()
	{
		var packagePaths = TestHelper.GetAllPackagePaths();
		if ( packagePaths.Count == 0 )
			packagePaths = new List<string> { TestHelper.GetPackagePath() };

		int found = 0;

		foreach ( var packagePath in packagePaths )
		{
			using var package = DbpfPackage.Open( packagePath );

			foreach ( var modlEntry in package.FindAll( ResourceType.Model ) )
			{
				if ( modlEntry.MemSize == 0 || modlEntry.FileSize == 0 )
					continue;

				ResolvedModel model;
				try
				{
					model = ModlModelLoader.LoadModel( package, modlEntry );
				}
				catch { continue; }

				var bestLod = model.GetBestLod();
				if ( bestLod == null )
					continue;

				bool anyFoliageLike = false;
				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material == null ) continue;
					var shader = mesh.Material.Shader;
					if ( shader is ShaderType.Foliage
						or ShaderType.OutdoorProp
						or ShaderType.Phong
						or ShaderType.PhongAlpha )
					{
						anyFoliageLike = true;
						break;
					}
				}

				if ( !anyFoliageLike )
					continue;

				found++;
				Console.WriteLine( $"\n=== MODL {modlEntry.Key} ===" );

				for ( int mi = 0; mi < bestLod.Meshes.Count; mi++ )
				{
					var mesh = bestLod.Meshes[mi];
					if ( mesh.Material == null )
					{
						Console.WriteLine( $"  mesh#{mi}: NO MATERIAL" );
						continue;
					}

					// BREAKPOINT HERE to inspect specific textures
					var diag = AnalyzeMesh( package, $"MODL_{modlEntry.Key.Instance:X}", mi, mesh );
					Console.WriteLine( diag.Summary );
				}

				if ( found >= 50 )
					break;
			}

			if ( found >= 50 )
				break;
		}

		Console.WriteLine( $"\nFoliage-like MODLs inspected: {found}" );
		Assert.IsTrue( found > 0, "No foliage-like MODLs found." );
	}

	/// <summary>
	/// Comprehensive distribution test: for ALL materials with alpha test enabled,
	/// what format is the diffuse texture? Tallies DXT1 vs DXT5 vs RLE etc.
	/// </summary>
	[TestMethod]
	public void AlphaTest_DiffuseFormat_Distribution()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var formatCounts = new Dictionary<string, int>();
		int totalAlphaTest = 0;
		int noAlphaInDiffuse = 0;

		foreach ( var modlEntry in package.FindAll( ResourceType.Model ).Take( 1000 ) )
		{
			if ( modlEntry.MemSize == 0 || modlEntry.FileSize == 0 )
				continue;

			ResolvedModel model;
			try { model = ModlModelLoader.LoadModel( package, modlEntry ); }
			catch { continue; }

			var bestLod = model.GetBestLod();
			if ( bestLod == null ) continue;

			foreach ( var mesh in bestLod.Meshes )
			{
				if ( mesh.Material == null ) continue;

				// Check if this material would have alpha test
				var matd = mesh.Material;
				bool hasAlphaMap = mesh.TextureKeys.ContainsKey( ShaderFieldType.AlphaMap );
				bool useDiffuseForAlpha = false;
				foreach ( var se in matd.ShaderEntries )
				{
					if ( se.Field == ShaderFieldType.UseDiffuseForAlphaTest && se is ShaderFloat f && f.Value > 0f )
						useDiffuseForAlpha = true;
				}
				bool isAlphaTest = IsAlphaTestShader( matd.Shader );

				if ( !hasAlphaMap && !useDiffuseForAlpha && !isAlphaTest )
					continue;

				totalAlphaTest++;

				// Check diffuse texture format
				if ( mesh.TextureKeys.TryGetValue( ShaderFieldType.DiffuseMap, out var diffuseKey ) )
				{
					var formatStr = GetTextureFormatString( package, diffuseKey );
					formatCounts.TryGetValue( formatStr, out var c );
					formatCounts[formatStr] = c + 1;

					// Flag textures with no alpha
					if ( formatStr.Contains( "DXT1" ) || formatStr.Contains( "DST1" ) )
						noAlphaInDiffuse++;
				}
				else
				{
					formatCounts.TryGetValue( "NO_DIFFUSE_KEY", out var c );
					formatCounts["NO_DIFFUSE_KEY"] = c + 1;
				}
			}
		}

		Console.WriteLine( $"Materials with alpha test: {totalAlphaTest}" );
		Console.WriteLine( $"Diffuse has NO alpha (DXT1/DST1): {noAlphaInDiffuse}" );
		Console.WriteLine( "Format distribution:" );
		foreach ( var kv in formatCounts.OrderByDescending( kv => kv.Value ) )
			Console.WriteLine( $"  {kv.Key}: {kv.Value}" );

		Assert.IsTrue( totalAlphaTest > 0, "No alpha-test materials found." );
	}

	private record MeshDiagnostic( string Summary, bool HasIssue );

	private MeshDiagnostic AnalyzeMesh( DbpfPackage package, string objectName, int meshIndex, ResolvedMesh mesh )
	{
		var matd = mesh.Material!;
		var lines = new List<string>();
		bool hasIssue = false;

		lines.Add( $"  [{objectName}] mesh#{meshIndex}:" );
		lines.Add( $"    Shader:       {matd.Shader}" );
		lines.Add( $"    MountPath:    {mesh.MaterialMountPath}" );

		// Diffuse texture
		if ( mesh.TextureKeys.TryGetValue( ShaderFieldType.DiffuseMap, out var diffuseKey ) )
		{
			var diffuseFormat = GetTextureFormatString( package, diffuseKey );
			var isMounted = IsTextureMounted( package, diffuseKey );
			lines.Add( $"    Diffuse:      {diffuseKey} → {diffuseFormat} (mounted={isMounted})" );

			if ( diffuseFormat.Contains( "DXT1" ) || diffuseFormat.Contains( "DST1" ) )
			{
				lines.Add( $"    !! DIFFUSE HAS NO ALPHA (DXT1/DST1) -- clip(alpha-0.5) will never fire!" );
				hasIssue = true;
			}
			if ( !isMounted )
			{
				lines.Add( $"    !! DIFFUSE TEXTURE TYPE NOT MOUNTED -- texture won't load!" );
				hasIssue = true;
			}
		}
		else
		{
			lines.Add( $"    Diffuse:      <none>" );
		}

		// Normal map
		if ( mesh.TextureKeys.TryGetValue( ShaderFieldType.NormalMap, out var normalKey ) )
		{
			var normalFormat = GetTextureFormatString( package, normalKey );
			lines.Add( $"    NormalMap:     {normalKey} → {normalFormat}" );
		}

		// Specular map
		if ( mesh.TextureKeys.TryGetValue( ShaderFieldType.SpecularMap, out var specKey ) )
		{
			var specFormat = GetTextureFormatString( package, specKey );
			lines.Add( $"    SpecularMap:   {specKey} → {specFormat}" );
		}

		// Alpha map
		bool hasAlphaMap = mesh.TextureKeys.TryGetValue( ShaderFieldType.AlphaMap, out var alphaKey );
		if ( hasAlphaMap )
		{
			var alphaFormat = GetTextureFormatString( package, alphaKey );
			var isMounted = IsTextureMounted( package, alphaKey );
			lines.Add( $"    AlphaMap:      {alphaKey} → {alphaFormat} (mounted={isMounted})" );

			if ( !isMounted )
			{
				lines.Add( $"    !! ALPHA MAP TYPE NOT MOUNTED -- separate alpha texture won't load!" );
				hasIssue = true;
			}
		}
		else
		{
			lines.Add( $"    AlphaMap:      <none>" );
		}

		// Shader float params
		bool useDiffuseForAlpha = false;
		float alphaMaskThreshold = 0f;
		float transparency = 0f;

		foreach ( var se in matd.ShaderEntries )
		{
			if ( se.Field == ShaderFieldType.UseDiffuseForAlphaTest && se is ShaderFloat useDiffF )
			{
				useDiffuseForAlpha = useDiffF.Value > 0f;
				lines.Add( $"    UseDiffuseForAlphaTest: {useDiffF.Value}" );
			}
			if ( se.Field == ShaderFieldType.AlphaMaskThreshold && se is ShaderFloat threshF )
			{
				alphaMaskThreshold = threshF.Value;
				lines.Add( $"    AlphaMaskThreshold: {threshF.Value}" );
			}
			if ( se.Field == ShaderFieldType.Transparency && se is ShaderFloat transF )
			{
				transparency = transF.Value;
				lines.Add( $"    Transparency: {transF.Value}" );
			}
		}

		// Determine what BuildMaterialFromMatd would do
		bool isAlphaTest = IsAlphaTestShader( matd.Shader );
		bool isGlass = IsGlassShader( matd.Shader );
		bool needsAlphaTest = hasAlphaMap || useDiffuseForAlpha || isAlphaTest;

		lines.Add( $"    -> IsAlphaTestShader: {isAlphaTest}" );
		lines.Add( $"    -> IsGlass: {isGlass}" );
		lines.Add( $"    -> NeedsAlphaTest: {needsAlphaTest}" );
		lines.Add( $"    -> HasAlphaMap: {hasAlphaMap}" );
		lines.Add( $"    -> UseDiffuseForAlpha: {useDiffuseForAlpha}" );

		// Key diagnostic: alpha test is ON but diffuse has no alpha and no separate alpha map
		if ( needsAlphaTest && !hasAlphaMap && !useDiffuseForAlpha )
		{
			// Alpha test relies purely on diffuse alpha — check if it's DXT1
			if ( mesh.TextureKeys.TryGetValue( ShaderFieldType.DiffuseMap, out var dk ) )
			{
				var fmt = GetTextureFormatString( package, dk );
				if ( fmt.Contains( "DXT1" ) || fmt.Contains( "DST1" ) )
				{
					lines.Add( $"    ** ROOT CAUSE: Alpha test enabled but diffuse is {fmt} (no alpha)!" );
					lines.Add( $"       F_ALPHA_TEST=true, clip(1.0-0.5) never fires -> fully opaque." );
					hasIssue = true;
				}
			}
		}

		return new MeshDiagnostic( string.Join( "\n", lines ), hasIssue );
	}

	/// <summary>
	/// Look up a texture resource key in the package and determine its format string.
	/// </summary>
	private static string GetTextureFormatString( DbpfPackage package, ResourceKey key )
	{
		var entry = package.Find( key );
		if ( entry == null )
			return $"NOT_IN_PACKAGE(type=0x{(uint)key.Type:X8})";

		var resType = key.Type;

		if ( resType == ResourceType.DstImage )
		{
			try
			{
				var dst = package.GetResource<DstImage>( entry.Value );
				return $"DST->{dst.Format} ({dst.Width}x{dst.Height}, shuffled={dst.IsShuffled})";
			}
			catch ( Exception ex )
			{
				return $"DST_PARSE_ERROR({ex.Message})";
			}
		}

		if ( resType == ResourceType.RleImage || resType == ResourceType.RleImageAlt )
		{
			try
			{
				var rle = package.GetResource<RleImage>( entry.Value );
				return $"RLE_{rle.Version} ({rle.Width}x{rle.Height}, mips={rle.MipCount}, specular={rle.HasSpecular})";
			}
			catch ( Exception ex )
			{
				return $"RLE_PARSE_ERROR({ex.Message})";
			}
		}

		// Some other image type (Image_*, Thumbnail_*, etc.)
		return $"OTHER_TYPE(0x{(uint)resType:X8}, {resType})";
	}

	/// <summary>
	/// Check if a texture resource key uses one of the 3 mounted image types.
	/// </summary>
	private static bool IsTextureMounted( DbpfPackage package, ResourceKey key )
	{
		var entry = package.Find( key );
		if ( entry == null )
			return false;

		return MountedImageTypes.Contains( key.Type );
	}
}
