using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Gltf;

namespace Sandbox.Tests.Gltf;

[TestClass]
public class GltfCompilerDenseAccessorTests
{
	private static readonly byte[] Source = Encoding.UTF8.GetBytes( """
		{"asset":{"version":"2.0"},"buffers":[{"uri":"x.bin","byteLength":12}],"bufferViews":[{"buffer":0,"byteLength":1},{"buffer":0,"byteOffset":4,"byteLength":4}],"accessors":[{"componentType":5125,"count":3,"type":"SCALAR","sparse":{"count":1,"indices":{"bufferView":0,"componentType":5121},"values":{"bufferView":1}}}]}
		""" );
	private static byte[] Resolve( string _ ) => [1, 0, 0, 0, 42, 0, 0, 0, 0, 0, 0, 0];

	[TestMethod]
	public void SparseAccessorBecomesStandardDenseViewWithSameValues()
	{
		var output = GltfCompiler.CompileWithDenseAccessors( Source, Resolve );
		using var document = GltfReader.Read( output );
		var accessor = document.Root.GetProperty( "accessors" )[0];
		Assert.IsFalse( accessor.TryGetProperty( "sparse", out _ ) );
		var view = document.Root.GetProperty( "bufferViews" )[accessor.GetProperty( "bufferView" ).GetInt32()];
		Assert.AreEqual( 0, view.GetProperty( "byteOffset" ).GetInt32() % 4 );
		Assert.AreEqual( 12, view.GetProperty( "byteLength" ).GetInt32() );
		CollectionAssert.AreEqual( new uint[] { 0, 42, 0 }, document.ReadIndexAccessor( 0 ) );
		CollectionAssert.AreEqual( output, GltfCompiler.CompileWithDenseAccessors( output ) );
	}

	[TestMethod]
	public void InterleavedNormalizedAccessorKeepsFormatAndOverlay()
	{
		var source = Encoding.UTF8.GetBytes( """
			{"asset":{"version":"2.0"},"buffers":[{"uri":"x.bin","byteLength":24}],"bufferViews":[{"buffer":0,"byteLength":16,"byteStride":8},{"buffer":0,"byteOffset":16,"byteLength":1},{"buffer":0,"byteOffset":20,"byteLength":4}],"accessors":[{"bufferView":0,"byteOffset":2,"componentType":5123,"normalized":true,"count":2,"type":"VEC2","sparse":{"count":1,"indices":{"bufferView":1,"componentType":5121},"values":{"bufferView":2}}}]}
			""" );
		byte[] bytes = [99, 99, 0, 0, 255, 255, 99, 99, 99, 99, 1, 0, 2, 0, 99, 99, 1, 0, 0, 0, 255, 255, 0, 0];
		using var document = GltfReader.Read( GltfCompiler.CompileWithDenseAccessors( source, _ => bytes ) );
		var accessor = document.Root.GetProperty( "accessors" )[0];
		Assert.IsTrue( accessor.GetProperty( "normalized" ).GetBoolean() );
		Assert.AreEqual( 5123, accessor.GetProperty( "componentType" ).GetInt32() );
		Assert.IsFalse( accessor.TryGetProperty( "sparse", out _ ) );
		CollectionAssert.AreEqual( new float[] { 0, 1, 1, 0 }, document.ReadFloatAccessor( 0 ) );
	}

	[TestMethod]
	public void OpaqueMetadataKeepsSparseLayout()
	{
		var text = Encoding.UTF8.GetString( Source ).Replace( "\"asset\":", "\"extras\":{\"keep\":true},\"asset\":" );
		using var document = GltfReader.Read( GltfCompiler.CompileWithDenseAccessors( Encoding.UTF8.GetBytes( text ), Resolve ) );
		Assert.IsTrue( document.Root.GetProperty( "accessors" )[0].TryGetProperty( "sparse", out _ ) );
		Assert.IsTrue( document.Root.GetProperty( "extras" ).GetProperty( "keep" ).GetBoolean() );
	}

	[TestMethod]
	public void InvalidSparseIndexIsRejected()
	{
		Assert.ThrowsException<GltfException>( () => GltfCompiler.CompileWithDenseAccessors( Source, _ => [3, 0, 0, 0, 42, 0, 0, 0, 0, 0, 0, 0] ) );
	}
}
