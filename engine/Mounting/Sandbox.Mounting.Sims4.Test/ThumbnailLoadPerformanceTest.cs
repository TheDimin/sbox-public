using System.Diagnostics;
using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Performance baseline for the EntryCard thumbnail load path.
///
/// When a thumbnail is requested for a Sims4 mount decoration, the engine calls:
///   ModlLoader.Load() → ModlModelLoader.LoadModel() [parse]
///     → Material.Load() per mesh [engine call]
///       → Sims4MaterialLoader.Load() → BuildMaterialFromMatd()
///         → Texture.Load() per texture key [engine call]
///
/// These tests measure the parse-layer cost (everything that happens before the
/// engine Material/Texture calls) so we can track regressions as we optimise.
///
/// The async optimisation (Iteration 1-3) replaces the sequential
///   foreach { Texture.Load(path) }
/// with
///   await Task.WhenAll(textureKeys.Select(k => Texture.LoadAsync(k)))
/// allowing the GPU to compile multiple textures concurrently.
/// </summary>
[TestClass]
public class ThumbnailLoadPerformanceTest
{
	/// <summary>
	/// Find the MODL entry that would trigger the most concurrent texture loads
	/// if our async path were running.  Reports the count so we know the maximum
	/// parallelism available, and asserts it is > 0 (i.e. there is real work to
	/// parallelize).
	/// </summary>
	[TestMethod]
	public void WorstCase_TextureCount_DocumentsParallelismOpportunity()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var modlEntries = package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.ToList();

		if ( modlEntries.Count == 0 )
		{
			Assert.Inconclusive( "No MODL entries found in package." );
			return;
		}

		int maxTextures = 0;
		ResourceKey worstKey = default;
		int maxMeshes = 0;
		int totalModlsChecked = 0;

		// Scan a sample to find worst-case without exhausting all packages
		foreach ( var entry in modlEntries.Take( 500 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				totalModlsChecked++;
				int texCount = bestLod.Meshes.Sum( m => m.TextureKeys.Count );
				if ( texCount > maxTextures )
				{
					maxTextures = texCount;
					worstKey = entry.Key;
					maxMeshes = bestLod.Meshes.Count;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Checked {totalModlsChecked} MODLs (from {modlEntries.Count} total)" );
		Console.WriteLine( $"Worst-case MODL: G={worstKey.Group:X} I={worstKey.Instance:X}" );
		Console.WriteLine( $"  Meshes: {maxMeshes}" );
		Console.WriteLine( $"  Total texture keys (sequential loads today): {maxTextures}" );
		Console.WriteLine( $"  With async Task.WhenAll these {maxTextures} loads run concurrently" );

		Assert.IsTrue( maxTextures > 0,
			"No MODL has any texture keys — nothing to parallelise." );
	}

	/// <summary>
	/// Baseline: measures how long ModlModelLoader.LoadModel() takes for the
	/// worst-case model (most texture keys across meshes).
	///
	/// This is the parse cost before ANY engine calls.  It should be fast
	/// (file I/O + binary decode only).  Budget: 200ms after warm-up.
	/// </summary>
	[TestMethod]
	public void WorstCase_ParseTime_Under200ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var modlEntries = package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.ToList();

		if ( modlEntries.Count == 0 )
		{
			Assert.Inconclusive( "No MODL entries found in package." );
			return;
		}

		// Find the entry with the most texture keys
		ResourceEntry? worstEntry = null;
		int maxTextures = 0;

		foreach ( var entry in modlEntries.Take( 500 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;
				int count = bestLod.Meshes.Sum( m => m.TextureKeys.Count );
				if ( count > maxTextures )
				{
					maxTextures = count;
					worstEntry = entry;
				}
			}
			catch { }
		}

		if ( worstEntry == null )
		{
			Assert.Inconclusive( "No MODL with texture keys found." );
			return;
		}

		// Warm up — load once to prime decompressor caches
		ModlModelLoader.LoadModel( package, worstEntry.Value );

		// Measure 5 iterations
		var sw = Stopwatch.StartNew();
		for ( int i = 0; i < 5; i++ )
			ModlModelLoader.LoadModel( package, worstEntry.Value );
		sw.Stop();

		var avgMs = sw.ElapsedMilliseconds / 5.0;
		Console.WriteLine( $"Worst-case MODL (G={worstEntry.Value.Key.Group:X} I={worstEntry.Value.Key.Instance:X})" );
		Console.WriteLine( $"  Texture keys: {maxTextures}" );
		Console.WriteLine( $"  Parse time avg: {avgMs:F1}ms over 5 runs" );
		Console.WriteLine( $"  This cost is paid before any Material/Texture engine calls." );

		Assert.IsTrue( avgMs < 200,
			$"ModlModelLoader.LoadModel() averaged {avgMs:F1}ms — should be <200ms (parse only)." );
	}

	/// <summary>
	/// Throughput: parse N complex models (with materials and texture keys)
	/// and assert we can do it fast enough to not stall the game.
	///
	/// Budget: 50 models in under 2000ms (cold parse, no caching).
	/// This directly maps to "how many thumbnails can we service per second
	/// at the parse layer before async GPU work begins".
	/// </summary>
	[TestMethod]
	public void Throughput_Parse50ComplexModels_Under2000ms()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		// Collect entries that have at least one texture key (i.e. a material worth loading)
		var complexEntries = new List<ResourceEntry>();
		foreach ( var entry in package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 2000 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;
				if ( bestLod.Meshes.Any( m => m.TextureKeys.Count > 0 ) )
					complexEntries.Add( entry );
			}
			catch { }

			if ( complexEntries.Count >= 50 )
				break;
		}

