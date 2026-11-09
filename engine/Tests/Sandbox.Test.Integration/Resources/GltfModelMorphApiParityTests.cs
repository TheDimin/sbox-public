using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ResourceTests;

/// <summary>
/// Positive API parity requirements, separate from basic clip rendering.
/// Native signed-pair proxies intentionally do not satisfy these requirements yet.
/// </summary>
[TestClass]
public sealed class GltfModelMorphApiParityTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void PublicMorphIdentityExposesSourceTargetsInsteadOfNativeSignedPairs( bool compiled )
	{
		var root = JsonNode.Parse( GltfModelMorphLoadingTests.Source );
		root["meshes"][0]["extras"] = new JsonObject { ["targetNames"] = new JsonArray( "Stretch" ) };
		WithModel( compiled, root.ToJsonString(), (scene, model) =>
		{
			Assert.AreEqual( 1, model.MorphCount, "One source target must not appear as two public implementation controllers." );
			Assert.AreEqual( "Stretch", model.GetMorphName( 0 ) );
		} );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExplicitComponentMorphOverrideSurvivesNormalAnimationUpdates( bool compiled )
	{
		WithModel( compiled, GltfModelMorphLoadingTests.Source, (scene, model) =>
		{
			var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.Model = model; renderer.UseAnimGraph = false; renderer.PlaybackRate = 0; renderer.Enabled = true;
			renderer.Sequence.Name = "Signed"; renderer.Sequence.Looping = false; renderer.Sequence.TimeNormalized = .5f;
			renderer.AnimationUpdate();
			const string proxy = "gltf_node_0_morph_0_positive";
			renderer.Morphs.Set( proxy, .375f, 0 );
			for ( var frame = 0; frame < 3; frame++ ) renderer.AnimationUpdate();
			Assert.IsTrue( renderer.Morphs.ContainsOverride( proxy ) );
			Assert.AreEqual( .375f, ((SceneModel)renderer.SceneObject).Morphs.Get( proxy ), .00001f, "Animation-driven defaults must not replace an explicit component override." );
			renderer.Morphs.Clear( proxy, 0 );
			renderer.AnimationUpdate();
			Assert.AreEqual( 1f, ((SceneModel)renderer.SceneObject).Morphs.Get( proxy ), .00001f );
		} );
	}

	private static void WithModel( bool compiled, string json, Action<Scene, Model> validate )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var previousMount = GlobalContext.Current.FileMount;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_morph_api_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		Model model = null; Scene scene = null;
		try
		{
			Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
			var bytes = Encoding.UTF8.GetBytes( json );
			var filename = "morph_api_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( bytes ) : bytes );
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
			model = Model.Load( filename );
			Assert.IsTrue( model is { IsValid: true, IsError: false } );
			scene = new Scene(); using var scope = scene.Push();
			validate( scene, model );
		}
		finally
		{
			scene?.Destroy(); model?.Destroy();
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( directory, recursive: true );
		}
	}
}
