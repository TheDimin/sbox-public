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

			var collision = Model.Load( "mount://gtav/collision/h4_islandx_terrain_06_14.vmdl" );
			Assert.IsTrue( collision?.Physics?.Parts.Count > 0 );
			var texture = Texture.Load( "mount://gtav/textures/v_29_founsmllrmdirt+hidr/ah_found_dirtbigah_found_dirt2_a.vtex" );
			Assert.IsTrue( texture is not null && !texture.IsError && texture.Width > 1 );
			var dictionary = mount.GetAll( ResourceType.Model ).First( r => r.RelativePath.StartsWith( "models/" ) && r.RelativePath.Count( c => c == '/' ) == 2 );
			Assert.IsTrue( await dictionary.GetOrCreate() is Model member && !member.IsError );

			var scene = SceneFile.Load( "mount://gtav/maps/lr_sc1_rd_strm_4.scene" );
			Assert.IsTrue( scene?.GameObjects?.Length > 0 );
		}
		finally
		{
			await MountDirectory.SetMountState( "gtav", false );
		}
	}
}
