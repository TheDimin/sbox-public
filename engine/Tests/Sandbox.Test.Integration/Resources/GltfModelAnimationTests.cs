using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Linq;
using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;

namespace ResourceTests;

/// <summary>Exercises the real Model.Load and SkinnedModelRenderer sequence route.</summary>
[TestClass]
public sealed class GltfModelAnimationTests
{
	[TestMethod]
	[DataRow( false, false )]
	[DataRow( true, false )]
	[DataRow( false, true )]
	[DataRow( true, true )]
	public void LoadedModelMovesNativeBonesThroughComponentSequenceControls( bool skinned, bool compiled )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var previousMount = GlobalContext.Current.FileMount;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_animation_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		Scene scene = null;
		Model model = null;
		try
		{
			// Resolve the native stock shader before isolating the managed source mount.
			Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
			var source = Source( skinned );
			var filename = "animation_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
			model = Model.Load( filename );
			Assert.IsTrue( model is { IsValid: true, IsError: false } );
			Assert.AreEqual( 2, model.AnimationCount );
			CollectionAssert.AreEqual( new[] { "Move", "Other" }, model.AnimationNames.ToArray() );
			Assert.IsTrue( model.BoneCount > 0, "Model.Load must publish an actual native rig." );

			scene = new Scene();
			using var scope = scene.Push();
			var first = CreateRenderer( scene, model );
			var second = CreateRenderer( scene, model );
			first.Sequence.Name = "Move";
			second.Sequence.Name = "Other";
			first.Sequence.TimeNormalized = .25f;
			second.Sequence.TimeNormalized = .75f;
			first.AnimationUpdate();
			second.AnimationUpdate();
			AssertNativeTranslation( first, .25f );
			AssertNativeTranslation( second, .75f );
			Assert.AreEqual( .75f, second.Sequence.Time, .000001f );

			first.Sequence.Looping = false;
			Assert.ThrowsException<ArgumentException>( () => first.Sequence.Name = "Missing" );
			Assert.AreEqual( "Move", first.Sequence.Name, "A rejected selection must not overwrite the component's serialized selection." );
			Assert.AreEqual( "Move", ((SceneModel)first.SceneObject).CurrentSequence.Name );
			Assert.AreEqual( .25f, first.Sequence.Time, .000001f );
			Assert.AreEqual( 0f, first.PlaybackRate );
			Assert.IsFalse( first.Sequence.Looping );
			first.AnimationUpdate();
			AssertNativeTranslation( first, .25f );

			first.Sequence.Looping = true;
			first.PlaybackRate = 1;
			((SceneModel)first.SceneObject).Update( 1f );
			Assert.AreEqual( .25f, first.Sequence.Time, .000001f );
			first.Sequence.Looping = false;
			((SceneModel)first.SceneObject).Update( 1f );
			Assert.IsTrue( first.Sequence.IsFinished );
			AssertNativeTranslation( first, 1f );
			AssertNativeTranslation( second, .75f );

			var firstSceneModel = (SceneModel)first.SceneObject;
			firstSceneModel.UpdateToBindPose();
			AssertNativeTranslation( first, skinned ? 0f : 3f );
			Assert.AreEqual( "Move", first.Sequence.Name );
			Assert.AreEqual( 1f, first.Sequence.Time );
			firstSceneModel.Update( 0 );
			AssertNativeTranslation( first, 1f );

			scene.Destroy();
			scene = null;
			model.OnReloaded();
			Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.ForModel( model, 0 ), "Reloaded declarations must be revalidated rather than defaulting to a different weight decoder." );
			model.Destroy();
			Assert.ThrowsException<GltfException>( () => GltfSkinningMeshFormats.ForModel( model, 0 ), "Disposed format metadata must not make native calls or fall back to UNORM8." );
			model = null;
		}
		finally
		{
			scene?.Destroy();
			model?.Destroy();
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( directory, recursive: true );
		}
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void DirectSceneModelRetainsExactWrapperUntilModelReplacement( bool compiled )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread, "The native lifetime fixture must run on the engine main thread." );
		var previousMount = GlobalContext.Current.FileMount;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_lifetime_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		var world = new SceneWorld();
		GltfModelAnimation metadata = null;
		try
		{
			Assert.IsNull( world.ChangeListener, "Use an unrendered private world so a renderer cache cannot mask a missing SceneModel owner." );
			Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false }, "The lifetime fixture requires the stock material before its isolated source mount." );
			var source = Source( skinned: true );
			var filename = "lifetime_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
			var owned = CreateDirectModel( world, filename );
			metadata = owned.Metadata;
			DropTemporaryRetention( owned.CacheKey );
			CollectFinalizedResources();
			AssertExactWrapperRetained( owned.SceneModel, owned.WeakModel );
			Assert.AreEqual( System.Text.Json.JsonValueKind.Object, metadata.Document.Root.ValueKind, "The live scene owner must preserve the retained glTF document after forced GC." );
			owned.SceneModel.CurrentSequence.Name = "Move";
			owned.SceneModel.CurrentSequence.Time = .25f;
			owned.SceneModel.Update( 0 );
			Assert.AreEqual( .25f, owned.SceneModel.CurrentSequence.Time, "The retained glTF wrapper must still support exact sequence evaluation." );

			var replacement = Model.Cube;
			Assert.IsTrue( replacement is { IsValid: true, IsError: false } && replacement.HasRenderMeshes(), "The native replacement control requires a renderable cube resource." );
			var replacementBinding = replacement.native.GetBindingPtr();
			owned.SceneModel.Model = replacement;
			DropTemporaryRetention( owned.CacheKey );
			CollectFinalizedResources();
			Assert.IsFalse( HasTarget( owned.WeakModel ), "Replacing the model must release this scene object's managed glTF owner." );
			Assert.ThrowsException<ObjectDisposedException>( () => { _ = metadata.Document.Root; }, "The replaced glTF model's finalizer must dispose its source document." );
			var assigned = owned.SceneModel.Model;
			Assert.IsTrue( assigned is { IsValid: true, IsError: false } && assigned.HasRenderMeshes(), "The native replacement must remain valid and renderable after the old glTF model is released." );
			Assert.AreEqual( replacement.ResourcePath, assigned.ResourcePath, "Native replacement must preserve resource path identity." );
			Assert.AreEqual( replacement.Guid, assigned.Guid, "Native replacement must preserve resource GUID identity." );
			Assert.AreEqual( replacementBinding, assigned.native.GetBindingPtr(), "Native binding identity, rather than a cached managed wrapper instance, identifies the replacement resource." );
			owned.SceneModel.Delete();
		}
		finally
		{
			world.Delete();
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( directory, recursive: true );
		}
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static (SceneModel SceneModel, WeakReference<Model> WeakModel, GltfModelAnimation Metadata, long CacheKey) CreateDirectModel( SceneWorld world, string filename )
	{
		var model = Model.Load( filename );
		Assert.IsTrue( model is { IsValid: true, IsError: false }, "Actual Model.Load must construct the glTF lifetime fixture." );
		var sceneModel = new SceneModel( world, model, Transform.Zero );
		return (sceneModel, new WeakReference<Model>( model ), model.GltfAnimation, model.native.GetBindingPtr().ToInt64());
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static void AssertExactWrapperRetained( SceneModel sceneModel, WeakReference<Model> weak )
	{
		Assert.IsTrue( weak.TryGetTarget( out var model ), "The live direct SceneModel must retain its document-owning Model after temporary cache expiry." );
		Assert.AreSame( model, sceneModel.Model, "Native lookups must keep the exact wrapper used by declaration metadata." );
		Assert.AreEqual( GltfSkinWeightFormat.Float32, GltfSkinningMeshFormats.ForModel( sceneModel.Model, 0 ), "Temporary cache expiry must preserve the exact registered FLOAT weight decoder." );
	}

	[MethodImpl( MethodImplOptions.NoInlining )]
	private static bool HasTarget( WeakReference<Model> weak ) => weak.TryGetTarget( out _ );

	private static void DropTemporaryRetention( long key )
	{
		// Deterministically expire only this test-owned resource's temporary strong
		// cache generations. Preserve the weak canonical lookup and all user assets.
		var cache = typeof( Model ).Assembly.GetType( "NativeResourceCache", throwOnError: true );
		foreach ( var name in new[] { "retained", "retainedPrevious" } )
		{
			var retained = (ConcurrentDictionary<long, object>)cache.GetField( name, BindingFlags.Static | BindingFlags.NonPublic ).GetValue( null );
			retained.TryRemove( key, out _ );
		}
	}

	private static void CollectFinalizedResources()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	private static SkinnedModelRenderer CreateRenderer( Scene scene, Model model )
	{
		var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		renderer.Model = model;
		renderer.UseAnimGraph = false;
		renderer.PlaybackRate = 0;
		renderer.Enabled = true;
		Assert.IsTrue( renderer.SceneObject is SceneModel && renderer.SceneObject.IsValid() );
		return renderer;
	}

	private static void AssertNativeTranslation( SkinnedModelRenderer renderer, float metres )
	{
		var sceneModel = (SceneModel)renderer.SceneObject;
		var y = Enumerable.Range( 0, renderer.Model.BoneCount ).Select( bone => sceneModel.GetBoneWorldTransform( bone ).Position.y ).Max();
		Assert.AreEqual( metres / .0254f, y, .0001f, "The selected exact pose must reach native render bones, not only CPU metadata." );
	}

	private static byte[] Source( bool skinned )
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
		{
			foreach ( var value in new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0 } ) writer.Write( value );
			if ( skinned )
			{
				writer.Write( new byte[] { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 } );
				for ( var vertex = 0; vertex < 3; vertex++ ) foreach ( var value in new float[] { 1, 0, 0, 0 } ) writer.Write( value );
			}
		}
		var binary = stream.ToArray();
		var target = skinned ? 2 : 0;
		string Clip( string name ) => "{\"name\":\"" + name + "\",\"samplers\":[{\"input\":1,\"output\":2}],\"channels\":[{\"sampler\":0,\"target\":{\"node\":" + target + ",\"path\":\"translation\"}}]}";
		var skinViews = skinned ? ",{\"buffer\":0,\"byteOffset\":68,\"byteLength\":12},{\"buffer\":0,\"byteOffset\":80,\"byteLength\":48}" : "";
		var skinAccessors = skinned ? ",{\"bufferView\":3,\"componentType\":5121,\"count\":3,\"type\":\"VEC4\"},{\"bufferView\":4,\"componentType\":5126,\"count\":3,\"type\":\"VEC4\"}" : "";
		var skinAttributes = skinned ? ",\"JOINTS_0\":3,\"WEIGHTS_0\":4" : "";
		var nodes = skinned ? "[{\"mesh\":0,\"skin\":0},{\"children\":[2]},{}]" : "[{\"mesh\":0,\"translation\":[3,0,0]}]";
		var skin = skinned ? ",\"skins\":[{\"joints\":[1,2]}]" : "";
		return Encoding.UTF8.GetBytes( "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( binary ) + "\",\"byteLength\":" + binary.Length + "}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36},{\"buffer\":0,\"byteOffset\":36,\"byteLength\":8},{\"buffer\":0,\"byteOffset\":44,\"byteLength\":24}" + skinViews + "],"
			+ "\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\",\"min\":[0,0,0],\"max\":[1,1,0]},{\"bufferView\":1,\"componentType\":5126,\"count\":2,\"type\":\"SCALAR\",\"min\":[0],\"max\":[1]},{\"bufferView\":2,\"componentType\":5126,\"count\":2,\"type\":\"VEC3\"}" + skinAccessors + "],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"POSITION\":0" + skinAttributes + "}}]}],\"nodes\":" + nodes + skin
			+ ",\"animations\":[" + Clip( "Move" ) + "," + Clip( "Other" ) + "],\"scenes\":[{\"nodes\":" + (skinned ? "[0,1]" : "[0]") + "}],\"scene\":0}" );
	}
}
