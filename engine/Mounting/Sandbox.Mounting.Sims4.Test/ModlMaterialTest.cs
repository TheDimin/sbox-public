using Sims4Reader;
using Sims4Reader.Material;
using Sims4Reader.Mesh;

namespace Sims4MountTest;

/// <summary>
/// Tests for MODL material resolution — verifying that the MODL → MLOD → MATD
/// pipeline correctly extracts materials, textures, and shader parameters.
/// </summary>
[TestClass]
public class ModlMaterialTest
{
	/// <summary>
	/// Find the first resolved mesh that has a material definition.
	/// </summary>
	private static ResolvedMesh? FindFirstMeshWithMaterial( DbpfPackage package )
	{
		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material != null )
						return mesh;
				}
			}
			catch { }
		}

		return null;
	}

	/// <summary>
	/// Find the first resolved mesh that has both geometry and a material.
	/// </summary>
	private static ResolvedMesh? FindFirstMeshWithGeometryAndMaterial( DbpfPackage package )
	{
		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Vertices.Length > 0 && mesh.Indices.Length > 0 && mesh.Material != null )
						return mesh;
				}
			}
			catch { }
		}

		return null;
	}

	[TestMethod]
	public void Material_HasShaderEntries()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mesh = FindFirstMeshWithMaterial( package );
		if ( mesh == null )
			Assert.Fail( "No MODL mesh with material found in package." );

		Assert.IsTrue( mesh.Material!.ShaderEntries.Count > 0,
			"Material should have at least one shader entry." );
	}

	[TestMethod]
	public void Material_HasTextureKeys()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mesh = FindFirstMeshWithMaterial( package );
		if ( mesh == null )
			Assert.Fail( "No MODL mesh with material found." );

		Assert.IsTrue( mesh.TextureKeys.Count > 0,
			"Mesh should have at least one texture key from the material." );
	}

	[TestMethod]
	public void Material_TextureKeys_ContainDiffuseMap()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		bool anyHasDiffuse = false;
		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.DiffuseMap ) )
					{
						anyHasDiffuse = true;
						break;
					}
				}
				if ( anyHasDiffuse ) break;
			}
			catch { }
		}

		Assert.IsTrue( anyHasDiffuse,
			"Expected at least one MODL mesh to have a DiffuseMap texture key." );
	}

	[TestMethod]
	public void Material_ShaderEntries_ContainFloatParams()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		bool anyHasFloat = false;
		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 200 ) )
		{
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material == null ) continue;

					foreach ( var se in mesh.Material.ShaderEntries )
					{
						if ( se is ShaderFloat || se is ShaderFloat3 )
						{
							anyHasFloat = true;
							break;
						}
					}
					if ( anyHasFloat ) break;
				}
				if ( anyHasFloat ) break;
			}
			catch { }
		}

		Assert.IsTrue( anyHasFloat,
			"Expected at least one MODL material to have float/color shader parameters." );
	}

	[TestMethod]
	public void Mesh_WithGeometryAndMaterial_HasNormals()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mesh = FindFirstMeshWithGeometryAndMaterial( package );
		if ( mesh == null )
			Assert.Fail( "No MODL mesh with geometry + material found." );

		int withNormals = 0;
		for ( int i = 0; i < Math.Min( mesh.Vertices.Length, 50 ); i++ )
		{
			var n = mesh.Vertices[i].Normal;
			if ( n != null && n.Length >= 3 )
			{
				float len = MathF.Sqrt( n[0] * n[0] + n[1] * n[1] + n[2] * n[2] );
				if ( len > 0.01f )
					withNormals++;
			}
		}

		Assert.IsTrue( withNormals > 0,
			$"Expected vertices to have normals, but 0 of {Math.Min( mesh.Vertices.Length, 50 )} had non-zero normals." );
	}

	[TestMethod]
	public void Mesh_WithGeometryAndMaterial_HasUVs()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mesh = FindFirstMeshWithGeometryAndMaterial( package );
		if ( mesh == null )
			Assert.Fail( "No MODL mesh with geometry + material found." );

		int withUV = 0;
		for ( int i = 0; i < Math.Min( mesh.Vertices.Length, 50 ); i++ )
		{
			var uv = mesh.Vertices[i].UV;
			if ( uv != null && uv.Length > 0 && uv[0] != null && uv[0].Length >= 2 )
				withUV++;
		}

		Assert.IsTrue( withUV > 0,
			$"Expected vertices to have UV coordinates, but 0 of {Math.Min( mesh.Vertices.Length, 50 )} had UVs." );
	}

	[TestMethod]
	public void Mesh_Indices_AreValidTriangles()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		var mesh = FindFirstMeshWithGeometryAndMaterial( package );
		if ( mesh == null )
			Assert.Fail( "No MODL mesh with geometry + material found." );

		Assert.IsTrue( mesh.Indices.Length >= 3,
			"Expected at least 3 indices (one triangle)." );
		Assert.AreEqual( 0, mesh.Indices.Length % 3,
			"Index count should be a multiple of 3 (triangle list)." );

		int vertexCount = mesh.Vertices.Length;
		for ( int i = 0; i < mesh.Indices.Length; i++ )
		{
			Assert.IsTrue( mesh.Indices[i] >= 0 && mesh.Indices[i] < vertexCount,
				$"Index[{i}] = {mesh.Indices[i]} is out of range [0, {vertexCount})." );
		}
	}

	[TestMethod]
	public void Material_Distribution()
	{
		var packagePath = TestHelper.GetPackagePath();
		using var package = DbpfPackage.Open( packagePath );

		int totalModls = 0;
		int withMaterial = 0;
		int withTextures = 0;
		int withDiffuse = 0;
		int withNormal = 0;
		int withSpecular = 0;
		int withEmission = 0;
		int withAlpha = 0;
		int withFloatParams = 0;

		var shaderTypes = new Dictionary<ShaderType, int>();

		foreach ( var entry in package.FindAll( ResourceType.Model ).Take( 500 ) )
		{
			totalModls++;
			try
			{
				var model = ModlModelLoader.LoadModel( package, entry );
				var bestLod = model.GetBestLod();
				if ( bestLod == null ) continue;

				foreach ( var mesh in bestLod.Meshes )
				{
					if ( mesh.Material != null )
					{
						withMaterial++;

						shaderTypes.TryGetValue( mesh.Material.Shader, out var c );
						shaderTypes[mesh.Material.Shader] = c + 1;

						bool hasFloat = false;
						foreach ( var se in mesh.Material.ShaderEntries )
						{
							if ( se is ShaderFloat || se is ShaderFloat3 )
								hasFloat = true;
						}
						if ( hasFloat ) withFloatParams++;
					}

					if ( mesh.TextureKeys.Count > 0 ) withTextures++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.DiffuseMap ) ) withDiffuse++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.NormalMap ) ) withNormal++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.SpecularMap ) ) withSpecular++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.EmissionMap ) ) withEmission++;
					if ( mesh.TextureKeys.ContainsKey( ShaderFieldType.AlphaMap ) ) withAlpha++;
				}
			}
			catch { }
		}

		var shaderDist = string.Join( ", ",
			shaderTypes.OrderByDescending( kv => kv.Value )
				.Take( 10 )
				.Select( kv => $"{kv.Key}={kv.Value}" ) );

		var summary = $"Total MODLs sampled: {totalModls}\n" +
			$"With material: {withMaterial}\n" +
			$"With any textures: {withTextures}\n" +
			$"  DiffuseMap: {withDiffuse}\n" +
			$"  NormalMap: {withNormal}\n" +
			$"  SpecularMap: {withSpecular}\n" +
			$"  EmissionMap: {withEmission}\n" +
			$"  AlphaMap: {withAlpha}\n" +
			$"With float params: {withFloatParams}\n" +
			$"Shader types: {shaderDist}";

		Console.WriteLine( summary );
		Assert.IsTrue( withMaterial > 0, $"No materials found.\n{summary}" );
		Assert.IsTrue( withTextures > 0, $"No textures found.\n{summary}" );
	}
}
