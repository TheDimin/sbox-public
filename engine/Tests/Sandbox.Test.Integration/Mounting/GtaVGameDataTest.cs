using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Mounting;
using NativeEngine;
using Sandbox.Rendering;
using Sandbox.Movement;
using MountDirectory = Sandbox.Mounting.Directory;

namespace MountingTests;

[TestClass]
public class GtaVGameDataTest
{
	// The mount host loads its DLL into a separate assembly context.
	sealed class WorldSession : IDisposable
	{
		readonly IDisposable session;
		readonly Action<Vector3?, IReadOnlyList<Vector3>, IReadOnlyList<Vector3>> update;

		internal WorldSession( BaseGameMount mount, GameObject root )
		{
			var assembly = mount.GetType().Assembly;
			var source = assembly.GetType( "Sandbox.Mounting.IGtaWorldSource", throwOnError: true );
			var sessionType = assembly.GetType( "Sandbox.Mounting.IGtaWorldSession", throwOnError: true );
			session = (IDisposable)source.GetMethod( "StartStreaming" ).Invoke( mount, [root] );
			update = sessionType.GetMethod( "Update", [typeof( Vector3? ), typeof( IReadOnlyList<Vector3> ), typeof( IReadOnlyList<Vector3> )] )
				.CreateDelegate<Action<Vector3?, IReadOnlyList<Vector3>, IReadOnlyList<Vector3>>>( session );
		}

		internal void Update( Vector3? camera, IReadOnlyList<Vector3> physics, IReadOnlyList<Vector3> interactions ) =>
			update( camera, physics, interactions );

		internal void Update( Vector3 camera, Vector3? physics ) =>
			Update( camera, physics.HasValue ? [physics.Value] : [camera], physics.HasValue ? [physics.Value] : [] );

		public void Dispose() => session.Dispose();
	}

	[TestMethod]
	public async Task DelPerroShorelineSupportsSwimmingAndBuoyancy()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GTA_V" ) != "1" || !Graphics.IsAvailable )
			Assert.Inconclusive( "Requires the Enhanced archives and SBOX_TEST_GRAPHICS=1." );

		var mount = await MountDirectory.Mount( "gtav" );
		var previousBatch = WaterSurfaceBatch.UseForGtaWorld;
		WaterSurfaceBatch.UseForGtaWorld = Environment.GetEnvironmentVariable( "GTA_UNBATCHED_CONTROL" ) != "1";
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			using var session = new WorldSession( mount, scene.CreateObject() );
			var point = new Vector3( -1750, -1450, 0 ) / 0.0254f;
			var timeout = System.Diagnostics.Stopwatch.StartNew();
			WaterBody water;
			do
			{
				session.Update( point, new[] { point }, Array.Empty<Vector3>() );
				water = WaterBody.Find( scene, point + Vector3.Down * 64 );
				if ( water is null ) System.Threading.Thread.Sleep( 10 );
			} while ( water is null && timeout.Elapsed.TotalSeconds < 60 );
			Assert.IsNotNull( water );
			Assert.IsTrue( water.WaveEdgeFade > 0 );
			// Put a different wave profile on the opposite side of the real Del Perro tile.
			var neighbourObject = scene.CreateObject();
			var neighbour = neighbourObject.Components.Create<WaterBody>();
			neighbour.Size = water.Size;
			neighbourObject.WorldPosition = water.WorldPosition + Vector3.Forward * water.Size.x;
			neighbour.Waves = true;
			neighbour.WaveLength = 640;
			neighbour.WaveDirection = water.WaveDirection + 90;
			neighbour.WaveHeight = water.WaveHeight + 12;
			neighbour.SharedSwellHeight = water.SharedSwellHeight;
			neighbour.WaveEdgeFade = water.WaveEdgeFade;
			float peak = 0;
			for ( int phase = 0; phase < 8; phase++ )
			{
				using var clock = Time.Scope( phase * 0.5, 1.0 / 60 );
				var edge = new Vector2( water.Size.x * 0.5f, 0 );
				float left = water.SampleSurface( edge, out var leftNormal );
				float right = neighbour.SampleSurface( -edge, out var rightNormal );
				Assert.AreEqual( left, right, 0.01f );
				Assert.IsTrue( leftNormal.Distance( rightNormal ) < 0.001f );
				peak = Math.Max( peak, Math.Abs( left ) );
			}
			Assert.IsTrue( peak > water.SharedSwellHeight * 0.8f && peak > 0 );
			Console.WriteLine( $"Shared edge peak across eight phases: {peak:F2} inches" );
			neighbourObject.Destroy();
			Assert.IsTrue( water.Contains( point + Vector3.Down * 64 ) );
			Assert.IsFalse( water.Contains( point + Vector3.Right * 30000 ) );