		if ( complexEntries.Count == 0 )
		{
			Assert.Inconclusive( "No complex MODL entries (with texture keys) found." );
			return;
		}

		int toUse = Math.Min( 50, complexEntries.Count );

		// Cold parse — no warm-up, simulates first-load
		var sw = Stopwatch.StartNew();
		int totalTexKeys = 0;
		for ( int i = 0; i < toUse; i++ )
		{
			var model = ModlModelLoader.LoadModel( package, complexEntries[i] );
			var bestLod = model.GetBestLod();
			if ( bestLod != null )
				totalTexKeys += bestLod.Meshes.Sum( m => m.TextureKeys.Count );
		}
		sw.Stop();

		var perModel = sw.ElapsedMilliseconds / (double)toUse;
		Console.WriteLine( $"Parsed {toUse} complex MODLs in {sw.ElapsedMilliseconds}ms ({perModel:F1}ms/model)" );
		Console.WriteLine( $"Total texture keys across models: {totalTexKeys}" );
		Console.WriteLine( $"With async loading, all {totalTexKeys} texture loads can run concurrently." );

		Assert.IsTrue( sw.ElapsedMilliseconds < 2000,
			$"Parsing {toUse} complex MODLs took {sw.ElapsedMilliseconds}ms — should be <2000ms." );
	}

	/// <summary>
	/// Documents the texture key distribution across shader field types
	/// for all models in the package. This tells us which texture slots
	/// (Diffuse, Normal, Specular, etc.) are most commonly used and therefore
	/// what the async path will be loading most often.
	/// </summary>
	[TestMethod]
	public void TextureKeyDistribution_DocumentsAsyncLoadProfile()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var fieldCounts = new Dictionary<Sims4Reader.Material.ShaderFieldType, int>();
		int totalModels = 0;
		int modelsWithTextures = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model )
			.Where( e => e.MemSize > 0 && e.FileSize > 0 )
			.Take( 1000 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				totalModels++;
				bool hasAny = false;
				foreach ( var mesh in bestLod.Meshes )
				foreach ( var (field, _) in mesh.TextureKeys )
				{
					hasAny = true;
					fieldCounts.TryGetValue( field, out var c );
					fieldCounts[field] = c + 1;
				}
				if ( hasAny ) modelsWithTextures++;
			}
			catch { }
		}

		Console.WriteLine( $"Sampled {totalModels} MODLs, {modelsWithTextures} have texture keys" );
		Console.WriteLine( "Texture field distribution (async load profile):" );
		foreach ( var (field, count) in fieldCounts.OrderByDescending( kv => kv.Value ) )
			Console.WriteLine( $"  {field,-30} {count,5} refs" );

		int totalTexRefs = fieldCounts.Values.Sum();
		Console.WriteLine( $"Total texture refs: {totalTexRefs} across {totalModels} sampled models" );
		Console.WriteLine( $"Avg textures per model-with-textures: {(modelsWithTextures > 0 ? totalTexRefs / (double)modelsWithTextures : 0):F1}" );

		Assert.IsTrue( totalModels > 0, "No MODLs found to sample." );
	}
}
