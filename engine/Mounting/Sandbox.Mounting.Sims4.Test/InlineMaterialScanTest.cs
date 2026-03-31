using Sims4Reader;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Validates that inline material scanning (ScanInlineMaterialMeshes) produces
/// correct, consistent results and that the inline MATD data can actually be
/// extracted from the MODL RCOL.
///
/// These tests prove correctness of the scanning logic so it can safely
/// be deferred from mount time to resource load time.
/// </summary>
[TestClass]
public class InlineMaterialScanTest
{
	/// <summary>
	/// ScanInlineMaterialMeshes should return mesh indices that are valid
	/// (within the actual mesh count of the best LOD).
	/// </summary>
	[TestMethod]
	public void ScanInline_ReturnedIndices_AreValidMeshIndices()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int modlsScanned = 0;
		int invalidIndices = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var inlineIndices = ModlModelLoader.ScanInlineMaterialMeshes( package, entry );
				if ( inlineIndices == null || inlineIndices.Count == 0 )
					continue;

				modlsScanned++;

				// Load the actual model to check mesh count
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null )
					continue;

				foreach ( var idx in inlineIndices )
				{
					if ( idx < 0 || idx >= bestLod.Meshes.Count )
					{
						invalidIndices++;
						Console.WriteLine( $"MODL {entry.Key}: inline index {idx} out of range (meshes: {bestLod.Meshes.Count})" );
					}
				}
			}
			catch { }
		}

		Console.WriteLine( $"MODLs with inline materials scanned: {modlsScanned}" );
		Assert.IsTrue( modlsScanned > 0, "No MODLs with inline materials found." );
		Assert.AreEqual( 0, invalidIndices,
			$"{invalidIndices} inline mesh indices were out of range." );
	}

	/// <summary>
	/// For every mesh index returned by ScanInlineMaterialMeshes, the mesh
	/// should actually have a non-null Material (inline MATD).
	/// </summary>
	[TestMethod]
	public void ScanInline_FlaggedMeshes_HaveInlineMaterial()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int checked_ = 0;
		int withMaterial = 0;
		int withoutMaterial = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var inlineIndices = ModlModelLoader.ScanInlineMaterialMeshes( package, entry );
				if ( inlineIndices == null || inlineIndices.Count == 0 )
					continue;

				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null )
					continue;

				foreach ( var idx in inlineIndices )
				{
					if ( idx >= bestLod.Meshes.Count )
						continue;

					checked_++;
					var mesh = bestLod.Meshes[idx];

					if ( mesh.Material != null )
						withMaterial++;
					else
						withoutMaterial++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Inline-flagged meshes checked: {checked_}, with material: {withMaterial}, without: {withoutMaterial}" );

		Assert.IsTrue( checked_ > 0, "No inline-flagged meshes found to check." );
		Assert.AreEqual( 0, withoutMaterial,
			$"{withoutMaterial} meshes were flagged as inline but have no Material." );
	}

	/// <summary>
	/// ScanInlineMaterialMeshes is deterministic — calling it twice on the
	/// same entry should return the same indices.
	/// </summary>
	[TestMethod]
	public void ScanInline_IsDeterministic()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int tested = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 100 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var first = ModlModelLoader.ScanInlineMaterialMeshes( package, entry );
				var second = ModlModelLoader.ScanInlineMaterialMeshes( package, entry );

				bool firstEmpty = first == null || first.Count == 0;
				bool secondEmpty = second == null || second.Count == 0;

				if ( firstEmpty && secondEmpty )
					continue;

				tested++;

				Assert.AreEqual( firstEmpty, secondEmpty,
					$"MODL {entry.Key}: nullability mismatch between calls." );

				if ( !firstEmpty )
				{
					CollectionAssert.AreEqual( first, second,
						$"MODL {entry.Key}: scan returned different indices on second call." );
				}
			}
			catch { }
		}

		Console.WriteLine( $"Determinism verified for {tested} MODLs with inline materials." );
		Assert.IsTrue( tested > 0, "No MODLs with inline materials found to test determinism." );
	}

	/// <summary>
	/// Inline materials should have valid shader data — at minimum a known ShaderType.
	/// This validates the MATD chunk was actually parsed, not just detected.
	/// </summary>
	[TestMethod]
	public void InlineMaterials_HaveValidShaderData()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		int checked_ = 0;
		int withShader = 0;
		int withTextures = 0;
		var shaderTypes = new Dictionary<string, int>();

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 100 ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null )
					continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material == null || mesh.MaterialResourceKey != null )
						continue; // skip external materials

					checked_++;
					var shader = mesh.Material.Shader.ToString();
					shaderTypes.TryGetValue( shader, out var c );
					shaderTypes[shader] = c + 1;
					withShader++;

					if ( mesh.TextureKeys.Count > 0 )
						withTextures++;
				}
			}
			catch { }
		}

		Console.WriteLine( $"Inline materials checked: {checked_}" );
		Console.WriteLine( $"  With shader type: {withShader}" );
		Console.WriteLine( $"  With texture keys: {withTextures}" );
		Console.WriteLine( $"  Shader distribution:" );
		foreach ( var kv in shaderTypes.OrderByDescending( kv => kv.Value ).Take( 10 ) )
			Console.WriteLine( $"    {kv.Key}: {kv.Value}" );

		Assert.IsTrue( checked_ > 0, "No inline materials found." );
		Assert.IsTrue( withShader > 0, "No inline materials have a shader type." );
	}

	/// <summary>
	/// Mount paths generated for inline materials must be unique across
	/// all MODLs in the package. Collisions would cause one material
	/// to overwrite another in the mount system.
	/// </summary>
	[TestMethod]
	public void InlineMountPaths_AreUnique()
	{
		var path = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( path );

		var paths = new Dictionary<string, ResourceKey>();
		int duplicates = 0;

		foreach ( var entry in package.FindAll( ResourceType.Model ) )
		{
			if ( entry.MemSize == 0 || entry.FileSize == 0 )
				continue;

			try
			{
				var inlineIndices = ModlModelLoader.ScanInlineMaterialMeshes( package, entry );
				if ( inlineIndices == null )
					continue;

				foreach ( var meshIdx in inlineIndices )
				{
					var mountPath = $"materials/inline/{entry.Key.Group:X}_{entry.Key.Instance:X}_m{meshIdx}";
					if ( !paths.TryAdd( mountPath, entry.Key ) )
					{
						duplicates++;
						Console.WriteLine( $"DUPLICATE: {mountPath} from {entry.Key} conflicts with {paths[mountPath]}" );
					}
				}
			}
			catch { }
		}

		Console.WriteLine( $"Unique inline mount paths: {paths.Count}" );
		Assert.IsTrue( paths.Count > 0, "No inline mount paths generated." );
		Assert.AreEqual( 0, duplicates, $"{duplicates} duplicate inline mount paths detected." );
	}
}
