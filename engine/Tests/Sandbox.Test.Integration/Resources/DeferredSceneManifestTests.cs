using Sandbox.Engine;
using Sandbox.Tasks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading;

namespace ResourceTests;

[TestClass]
[DoNotParallelize]
public class DeferredSceneManifestTests
{
	sealed class CatalogScope : IDisposable
	{
		readonly ResourceSystem previousResources = GlobalContext.Current.ResourceSystem;
		readonly bool previousEditor = Application.IsEditor;
		readonly List<GameResource> resources = [];
		static readonly PropertyInfo EditorProperty = typeof( Application ).GetProperty( nameof( Application.IsEditor ) );

		public CatalogScope( bool editor )
		{
			ThreadSafe.MarkMainThread();
			GlobalContext.Current.ResourceSystem = new ResourceSystem();
			EditorProperty.SetValue( null, editor );
		}

		public PrefabFile CreatePrefab()
		{
			// Use a real native resource for the dependency manifest, but a small managed
			// prefab body so the test does not depend on the shipped prefab's components.
			var file = (PrefabFile)GameResource.GetPromise( typeof( PrefabFile ), "prefabs/surface/default-bullet.prefab" );
			resources.Add( file );
			var root = new JsonObject
			{
				["__guid"] = Guid.NewGuid().ToString(),
				["Name"] = "Deferred manifest fixture",
				["Enabled"] = true,
				["Components"] = new JsonArray(),
				["Children"] = new JsonArray()
			};
			// Parsed JSON supports the same typed Guid reads as source asset files.
			file.RootObject = JsonNode.Parse( root.ToJsonString() ).AsObject();
			Assert.IsTrue( file.PostLoadInternal() );
			return file;
		}

		public SceneFile CreateScene()
		{
			var assetsPath = Path.GetFullPath( "unittest/addons/resources/Assets" );
			NativeEngine.FullFileSystem.AddProjectPath( "unittest", assetsPath );
			var file = (SceneFile)GameResource.GetPromise( typeof( SceneFile ), "test.scene" );
			resources.Add( file );
			file.Id = Guid.NewGuid();
			file.GameObjects = [];
			file.SceneProperties = new JsonObject();
			Assert.IsTrue( file.PostLoadInternal() );
			return file;
		}

		public SceneFile CreateSourceView( SceneFile file, string path, Guid guid )
		{
			var view = SceneFile.FromSource( path, guid, file.Serialize().ToJsonString(), [] );
			resources.Add( view );
			return view;
		}

		public void Dispose()
		{
			foreach ( var resource in resources )
			{
				resource.DestroyInternal();
				resource.Manifest?.Dispose();
				resource.Manifest = null;
				GC.SuppressFinalize( resource );
			}
			GlobalContext.Current.ResourceSystem = previousResources;
			EditorProperty.SetValue( null, previousEditor );
		}
	}

	[TestMethod]
	public void CatalogLookupsKeepMetadataAvailableWithoutStartingNativeLoads()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreatePrefab();
		Assert.IsNull( file.Manifest );
		Assert.AreSame( file, GameResource.Load<PrefabFile>( file.ResourcePath ) );
		Assert.AreSame( file, PrefabFile.Load( file.ResourcePath ) );
		Assert.AreSame( file, ResourceLibrary.GetAll<PrefabFile>().Single() );
		Assert.AreSame( file, PrefabFile.FindByGuid( Guid.Parse( file.RootObject["__guid"].GetValue<string>() ) ) );
		Assert.IsNull( file.Manifest );

