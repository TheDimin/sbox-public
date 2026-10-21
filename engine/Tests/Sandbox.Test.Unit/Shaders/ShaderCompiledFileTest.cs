using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Engine.Shaders;

namespace EngineTests;

[TestClass]
public class ShaderCompiledFileTest
{
	[TestMethod]
	public async Task ReplacesFileWhileOldBytesRemainMapped()
	{
		var directory = Directory.CreateTempSubdirectory( "shader-publish-" );
		try
		{
			var path = Path.Combine( directory.FullName, "test.shader_c" );
			File.WriteAllBytes( path, new byte[] { 1, 2, 3, 4 } );
			using ( var stream = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete ) )
			using ( var map = MemoryMappedFile.CreateFromFile( stream, null, 0, MemoryMappedFileAccess.Read, HandleInheritability.None, true ) )
			using ( var view = map.CreateViewAccessor( 0, 0, MemoryMappedFileAccess.Read ) )
			{
				await ShaderCompiledFile.WriteAsync( path, new byte[] { 9, 8 }, CancellationToken.None );
				CollectionAssert.AreEqual( new byte[] { 9, 8 }, File.ReadAllBytes( path ) );
				Assert.AreEqual( (byte)1, view.ReadByte( 0 ) );
				Assert.AreEqual( (byte)4, view.ReadByte( 3 ) );
				Assert.AreEqual( 1, Directory.GetFiles( directory.FullName ).Length );
			}
		}
		finally { directory.Delete( true ); }
	}

	[TestMethod]
	public async Task CancellationPreservesDestinationAndRemovesTemporaryFile()
	{
		var directory = Directory.CreateTempSubdirectory( "shader-publish-" );
		try
		{
			var path = Path.Combine( directory.FullName, "test.shader_c" );
			File.WriteAllBytes( path, new byte[] { 1, 2 } );
			try
			{
				await ShaderCompiledFile.WriteAsync( path, new byte[] { 9 }, new CancellationToken( true ) );
				Assert.Fail( "Cancelled publication must not succeed." );
			}
			catch ( OperationCanceledException ) { }
			CollectionAssert.AreEqual( new byte[] { 1, 2 }, File.ReadAllBytes( path ) );
			Assert.AreEqual( 1, Directory.GetFiles( directory.FullName ).Length );
		}
		finally { directory.Delete( true ); }
	}

	[TestMethod]
	public async Task CreatesNewCompiledFile()
	{
		var directory = Directory.CreateTempSubdirectory( "shader-publish-" );
		try
		{
			var path = Path.Combine( directory.FullName, "test.shader_c" );
			await ShaderCompiledFile.WriteAsync( path, new byte[] { 5, 6 }, CancellationToken.None );
			CollectionAssert.AreEqual( new byte[] { 5, 6 }, File.ReadAllBytes( path ) );
			Assert.AreEqual( 1, Directory.GetFiles( directory.FullName ).Length );
		}
		finally { directory.Delete( true ); }
	}
	[TestMethod]
	public async Task DeniedReplacementReportsDestinationAndPreservesOldBytes()
	{
		if ( !OperatingSystem.IsWindows() )
			Assert.Inconclusive( "Windows deny-delete sharing contract." );

		var directory = Directory.CreateTempSubdirectory( "shader-publish-" );
		try
		{
			var path = Path.Combine( directory.FullName, "locked.shader_c" );
			File.WriteAllBytes( path, new byte[] { 1, 2, 3, 4 } );
			IOException failure = null;
			using ( var reader = new FileStream( path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite ) )
			{
				try { await ShaderCompiledFile.WriteAsync( path, new byte[] { 9, 8 }, CancellationToken.None ); }
				catch ( IOException error ) { failure = error; }
				Assert.IsNotNull( failure, "A reader without delete sharing must block replacement." );
				StringAssert.Contains( failure.Message, path );
				StringAssert.Contains( failure.Message, "publish replacement" );
				Assert.IsInstanceOfType<IOException>( failure.InnerException );
				Assert.AreEqual( 32, failure.InnerException.HResult & 0xFFFF );
				Assert.AreEqual( 1, reader.ReadByte() );
			}
			CollectionAssert.AreEqual( new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes( path ) );
			Assert.AreEqual( 1, Directory.GetFiles( directory.FullName ).Length );
		}
		finally { directory.Delete( true ); }
	}

}
