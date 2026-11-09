using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public sealed class GltfMaterialDiscoveryTests
{
	[TestMethod]
	public void ConservativeDiscoveryIgnoresUnselectedMaterialPropertiesAndHonorsSelectedUnlit()
	{
		const string json = """
			{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_unlit"],"materials":[
			 {"normalTexture":{"index":99},"pbrMetallicRoughness":{"metallicFactor":"not-yet-realized"}},
			 {"normalTexture":{"index":99},"extensions":{"KHR_materials_unlit":{}}},
			 {"extensions":{"KHR_materials_unlit":{}}}]}
			""";
		using var supported = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_materials_unlit" } );
		Assert.IsTrue( supported.MaterialMayUseNormalTexture( 0 ) );
		Assert.IsFalse( supported.MaterialMayUseNormalTexture( 1 ) );
		Assert.IsFalse( supported.MaterialMayUseNormalTexture( 2 ) );
		Assert.IsFalse( supported.MaterialMayUseNormalTexture( null ) );
		using var fallback = GltfReader.Read( Encoding.UTF8.GetBytes( json ) );
		Assert.IsTrue( fallback.MaterialMayUseNormalTexture( 1 ), "Unsupported optional unlit falls back to the core lit material." );
	}
}
