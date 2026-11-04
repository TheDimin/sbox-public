using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

// Khronos Sample Assets Box; copyright 2017 Cesium, CC-BY-4.0.
// Attribution and license: Fixtures/KhronosBox.
[TestClass]
public sealed class GltfDracoAccessorTests
{
	private const string DracoBuffer = "RFJBQ08CAgEBAAAACAwBCwAAA19bCgEBEFUEXOONRgL/AAAAAQABAAkDAAECAQEJAwAAAwEBAQADAwEwARADACSWEwokBAAAAAD/BwAAAAAAvwAAAL8AAAC/AACAPwsGAwEBAQEBQAEA/wAAAH8AAAD/AqFBCAAA";

	[TestMethod]
	public void IndexAccessorCanBeReadAsBothUnsignedIntegersAndFloats()
	{
		var json = "{\"asset\":{\"version\":\"2.0\"},\"extensionsUsed\":[\"KHR_draco_mesh_compression\"],"
			+ "\"extensionsRequired\":[\"KHR_draco_mesh_compression\"],\"buffers\":[{\"uri\":\"data:application/octet-stream;base64,"
			+ DracoBuffer + "\",\"byteLength\":120}],\"bufferViews\":[{\"buffer\":0,\"byteLength\":120}],"
			+ "\"accessors\":[{\"componentType\":5123,\"count\":36,\"type\":\"SCALAR\"},"
			+ "{\"componentType\":5126,\"count\":24,\"type\":\"VEC3\",\"min\":[-1,-1,-1],\"max\":[1,1,1]}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":1},\"indices\":0,\"extensions\":{"
			+ "\"KHR_draco_mesh_compression\":{\"bufferView\":0,\"attributes\":{\"POSITION\":1}}}}]}]}";
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new HashSet<string> { "KHR_draco_mesh_compression" } );

		// Exercise the generic float accessor first, as the corpus scanner does.
		var floating = document.ReadFloatAccessor( 0 );
		var integers = document.ReadIndexAccessor( 0 );
		Assert.AreEqual( 36, integers.Length );
		Assert.AreEqual( integers.Length, floating.Length );
		for ( var i = 0; i < integers.Length; i++ )
			Assert.AreEqual( (float)integers[i], floating[i], $"Index {i}" );
	}
}
