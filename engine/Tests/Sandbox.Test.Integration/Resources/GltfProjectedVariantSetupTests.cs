using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using System;
using System.Collections.Generic;
using System.Text;

namespace ResourceTests;

[TestClass]
public sealed class GltfProjectedVariantSetupTests
{
	[TestMethod]
	public void FailedProjectedSetupDoesNotTakeDocumentReleaseOwnership()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), supportedExtensions: new HashSet<string> { "KHR_materials_variants" } );
		var released = 0;
		Assert.ThrowsException<ArgumentNullException>( () => GltfGpuAsset.PrepareProjected( document, GltfGpuAsset.DefaultComputeShaderPath, null, 0, () => released++ ) );
		Assert.AreEqual( 0, released ); Assert.AreEqual( "2.0", document.Root.GetProperty( "asset" ).GetProperty( "version" ).GetString() );
	}
	[TestMethod]
	public void SetupRetainsRawRouteAndDoesNotAcquireUnusedMaterials()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( Source ), supportedExtensions: new HashSet<string> { "KHR_materials_variants" } );
		using var asset = GltfGpuAsset.Prepare( document, GltfGpuAsset.DefaultComputeShaderPath, _ => null );
		var primitive = asset.Primitives[0]; var raw = primitive.Plan; var acquired = 0;
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => asset.ConfigureProjectedVariants( -1, ( _, _, _ ) => throw new InvalidOperationException() ) );
		Assert.AreSame( raw, primitive.Plan );
		asset.ConfigureProjectedVariants( 0, ( _, _, _ ) => { acquired++; throw new InvalidOperationException( "No material should be acquired during setup." ); } );
		Assert.AreEqual( 0, acquired ); Assert.AreSame( raw, asset.GetRawVariantPlan( primitive ) ); Assert.AreNotSame( raw, primitive.Plan );
		Assert.AreEqual( 5, primitive.Plan.Fields.Count( x => x.Semantic == "TEXCOORD" ) );
		Assert.ThrowsException<InvalidOperationException>( () => asset.ConfigureProjectedVariants( 0, ( _, _, _ ) => null ) );
	}

	private const string Source = """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
		 "extensions":{"KHR_materials_variants":{"variants":[{"name":"red"}]}},"materials":[{},{}],
		 "accessors":[{"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[0,0,0]}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]}]}}}]}],
		 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""";
}
