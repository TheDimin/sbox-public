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
			var dictionary = mount.GetAll( ResourceType.Model ).First( r => r.RelativePath.StartsWith( "models/" ) && !r.RelativePath.StartsWith( "models/fragments/" ) && r.RelativePath.Count( c => c == '/' ) == 2 );
			Assert.IsTrue( await dictionary.GetOrCreate() is Model member && !member.IsError );

			var fragment = Model.Load( "mount://gtav/models/fragments/prop_dumpster_02b.vmdl" );
			Assert.IsTrue( fragment is { IsError: false, MeshCount: > 0 } );
			Assert.IsTrue( fragment.Physics?.Parts.Count > 0 );

			var scene = SceneFile.Load( "mount://gtav/maps/lr_sc1_rd_strm_4.scene" );
			Assert.IsTrue( scene?.GameObjects?.Length > 0 );
			VerifyPlacedPhysics( scene );
		}
		finally
		{
			await MountDirectory.SetMountState( "gtav", false );
		}
	}

	private static void VerifyPlacedPhysics( SceneFile file )
	{
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			Assert.IsTrue( scene.Load( file ) );
			var bin = scene.Children.First( x => x.Name == "prop_bin_01a" );
			var body = bin.Components.Get<Rigidbody>();
			Assert.IsNotNull( body );
			body.ApplyImpulse( Vector3.Up * 100 );
			Assert.IsTrue( body.Velocity.z > 0 );
			Assert.IsFalse( bin.Components.Get<ModelCollider>().Static );
			var bush = scene.Children.First( x => x.Name == "prop_skunk_bush_01" );
			Assert.IsTrue( bush.Components.Get<ModelCollider>().Static );
			Assert.IsNull( bush.Components.Get<Rigidbody>() );
			var plant = scene.Children.First( x => x.Name == "prop_plant_group_05" );
			Assert.IsNotNull( plant.Components.Get<ModelRenderer>() );
			Assert.IsNull( plant.Components.Get<ModelCollider>() );

			var gate = GameObject.Clone( PrefabFile.Load( "mount://gtav/archetypes/sm_27_gatec_002.prefab" ) );
			var door = gate.Components.Get<Sandbox.Mapping.Door>();
			Assert.IsNotNull( door );
			door.Open();
			Assert.AreEqual( Sandbox.Mapping.Door.DoorState.Opening, door.State );

			var truck = scene.Children.First( x => x.Name == "prop_pallettruck_02" ).Components.Get<Prop>();
			var pieces = truck.Model.GetData<Sandbox.ModelEditor.Nodes.ModelBreakPiece[]>();
			Assert.AreEqual( 1, pieces.Length );
			Assert.AreEqual( "Pallet_01", pieces[0].PieceName );
			Assert.AreEqual( 100f, truck.Health );
			truck.OnDamage( new DamageInfo( 99, null, null ) );
			Assert.AreEqual( 1f, truck.Health );
			System.Collections.Generic.List<Gib> gibs = null;
			truck.OnGibsCreated += created => gibs = created;
			truck.OnDamage( new DamageInfo( 1, null, null ) );
			Assert.AreEqual( 1, gibs?.Count );
			Assert.AreEqual( pieces[0].Model, gibs[0].Model.Name );
			Assert.IsNotNull( gibs[0].Components.Get<Rigidbody>() );
		}
		finally
		{
			scene.Destroy();
		}
	}
}
