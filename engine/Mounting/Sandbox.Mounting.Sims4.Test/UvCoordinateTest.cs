using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests that UV coordinates are correctly decoded from MODL vertex buffers.
/// Verifies that Short2/Short4/Float2 encoded UVs produce valid [0,1] range values,
/// not all-zero (which would cause single-color rendering).
/// </summary>
[TestClass]
public class UvCoordinateTest
{
	/// <summary>
	/// Verify that decoded UVs are not all zero. A model with all-zero UVs
	/// would sample only the corner pixel of its texture, appearing single-colored.
	/// </summary>
	[TestMethod]
	public void Vertices_HaveNonZeroUVs()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 100 ).ToList();
		Assert.IsTrue( entries.Count > 0, "No MODL resources found." );

		int modelsWithUvs = 0;
		int modelsWithAllZeroUvs = 0;
		int totalVerticesChecked = 0;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length < 3 ) continue;

					// Check if this mesh has UV data at all
					bool hasUv = mesh.Vertices.Any( v => v.UV != null && v.UV.Length > 0 && v.UV[0] != null );
					if ( !hasUv ) continue;

					modelsWithUvs++;

					bool allZero = mesh.Vertices
						.Where( v => v.UV != null && v.UV.Length > 0 && v.UV[0] != null && v.UV[0].Length >= 2 )
						.All( v => v.UV![0][0] == 0f && v.UV[0][1] == 0f );

					if ( allZero )
						modelsWithAllZeroUvs++;

					totalVerticesChecked += mesh.Vertices.Length;
				}
			}
			catch
			{
				// Skip malformed entries
			}
		}

		Console.WriteLine( $"Meshes with UV data: {modelsWithUvs}" );
		Console.WriteLine( $"Meshes with ALL-ZERO UVs: {modelsWithAllZeroUvs}" );
		Console.WriteLine( $"Total vertices checked: {totalVerticesChecked}" );

		Assert.IsTrue( modelsWithUvs > 0, "No meshes with UV data found." );
		Assert.AreEqual( 0, modelsWithAllZeroUvs,
			$"{modelsWithAllZeroUvs}/{modelsWithUvs} meshes have all-zero UVs — Short2 normalization may be broken." );
	}

	/// <summary>
	/// Verify UV values fall in a reasonable range. Most UVs should be in [0,1],
	/// though tiled textures can exceed this. Values like 16383 or -32768 indicate
	/// raw unscaled integers leaking through.
	/// </summary>
	[TestMethod]
	public void UVs_AreInReasonableRange()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 100 ).ToList();
		Assert.IsTrue( entries.Count > 0, "No MODL resources found." );

		int meshesChecked = 0;
		int meshesInRange = 0;
		float globalMin = float.MaxValue;
		float globalMax = float.MinValue;

		foreach ( var entry in entries )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length < 3 ) continue;

					var uvsPresent = mesh.Vertices
						.Where( v => v.UV != null && v.UV.Length > 0 && v.UV[0] != null && v.UV[0].Length >= 2 )
						.ToArray();

					if ( uvsPresent.Length == 0 ) continue;
					meshesChecked++;

					float minU = uvsPresent.Min( v => v.UV![0][0] );
					float maxU = uvsPresent.Max( v => v.UV![0][0] );
					float minV = uvsPresent.Min( v => v.UV![0][1] );
					float maxV = uvsPresent.Max( v => v.UV![0][1] );

					float min = Math.Min( minU, minV );
					float max = Math.Max( maxU, maxV );

					if ( min < globalMin ) globalMin = min;
					if ( max > globalMax ) globalMax = max;

					// Reasonable range: [-10, 10] covers tiled textures too
					if ( min >= -10f && max <= 10f )
						meshesInRange++;
				}
			}
			catch
			{
				// Skip malformed entries
			}
		}

		Console.WriteLine( $"Meshes checked: {meshesChecked}" );
		Console.WriteLine( $"Meshes in reasonable UV range [-10,10]: {meshesInRange}" );
		Console.WriteLine( $"Global UV min: {globalMin:F6}, max: {globalMax:F6}" );

		Assert.IsTrue( meshesChecked > 0, "No meshes with UV data found." );

		double inRangePercent = (double)meshesInRange / meshesChecked * 100;
		Console.WriteLine( $"In-range: {inRangePercent:F1}%" );

		// At least 90% should be in reasonable range
		Assert.IsTrue( inRangePercent >= 90.0,
			$"Only {inRangePercent:F1}% of meshes have UVs in [-10,10]. Raw integers may be leaking through (global range: [{globalMin:F2}, {globalMax:F2}])." );
	}

	/// <summary>
	/// Check what UV element formats are used across MODL meshes. Helps understand
	/// the distribution of Short2 vs Short4 vs Float2 formats.
	/// </summary>
	[TestMethod]
	public void DiagnoseUvFormats()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var entries = package.FindAll( ResourceType.Model ).Take( 200 ).ToList();
		Assert.IsTrue( entries.Count > 0, "No MODL resources found." );

		var formatCounts = new Dictionary<string, int>();

		foreach ( var entry in entries )
		{
			try
			{
				var rcol = package.GetResource<RcolContainer>( entry );
				var modl = rcol.GetChunk<ModlChunk>();
				if ( modl == null ) continue;

				// Find VRTF chunks to check UV element formats
				foreach ( var chunkEntry in rcol.ChunkEntries )
				{
					if ( chunkEntry.Chunk is VertexFormat vrtf )
					{
						foreach ( var elem in vrtf.Elements )
						{
							if ( elem.Usage == ElementUsage.UV )
							{
								string key = $"UV[{elem.UsageIndex}]={elem.Format}";
								formatCounts.TryGetValue( key, out int count );
								formatCounts[key] = count + 1;
							}
						}
					}
				}
			}
			catch
			{
				// Skip malformed entries
			}
		}

		Console.WriteLine( "UV Element Format Distribution:" );
		foreach ( var kv in formatCounts.OrderByDescending( x => x.Value ) )
			Console.WriteLine( $"  {kv.Key}: {kv.Value}" );

		Assert.IsTrue( formatCounts.Count > 0, "No UV format entries found." );
	}
}