			var camera = scene.CreateObject().Components.Create<CameraComponent>();
			camera.WorldPosition = point + Vector3.Up * 512;
			camera.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
			camera.BackgroundColor = Color.Magenta;
			camera.CustomSize = new Vector2( 64 );
			camera.ZFar = 4096;
			scene.CreateObject().Components.Create<AmbientLight>().Color = Color.White;
			using var target = Texture.CreateRenderTarget().WithSize( 64, 64 )
				.WithFormat( ImageFormat.RGBA8888 ).Create( "Del Perro coast" );
			byte[] Capture()
			{
				EngineGlobal.SourceEnginePanelAppFrame();
				scene.GameTick( 1f / 60 );
				scene.PreRender();
				Assert.IsTrue( camera.RenderToTexture( target, new ViewSetup { EnablePostprocessing = false } ) );
				g_pRenderDevice.Present( IntPtr.Zero );
				g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
				var pixels = new byte[64 * 64 * 4];
				target.GetPixels<byte>( (0, 0, 64, 64), 0, 0, pixels, ImageFormat.RGBA8888 );
				EngineLoop.DrainFrameEndDisposables();
				RenderTarget.EndOfFrame();
				EngineLoop.RunAsyncTasks();
				return pixels;
			}
			water.Enabled = false;
			var clear = Capture();
			water.Enabled = true;
			Capture();
			var surface = Capture();
			int changed = 0;
			for ( int i = 0; i < surface.Length; i += 4 )
			{
				if ( Math.Abs( clear[i] - surface[i] ) + Math.Abs( clear[i + 1] - surface[i + 1] )
					+ Math.Abs( clear[i + 2] - surface[i + 2] ) > 24 ) changed++;
			}
			Console.WriteLine( $"Del Perro water changed {changed}/4096 pixels" );


			var swimmer = scene.CreateObject();
			swimmer.WorldPosition = point + Vector3.Down * (64 + water.WaveHeight);
			var player = swimmer.Components.Create<PlayerController>();
			player.UseInputControls = false;
			player.EnableFootstepSounds = false;
			var swim = swimmer.Components.Create<MoveModeSwim>();
			var cube = scene.CreateObject();
			cube.WorldPosition = point + new Vector3( 200, 0, -100 );
			cube.Components.Create<BoxCollider>().Scale = new Vector3( 16 );
			var body = cube.Components.Create<Rigidbody>();
			body.MassOverride = 10;
			session.Update( point + Vector3.Right * 100000, new[] { point }, Array.Empty<Vector3>() );
			for ( int i = 0; i < 10; i++ ) scene.GameTick( 0.02 );
			bool swimming = player.IsSwimming && swim.WaterLevel > swim.SwimLevel;
			float level = swim.WaterLevel, vz = body.Velocity.z;

