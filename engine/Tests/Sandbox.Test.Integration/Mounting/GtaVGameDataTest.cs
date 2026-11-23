using System;
using System.Linq;
using Sandbox.Mounting;
using MountDirectory = Sandbox.Mounting.Directory;

namespace MountingTests;

[TestClass]
public class GtaVGameDataTest
{
	[TestMethod]
	public async Task LoadEnhancedResourcesFromGameArchives()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GTA_V" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GTA_V=1 to test an installed copy of GTA V Enhanced." );

		var mount = await MountDirectory.Mount( "gtav" );
		Assert.IsTrue( mount?.IsMounted == true, "The installed game must mount successfully." );
		try
		{
			const string name = "h4_mph4_ter06_track_veg_w001";
			var model = Model.Load( $"mount://gtav/models/{name}.vmdl" );
			Assert.IsNotNull( model );
			Assert.IsFalse( model.IsError );
			var prefab = PrefabFile.Load( $"mount://gtav/archetypes/{name}.prefab" );
			Assert.IsNotNull( prefab?.RootObject );

			var building = PrefabFile.Load( "mount://gtav/archetypes/sc1_04_bld_02.prefab" );
			var template = SceneUtility.GetPrefabScene( building );
			var ladders = template.GetComponentsInChildren<BoxCollider>( true )
				.Where( x => x.Tags.Has( "ladder" ) ).OrderBy( x => x.Scale.z ).ToArray();
			Assert.AreEqual( 2, ladders.Length );
			Assert.IsTrue( ladders.All( x => !x.Enabled ) );
			Assert.IsTrue( ladders.All( x => x.IsTrigger && x.Static && x.Scale.x < x.Scale.y ) );
			Assert.AreEqual( 4.75f / 0.0254f, ladders[0].Scale.z, 0.01f );
			Assert.AreEqual( 5.75f / 0.0254f, ladders[1].Scale.z, 0.01f );
			Assert.IsFalse( template.GetComponentsInChildren<ModelCollider>( true ).Any( x => x.Tags.Has( "ladder" ) ) );

			var collision = Model.Load( "mount://gtav/collision/h4_islandx_terrain_06_14.vmdl" );
			Assert.IsTrue( collision?.Physics?.Parts.Count > 0 );
			var texture = Texture.Load( "mount://gtav/textures/v_29_founsmllrmdirt+hidr/ah_found_dirtbigah_found_dirt2_a.vtex" );
			Assert.IsTrue( texture is not null && !texture.IsError && texture.Width > 1 );
			var dictionary = mount.GetAll( ResourceType.Model ).First( r => r.RelativePath.StartsWith( "models/" ) && !r.RelativePath.StartsWith( "models/fragments/" ) && r.RelativePath.Count( c => c == '/' ) == 2 );
			Assert.IsTrue( await dictionary.GetOrCreate() is Model member && !member.IsError );

			var fragment = Model.Load( "mount://gtav/models/fragments/prop_dumpster_02b.vmdl" );
			Assert.IsTrue( fragment is { IsError: false, MeshCount: > 0 } );
			Assert.IsTrue( fragment.Physics?.Parts.Count > 0 );
			var dumpster = SceneUtility.GetPrefabScene( PrefabFile.Load( "mount://gtav/archetypes/prop_dumpster_02b.prefab" ) );
			var solid = dumpster.GetComponentsInChildren<ModelCollider>( true ).Single();
			Assert.IsFalse( solid.Enabled );
			Assert.IsTrue( solid.Model.Physics.Parts.Count > 0 );

			var world = SceneFile.Load( "mount://gtav/gtav.scene" );
			Assert.IsNotNull( world );
			Assert.AreEqual( 3, world.GameObjects.Length );

			var scene = SceneFile.Load( "mount://gtav/maps/lr_sc1_rd_strm_4.scene" );
			Assert.IsTrue( scene?.GameObjects?.Length > 0 );
		}
		finally
		{
			await MountDirectory.SetMountState( "gtav", false );
		}
	}
}
