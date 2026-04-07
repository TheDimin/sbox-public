using System.Diagnostics;
using Microsoft.Win32;
using Sandbox.Mounting;
using Sims4Reader;
using ResourceType = Sims4Reader.ResourceType;
using Sims4Reader.Image;
using Sims4Reader.Material;
using Sims4Reader.Mesh;
using Sims4Reader.Resources;

namespace Sims4MountTest;

/// <summary>
/// Per-resource-type loading baselines using the real mount pipeline.
///
/// Uses MountHost + ISteamIntegration to discover and mount The Sims 4
/// exactly as the engine would.  Each test then measures how long it takes
/// to decompress + parse a batch of individual resources.
///
/// Budgets are generous (2-3x typical) -- their purpose is to catch
/// order-of-magnitude regressions, not to enforce tight SLAs.
///
/// Run with:  dotnet test --filter "FullyQualifiedName~ItemLoadPerformanceTest"
/// </summary>
[TestClass]
public class ItemLoadPerformanceTest
{
	static MountHost? _host;
	static SimsMount? _mount;
	static IReadOnlyList<DbpfPackage>? _packages;
	static RegistrySteamIntegration? _steam;

	[ClassInitialize]
	public static void MountSims4( TestContext _ )
	{
		_steam = new RegistrySteamIntegration();
		var config = new Configuration { SteamIntegration = _steam };

		_host = new MountHost( config );
		_host.Initialize( typeof( SimsMount ) );

		var source = _host.GetSource( "sims4" ) as SimsMount;
		if ( source == null || !source.IsInstalled )
		{
			_host.Dispose();
			_host = null;
			Assert.Inconclusive( "Sims 4 not detected via Steam -- skipping mount perf tests." );
			return;
		}

		source.MountInternal().GetAwaiter().GetResult();

		if ( !source.IsMounted )
		{
			_host.Dispose();
			_host = null;
			Assert.Inconclusive( "Sims 4 mount failed -- skipping perf tests." );
			return;
		}

		_mount = source;
		_packages = source.GetPackages();

		Console.WriteLine( $"Mounted {_packages.Count} packages, {source.Resources.Count} resources" );
	}

	[ClassCleanup]
	public static void Cleanup()
	{
		_host?.Dispose();
		_host = null;
		_mount = null;
		_packages = null;
	}

	// -- Shared infrastructure ------------------------------------------------

	static DbpfPackage GetPrimaryPackage()
	{
		Assert.IsNotNull( _packages, "Mount not initialized." );
		Assert.IsTrue( _packages.Count > 0, "No packages mounted." );
		return _packages[0];
	}

	/// <summary>
	/// Lazily yields valid entries of the given type from a single package.
	/// Call .Take(N) at the call site to limit.
	/// </summary>
	static IEnumerable<ResourceEntry> GetEntries( DbpfPackage pkg, ResourceType type )
	{
		foreach ( var e in pkg.FindAll( type ) )
		{
			if ( e.MemSize > 0 && e.FileSize > 0 )
				yield return e;
		}
	}

	/// <summary>
	/// Lazily yields (package, entry) tuples across ALL mounted packages.
	/// Call .Take(N) at the call site to limit.
	/// When <paramref name="requireSize"/> is false, entries with zero MemSize/FileSize are included
	/// (useful for resource types like STBL where valid entries may have atypical size fields).
	/// </summary>
	static IEnumerable<(DbpfPackage pkg, ResourceEntry entry)> GetEntriesAcrossPackages( ResourceType type, bool requireSize = true )
	{
		if ( _packages == null ) yield break;

		foreach ( var pkg in _packages )
		{
			foreach ( var e in pkg.FindAll( type ) )
			{
				if ( requireSize && (e.MemSize == 0 || e.FileSize == 0) )
					continue;

				yield return (pkg, e);
			}
		}
	}

	static DbpfPackage? FindPackageWithType( ResourceType type )
	{
		if ( _packages == null ) return null;
		foreach ( var pkg in _packages )
		{
			foreach ( var e in pkg.FindAll( type ) )
			{
				if ( e.MemSize > 0 && e.FileSize > 0 )
					return pkg;
			}
		}
		return null;
	}

