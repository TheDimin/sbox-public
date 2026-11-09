using System;
using System.IO;
using Editor;

namespace Sandbox.Tests;

[TestClass]
public class GltfAssetImportTests
{
	[TestMethod]
	[DataRow( true )]
	[DataRow( false )]
	public void FailedReloadPreservesPriorCompiledFile( bool hadPrevious )
	{
		var folder = Path.Combine( Path.GetTempPath(), "sbox-gltf-import-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( folder );
		try
		{
			var path = Path.Combine( folder, "model.glb_c" );
			if ( hadPrevious ) File.WriteAllBytes( path, [1, 2, 3] );
			Assert.ThrowsException<InvalidOperationException>( () => GltfAssetImport.PublishAtomically( path, [4, 5], () => throw new InvalidOperationException() ) );
			Assert.AreEqual( hadPrevious, File.Exists( path ) );
			if ( hadPrevious ) CollectionAssert.AreEqual( new byte[] { 1, 2, 3 }, File.ReadAllBytes( path ) );
			Assert.AreEqual( hadPrevious ? 1 : 0, Directory.GetFiles( folder ).Length );
		}
		finally { Directory.Delete( folder, true ); }
	}
}