		var scene = file.GetScene();
		Assert.IsNotNull( scene );
		Assert.IsNotNull( file.Manifest );
		var manifest = file.Manifest;
		Assert.AreSame( scene, file.GetScene() );
		Assert.AreSame( manifest, file.EnsureManifest() );
	}

	[TestMethod]
	public void DestroyedCatalogEntriesDoNotStartDeferredNativeLoads()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreatePrefab();
		file.DestroyInternal();
		Assert.IsFalse( file.IsValid );
		Assert.IsNull( file.EnsureManifest() );
		Assert.IsNull( file.Manifest );
		Assert.IsNull( ResourceLibrary.Get<PrefabFile>( file.ResourcePath ) );
		Assert.IsNull( PrefabFile.FindByGuid( Guid.Parse( file.RootObject["__guid"].GetValue<string>() ) ) );
	}

	[TestMethod]
	public void GamesStillStartTheManifestAtRegistration()
	{
		using var scope = new CatalogScope( false );
		var file = scope.CreatePrefab();
		Assert.IsNotNull( file.Manifest );
		Assert.AreSame( file.Manifest, file.EnsureManifest() );
	}

	[TestMethod]
	public void LoadingASceneStartsItsDeferredManifest()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreateScene();
		Assert.AreSame( file, SceneFile.Load( file.ResourcePath ) );
		Assert.IsNull( file.Manifest );

		var scene = new Scene();
		using var sceneScope = scene.Push();
		try
		{
			var options = new SceneLoadOptions { ShowLoadingScreen = false };
			options.SetScene( file );
			Assert.IsTrue( scene.Load( options ) );
			Assert.IsNotNull( file.Manifest );
			Assert.AreEqual( file.Id, scene.Id );
			Assert.AreSame( file.Manifest, file.EnsureManifest() );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void OpeningAPrefabForEditingStartsItsDeferredManifest()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreatePrefab();
		Assert.IsNull( file.Manifest );
		Assert.IsNull( file.CachedScene );

		var scene = PrefabScene.CreateForEditing();
		using var sceneScope = scene.Push();
		try
		{
			Assert.IsTrue( scene.Load( file ) );
			Assert.IsNotNull( file.Manifest );
			Assert.AreSame( file, scene.Source );
			Assert.IsNull( file.CachedScene );
			var manifest = file.Manifest;
			Assert.IsTrue( scene.Load( file ) );
			Assert.AreSame( manifest, file.Manifest );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void SourceSceneUseBorrowsTheCatalogManifestWithoutOwningIt()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreateScene();
		Game.Resources.AssignGuid( file, Guid.NewGuid() );
		// A source view can retain an old path after a rename. Its GUID still finds the catalog entry.
		var view = scope.CreateSourceView( file, "old-test.scene", file.Guid );
		view.Id = Guid.NewGuid();
		view.SceneProperties["Name"] = "Unsaved source name";
		Assert.IsNull( file.Manifest );
		Assert.IsNull( view.Manifest );

		var scene = new Scene();
		using var sceneScope = scene.Push();
		try
		{
			var options = new SceneLoadOptions { ShowLoadingScreen = false };
			options.SetScene( view );
			Assert.IsTrue( scene.Load( options ) );
			Assert.IsNotNull( file.Manifest );
			Assert.AreSame( file.Manifest, view.EnsureManifest() );
			Assert.IsNull( view.Manifest );
			Assert.AreEqual( view.Id, scene.Id );
			Assert.AreNotEqual( file.Id, scene.Id );
			Assert.AreEqual( "Unsaved source name", view.SceneProperties["Name"].GetValue<string>() );
			Assert.IsNull( file.SceneProperties["Name"] );

			var manifest = file.Manifest;
			view.DestroyInternal();
			Assert.IsNull( view.EnsureManifest() );
			Assert.AreSame( manifest, file.EnsureManifest() );
			Assert.AreSame( file, Game.Resources.Get<SceneFile>( file.Guid ) );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void MissingSourceScenesDoNotCreateCatalogEntriesOrManifests()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreateScene();
		var view = scope.CreateSourceView( file, "missing-source.scene", Guid.NewGuid() );
		Assert.IsNull( view.EnsureManifest() );
		Assert.IsNull( view.Manifest );
		Assert.IsNull( file.Manifest );
		Assert.IsNull( Game.Resources.Get<SceneFile>( view.ResourcePath ) );
		Assert.IsNull( new SceneFile().EnsureManifest() );
	}

	static void PumpUntilComplete( Task task )
	{
		var deadline = DateTime.UtcNow.AddSeconds( 10 );
		while ( !task.IsCompleted && DateTime.UtcNow < deadline )
		{
			SyncContext.MainThread.ProcessQueue();
			Thread.Sleep( 1 );
		}
		Assert.IsTrue( task.IsCompleted, "Manifest wait did not finish." );
		task.GetAwaiter().GetResult();
	}

	[TestMethod]
	public void WorkerWaitStartsDeferredManifestOnTheMainThread()
	{
		using var scope = new CatalogScope( true );
		SyncContext.Init();
		var file = scope.CreatePrefab();
		using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 2 ) );
		var task = Task.Run( async () =>
		{
			Assert.IsFalse( ThreadSafe.IsMainThread );
			try { await file.WaitForLoadAsync( cancellation.Token ); }
			catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested ) { }
		} );
		PumpUntilComplete( task );
		Assert.IsNotNull( file.Manifest );
		Assert.IsFalse( file.RequiresMainThreadForManifest );
	}

	[TestMethod]
	public void CancelledWorkerWaitDoesNotStartDeferredManifest()
	{
		using var scope = new CatalogScope( true );
		SyncContext.Init();
		var file = scope.CreatePrefab();
		using var cancellation = new CancellationTokenSource();
		using var queued = new ManualResetEventSlim();
		var task = Task.Run( async () =>
		{
			var wait = file.WaitForLoadAsync( cancellation.Token );
			queued.Set();
			try { await wait; }
			catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested ) { }
		} );
		using ( SyncContext.Scope( null ) )
			Assert.IsTrue( queued.Wait( TimeSpan.FromSeconds( 5 ) ) );
		cancellation.Cancel();
		PumpUntilComplete( task );
		Assert.IsNull( file.Manifest );
	}

	[TestMethod]
	public void EagerWorkerWaitDoesNotNeedTheMainThreadQueue()
	{
		using var scope = new CatalogScope( false );
		var file = scope.CreatePrefab();
		Assert.IsNotNull( file.Manifest );
		var task = Task.Run( async () =>
		{
			try { await file.WaitForLoadAsync( new CancellationToken( true ) ); }
			catch ( OperationCanceledException ) { }
		} );
		using ( SyncContext.Scope( null ) )
			Assert.IsTrue( task.Wait( TimeSpan.FromSeconds( 5 ) ) );
		task.GetAwaiter().GetResult();
	}

	[TestMethod]
	public void ExplicitWaitStartsTheManifestAndReusesIt()
	{
		using var scope = new CatalogScope( true );
		var file = scope.CreatePrefab();
		Assert.IsNull( file.Manifest );

		// Cancellation bounds this test even when the native fixture has not finished
		// loading. It still exercises the real WaitForLoadAsync entry point and loader.
		void StartWait()
		{
			var previousContext = SynchronizationContext.Current;
			Task wait;
			try
			{
				SynchronizationContext.SetSynchronizationContext( null );
				wait = file.WaitForLoadAsync( new CancellationToken( true ) );
			}
			finally
			{
				SynchronizationContext.SetSynchronizationContext( previousContext );
			}

			try { wait.GetAwaiter().GetResult(); }
			catch ( OperationCanceledException ) { }
		}

		StartWait();
		Assert.IsNotNull( file.Manifest );
		var manifest = file.Manifest;
		StartWait();
		Assert.AreSame( manifest, file.Manifest );
		Assert.IsTrue( file.IsValid );
	}
}
