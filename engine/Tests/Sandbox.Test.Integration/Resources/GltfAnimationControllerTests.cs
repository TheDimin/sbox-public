using Sandbox.Engine;
using Sandbox.Gltf;
using Editor;
using System;
using System.IO;
using System.Text;

namespace ResourceTests;

/// <summary>Real renderer ownership, separate from numeric and GPU-output acceptance.</summary>
[TestClass]
public sealed class GltfAnimationControllerTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ControllerSuspendsAndRestoresSequenceAndCannotBeStolen( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		var renderer = fixture.Renderer;
		renderer.Sequence.Name = "Move";
		renderer.Sequence.Time = .375f;
		var first = fixture.Controller( "Move", "Scale" );
		Assert.IsTrue( first.IsBound );
		first.Time = .75f;
		Assert.AreEqual( .375f, renderer.Sequence.Time );
		var second = fixture.Controller( "Move" );
		Assert.IsFalse( second.IsBound );
		Assert.IsFalse( string.IsNullOrWhiteSpace( second.ValidationError ) );
		Assert.AreEqual( .75f, first.Time );
		second.Enabled = false;
		first.Enabled = false;
		Assert.AreEqual( "Move", renderer.Sequence.Name );
		Assert.AreEqual( .375f, renderer.Sequence.Time );
	}

	[TestMethod]
	public void InvalidSelectionAndTargetChangeCannotKeepAStaleBinding()
	{
		using var fixture = new Fixture();
		var controller = fixture.Controller( "Move", "Scale" );
		controller.Time = .25f;
		Assert.ThrowsException<GltfException>( () => controller.SetClips( ["Move", "Overlap"] ) );
		Assert.AreEqual( .25f, controller.Time );
		Assert.IsTrue( controller.IsBound );
		controller.Target = null;
		Assert.IsFalse( controller.IsBound );
		Assert.IsTrue( string.IsNullOrEmpty( fixture.Renderer.Sequence.Name ) );
	}

	[TestMethod]
	public void InspectorListMutationsValidateWithoutDiscardingTheAcceptedSelection()
	{
		using var fixture = new Fixture();
		var controller = fixture.Controller( "Move", "Scale" );
		controller.Time = .5f;
		controller.Clips.Add( "Overlap" );
		using ( fixture.Scene.Push() ) fixture.Scene.EditorTick( 0, 0 );
		Assert.IsTrue( controller.IsBound );
		Assert.IsFalse( string.IsNullOrWhiteSpace( controller.ValidationError ) );
		Assert.AreEqual( .5f, controller.Time );
		controller.Clips.Clear();
		using ( fixture.Scene.Push() ) fixture.Scene.EditorTick( 0, 0 );
		Assert.IsTrue( controller.IsBound );
		Assert.IsTrue( string.IsNullOrEmpty( controller.ValidationError ) );
		Assert.AreEqual( 0f, controller.Time );
		Assert.AreEqual( 0f, controller.Duration );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void SameModelReloadRebindsWithoutLosingSuspendedOrControllerTime( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		fixture.Renderer.Sequence.Name = "Move";
		fixture.Renderer.Sequence.Time = .375f;
		var controller = fixture.Controller( "Move", "Scale" );
		controller.Time = .75f;
		var replacement = new GltfManagedModelResource( GltfReader.Read( Source() ) );
		fixture.Renderer.Model.ReplaceGltfResource( replacement );
		using ( fixture.Scene.Push() ) fixture.Scene.EditorTick( 0, 0 );
		Assert.IsTrue( controller.IsBound );
		Assert.AreEqual( .75f, controller.Time );
		controller.Enabled = false;
		Assert.AreEqual( "Move", fixture.Renderer.Sequence.Name );
		Assert.AreEqual( .375f, fixture.Renderer.Sequence.Time );
	}

	[TestMethod]
	public void MissingSelectedAliasAfterReloadUnbindsWithoutUsingTheOldGeneration()
	{
		using var fixture = new Fixture();
		fixture.Renderer.Sequence.Name = "Move"; fixture.Renderer.Sequence.Time = .375f;
		var controller = fixture.Controller( "Move", "Scale" ); controller.Time = .75f;
		var text = Encoding.UTF8.GetString( Source() ).Replace( "\"name\":\"Scale\"", "\"name\":\"Renamed\"", StringComparison.Ordinal );
		var replacement = new GltfManagedModelResource( GltfReader.Read( Encoding.UTF8.GetBytes( text ) ) );
		fixture.Renderer.Model.ReplaceGltfResource( replacement );
		Assert.IsFalse( controller.IsBound );
		Assert.IsFalse( string.IsNullOrWhiteSpace( controller.ValidationError ) );
		Assert.AreEqual( "Move", fixture.Renderer.Sequence.Name );
		Assert.AreEqual( .375f, fixture.Renderer.Sequence.Time );
	}

	[TestMethod]
	public void TwoRenderersKeepIndependentControllerClocks()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var second = fixture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		second.Model = fixture.Renderer.Model; second.UseAnimGraph = false; second.PlayAnimationsInEditorScene = true;
		second.PlaybackRate = 0; second.Enabled = true;
		var firstController = fixture.Controller( "Move", "Scale" );
		var secondController = fixture.Scene.CreateObject().Components.Create<GltfAnimationController>( false );
		secondController.Target = second; secondController.SetClips( ["Move"] ); secondController.Enabled = true;
		firstController.Time = .25f; secondController.Time = .75f;
		Assert.AreEqual( .25f, firstController.Time ); Assert.AreEqual( .75f, secondController.Time );
		firstController.Enabled = false;
		Assert.IsTrue( secondController.IsBound ); Assert.AreEqual( .75f, secondController.Time );
	}

	[TestMethod]
	public void ExternalNotificationFailureKeepsCommittedGenerationAndNotifiesOthers()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var first = fixture.Renderer;
		var second = fixture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		second.Model = first.Model; second.UseAnimGraph = false; second.PlayAnimationsInEditorScene = true;
		second.PlaybackRate = 0; second.Enabled = true;
		first.Sequence.Name = "Move"; first.Sequence.Time = .375f;
		var controller = fixture.Controller( "Move", "Scale" ); controller.Time = .75f;
		var oldResource = first.Model.GltfResource;
		var oldFirst = first.GltfInstance; var oldSecond = second.GltfInstance;
		first.SceneObject.RenderingEnabled = false;
		var callbacks = 0;
		void FailSecond() { if ( ++callbacks == 2 ) throw new InvalidOperationException( "Injected notification failure." ); }
		first.ModelChanged += FailSecond; second.ModelChanged += FailSecond;
		var replacement = new GltfManagedModelResource( GltfReader.Read( Source() ) );
		var notificationError = first.Model.ReplaceGltfResource( replacement );
		Assert.IsNotNull( notificationError );
		first.ModelChanged -= FailSecond; second.ModelChanged -= FailSecond;
		Assert.AreEqual( 2, callbacks );
		Assert.AreSame( replacement, first.Model.GltfResource );
		Assert.AreNotSame( oldFirst, first.GltfInstance ); Assert.AreNotSame( oldSecond, second.GltfInstance );
		Assert.IsTrue( first.SceneObject.IsValid() ); Assert.IsTrue( second.SceneObject.IsValid() );
		Assert.IsFalse( first.SceneObject.RenderingEnabled, "Reload must preserve a deliberately hidden renderer." );
		Assert.IsTrue( controller.IsBound ); Assert.AreEqual( .75f, controller.Time );
		Assert.IsTrue( replacement.IsValid ); Assert.IsFalse( oldResource.IsValid );
		controller.Enabled = false;
		Assert.AreEqual( .375f, first.Sequence.Time );
	}

	[TestMethod]
	public void InternalParticipantFailureRestoresOldInstancesAndAllowsRetry()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var first = fixture.Renderer;
		var second = fixture.Scene.CreateObject().Components.Create<FailingReloadRenderer>( false );
		second.Model = first.Model; second.Enabled = true;
		first.Sequence.Name = "Move"; first.Sequence.Time = .375f;
		var controller = fixture.Controller( "Move", "Scale" ); controller.Time = .75f;
		var oldResource = first.Model.GltfResource;
		var oldFirst = first.GltfInstance; var oldSecond = second.GltfInstance;
		second.FailNextCreation = true;
		var replacement = new GltfManagedModelResource( GltfReader.Read( Source() ) );
		Assert.ThrowsException<InvalidOperationException>( () => first.Model.ReplaceGltfResource( replacement ) );
		Assert.AreSame( oldResource, first.Model.GltfResource );
		Assert.AreSame( oldFirst, first.GltfInstance ); Assert.AreSame( oldSecond, second.GltfInstance );
		Assert.IsTrue( first.SceneObject.IsValid() ); Assert.IsTrue( second.SceneObject.IsValid() );
		Assert.IsTrue( controller.IsBound ); Assert.AreEqual( .75f, controller.Time );
		Assert.IsTrue( string.IsNullOrEmpty( controller.ValidationError ) );
		Assert.IsFalse( replacement.IsValid );
		var retry = new GltfManagedModelResource( GltfReader.Read( Source() ) );
		first.Model.ReplaceGltfResource( retry );
		Assert.AreSame( retry, first.Model.GltfResource ); Assert.IsTrue( controller.IsBound );
		Assert.AreEqual( .75f, controller.Time );
		controller.Enabled = false; Assert.AreEqual( .375f, first.Sequence.Time );
	}

	[TestMethod]
	public void FailedReloadPreservesSkinnedBoneProxyObjectsAndTheirPose()
	{
		using var fixture = new Fixture( skinned: true );
		using var scope = fixture.Scene.Push();
		var renderer = fixture.Renderer;
		var controller = fixture.Controller( "Move" ); controller.Time = .5f;
		var bone = renderer.GetBoneObject( 1 );
		Assert.IsNotNull( bone );
		var before = bone.LocalTransform;
		var failing = fixture.Scene.CreateObject().Components.Create<FailingReloadRenderer>( false );
		failing.Model = renderer.Model; failing.Enabled = true; failing.FailNextCreation = true;
		var replacement = new GltfManagedModelResource( GltfReader.Read( SkinnedSource() ) );
		Assert.ThrowsException<InvalidOperationException>( () => renderer.Model.ReplaceGltfResource( replacement ) );
		Assert.AreSame( bone, renderer.GetBoneObject( 1 ) );
		Assert.AreEqual( before, bone.LocalTransform );
		controller.Time = .75f;
		Assert.AreSame( bone, renderer.GetBoneObject( 1 ) );
		Assert.AreNotEqual( before, bone.LocalTransform, "The restored proxy must remain connected to the old pose evaluator." );
		var retry = new GltfManagedModelResource( GltfReader.Read( SkinnedSource() ) );
		Assert.IsNull( renderer.Model.ReplaceGltfResource( retry ) );
		Assert.IsTrue( controller.IsBound ); Assert.AreEqual( .75f, controller.Time );
		Assert.IsNotNull( renderer.GetBoneObject( 1 ) );
	}

	[TestMethod]
	public void PostCommitNotificationFailureKeepsDiskAndLiveGenerationTogether()
	{
		using var fixture = new Fixture();
		var directory = Path.Combine( Path.GetTempPath(), "gltf_publication_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		static void FailNotification() => throw new InvalidOperationException( "Injected post-commit notification failure." );
		fixture.Renderer.ModelChanged += FailNotification;
		try
		{
			var path = Path.Combine( directory, "model.glb_c" );
			var oldBytes = GltfCompiler.Compile( Source() );
			var changed = Encoding.UTF8.GetString( Source() ).Replace( "\"name\":\"Scale\"", "\"name\":\"Renamed\"", StringComparison.Ordinal );
			var newBytes = GltfCompiler.Compile( Encoding.UTF8.GetBytes( changed ) );
			File.WriteAllBytes( path, oldBytes );
			var replacement = new GltfManagedModelResource( GltfReader.Read( newBytes ) );
			AggregateException warning = null;
			GltfAssetImport.PublishAtomically( path, newBytes, () => warning = fixture.Renderer.Model.ReplaceGltfResource( replacement ) );
			Assert.IsNotNull( warning );
			CollectionAssert.AreEqual( newBytes, File.ReadAllBytes( path ) );
			Assert.AreSame( replacement, fixture.Renderer.Model.GltfResource );
			Assert.IsTrue( replacement.IsValid );
		}
		finally { fixture.Renderer.ModelChanged -= FailNotification; Directory.Delete( directory, true ); }
	}

	[TestMethod]
	public void PreCommitApplicationFailureRestoresDiskAndLiveGeneration()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var failing = fixture.Scene.CreateObject().Components.Create<FailingReloadRenderer>( false );
		failing.Model = fixture.Renderer.Model; failing.Enabled = true; failing.FailNextCreation = true;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_publication_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		try
		{
			var path = Path.Combine( directory, "model.glb_c" );
			var oldBytes = GltfCompiler.Compile( Source() );
			var changed = Encoding.UTF8.GetString( Source() ).Replace( "\"name\":\"Scale\"", "\"name\":\"Renamed\"", StringComparison.Ordinal );
			var newBytes = GltfCompiler.Compile( Encoding.UTF8.GetBytes( changed ) );
			File.WriteAllBytes( path, oldBytes );
			var oldResource = fixture.Renderer.Model.GltfResource;
			var oldInstance = fixture.Renderer.GltfInstance;
			var replacement = new GltfManagedModelResource( GltfReader.Read( newBytes ) );
			Assert.ThrowsException<InvalidOperationException>( () => GltfAssetImport.PublishAtomically( path, newBytes, () => fixture.Renderer.Model.ReplaceGltfResource( replacement ) ) );
			CollectionAssert.AreEqual( oldBytes, File.ReadAllBytes( path ) );
			Assert.AreSame( oldResource, fixture.Renderer.Model.GltfResource );
			Assert.AreSame( oldInstance, fixture.Renderer.GltfInstance );
			Assert.IsFalse( replacement.IsValid );
		}
		finally { Directory.Delete( directory, true ); }
	}

	[Expose]
	public sealed class FailingReloadRenderer : ModelRenderer
	{
		internal bool FailNextCreation;
		internal override void OnSceneObjectCreated( SceneObject value )
		{
			base.OnSceneObjectCreated( value );
			if ( !FailNextCreation ) return;
			FailNextCreation = false;
			throw new InvalidOperationException( "Injected internal renderer application failure." );
		}
	}

	[TestMethod]
	public void ReentrantReplacementIsRejectedBeforeItCanChangeTheOuterGeneration()
	{
		using var fixture = new Fixture();
		var renderer = fixture.Renderer;
		var controller = fixture.Controller( "Move" ); controller.Time = .5f;
		var before = renderer.GltfInstance; var oldResource = renderer.Model.GltfResource;
		var nested = new GltfManagedModelResource( GltfReader.Read( Source() ) );
		void Reenter() => renderer.Model.ReplaceGltfResource( nested );
		renderer.ModelChanged += Reenter;
		try
		{
			var replacement = new GltfManagedModelResource( GltfReader.Read( Source() ) );
			var notificationError = renderer.Model.ReplaceGltfResource( replacement );
			Assert.IsNotNull( notificationError );
			Assert.AreSame( replacement, renderer.Model.GltfResource );
			Assert.AreNotSame( before, renderer.GltfInstance );
			Assert.IsTrue( controller.IsBound ); Assert.AreEqual( .5f, controller.Time );
			Assert.IsTrue( replacement.IsValid ); Assert.IsFalse( oldResource.IsValid );
		}
		finally { renderer.ModelChanged -= Reenter; nested.ReleaseOwner(); }
	}

	[TestMethod]
	public void ModelReplacementReleasesOldBindingWithoutClaimingNativeModel()
	{
		using var fixture = new Fixture();
		var controller = fixture.Controller( "Move" );
		Assert.IsTrue( controller.IsBound );
		fixture.Renderer.Model = Model.Cube;
		using ( fixture.Scene.Push() ) fixture.Scene.EditorTick( 0, 0 );
		Assert.IsFalse( controller.IsBound );
		Assert.IsFalse( string.IsNullOrWhiteSpace( controller.ValidationError ) );
		Assert.AreEqual( Model.Cube.ResourcePath, fixture.Renderer.Model.ResourcePath );
	}

	[TestMethod]
	public void ModelAssignmentNotificationFailureKeepsCommittedNativeModel()
	{
		using var fixture = new Fixture();
		var controller = fixture.Controller( "Move" );
		static void FailNotification() => throw new InvalidOperationException( "Injected assignment notification failure." );
		fixture.Renderer.ModelChanged += FailNotification;
		try
		{
			fixture.Renderer.Model = Model.Cube;
			Assert.AreEqual( Model.Cube.ResourcePath, fixture.Renderer.Model.ResourcePath );
			Assert.IsTrue( fixture.Renderer.SceneObject.IsValid() );
			Assert.IsFalse( controller.IsBound );
		}
		finally { fixture.Renderer.ModelChanged -= FailNotification; }
	}

	[TestMethod]
	public void ModelAssignmentInternalFailureRestoresOldModelAndInstance()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var renderer = fixture.Scene.CreateObject().Components.Create<FailingReloadRenderer>( false );
		renderer.Model = fixture.Renderer.Model; renderer.Enabled = true;
		var oldModel = renderer.Model; var oldInstance = renderer.GltfInstance;
		renderer.FailNextCreation = true;
		Assert.ThrowsException<InvalidOperationException>( () => renderer.Model = Model.Cube );
		Assert.AreSame( oldModel, renderer.Model ); Assert.AreSame( oldInstance, renderer.GltfInstance );
		Assert.IsTrue( renderer.SceneObject.IsValid() );
		renderer.Model = Model.Cube;
		Assert.AreEqual( Model.Cube.ResourcePath, renderer.Model.ResourcePath );
	}

	[TestMethod]
	public void NativeModelCannotBeClaimedOrModified()
	{
		using var fixture = new Fixture();
		using var scope = fixture.Scene.Push();
		var native = fixture.Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		native.Model = Model.Cube; native.UseAnimGraph = false; native.Enabled = true;
		var controller = fixture.Scene.CreateObject().Components.Create<GltfAnimationController>( false );
		controller.Target = native;
		controller.SetClips( ["Move"] ); controller.Enabled = true;
		Assert.IsFalse( controller.IsBound );
		Assert.IsFalse( string.IsNullOrWhiteSpace( controller.ValidationError ) );
		Assert.AreEqual( Model.Cube.ResourcePath, native.Model.ResourcePath );
	}

	private sealed class Fixture : IDisposable
	{
		private readonly BaseFileSystem previousMount;
		private readonly string directory;
		private Model model;
		internal Scene Scene { get; private set; }
		internal SkinnedModelRenderer Renderer { get; private set; }
		internal Fixture( bool compiled = false, bool skinned = false )
		{
			Assert.IsTrue( ThreadSafe.IsMainThread );
			previousMount = GlobalContext.Current.FileMount;
			directory = Path.Combine( Path.GetTempPath(), "gltf_controller_" + Guid.NewGuid().ToString( "N" ) );
			Directory.CreateDirectory( directory );
			try
			{
				Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
				Assert.IsTrue( Model.Cube is { IsValid: true, IsError: false } );
				var filename = "controller_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				var source = skinned ? SkinnedSource() : Source();
				File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
				GlobalContext.Current.FileMount = new AggregateFileSystem();
				Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
				model = Model.Load( filename );
				Scene = Scene.CreateEditorScene();
				using var scope = Scene.Push();
				Renderer = Scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
				Renderer.Model = model; Renderer.UseAnimGraph = false; Renderer.PlayAnimationsInEditorScene = true; Renderer.CreateBoneObjects = skinned;
				Renderer.PlaybackRate = 0; Renderer.Enabled = true;
			}
			catch { Dispose(); throw; }
		}
		internal GltfAnimationController Controller( params string[] clips )
		{
			using var scope = Scene.Push();
			var controller = Scene.CreateObject().Components.Create<GltfAnimationController>( false );
			controller.Target = Renderer; controller.SetClips( clips ); controller.Enabled = true;
			return controller;
		}
		public void Dispose()
		{
			Scene?.Destroy(); Scene = null; model?.Destroy(); model = null;
			GlobalContext.Current.FileMount = previousMount;
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}

	private static byte[] SkinnedSource()
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			foreach ( var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0 } ) writer.Write( value );
			writer.Write( new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 } );
			for ( var i = 0; i < 3; i++ ) foreach ( var value in new float[] { 1, 0, 0, 0 } ) writer.Write( value );
		}
		var bytes = stream.ToArray();
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\",\"byteLength\":" + bytes.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":68,\"byteLength\":12},{\"buffer\":0,\"byteOffset\":80,\"byteLength\":48}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"},{\"bufferView\":3,\"componentType\":5121,\"count\":3,\"type\":\"VEC4\"},{\"bufferView\":4,\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0,\"JOINTS_0\":3,\"WEIGHTS_0\":4}}]}],\"nodes\":[{\"mesh\":0,\"skin\":0},{\"children\":[2]},{}],\"skins\":[{\"joints\":[1,2]}],"
			+ "\"animations\":[{\"name\":\"Move\",\"samplers\":[{\"input\":1,\"output\":2}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":2,\"path\":\"translation\"}}]}],\"scenes\":[{\"nodes\":[0,1]}],\"scene\":0}";
		return Encoding.UTF8.GetBytes( json );
	}

	private static byte[] Source()
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
			foreach ( var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 2, 0, 0, 1, 1, 1, 3, 3, 3 } ) writer.Write( value );
		var bytes = stream.ToArray();
		static string Clip( string name, int output, int node, string path ) => "{\"name\":\"" + name + "\",\"samplers\":[{\"input\":1,\"output\":" + output + "}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":" + node + ",\"path\":\"" + path + "\"}}]}";
		var json = "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( bytes ) + "\",\"byteLength\":" + bytes.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24},{\"buffer\":0,\"byteOffset\":68,\"byteLength\":24}],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"},{\"bufferView\":3,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0}}]}],\"nodes\":[{\"mesh\":0},{}],\"animations\":[" + Clip( "Move", 2, 0, "translation" ) + "," + Clip( "Scale", 3, 1, "scale" ) + "," + Clip( "Overlap", 2, 0, "translation" ) + "],\"scenes\":[{\"nodes\":[0,1]}],\"scene\":0}";
		return Encoding.UTF8.GetBytes( json );
	}
}
