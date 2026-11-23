using Sandbox;

namespace SceneTests.Components;

[TestClass]
public sealed class MeshComponentLodTests
{
	[TestInitialize]
	public void Initialize()
	{
		if ( Surface.FindByName( "default" ) is null )
			Assert.IsNotNull( Game.Resources.LoadGameResource<Surface>( "surfaces/default.surface", EngineFileSystem.CoreContent ) );
	}

	private static PolygonMesh Triangle( float z )
	{
		var mesh = new PolygonMesh();
		var a = mesh.AddVertex( new Vector3( 0, 0, z ) );
		var b = mesh.AddVertex( new Vector3( 16, 0, z ) );
		var c = mesh.AddVertex( new Vector3( 0, 16, z ) );
		mesh.AddFace( a, b, c );
		return mesh;
	}

	[TestMethod]
	public void LodMeshesPreserveCollisionAndInvalidateAfterEdit()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>( false );
			component.Mesh = Triangle( 0 );
			component.Collision = MeshComponent.CollisionType.Mesh;
			component.Lods = new()
			{
				new() { Mesh = Triangle( 1 ), SwitchDistance = 200 },
				new() { Mesh = Triangle( 2 ), SwitchDistance = 400 }
			};
			component.LodSourceFingerprint = component.GetLodSourceFingerprint();
			component.Enabled = true;
			CollectionAssert.AreEqual( new[] { 1, 2, 252 }, component.Model.MeshInfo.Meshes.Select( x => x.LodMask ).OrderBy( x => x ).ToArray() );
			Assert.IsTrue( component.Model.Trace.Ray( new Vector3( 2, 2, 10 ), new Vector3( 2, 2, -10 ) ).Run().Hit );
			Assert.AreEqual( 1, component.Model.Physics.Parts.Sum( x => x.Meshes.Count ) );
			component.Mesh.AddVertex( new Vector3( 30, 30, 30 ) );
			component.RebuildMesh();
			Assert.IsTrue( component.Model.MeshInfo.Meshes.All( x => (x.LodMask & 7) == 7 ) );
			Assert.IsTrue( component.Model.Trace.Ray( new Vector3( 2, 2, 10 ), new Vector3( 2, 2, -10 ) ).Run().Hit );
		}
		finally
		{
			scene.Destroy();
		}
	}

	[TestMethod]
	public void LodRenderBoundsAreIndependentAndRoundTrip()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>( false );
			component.Mesh = Triangle( 0 );
			component.Collision = MeshComponent.CollisionType.None;
			var lod = Triangle( 1 );
			var original = Json.Serialize( lod );
			var bounds = new BBox( new Vector3( -25 ), new Vector3( 25 ) );
			component.Lods = new() { new() { Mesh = lod, SwitchDistance = 200, RenderBounds = bounds } };
			component.LodSourceFingerprint = component.GetLodSourceFingerprint();
			component.Enabled = true;
			var card = component.Model.MeshInfo.Meshes.Single( x => (x.LodMask & 2) != 0 );
			var changed = typeof( MeshComponent ).GetMethod( "LodSettingsChanged", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic );
			Assert.IsFalse( (bool)changed.Invoke( component, null ), "An unchanged LOD must not rebuild on every frame." );
			Assert.AreEqual( bounds.Mins, card.BoundsMin );
			Assert.AreEqual( bounds.Maxs, card.BoundsMax );
			Assert.AreEqual( original, Json.Serialize( lod ) );
			var roundTrip = Json.Deserialize<MeshComponent.LodLevel>( Json.Serialize( component.Lods[0] ) );
			Assert.AreEqual( bounds, roundTrip.RenderBounds );
			component.Lods[0].RenderBounds = new BBox( new Vector3( -40 ), new Vector3( 40 ) );
			component.RebuildMesh();
			Assert.AreEqual( new Vector3( -40 ), component.Model.MeshInfo.Meshes.Single( x => (x.LodMask & 2) != 0 ).BoundsMin );
		}
		finally { scene.Destroy(); }
	}

	[TestMethod]
	public void InvalidLodRenderBoundsFallBackToEditableMesh()
	{
		var scene = Scene.CreateEditorScene();
		try
		{
			using var scope = scene.Push();
			var component = scene.CreateObject().AddComponent<MeshComponent>( false );
			component.Mesh = Triangle( 0 );
			component.Collision = MeshComponent.CollisionType.None;
			component.Lods = new() { new() { Mesh = Triangle( 1 ), SwitchDistance = 200, RenderBounds = new BBox( new Vector3( float.NaN ), Vector3.One ) } };
			component.LodSourceFingerprint = component.GetLodSourceFingerprint();
			component.Enabled = true;
			Assert.IsTrue( component.Model.IsValid() );
			Assert.IsTrue( component.Model.MeshInfo.Meshes.All( x => (x.LodMask & 3) == 3 ) );
		}
		finally { scene.Destroy(); }
	}
}
