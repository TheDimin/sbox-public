using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;

namespace ResourceTests;

/// <summary>
/// Positive actual-loader acceptance for signed morph controllers and independent
/// component instances. GPU appearance needs the separate posed raster oracle.
/// These tests intentionally remain red until Model.Load connects morph targets.
/// </summary>
[TestClass]
public sealed class GltfModelMorphLoadingTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void LoadedMorphModelPreservesSignedWeightsAcrossIndependentComponents( bool compiled )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var previousMount = GlobalContext.Current.FileMount;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_morph_load_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		Scene scene = null;
		Model model = null;
		try
		{
			Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
			var source = Encoding.UTF8.GetBytes( Source );
			var filename = "signed_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
			model = Model.Load( filename );
			Assert.IsTrue( model is { IsValid: true, IsError: false } );
			Assert.IsTrue( model.MorphCount >= 2, "Native positive and negative targets must be present." );
			scene = new Scene();
			using var scope = scene.Push();
			var first = Create( scene, model );
			var second = Create( scene, model );
			first.Sequence.TimeNormalized = 1f;
			second.Sequence.TimeNormalized = .5f;
			((SceneModel)first.SceneObject).Update( 0 );
			((SceneModel)second.SceneObject).Update( 0 );
			AssertWeights( first, 0, 1 );
			AssertWeights( second, 1, 0 );
			first.Sequence.TimeNormalized = 0;
			((SceneModel)first.SceneObject).Update( 0 );
			AssertWeights( first, 0, 0 );
			AssertWeights( second, 1, 0 );
		}
		finally
		{
			scene?.Destroy(); model?.Destroy();
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( directory, recursive: true );
		}
	}

	[TestMethod]
	[DataRow( false, .25f )]
	[DataRow( true, .25f )]
	[DataRow( false, 2f )]
	[DataRow( true, 2f )]
	public void StaticMorphModelsApplyMeshDefaultsAndNodeOverridesWithoutAClip( bool compiled, float meshWeight )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var root = JsonNode.Parse( Source ).AsObject();
		root.Remove( "animations" );
		root["meshes"][0]["weights"] = new JsonArray( JsonValue.Create( meshWeight ) );
		root["nodes"] = new JsonArray(
			new JsonObject { ["mesh"] = 0, ["translation"] = new JsonArray( 1, 2, 3 ), ["scale"] = new JsonArray( -2, 3, .5f ) },
			new JsonObject { ["mesh"] = 0, ["weights"] = new JsonArray( -2f ) } );
		root["scenes"][0]["nodes"] = new JsonArray( 0, 1 );
		var bytes = Encoding.UTF8.GetBytes( root.ToJsonString() );
		var previousMount = GlobalContext.Current.FileMount;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_static_morph_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		Scene scene = null; Model model = null;
		try
		{
			Assert.IsTrue( Material.Load( "materials/default/white.vmat" ) is { IsValid: true, IsError: false } );
			var filename = "static_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( bytes ) : bytes );
			GlobalContext.Current.FileMount = new AggregateFileSystem();
			Sandbox.FileSystem.Mounted.Mount( new LocalFileSystem( directory ) );
			model = Model.Load( filename );
			Assert.IsTrue( model is { IsValid: true, IsError: false } );
			Assert.AreEqual( 0, model.AnimationCount );
			Assert.IsTrue( model.MorphCount >= 4 );
			scene = new Scene();
			using var scope = scene.Push();
			var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
			renderer.Model = model; renderer.UseAnimGraph = false; renderer.PlaybackRate = 0; renderer.Enabled = true;
			var sceneModel = (SceneModel)renderer.SceneObject;
			Assert.AreEqual( 0, sceneModel.CurrentSequence.SequenceNames.Count );
			sceneModel.UpdateToBindPose();
			Assert.AreEqual( meshWeight, sceneModel.Morphs.Get( "gltf_node_0_morph_0_positive" ), .00001f );
			Assert.AreEqual( 0f, sceneModel.Morphs.Get( "gltf_node_0_morph_0_negative" ), .00001f );
			Assert.AreEqual( 0f, sceneModel.Morphs.Get( "gltf_node_1_morph_0_positive" ), .00001f );
			Assert.AreEqual( 2f, sceneModel.Morphs.Get( "gltf_node_1_morph_0_negative" ), .00001f );
			sceneModel.Update( .5f );
			Assert.AreEqual( meshWeight, sceneModel.Morphs.Get( "gltf_node_0_morph_0_positive" ), .00001f );
			Assert.AreEqual( 2f, sceneModel.Morphs.Get( "gltf_node_1_morph_0_negative" ), .00001f );
		}
		finally
		{
			scene?.Destroy(); model?.Destroy();
			GlobalContext.Current.FileMount = previousMount;
			Directory.Delete( directory, recursive: true );
		}
	}

	private static SkinnedModelRenderer Create( Scene scene, Model model )
	{
		var renderer = scene.CreateObject().Components.Create<SkinnedModelRenderer>( false );
		renderer.Model = model; renderer.UseAnimGraph = false; renderer.PlaybackRate = 0; renderer.Enabled = true;
		renderer.Sequence.Name = "Signed"; renderer.Sequence.Looping = false;
		Assert.IsTrue( renderer.SceneObject is SceneModel && renderer.SceneObject.IsValid() );
		return renderer;
	}

	private static void AssertWeights( SkinnedModelRenderer renderer, float positive, float negative )
	{
		var model = (SceneModel)renderer.SceneObject;
		Assert.AreEqual( positive, model.Morphs.Get( "gltf_node_0_morph_0_positive" ), .00001f );
		Assert.AreEqual( negative, model.Morphs.Get( "gltf_node_0_morph_0_negative" ), .00001f );
	}

	internal const string Source = """
		{"asset":{"version":"2.0","generator":"sbox glTF signed morph Model.Load acceptance fixture"},"buffers":[{"uri":"data:application/octet-stream;base64,mpkZvwAAAL8AAAAAmpkZPwAAAL8AAAAAAAAAAAAAAD8AAAAAAAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AAAAAAAAAAAAAIA/AACAPgAAAAAAAAAAAACAPgAAAAAAAAAAAACAPgAAAAAAAAAAAAAAAAAAgD8AAABAAAAAAAAAgD8AAIC/","byteLength":132}],"bufferViews":[{"buffer":0,"byteOffset":0,"byteLength":36},{"buffer":0,"byteOffset":36,"byteLength":36},{"buffer":0,"byteOffset":72,"byteLength":36},{"buffer":0,"byteOffset":108,"byteLength":12},{"buffer":0,"byteOffset":120,"byteLength":12}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[-0.6,-0.5,0],"max":[0.6,0.5,0]},{"bufferView":1,"componentType":5126,"count":3,"type":"VEC3"},{"bufferView":2,"componentType":5126,"count":3,"type":"VEC3","min":[0.25,0,0],"max":[0.25,0,0]},{"bufferView":3,"componentType":5126,"count":3,"type":"SCALAR","min":[0],"max":[2]},{"bufferView":4,"componentType":5126,"count":3,"type":"SCALAR"}],"meshes":[{"weights":[0],"primitives":[{"attributes":{"POSITION":0,"NORMAL":1},"targets":[{"POSITION":2}]}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0,"animations":[{"samplers":[{"input":3,"output":4}],"channels":[{"sampler":0,"target":{"node":0,"path":"weights"}}],"name":"Signed"}]}
		""";
}
