using Editor;
using Sandbox;
using Sandbox.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

[TestClass, DoNotParallelize]
public class PublishGeneratedDependenciesTests
{
	private static readonly byte[] DependencyBytes = [0x67, 0x6c, 0x54, 0x46, 2, 0, 0, 0];

	[TestMethod]
	[DataRow( false, false, false )]
	[DataRow( true, false, false )]
	[DataRow( false, true, false )]
	[DataRow( true, true, false )]
	[DataRow( false, false, true )]
	[DataRow( true, false, true )]
	[DataRow( false, true, true )]
	[DataRow( true, true, true )]
	public void DirectAndReferencedGeneratedFilesAreCollectedAfterCompilationAndDeduplicated( bool precompiled, bool excludeDirect, bool includeSource )
	{
		var root = Path.Combine( Path.GetTempPath(), "publish-generated-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var filesystem = new LocalFileSystem( root );
		var context = new GlobalContext { FileMount = filesystem };
		try
		{
			using var scope = new GlobalContext.GlobalContextScope( context );
			var direct = new GeneratedAsset( root, "direct", "textures/direct.bin", "textures/shared.bin" ) { Recompilable = true };
			var referenced = new GeneratedAsset( root, "referenced", "textures/referenced.bin", "textures/shared.bin" ) { Recompilable = true };
			direct.References.Add( referenced );
			if ( precompiled ) { direct.Generate(); referenced.Generate(); }
			var manifest = new ProjectPublisher.PackageManifest { IncludeSourceFiles = includeSource };
			if ( excludeDirect ) manifest.SetPublishExclusions( "textures/direct.bin" );
			Collect( manifest, [direct] );
			var expected = new List<string> { "direct.asset_c", "referenced.asset_c", "textures/referenced.bin", "textures/shared.bin" };
			if ( !excludeDirect ) expected.Add( "textures/direct.bin" );
			if ( includeSource ) expected.AddRange( ["direct.asset", "referenced.asset"] );
			CollectionAssert.AreEquivalent( expected.ToArray(), manifest.Assets.Select( x => x.Name ).ToArray() );
			CollectionAssert.AreEqual( Array.Empty<string>(), manifest.Errors.ToArray() );
			foreach ( var name in expected.Where( x => x.StartsWith( "textures/", StringComparison.Ordinal ) ) )
				CollectionAssert.AreEqual( DependencyBytes, File.ReadAllBytes( manifest.Assets.Single( x => x.Name == name ).AbsolutePath ) );
			Assert.AreEqual( 1, manifest.Assets.Count( x => x.Name == "textures/shared.bin" ) );
			Assert.AreEqual( includeSource, manifest.Assets.Any( x => x.Name.EndsWith( ".asset", StringComparison.Ordinal ) ) );
			if ( precompiled ) Assert.IsTrue( direct.ReturnedFalse && referenced.ReturnedFalse, "A false no-op result must not be treated as a compile failure." );
		}
		finally
		{
			context.CancellationTokenSource.Dispose();
			filesystem.Dispose();
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void SuccessfulAssetWithoutPrimaryCompiledFileStillPublishesGeneratedGameFile()
	{
		var root = Path.Combine( Path.GetTempPath(), "publish-generated-extra-only-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var filesystem = new LocalFileSystem( root );
		var context = new GlobalContext { FileMount = filesystem };
		try
		{
			using var scope = new GlobalContext.GlobalContextScope( context );
			var asset = new GeneratedAsset( root, "extra-only", "textures/extra.bin" ) { ExtraOnly = true };
			Assert.IsFalse( asset.CanRecompile );
			Assert.IsFalse( asset.IsCompiled );
			var manifest = new ProjectPublisher.PackageManifest();
			Collect( manifest, [asset] );
			CollectionAssert.AreEquivalent( new[] { "textures/extra.bin" }, manifest.Assets.Select( x => x.Name ).ToArray() );
			CollectionAssert.AreEqual( DependencyBytes, File.ReadAllBytes( manifest.Assets.Single().AbsolutePath ) );
			CollectionAssert.AreEqual( Array.Empty<string>(), manifest.Errors.ToArray() );
		}
		finally
		{
			context.CancellationTokenSource.Dispose();
			filesystem.Dispose();
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void PendingRecompilableAssetDoesNotPublishStalePrimaryOrExtras()
	{
		var root = Path.Combine( Path.GetTempPath(), "publish-generated-pending-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		var filesystem = new LocalFileSystem( root );
		var context = new GlobalContext { FileMount = filesystem };
		try
		{
			using var scope = new GlobalContext.GlobalContextScope( context );
			var asset = new GeneratedAsset( root, "pending", "textures/stale.bin" ) { Recompilable = true, Pending = true, ExposeBeforeCompile = true };
			asset.Generate();
			Assert.IsTrue( asset.CanRecompile );
			Assert.IsFalse( asset.IsCompiled );
			Assert.IsFalse( asset.IsCompileFailed );
			var manifest = new ProjectPublisher.PackageManifest();
			Collect( manifest, [asset] );
			Assert.IsTrue( asset.ReturnedFalse );
			Assert.IsFalse( manifest.Assets.Any( x => x.Name == "pending.asset_c" || x.Name == "textures/stale.bin" ) );
			Assert.IsTrue( manifest.Errors.Any( x => x.Contains( "pending.asset", StringComparison.Ordinal ) && x.Contains( "compiled", StringComparison.OrdinalIgnoreCase ) ) );
		}
		finally
		{
			context.CancellationTokenSource.Dispose();
			filesystem.Dispose();
			Directory.Delete( root, true );
		}
	}

	[TestMethod]
	public void FailedCompileDoesNotPublishPreviouslyListedDependency()
	{
		var root = Path.Combine( Path.GetTempPath(), "publish-generated-failed-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( Path.Combine( root, "textures" ) );
		var filesystem = new LocalFileSystem( root );
		var context = new GlobalContext { FileMount = filesystem };
		try
		{
			using var scope = new GlobalContext.GlobalContextScope( context );
			File.WriteAllBytes( Path.Combine( root, "textures", "stale.bin" ), DependencyBytes );
			var asset = new GeneratedAsset( root, "failed", "textures/stale.bin" ) { Fail = true, ExposeBeforeCompile = true };
			var manifest = new ProjectPublisher.PackageManifest();
			Collect( manifest, [asset] );
			Assert.IsFalse( manifest.Assets.Any( x => x.Name == "textures/stale.bin" ) );
			Assert.IsTrue( manifest.Errors.Any( x => x.Contains( "failed.asset", StringComparison.Ordinal ) ) );
		}
		finally
		{
			context.CancellationTokenSource.Dispose();
			filesystem.Dispose();
			Directory.Delete( root, true );
		}
	}

	private static void Collect( ProjectPublisher.PackageManifest manifest, List<Asset> assets )
	{
		var method = typeof( ProjectPublisher.PackageManifest ).GetMethod( "CollectAssets", BindingFlags.Instance | BindingFlags.NonPublic,
			null, [typeof( List<Asset> ), typeof( CancellationToken ), typeof( bool )], null );
		Assert.IsNotNull( method );
		Sandbox.Tasks.AsyncPump.RunSynchronously( () => (Task)method.Invoke( manifest, [assets, CancellationToken.None, false] ) );
	}

	private sealed class GeneratedAsset : MountAsset
	{
		private readonly string root;
		private readonly string compiled;
		private readonly string[] dependencies;
		private bool failed;
		private bool generated;
		public bool Fail { get; init; }
		public bool ExposeBeforeCompile { get; init; }
		public bool ExtraOnly { get; init; }
		public bool Recompilable { get; init; }
		public bool Pending { get; init; }
		public bool ReturnedFalse { get; private set; }
		public List<Asset> References { get; } = [];

		public GeneratedAsset( string root, string name, params string[] dependencies ) : base( BitConverter.ToUInt64( Guid.NewGuid().ToByteArray() ), null, null )
		{
			this.root = root;
			this.dependencies = dependencies;
			compiled = name + ".asset_c";
			Path = RelativePath = name + ".asset";
			AbsolutePath = AbsoluteSourcePath = System.IO.Path.Combine( root, RelativePath );
			AbsoluteCompiledPath = System.IO.Path.Combine( root, compiled );
			AssetType = new AssetType();
			File.WriteAllText( AbsolutePath, "source" );
		}

		public void Generate()
		{
			if ( !ExtraOnly ) File.WriteAllBytes( AbsoluteCompiledPath, [7, 8, 9] );
			foreach ( var dependency in dependencies )
			{
				var file = System.IO.Path.Combine( root, dependency );
				Directory.CreateDirectory( System.IO.Path.GetDirectoryName( file ) );
				if ( !File.Exists( file ) ) File.WriteAllBytes( file, DependencyBytes );
			}
			generated = true;
		}

		public override bool IsProcedural => false;
		public override bool CanRecompile => Recompilable;
		public override bool IsCompiled => !Pending && File.Exists( AbsoluteCompiledPath );
		public override bool IsCompileFailed => failed;
		public override string GetSourceFile( bool absolute = false ) => absolute ? AbsolutePath : RelativePath;
		public override string GetCompiledFile( bool absolute = false ) => !File.Exists( AbsoluteCompiledPath ) ? null : absolute ? AbsoluteCompiledPath : compiled;
		public override List<Asset> GetReferences( bool deep ) => References;
		public override List<string> GetAdditionalGameFiles() => ExposeBeforeCompile || generated || File.Exists( AbsoluteCompiledPath ) ? dependencies.ToList() : [];
		public override ValueTask<bool> CompileIfNeededAsync( float timeout = 30 )
		{
			if ( Fail ) { failed = true; ReturnedFalse = true; return ValueTask.FromResult( false ); }
			if ( Pending || generated || File.Exists( AbsoluteCompiledPath ) ) { ReturnedFalse = true; return ValueTask.FromResult( false ); }
			if ( !generated && !File.Exists( AbsoluteCompiledPath ) ) Generate();
			return ValueTask.FromResult( true );
		}
	}
}
