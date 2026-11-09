using Sandbox;
using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;
using Sandbox.Engine;
using Sandbox.Gltf;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace ResourceTests;

/// <summary>Model.Load scene-selection policy and empty-resource behavior, independent of captures.</summary>
[TestClass]
public sealed class GltfSceneSelectionTests
{
	public static void VerifyStage() => Sandbox.Gltf.DirectGpu.Validation.Stage8Identity.Verify();
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ExplicitDefaultSelectsOnlyItsOwnScene( bool compiled )
	{
		WithModel( TwoScenes( true ), compiled, model =>
		{
			Assert.IsTrue( model.IsValid );
			Assert.IsFalse( model.IsError );
			Assert.IsTrue( model.HasRenderMeshes() );
			Assert.AreEqual( 1, model.MeshCount, "Scenes must not be merged." );
			CollectionAssert.AreEqual( new[] { 1 }, model.GltfResource.Asset.Nodes.Select( node => node.Index ).ToArray() );
		} );
	}

	[TestMethod]
	[DataRow( false, 0 )]
	[DataRow( true, 0 )]
	[DataRow( false, 1 )]
	[DataRow( true, 1 )]
	public void ExplicitDefaultWithoutDrawableGeometryIsAValidEmptyModel( bool compiled, int shape )
	{
		// A legal primitive without POSITION has attributes, but cannot be drawn.
		var source = shape == 0
			? "{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{}],\"scene\":0}"
			: TwoScenes( true, normalsOnly: true );
		WithModel( source, compiled, model =>
		{
			Assert.IsTrue( model.IsValid );
			Assert.IsFalse( model.IsError );
			Assert.IsFalse( model.HasRenderMeshes() );
			Assert.AreEqual( 0, model.MeshCount );
			Assert.AreEqual( 0, model.GltfResource.Asset.Nodes.Count );
		} );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void EmptyModelCanBeAssignedToRendererWithoutDraws( bool compiled )
	{
		const string source = "{\"asset\":{\"version\":\"2.0\"},\"scenes\":[{}],\"scene\":0}";
		WithModel( source, compiled, model =>
		{
			var scene = Scene.CreateEditorScene();
			try
			{
				using var scope = scene.Push();
				var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
				renderer.Model = model;
				renderer.Enabled = true;
				scene.EditorTick( 0, 0 );
				Assert.IsTrue( renderer.SceneObject.IsValid() );
				Assert.IsNotNull( renderer.GltfInstance );
				Assert.AreEqual( 0, renderer.GltfInstance.DrawCalls );
				// EditorTick constructs the instance; it does not prove a render callback.
			}
			finally { scene.Destroy(); }
		} );
	}

	[TestMethod]
	public void NoDefaultSceneIsValidDocumentData()
	{
		using var document = GltfReader.Read( Encoding.UTF8.GetBytes( TwoScenes( false ) ) );
		Assert.IsNull( document.GetDefaultScene() );
		Assert.AreEqual( 2, document.Root.GetProperty( "scenes" ).GetArrayLength() );
	}

	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void ModelLoadAcceptsUnselectedAssetAndDefaultRenderReportsMissingScene( bool compiled )
	{
		WithModel( TwoScenes( false ), compiled, model =>
		{
			Assert.IsTrue( model.IsValid );
			Assert.IsFalse( model.IsError );
			Assert.IsNull( model.GltfSelectedScene );
			Assert.AreEqual( 2, model.GltfSceneCount );
			Assert.AreEqual( 0, model.MeshCount );
			var error = Assert.ThrowsException<GltfException>( () => model.GltfResource.CreateInstance( null ) );
			StringAssert.Contains( error.Message, "no default scene" );
		} );
	}

	private static void WithModel( string json, bool compiled, Action<Model> check )
	{
		Assert.IsTrue( ThreadSafe.IsMainThread );
		var aggregate = Sandbox.FileSystem.Mounted ?? throw new InvalidOperationException( "Mounted filesystem unavailable." );
		LocalFileSystem mount = null;
		var directory = Path.Combine( Path.GetTempPath(), "gltf_scene_selection_" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( directory );
		Model model = null;
		try
		{
			var source = Encoding.UTF8.GetBytes( json );
			var filename = "scene_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
			File.WriteAllBytes( Path.Combine( directory, filename ), compiled ? GltfCompiler.Compile( source ) : source );
			mount = new LocalFileSystem( directory );
			aggregate.Mount( mount );
			model = Model.Load( filename );
			Assert.IsNotNull( model );
			check( model );
		}
		finally
		{
			try { model?.Destroy(); }
			finally
			{
				try { if ( mount is not null ) aggregate.UnMount( mount ); }
				finally { mount?.Dispose(); Directory.Delete( directory, recursive: true ); }
			}
		}
	}

	private static string TwoScenes( bool explicitDefault, bool normalsOnly = false )
	{
		using var stream = new MemoryStream();
		using ( var writer = new BinaryWriter( stream, Encoding.UTF8, leaveOpen: true ) )
			foreach ( var value in normalsOnly ? new float[] { 0, 0, 1, 0, 0, 1, 0, 0, 1 } : new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0 } ) writer.Write( value );
		var binary = stream.ToArray();
		return "{\"asset\":{\"version\":\"2.0\"},\"buffers\":[{\"uri\":\"data:application/octet-stream;base64," + Convert.ToBase64String( binary ) + "\",\"byteLength\":36}],"
			+ "\"bufferViews\":[{\"buffer\":0,\"byteLength\":36}],\"accessors\":[{\"bufferView\":0,\"componentType\":5126,\"count\":3,\"type\":\"VEC3\"" + (normalsOnly ? "" : ",\"min\":[0,0,0],\"max\":[1,1,0]") + "}],"
			+ "\"meshes\":[{\"primitives\":[{\"attributes\":{\"" + (normalsOnly ? "NORMAL" : "POSITION") + "\":0}}]}],\"nodes\":[{\"mesh\":0},{\"mesh\":0,\"translation\":[5,0,0]}],\"scenes\":[{\"nodes\":[0]},{\"nodes\":[1]}]"
			+ (explicitDefault ? ",\"scene\":1" : "") + "}";
	}
}