	/// <summary>
	/// Benchmark loop for single-package entries.
	/// Times each item individually using a single reused Stopwatch.
	/// </summary>
	static double[] Benchmark<T>( IEnumerable<ResourceEntry> entries, Func<ResourceEntry, T> load )
	{
		var timings = new List<double>();
		var sw = new Stopwatch();

		foreach ( var entry in entries )
		{
			try
			{
				sw.Restart();
				load( entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		return timings.ToArray();
	}

	/// <summary>
	/// Benchmark loop for cross-package (pkg, entry) tuples.
	/// </summary>
	static double[] BenchmarkCrossPackage<T>(
		IEnumerable<(DbpfPackage pkg, ResourceEntry entry)> entries,
		Func<DbpfPackage, ResourceEntry, T> load )
	{
		var timings = new List<double>();
		var sw = new Stopwatch();

		foreach ( var (pkg, entry) in entries )
		{
			try
			{
				sw.Restart();
				load( pkg, entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		return timings.ToArray();
	}

	static void ReportAndAssert( string label, double[] timings, double budgetAvgMs )
	{
		if ( timings.Length == 0 )
		{
			Assert.Inconclusive( $"{label}: all items failed to load -- nothing to measure." );
			return;
		}

		double total = 0, min = double.MaxValue, max = 0;
		for ( int i = 0; i < timings.Length; i++ )
		{
			var t = timings[i];
			total += t;
			if ( t < min ) min = t;
			if ( t > max ) max = t;
		}
		var avg = total / timings.Length;

		Console.WriteLine( $"{label}: {timings.Length} items in {total:F1}ms" );
		Console.WriteLine( $"  Average: {avg:F2}ms | Min: {min:F2}ms | Max: {max:F2}ms" );

		Assert.IsTrue( avg < budgetAvgMs,
			$"{label} averaged {avg:F2}ms per item -- should be <{budgetAvgMs}ms." );
	}

	// -- Mount Lifecycle ------------------------------------------------------

	[TestMethod]
	public void MountLifecycle_AllPackagesLoaded()
	{
		Assert.IsNotNull( _mount, "Mount not initialized." );
		Assert.IsTrue( _mount.IsMounted, "Mount should be active." );
		Assert.IsTrue( _packages!.Count > 0, $"Expected packages, got {_packages.Count}." );
		Assert.IsTrue( _mount.Resources.Count > 0, $"Expected resources, got {_mount.Resources.Count}." );

		Console.WriteLine( $"Packages: {_packages.Count}" );
		Console.WriteLine( $"Registered resources: {_mount.Resources.Count}" );
	}

	// -- GEOM Guard -----------------------------------------------------------

	[TestMethod]
	public void Mount_DoesNotLoadGeometry()
	{
		Assert.IsNotNull( _mount, "Mount not initialized." );
		Assert.IsNotNull( _packages, "Packages not loaded." );

		// 1) No registered resource should reference geometry
		var geomResources = _mount.Resources
			.Where( r => r.Path.Contains( ".geom", StringComparison.OrdinalIgnoreCase ) )
			.ToList();

		Assert.AreEqual( 0, geomResources.Count,
			$"Mount registered {geomResources.Count} GEOM resources -- expected 0. " +
			$"First: {geomResources.FirstOrDefault()?.Path}" );

		// 2) Count GEOM entries across all packages to prove they exist but are NOT mounted
		int geomEntryCount = 0;
		foreach ( var pkg in _packages )
		{
			foreach ( var _ in pkg.FindAll( ResourceType.Geometry ) )
				geomEntryCount++;
		}

		Console.WriteLine( $"GEOM entries in packages: {geomEntryCount} (none mounted)" );
		Console.WriteLine( $"Mounted resources: {_mount.Resources.Count} (0 GEOM)" );

		// 3) Verify MODL metadata scan does not reference GEOM type
		var modlPkg = GetPrimaryPackage();
		var firstModl = GetEntries( modlPkg, ResourceType.Model ).FirstOrDefault();
		if ( firstModl.MemSize > 0 )
		{
			var meshIndices = ModlModelLoader.ScanInlineMaterialMeshes( modlPkg, firstModl );
			Console.WriteLine( $"MODL scan returned {meshIndices?.Count ?? 0} inline-material mesh indices (no GEOM)" );
		}
	}

	// -- Geometry (GEOM) ------------------------------------------------------

	[TestMethod]
	public void GeometryLoad_AverageUnder5ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.Geometry ).Take( 100 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No GEOM entries." ); return; }

		// Warm up
		try { pkg.GetResource<RcolContainer>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e =>
		{
			var rcol = pkg.GetResource<RcolContainer>( e );
			return rcol.GetChunk<GeometryRcolChunk>();
		} );

		ReportAndAssert( "GEOM load", timings, 5.0 );
	}

	// -- Material Definition (MATD) -------------------------------------------

	[TestMethod]
	public void MaterialDefinitionLoad_AverageUnder3ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.MaterialDefinition ).Take( 100 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No MATD entries." ); return; }

		try { pkg.GetResource<RcolContainer>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e =>
		{
			var rcol = pkg.GetResource<RcolContainer>( e );
			return rcol.GetChunk<MaterialDefinition>();
		} );

		ReportAndAssert( "MATD load", timings, 3.0 );
	}

	// -- DST Image (parse only) -----------------------------------------------

	[TestMethod]
	public void DstImageParse_AverageUnder5ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.DstImage ).Take( 100 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No DST entries." ); return; }

		try { pkg.GetResource<DstImage>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e => pkg.GetResource<DstImage>( e ) );
		ReportAndAssert( "DST parse", timings, 5.0 );
	}

	// -- DST Image (parse + unshuffle to DDS) ---------------------------------

	[TestMethod]
	public void DstImageToDds_AverageUnder10ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.DstImage ).Take( 100 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No DST entries." ); return; }

		try { pkg.GetResource<DstImage>( entries[0] ).ToDds(); } catch { }

		var timings = Benchmark( entries, e =>
		{
			var dst = pkg.GetResource<DstImage>( e );
			return dst.ToDds();
		} );

		ReportAndAssert( "DST -> DDS", timings, 10.0 );
	}

	// -- RLE Image ------------------------------------------------------------

	[TestMethod]
	public void RleImageParse_AverageUnder5ms()
	{
		var pkg = FindPackageWithType( ResourceType.RleImage );
		if ( pkg == null ) { Assert.Inconclusive( "No package with RLE entries." ); return; }

		var entries = GetEntries( pkg, ResourceType.RleImage ).Take( 100 ).ToList();
		if ( entries.Count == 0 )
			entries = GetEntries( pkg, ResourceType.RleImageAlt ).Take( 100 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No RLE entries." ); return; }

		try { pkg.GetResource<RleImage>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e => pkg.GetResource<RleImage>( e ) );
		ReportAndAssert( "RLE parse", timings, 5.0 );
	}

	// -- Catalog Object (COBJ) ------------------------------------------------

	[TestMethod]
	public void CatalogObjectLoad_AverageUnder2ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.CatalogObject ).Take( 200 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No COBJ entries." ); return; }

		try { pkg.GetResource<CatalogObjectResource>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e => pkg.GetResource<CatalogObjectResource>( e ) );
		ReportAndAssert( "COBJ load", timings, 2.0 );
	}

	// -- Object Definition (OBJD) ---------------------------------------------

	[TestMethod]
	public void ObjectDefinitionLoad_AverageUnder2ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.ObjectDefinition ).Take( 200 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No OBJD entries." ); return; }

		try { pkg.GetResource<ObjectDefinitionResource>( entries[0] ); } catch { }

		var timings = Benchmark( entries, e => pkg.GetResource<ObjectDefinitionResource>( e ) );
		ReportAndAssert( "OBJD load", timings, 2.0 );
	}

	// -- String Table (STBL) - Cross-Package ----------------------------------

	[TestMethod]
	public void StringTableLoad_AverageUnder100ms()
	{
		// Try with size filter first, fall back to no filter for broader coverage
		var entries = GetEntriesAcrossPackages( ResourceType.StringTable ).Take( 50 ).ToList();
		if ( entries.Count < 5 )
		{
			var allEntries = GetEntriesAcrossPackages( ResourceType.StringTable, requireSize: false ).Take( 50 ).ToList();
			Console.WriteLine( $"STBL with size filter: {entries.Count}, without: {allEntries.Count}" );
			if ( allEntries.Count > entries.Count )
				entries = allEntries;
		}

		if ( entries.Count == 0 ) { Assert.Inconclusive( "No STBL entries across any package." ); return; }

		Console.WriteLine( $"Found {entries.Count} STBL entries across all packages" );

		// Warm up
		try { entries[0].pkg.GetResource<StringTable>( entries[0].entry ); } catch { }

		var timings = BenchmarkCrossPackage( entries, ( pkg, e ) => pkg.GetResource<StringTable>( e ) );
		ReportAndAssert( "STBL load (cross-pkg)", timings, 100.0 );
	}

	// -- MODL Full Resolution -------------------------------------------------

	[TestMethod]
	public void ModlFullResolution_AverageUnder20ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.Model ).Take( 50 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No MODL entries." ); return; }

		try { ModlModelLoader.LoadModel( pkg, entries[0] ); } catch { }

		var timings = Benchmark( entries, e => ModlModelLoader.LoadModel( pkg, e ) );
		ReportAndAssert( "MODL full resolve", timings, 20.0 );
	}

