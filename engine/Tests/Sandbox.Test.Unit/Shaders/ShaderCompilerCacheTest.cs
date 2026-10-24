using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Sandbox.Engine.Shaders;

namespace EngineTests;

[TestClass]
public class ShaderCompilerCacheTest
{
	DirectoryInfo directory;
	string CompiledPath => Path.Combine( directory.FullName, "test.shader_c" );
	string CacheDirectory => Path.Combine( directory.FullName, "records" );
	static readonly byte[] OriginalBytes = [1, 2, 3, 4];

	[TestInitialize]
	public void Initialize()
	{
		directory = Directory.CreateTempSubdirectory( "shader-compiler-cache-" );
		File.WriteAllBytes( CompiledPath, OriginalBytes );
	}

	[TestCleanup]
	public void Cleanup() => directory?.Delete( true );

	ShaderCompilerCache CreateCache( string identity = "compiler-a" ) => new( CacheDirectory, identity );

	async Task<ShaderCompilerCache> RecordOriginalAsync()
	{
		var cache = CreateCache();
		await cache.RecordAsync( CompiledPath, OriginalBytes, CancellationToken.None );
		return cache;
	}

	[TestMethod]
	public async Task NewInstanceReusesPersistedOutputWithoutChangingFiles()
	{
		var cache = await RecordOriginalAsync();
		var outputTime = File.GetLastWriteTimeUtc( CompiledPath );
		var recordPath = cache.GetRecordPath( CompiledPath );
		var recordBytes = File.ReadAllBytes( recordPath );
		var recordTime = File.GetLastWriteTimeUtc( recordPath );

		Assert.IsTrue( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
		CollectionAssert.AreEqual( OriginalBytes, File.ReadAllBytes( CompiledPath ) );
		CollectionAssert.AreEqual( recordBytes, File.ReadAllBytes( recordPath ) );
		Assert.AreEqual( outputTime, File.GetLastWriteTimeUtc( CompiledPath ) );
		Assert.AreEqual( recordTime, File.GetLastWriteTimeUtc( recordPath ) );
	}

	[TestMethod]
	public async Task CompilerChangeRequiresNewSuccessfulPublication()
	{
		await RecordOriginalAsync();
		var changedCompiler = CreateCache( "compiler-b" );
		Assert.IsFalse( await changedCompiler.IsCurrentAsync( CompiledPath, CancellationToken.None ) );
		Assert.IsTrue( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );

		await changedCompiler.RecordAsync( CompiledPath, OriginalBytes, CancellationToken.None );
		Assert.IsTrue( await CreateCache( "compiler-b" ).IsCurrentAsync( CompiledPath, CancellationToken.None ) );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	public async Task MissingOutputCannotReuseRecord()
	{
		await RecordOriginalAsync();
		File.Delete( CompiledPath );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	public async Task TruncatedOutputCannotReuseRecord()
	{
		await RecordOriginalAsync();
		File.WriteAllBytes( CompiledPath, [1, 2] );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	public async Task ChangedBytesInvalidateOutputWithUnchangedSizeAndWriteTime()
	{
		await RecordOriginalAsync();
		var writeTime = File.GetLastWriteTimeUtc( CompiledPath );
		var length = new FileInfo( CompiledPath ).Length;
		File.WriteAllBytes( CompiledPath, [1, 2, 3, 9] );
		File.SetLastWriteTimeUtc( CompiledPath, writeTime );

		Assert.AreEqual( length, new FileInfo( CompiledPath ).Length );
		Assert.AreEqual( writeTime, File.GetLastWriteTimeUtc( CompiledPath ) );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	public async Task MissingRecordRequiresCompile()
	{
		var cache = CreateCache();
		Assert.IsFalse( await cache.IsCurrentAsync( CompiledPath, CancellationToken.None ) );
		await cache.RecordAsync( CompiledPath, OriginalBytes, CancellationToken.None );
		File.Delete( cache.GetRecordPath( CompiledPath ) );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	[DataRow( "{" )]
	[DataRow( "null" )]
	[DataRow( "{}" )]
	[DataRow( "{\"Compiler\":\"compiler-a\",\"Output\":\"invalid\"}" )]
	public async Task InvalidRecordRequiresCompile( string content )
	{
		var cache = await RecordOriginalAsync();
		File.WriteAllText( cache.GetRecordPath( CompiledPath ), content );
		Assert.IsFalse( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	public async Task CancelledPublicationPreservesPreviousRecord()
	{
		var cache = await RecordOriginalAsync();
		var recordPath = cache.GetRecordPath( CompiledPath );
		var recordBytes = File.ReadAllBytes( recordPath );
		try
		{
			await CreateCache( "compiler-b" ).RecordAsync( CompiledPath, [9, 8], new CancellationToken( true ) );
			Assert.Fail( "Cancelled publication must not succeed." );
		}
		catch ( OperationCanceledException ) { }

		CollectionAssert.AreEqual( recordBytes, File.ReadAllBytes( recordPath ) );
		Assert.AreEqual( 1, Directory.GetFiles( CacheDirectory ).Length );
		Assert.IsTrue( await CreateCache().IsCurrentAsync( CompiledPath, CancellationToken.None ) );
	}

	[TestMethod]
	[DataRow( null )]
	[DataRow( "" )]
	public async Task UnavailableIdentityCannotAcceptOrPublishRecord( string identity )
	{
		var cache = await RecordOriginalAsync();
		var recordPath = cache.GetRecordPath( CompiledPath );
		var recordBytes = File.ReadAllBytes( recordPath );
		var unavailable = CreateCache( identity );
		Assert.IsFalse( await unavailable.IsCurrentAsync( CompiledPath, CancellationToken.None ) );
		await unavailable.RecordAsync( CompiledPath, [9, 8], CancellationToken.None );
		CollectionAssert.AreEqual( recordBytes, File.ReadAllBytes( recordPath ) );

		File.Delete( recordPath );
		await unavailable.RecordAsync( CompiledPath, OriginalBytes, CancellationToken.None );
		Assert.IsFalse( File.Exists( recordPath ) );
	}
}
