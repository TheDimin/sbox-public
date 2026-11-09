using Sandbox.Gltf;
using System;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json.Nodes;

namespace ResourceTests;

/// <summary>
/// Public resource/component lifecycle acceptance. These tests do not replace
/// actual draw binding and rendered-pixel checks for variant material fidelity.
/// </summary>
[TestClass]
public sealed class GltfMaterialVariantApiTests
{
	[TestMethod]
	public void RejectedTaggedOverrideDoesNotPoisonLaterComponentReplay()
	{
		using var fixture = new Fixture( false ); var renderer = fixture.Create();
		var material = Material.Load( "materials/default/white.vmat" );
		Assert.ThrowsException<NotSupportedException>( () => renderer.SetMaterialOverride( material, "unsupported_tag" ) );
		renderer.Enabled = false; renderer.Enabled = true;
		Assert.IsTrue( renderer.SceneObject.IsValid() );
	}
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public void TwoRenderersSelectIndependentlyAndReset( bool compiled )
	{
		using var fixture = new Fixture( compiled );
		CollectionAssert.AreEqual( new[] { "red", "blue", "unused" }, fixture.Model.MaterialVariantNames.ToArray() );
		var first = fixture.Create(); var second = fixture.Create();
		Assert.IsNull( GetVariant( first ) ); Assert.IsNull( GetVariant( second ) );
		SetVariant( first, 0 ); SetVariant( second, 1 );
		Assert.AreEqual( 0, GetVariant( first ) ); Assert.AreEqual( 1, GetVariant( second ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SetVariant( first, 3 ) );
		Assert.ThrowsException<ArgumentOutOfRangeException>( () => SetVariant( first, -1 ) );
		Assert.AreEqual( 0, GetVariant( first ) ); Assert.AreEqual( 1, GetVariant( second ) );
		SetVariant( first, null );
		Assert.IsNull( GetVariant( first ) ); Assert.AreEqual( 1, GetVariant( second ) );
		second.Enabled = false; second.Enabled = true;
		Assert.AreEqual( 1, GetVariant( second ) );
	}

	[TestMethod]
	public void ReloadReordersByUniqueNameAndRejectsMissingIdentity()
	{
		using var fixture = new Fixture( false );
		var renderer = fixture.Create(); SetVariant( renderer, 0 );
		var reordered = JsonNode.Parse( Source );
		reordered["extensions"]["KHR_materials_variants"]["variants"] = new JsonArray(
			new JsonObject { ["name"] = "blue" }, new JsonObject { ["name"] = "red" }, new JsonObject { ["name"] = "unused" } );
		var mappings = reordered["meshes"][0]["primitives"][0]["extensions"]["KHR_materials_variants"]["mappings"].AsArray();
		mappings[0]["variants"] = new JsonArray( 1 ); mappings[1]["variants"] = new JsonArray( 0 );
		var replacement = fixture.Resource( reordered.ToJsonString() );
		fixture.Model.ReplaceGltfResource( replacement );
		Assert.AreEqual( 1, GetVariant( renderer ) );
		var oldInstance = renderer.GltfInstance;
		var missing = fixture.Resource( Source.Replace( "{\"name\":\"red\"}", "{\"name\":\"green\"}" ) );
		Assert.ThrowsException<InvalidOperationException>( () => fixture.Model.ReplaceGltfResource( missing ) );
		Assert.IsFalse( missing.IsValid, "Rejected replacement must release its owner." );
		Assert.IsTrue( replacement.IsValid, "Rollback must preserve the prior generation's owner." );
		Assert.AreEqual( 1, GetVariant( renderer ) ); Assert.AreSame( oldInstance, renderer.GltfInstance );
		CollectionAssert.AreEqual( new[] { "blue", "red", "unused" }, fixture.Model.MaterialVariantNames.ToArray() );
	}

	private static PropertyInfo VariantProperty => typeof( ModelRenderer ).GetProperty( "MaterialVariant" )
		?? throw new AssertFailedException( "ModelRenderer.MaterialVariant is not implemented." );
	private static int? GetVariant( ModelRenderer renderer ) => (int?)VariantProperty.GetValue( renderer );
	private static void SetVariant( ModelRenderer renderer, int? value )
	{
		try { VariantProperty.SetValue( renderer, value ); }
		catch ( TargetInvocationException error ) when ( error.InnerException is not null )
		{
			ExceptionDispatchInfo.Capture( error.InnerException ).Throw(); throw;
		}
	}

	private sealed class Fixture : IDisposable
	{
		private readonly string directory = Path.Combine( Path.GetTempPath(), "gltf_variants_" + Guid.NewGuid().ToString( "N" ) );
		private LocalFileSystem mount;
		private Scene scene;
		internal Model Model { get; private set; }
		internal Fixture( bool compiled )
		{
			try
			{
				Directory.CreateDirectory( directory );
				var bytes = Encoding.UTF8.GetBytes( Source );
				var name = "variants_" + Guid.NewGuid().ToString( "N" ) + (compiled ? ".glb_c" : ".gltf");
				File.WriteAllBytes( Path.Combine( directory, name ), compiled ? GltfCompiler.Compile( bytes ) : bytes );
				mount = new LocalFileSystem( directory ); Sandbox.FileSystem.Mounted.Mount( mount );
				Model = Sandbox.Model.Load( name ); Assert.IsTrue( Model is { IsValid: true, IsError: false } );
				scene = Scene.CreateEditorScene();
			}
			catch { Dispose(); throw; }
		}
		internal ModelRenderer Create()
		{
			using var scope = scene.Push();
			var renderer = scene.CreateObject().Components.Create<ModelRenderer>( false );
			renderer.Model = Model; renderer.Enabled = true; scene.EditorTick( 0, 0 ); return renderer;
		}
		internal GltfManagedModelResource Resource( string json )
		{
			var document = GltfReader.Read( Encoding.UTF8.GetBytes( json ), supportedExtensions: new System.Collections.Generic.HashSet<string> { "KHR_materials_variants" } );
			try { return new GltfManagedModelResource( document ); }
			catch { document.Dispose(); throw; }
		}
		public void Dispose()
		{
			scene?.Destroy(); Model?.Destroy();
			if ( mount is not null ) { Sandbox.FileSystem.Mounted.UnMount( mount ); mount.Dispose(); mount = null; }
			if ( Directory.Exists( directory ) ) Directory.Delete( directory, true );
		}
	}

	private const string Source = """
		{"asset":{"version":"2.0"},"extensionsUsed":["KHR_materials_variants"],
		 "extensions":{"KHR_materials_variants":{"variants":[{"name":"red"},{"name":"blue"},{"name":"unused"}]}},
		 "buffers":[{"byteLength":36,"uri":"data:application/octet-stream;base64,AAAAAAAAAAAAAAAAAACAPwAAAAAAAAAAAAAAAAAAgD8AAAAA"}],
		 "bufferViews":[{"buffer":0,"byteLength":36}],"accessors":[{"bufferView":0,"componentType":5126,"count":3,"type":"VEC3","min":[0,0,0],"max":[1,1,0]}],
		 "materials":[{},{"pbrMetallicRoughness":{"baseColorFactor":[1,0,0,1]}},{"pbrMetallicRoughness":{"baseColorFactor":[0,0,1,1]}}],
		 "meshes":[{"primitives":[{"attributes":{"POSITION":0},"material":0,"extensions":{"KHR_materials_variants":{"mappings":[{"material":1,"variants":[0]},{"material":2,"variants":[1]}]}}}]}],
		 "nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}
		""";
}