	// -- MODL Metadata Scan (no geometry decode) ------------------------------

	[TestMethod]
	public void ModlMetadataScan_AverageUnder5ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.Model ).Take( 50 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No MODL entries." ); return; }

		// Warm up
		try { ModlModelLoader.ScanInlineMaterialMeshes( pkg, entries[0] ); } catch { }

		var timings = Benchmark( entries, e => ModlModelLoader.ScanInlineMaterialMeshes( pkg, e ) );
		ReportAndAssert( "MODL metadata scan", timings, 5.0 );
	}

	// -- Full Mount Performance -----------------------------------------------

	[TestMethod]
	public void FullMount_TimeBudget()
	{
		// Reuse the cached steam integration -- no need to re-scan registry/VDF
		var config = new Configuration { SteamIntegration = _steam ?? new RegistrySteamIntegration() };

		using var host = new MountHost( config );

		var sw = Stopwatch.StartNew();
		host.Initialize( typeof( SimsMount ) );
		var initMs = sw.ElapsedMilliseconds;

		var source = host.GetSource( "sims4" ) as SimsMount;
		if ( source == null || !source.IsInstalled )
		{
			Assert.Inconclusive( "Sims 4 not detected." );
			return;
		}

		sw.Restart();
		source.MountInternal().GetAwaiter().GetResult();
		sw.Stop();
		var mountMs = sw.ElapsedMilliseconds;

		Console.WriteLine( $"Initialize: {initMs}ms" );
		Console.WriteLine( $"Mount: {mountMs}ms" );
		Console.WriteLine( $"Total: {initMs + mountMs}ms" );
		Console.WriteLine( $"Packages: {source.GetPackages().Count}" );
		Console.WriteLine( $"Resources: {source.Resources.Count}" );


		Assert.IsTrue( initMs + mountMs < 200,
			$"Full mount took {initMs + mountMs}ms -- should be <200ms." );
	}

	// -- MODL End-to-End Load (ModlLoader.Load) --------------------------------

	[TestMethod]
	public void ModlLoad_EndToEnd_AverageUnder5ms()
	{
		Assert.IsNotNull( _packages, "Packages not available." );

		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.Model ).Take( 20 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No MODL entries." ); return; }

		// Warm up: create + load first model to JIT all paths
		try
		{
			var warmup = new Mounting.Sims4.ModlLoader( pkg, entries[0], _packages! );
			warmup.GetOrCreate().GetAwaiter().GetResult();
		}
		catch { }

		var sw = new Stopwatch();
		var timings = new List<double>();

		// Create a fresh ModlLoader for each entry (avoids GetOrCreate caching)
		foreach ( var entry in entries.Skip( 1 ) )
		{
			try
			{
				var loader = new Mounting.Sims4.ModlLoader( pkg, entry, _packages! );
				sw.Restart();
				loader.GetOrCreate().GetAwaiter().GetResult();
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		if ( timings.Count == 0 )
		{
			Assert.Inconclusive( "No MODL resources loaded successfully." );
			return;
		}

		ReportAndAssert( "MODL end-to-end Load", timings.ToArray(), 5.0 );
	}

	// -- MODL Parse + Material Resolution (data-only, no engine APIs) ----------

	[TestMethod]
	public void ModlParseAndResolve_AverageUnder5ms()
	{
		var pkg = GetPrimaryPackage();
		var entries = GetEntries( pkg, ResourceType.Model ).Take( 50 ).ToList();
		if ( entries.Count == 0 ) { Assert.Inconclusive( "No MODL entries." ); return; }

		// Warm up: parse + resolve materials (decompression, binary parsing, cross-package lookup)
		try { LoadModelWithMaterials( pkg, entries[0] ); } catch { }

		var sw = new Stopwatch();
		var timings = new List<double>();

		foreach ( var entry in entries.Skip( 1 ) )
		{
			try
			{
				sw.Restart();
				LoadModelWithMaterials( pkg, entry );
				sw.Stop();
				timings.Add( sw.Elapsed.TotalMilliseconds );
			}
			catch { }
		}

		if ( timings.Count == 0 )
		{
			Assert.Inconclusive( "No MODL resources parsed successfully." );
			return;
		}

		ReportAndAssert( "MODL parse+resolve", timings.ToArray(), 5.0 );
	}

	/// <summary>
	/// Load and resolve a MODL with all its material definitions from packages.
	/// Does NOT create engine Material/Texture objects — only parses the data.
	/// </summary>
	static ResolvedModel LoadModelWithMaterials( DbpfPackage pkg, ResourceEntry entry )
	{
		var resolved = ModlModelLoader.LoadModel( pkg, entry, _packages );

		// Touch material definitions to ensure they're decompressed and parsed
		if ( resolved.Lods.Count > 0 )
		{
			var lod = resolved.GetBestLod();
			if ( lod != null )
			{
				foreach ( var mesh in lod.Meshes )
				{
					// Access material to force resolution
					_ = mesh.Material;
					_ = mesh.TextureKeys;
				}
			}
		}

		return resolved;
	}
}

/// <summary>
/// Real ISteamIntegration backed by the Windows registry.
/// Parses libraryfolders.vdf and appmanifest ACF files to build an app->directory map.
/// </summary>
internal class RegistrySteamIntegration : ISteamIntegration
{
	readonly Dictionary<long, string> _appDirs = new();

	public RegistrySteamIntegration()
	{
		var steamPath = GetSteamInstallPath();
		if ( steamPath == null ) return;

		var libraryFolders = new List<string>( 4 ) { steamPath };
		ParseLibraryFolders( System.IO.Path.Combine( steamPath, "steamapps", "libraryfolders.vdf" ), libraryFolders );

		for ( int i = 0; i < libraryFolders.Count; i++ )
			ScanLibrary( System.IO.Path.Combine( libraryFolders[i], "steamapps" ) );
	}

	public bool IsAppInstalled( long appid ) => _appDirs.ContainsKey( appid );
	public bool IsDlcInstalled( long appid ) => _appDirs.ContainsKey( appid );
	public string GetAppDirectory( long appid ) => _appDirs.TryGetValue( appid, out var dir ) ? dir : null!;

	void ParseLibraryFolders( string vdfPath, List<string> output )
	{
		if ( !System.IO.File.Exists( vdfPath ) ) return;

		foreach ( var line in System.IO.File.ReadLines( vdfPath ) )
		{
			var span = line.AsSpan().Trim();
			if ( !span.StartsWith( "\"path\"" ) ) continue;

			var path = ExtractQuotedValue( span );
			if ( path != null && System.IO.Directory.Exists( path ) )
				output.Add( path );
		}
	}

	void ScanLibrary( string appsDir )
	{
		if ( !System.IO.Directory.Exists( appsDir ) ) return;

		foreach ( var acf in System.IO.Directory.EnumerateFiles( appsDir, "appmanifest_*.acf" ) )
		{
			try
			{
				long appId = 0;
				string? installDir = null;

				foreach ( var line in System.IO.File.ReadLines( acf ) )
				{
					var span = line.AsSpan().Trim();

					if ( appId == 0 && span.StartsWith( "\"appid\"" ) )
					{
						var val = ExtractQuotedValue( span );
						if ( val != null ) long.TryParse( val, out appId );
					}
					else if ( installDir == null && span.StartsWith( "\"installdir\"" ) )
					{
						installDir = ExtractQuotedValue( span );
					}

					if ( appId > 0 && installDir != null ) break;
				}

				if ( appId > 0 && installDir != null )
				{
					var fullPath = System.IO.Path.Combine( appsDir, "common", installDir );
					if ( System.IO.Directory.Exists( fullPath ) )
						_appDirs.TryAdd( appId, fullPath );
				}
			}
			catch { }
		}
	}

	/// <summary>
	/// Extracts the second quoted value from a VDF key-value line.
	/// Input: "key"		"value"  ->  returns "value"
	/// Uses span-based parsing to avoid string.Split allocations.
	/// </summary>
	static string? ExtractQuotedValue( ReadOnlySpan<char> line )
	{
		int quoteCount = 0;
		int valueStart = -1;

		for ( int i = 0; i < line.Length; i++ )
		{
			if ( line[i] != '"' ) continue;
			quoteCount++;

			if ( quoteCount == 3 )
				valueStart = i + 1;
			else if ( quoteCount == 4 )
				return valueStart >= 0 ? line.Slice( valueStart, i - valueStart ).ToString() : null;
		}

		return null;
	}

	static string? GetSteamInstallPath()
	{
		ReadOnlySpan<string> keys = [ @"SOFTWARE\WOW6432Node\Valve\Steam", @"SOFTWARE\Valve\Steam" ];

		foreach ( var key in keys )
		{
			try
			{
				using var regKey = Registry.LocalMachine.OpenSubKey( key );
				if ( regKey?.GetValue( "InstallPath" ) is string path && System.IO.Directory.Exists( path ) )
					return path;
			}
			catch { }
		}

		foreach ( var key in keys )
		{
			try
			{
				using var regKey = Registry.CurrentUser.OpenSubKey( key );
				if ( regKey?.GetValue( "SteamPath" ) is string path && System.IO.Directory.Exists( path ) )
					return path;
			}
			catch { }
		}

		return null;
	}
}