			Assert.IsTrue( changed > 3072, $"Only {changed} water pixels changed" );
			if ( Environment.GetEnvironmentVariable( "GTA_COAST_CAPTURE" ) is { Length: > 0 } capturePath )
			{
				swimmer.Destroy();
				scene.GameTick( 1f / 60 );
				var eye = new Vector3( -1680, -1310, 8 ) / 0.0254f;
				camera.WorldPosition = eye;
				camera.WorldRotation = Rotation.LookAt( new Vector3( -0.8f, 0.3f, -0.14f ) );
				camera.ZFar = 400000;
				camera.FieldOfView = 70;
				camera.BackgroundColor = new Color( 0.44f, 0.57f, 0.67f );
				camera.CustomSize = new Vector2( 640, 360 );
				for ( int i = 0; i < 45; i++ )
				{
					session.Update( eye, new[] { eye }, Array.Empty<Vector3>() );
					scene.GameTick( 1f / 60 );
				}
				using var photo = Texture.CreateRenderTarget().WithSize( 640, 360 ).WithFormat( ImageFormat.RGBA8888 ).Create( "Coast comparison" );
				byte[] SaveView( string suffix )
				{
					scene.PreRender();
					Assert.IsTrue( camera.RenderToTexture( photo, new ViewSetup { EnablePostprocessing = false } ) );
					g_pRenderDevice.Present( IntPtr.Zero );
					g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
					var pixels = new byte[640 * 360 * 4];
					photo.GetPixels<byte>( (0, 0, 640, 360), 0, 0, pixels, ImageFormat.RGBA8888 );
					System.IO.File.WriteAllBytes( capturePath + suffix, pixels );
					return pixels;
				}
				SaveView( "" );
				// A repeatable retail seabed point south of the pier. The original mount
				// misses this side of the triangular water quad.
				var beach = new Vector3( -1529, -1366, 0 ) / 0.0254f;
				Vector3[] probes = [beach];
				var search = System.Diagnostics.Stopwatch.StartNew();
				while ( search.Elapsed.TotalSeconds < 35 )
				{
					session.Update( eye, probes, Array.Empty<Vector3>() );
					scene.GameTick( 1f / 60 );
					EngineLoop.RunAsyncTasks();
					var bed = scene.Trace.Ray( beach + Vector3.Up * 500, beach + Vector3.Down * 500 ).WithoutTags( "water", "trigger" ).Run();
					if ( bed.Hit && bed.Normal.z > 0.7f && bed.EndPosition.z is > -36 and < 4 ) break;
					System.Threading.Thread.Sleep( 10 );
				}
				Assert.IsTrue( search.Elapsed.TotalSeconds < 35, "Retail seabed did not load" );
				{
					camera.WorldPosition = beach + new Vector3( 7, 2, 4 ) / 0.0254f;
					camera.WorldRotation = Rotation.LookAt( new Vector3( -1, 0.45f, -0.32f ) );
					var startTime = Time.NowDouble;
					for ( int i = 0; i < 135; i++ )
					{
						using var clock = Time.Scope( startTime + i / 60.0, 1.0 / 60 );
						session.Update( camera.WorldPosition, probes, Array.Empty<Vector3>() );
						scene.GameTick( 1f / 60 );
						EngineLoop.RunAsyncTasks();
					}
					Assert.IsTrue( camera.WorldPosition.Distance( beach + new Vector3( 7, 2, 4 ) / 0.0254f ) < 1 );
					var overlays = scene.GetAllComponents<WaterOverlay>().Count( o => o.Body.IsValid() );
					Console.WriteLine( $"Active water overlays at beach: {overlays}, total {scene.GetAllComponents<WaterOverlay>().Count()}" );
					SaveView( ".beach" );
					if ( Environment.GetEnvironmentVariable( "GTA_WATER_BENCH" ) == "1" )
					{
						for ( int i = 0; i < 400; i++ )
							session.Update( camera.WorldPosition, probes, Array.Empty<Vector3>() );
						var waterRenderers = scene.GetAllComponents<WaterBody>()
							.SelectMany( w => w.GameObject.Components.GetAll<ModelRenderer>( FindMode.EverythingInSelfAndDescendants ) )
							.Concat( scene.GetAllComponents<WaterSurfaceBatch>().Select( b => b.GetComponent<ModelRenderer>() ).Where( r => r.IsValid() ) ).Distinct().ToArray();
						long GraphicsTicks()
						{
							var start = new System.Diagnostics.ProcessStartInfo( "powershell.exe" )
							{
								UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true
							};
							start.ArgumentList.Add( "-NoProfile" );
							start.ArgumentList.Add( "-Command" );
							start.ArgumentList.Add( $"$rows=Get-CimInstance Win32_PerfRawData_GPUPerformanceCounters_GPUEngine -Filter \"Name LIKE 'pid_{Environment.ProcessId}[_]%engtype[_]3D'\" -ErrorAction Stop; if (!$rows) {{exit 1}}; ($rows | Measure-Object RunningTime -Sum).Sum.ToString('0')" );
							using var read = System.Diagnostics.Process.Start( start );
							var output = read.StandardOutput.ReadToEnd();
							read.WaitForExit();
							return long.Parse( output.Trim() );
						}
						using var process = System.Diagnostics.Process.GetCurrentProcess();
						foreach ( var mode in new[] { true, false, false, true, true, false } )
						{
							foreach ( var renderer in waterRenderers ) renderer.Enabled = mode;
							SaveView( mode ? ".on" : ".off" );
							void Frame()
							{
								scene.PreRender();
								Assert.IsTrue( camera.RenderToTexture( photo, new ViewSetup { EnablePostprocessing = false } ) );
								g_pRenderDevice.Present( IntPtr.Zero );
								g_pRenderDevice.ForceFlushGPU( IntPtr.Zero );
							}
							for ( int i = 0; i < 5; i++ ) Frame();
							long startGpu = GraphicsTicks();
							var startCpu = process.TotalProcessorTime;
							const int samples = 96;
							for ( int i = 0; i < samples; i++ ) Frame();
							var cpu = (process.TotalProcessorTime - startCpu).TotalMilliseconds / samples;
							var gpu = (GraphicsTicks() - startGpu) / (10000.0 * samples);
							Console.WriteLine( $"WATER BENCH detailed={mode} renderers={waterRenderers.Length} frames={samples} process_cpu_ms={cpu:F3} graphics_busy_ms={gpu:F3}" );
						}
						foreach ( var renderer in waterRenderers ) renderer.Enabled = true;
					}
					if ( overlays > 0 )
					{
						foreach ( var overlay in scene.GetAllComponents<WaterOverlay>() ) overlay.Enabled = false;
						SaveView( ".nofoam" );
					}
				}
				camera.WorldPosition = new Vector3( -2500, -1400, 2300 ) / 0.0254f;
				camera.WorldRotation = Rotation.LookAt( Vector3.Down, Vector3.Forward );
				camera.FieldOfView = 8;
				camera.BackgroundColor = Color.Magenta;
				session.Update( camera.WorldPosition, Array.Empty<Vector3>(), Array.Empty<Vector3>() );
				scene.GameTick( 1f / 60 );
				Assert.AreEqual( 0, scene.GetAllComponents<WaterBody>().Count( w => w.Active ) );
				var offshore = SaveView( ".offshore" );
				var levels = new bool[256];
				for ( int i = 0; i < offshore.Length; i += 4 )
				{
					Assert.IsFalse( offshore[i] == 255 && offshore[i + 1] == 0 && offshore[i + 2] == 255, "Missing offshore surface" );
					levels[offshore[i + 1]] = true;
				}
				Assert.IsTrue( levels.Count( x => x ) >= 4, "Distant water has no visible wave shading" );
			}
			Assert.IsTrue( swimming, $"Swim level {level}" );
			Assert.IsTrue( vz > 0, $"Expected upward buoyancy, got {vz}" );
		}
		finally
		{
			WaterSurfaceBatch.UseForGtaWorld = previousBatch;
			scene.Destroy();
			await MountDirectory.SetMountState( "gtav", false );
		}
	}

	[TestMethod]
	public async Task FixedPlacementPreservesMovablePrefab()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GTA_V" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GTA_V=1 to test the installed archives." );

		var mount = await MountDirectory.Mount( "gtav" );
		Assert.IsTrue( mount?.IsMounted == true );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			// This retail map contains one explicitly fixed instance of the movable door.
			scene.Load( SceneFile.Load( "mount://gtav/maps/bh1_02_cutscenetrailer.scene" ) );
			var fixedDoor = scene.Children.Single( x => x.Name == "prop_a_trailer_door_01" );
			Assert.IsTrue( fixedDoor.IsStatic );
			Assert.IsTrue( fixedDoor.Tags.Has( "gta-placement-fixed", includeAncestors: false ) );
			var prefab = PrefabFile.Load( "mount://gtav/archetypes/prop_a_trailer_door_01.prefab" );
			var ordinaryDoor = GameObject.Clone( prefab, Transform.Zero );
			Assert.IsFalse( ordinaryDoor.IsStatic );
			var fixedArchetypeTags = fixedDoor.Tags.TryGetAll( false ).Where( tag => tag.StartsWith( "gta-archetype-", StringComparison.Ordinal ) ).ToArray();
			var ordinaryArchetypeTags = ordinaryDoor.Tags.TryGetAll( false ).Where( tag => tag.StartsWith( "gta-archetype-", StringComparison.Ordinal ) ).ToArray();
			CollectionAssert.AreEquivalent( fixedArchetypeTags, ordinaryArchetypeTags );
			Assert.IsFalse( ordinaryDoor.Tags.TryGetAll( false ).Any( tag => tag.StartsWith( "gta-placement-", StringComparison.Ordinal ) ) );
			Assert.IsFalse( SceneUtility.GetPrefabScene( prefab ).IsStatic );
		}
		finally
		{
			scene.Destroy();
			await MountDirectory.SetMountState( "gtav", false );
		}
	}
	[TestMethod]
	public async Task LodLightMapsLoadWithoutPreviewDaylight()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GTA_V" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GTA_V=1 to test the installed archives." );

		await MountDirectory.Mount( "gtav" );
		try
		{
			foreach ( var prefix in new[] { "lodlights", "distlodlights" } )
			{
				var scene = new Scene();
				try
				{
					using var scope = scene.Push();
					scene.Load( SceneFile.Load( $"mount://gtav/maps/{prefix}_small049.scene" ) );
					var sprites = scene.GetAllComponents<SpriteRenderer>().ToArray();
					Assert.AreEqual( 396, sprites.Length );
					var corona = Texture.Load( "mount://gtav/textures/graphics/corona.vtex" );
					Assert.IsTrue( corona is { IsError: false, Width: > 1 } );
					Assert.IsTrue( sprites.All( x => x.Sprite?.Animations.FirstOrDefault()?.Frames.FirstOrDefault()?.Texture == corona ) );
					Assert.IsTrue( sprites.Any( x => x.WorldPosition.Distance( new Vector3( 184.927185f, -156.619064f, 61.04987f ) / 0.0254f ) < 0.1f ) );
					Assert.AreEqual( prefix == "lodlights", scene.GetAllComponents<PointLight>().Any() );
					Assert.IsFalse( scene.GetAllComponents<DirectionalLight>().Any() );
					Assert.IsFalse( scene.GetAllComponents<SkyBox2D>().Any() );
				}
				finally
				{
					scene.Destroy();
				}
			}
		}
		finally
		{
			await MountDirectory.SetMountState( "gtav", false );
		}
	}

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
			Assert.IsTrue( template.GetComponentsInChildren<ModelRenderer>( true ).First().GameObject.IsStatic );
			var barrel = SceneUtility.GetPrefabScene( PrefabFile.Load( "mount://gtav/archetypes/prop_barrel_02a.prefab" ) );
			Assert.IsFalse( barrel.GetComponentsInChildren<ModelRenderer>( true ).First().GameObject.IsStatic );
			var ladders = template.GetComponentsInChildren<BoxCollider>( true )
				.Where( x => x.Tags.Has( "ladder" ) ).OrderBy( x => x.Scale.z ).ToArray();
			Assert.AreEqual( 2, ladders.Length );
			Assert.IsTrue( ladders.All( x => x.Enabled && !x.GameObject.Enabled ) );
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
			VerifyPlacedPhysics( scene );
		}
		finally
		{
			await MountDirectory.SetMountState( "gtav", false );
		}
	}

	private static void VerifyPlacedPhysics( SceneFile file )
	{
		var previousConnection = Connection.Local;
		var previousSystem = Networking.System;
		var scene = new Scene();
		try
		{
			Connection.Local = new SceneTests.TestConnection( Guid.NewGuid(), isHost: true );
			Networking.System = null;
			using var scope = scene.Push();
			Assert.IsTrue( scene.Load( file ) );
			var props = scene.GetAllComponents<Prop>().ToArray();
			Assert.AreEqual( 345, props.Length );
			Assert.IsTrue( props.All( x => x.Components.Get<ModelRenderer>()?.Model is { IsError: false } ) );
			Assert.AreEqual( 59, props.Count( x => !x.PhysicsEnabled ) );
			Assert.AreEqual( 187, props.Count( x => x.Components.Get<Rigidbody>() is not null ) );
			Assert.IsTrue( props.Where( x => !x.PhysicsEnabled ).All( x =>
				x.Components.Get<ModelCollider>() is null && x.Components.Get<Rigidbody>() is null ) );

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
			Assert.IsTrue( door.State is Sandbox.Mapping.Door.DoorState.Opening or Sandbox.Mapping.Door.DoorState.Open );

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
			Connection.Local = previousConnection;
			Networking.System = previousSystem;
		}
	}

	[TestMethod]
	public async Task LadderObjectTracksPlayerDistance()
	{
		if ( Environment.GetEnvironmentVariable( "SBOX_TEST_GTA_V" ) != "1" )
			Assert.Inconclusive( "Set SBOX_TEST_GTA_V=1 to test the installed archives." );

		var mount = await MountDirectory.Mount( "gtav" );
		var scene = new Scene();
		try
		{
			using var scope = scene.Push();
			var template = SceneUtility.GetPrefabScene( PrefabFile.Load( "mount://gtav/archetypes/sc1_04_bld_02.prefab" ) );
			var ladder = template.GetComponentsInChildren<BoxCollider>( true ).MaxBy( x => x.Scale.z );
			Assert.IsFalse( ladder.GameObject.Enabled );
			const float units = 1f / 0.0254f;
			var building = new Vector3( 175.20032f, -1303.1029f, 35.49249f ) * units;
			var center = building + ladder.WorldPosition;
			var approach = ladder.WorldRotation.Forward;
			var nearTop = center + ladder.WorldRotation.Up * ladder.Scale.z * 0.45f;
			var camera = center + Vector3.Forward * 500 * units;

			var root = scene.CreateObject();
			using var session = new WorldSession( mount, root );
			var timer = System.Diagnostics.Stopwatch.StartNew();
			GameObject instance = null;
			while ( instance is null && timer.Elapsed.TotalSeconds < 30 )
			{
				session.Update( camera, nearTop + approach * (4.9f * units) );
				instance = root.Children.FirstOrDefault( x => x.Tags.Has( "ladder" ) && x.WorldPosition.Distance( center ) < 1 );
				System.Threading.Thread.Sleep( 1 );
			}
			Assert.IsNotNull( instance, "Expected the retail ladder near its upper end." );
			Assert.IsTrue( instance.Enabled );
			session.Update( camera, nearTop + approach * (5.1f * units) );
			Assert.IsFalse( instance.Enabled );
			session.Update( camera, nearTop );
			Assert.IsTrue( instance.Enabled );
			session.Update( nearTop, null );
			Assert.IsFalse( instance.IsValid() && instance.Active );
		}
		finally
		{
			scene.Destroy();
			await MountDirectory.SetMountState( "gtav", false );
		}
	}
}
