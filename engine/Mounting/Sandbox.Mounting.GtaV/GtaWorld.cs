using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Sandbox;

namespace Sandbox.Mounting;

/// <summary>Supplies a scene-local streaming session from the installed GTA mount.</summary>
public interface IGtaWorldSource
{
	IGtaWorldSession StartStreaming( GameObject root );
}

public interface IGtaWorldSession : System.IDisposable
{
	bool IsCollisionReady( Vector3 worldPosition );
	void SetCameraView( Vector3 forward ) { }
	void SetCollisionTrajectories( IReadOnlyList<(Vector3 Position, Vector3 Velocity)> trajectories ) { }
	void SetWorldVariants( string active, IReadOnlyList<GtaWorldVariant> variants ) { }
	void Update( Vector3? camera, IReadOnlyList<Vector3> physics, IReadOnlyList<Vector3> interactions );
	void Update( Vector3 camera, Vector3? physics ) => Update( camera, physics.HasValue ? [physics.Value] : [camera], physics.HasValue ? [physics.Value] : [] );
}

public sealed class GtaWorldVariant
{
	[Property] public string Name { get; set; }
	[Property] public List<string> Maps { get; set; } = new();
	[Property] public List<string> Collision { get; set; } = new();
}

public sealed class GtaWorld : Component, Component.ExecuteInEditor
{
	[Property] public GameObject PhysicsFocus { get; set; }
	[Property] public string ActiveVariant { get; set; }
	[Property] public List<GtaWorldVariant> Variants { get; set; } = new();

	protected override void OnEnabled() => GtaWorldStreamingSystem.ForScene( Scene ).Attach( this );
	protected override void OnDisabled() => GtaWorldStreamingSystem.ForScene( Scene ).Detach( this );
}

public sealed class GtaWorldStreamingSystem
{
	[ConVar( "gtav_lod_debug", Help = "Show resident and visible GTA placements by camera distance and authored LOD." )]
	public static bool LodDebug { get; set; }

	[ConVar( "gtav_lod_scale", Min = 0.1f, Max = 10f, Help = "Multiplier for authored GTA render LOD distances." )]
	public static float LodScale { get; set; } = 1f;
	GtaWorld world;
	BaseGameMount mount;
	IGtaWorldSession session;
	Task<BaseGameMount> mountRequest;

	// A mount can be loaded after the scene has discovered its game object systems.
	static readonly ConditionalWeakTable<Scene, GtaWorldStreamingSystem> Systems = new();
	readonly Scene scene;

	GtaWorldStreamingSystem( Scene scene ) => this.scene = scene;

	internal static GtaWorldStreamingSystem ForScene( Scene scene ) =>
		Systems.GetValue( scene, static key => new GtaWorldStreamingSystem( key ) );

	/// <summary>True when nearby world collision packets have finished activation at a requested focus.</summary>
	public bool IsCollisionReady( Vector3 worldPosition ) => session?.IsCollisionReady( worldPosition ) == true;

	internal void Attach( GtaWorld component )
	{
		if ( world != component && world is not null ) Detach( world );
		world = component;
		if ( hook is null )
			hook = scene.AddHook( GameObjectSystem.Stage.FinishUpdate, 0, Update, nameof( GtaWorldStreamingSystem ), "Stream mounted world" );
	}

	System.IDisposable hook;

	internal void Detach( GtaWorld component )
	{
		if ( world != component ) return;
		hook?.Dispose();
		hook = null;
		session?.Dispose();
		session = null;
		mount = null;
		mountRequest = null;
		world = null;
	}

	readonly List<Vector3> physics = new();
	readonly List<Vector3> interactions = new();
	readonly List<(Vector3 Position, Vector3 Velocity)> trajectories = new();
	readonly HashSet<GameObject> seen = new();

	void AddFocus( GameObject obj, Rigidbody body, bool interaction )
	{
		if ( interaction ) interactions.Add( obj.WorldPosition );
		if ( !seen.Add( obj ) ) return;
		physics.Add( obj.WorldPosition );
		if ( body.IsValid() && body.Velocity.LengthSquared > 1 )
		{
			trajectories.Add( (obj.WorldPosition, body.Velocity) );
			// Half a second of look-ahead, capped to keep consecutive search areas overlapping.
			physics.Add( obj.WorldPosition + (body.Velocity * 0.5f).ClampLength( 3000 ) );
		}
	}

	CameraComponent GetEditorCamera()
	{
		CameraComponent fallback = null;
		foreach ( var camera in scene.GetAllComponents<CameraComponent>() )
		{
			if ( camera.CustomSize is not { x: > 0, y: > 0 } ) continue;
			if ( camera.GameObject.Tags.Has( "selected_scene_viewport" ) ) return camera;
			if ( camera.GameObject.Name == "editor_camera" ) fallback ??= camera;
		}
		return scene.IsEditor ? fallback : scene.Camera;
	}

	void Update()
	{
		var current = Directory.Get( "gtav" );
		if ( current != mount || current?.IsMounted != true )
		{
			session?.Dispose();
			session = null;
			mount = current;
		}
		if ( current?.IsMounted != true )
		{
			if ( current?.IsInstalled == true && Networking.IsActive && !Networking.IsHost && mountRequest is null )
				mountRequest = Directory.Mount( "gtav" );
			return;
		}
		mountRequest = null;
		if ( current is not IGtaWorldSource source ) return;
		var camera = Application.IsDedicatedServer ? null : Application.IsEditor
			? GetEditorCamera() : scene.Camera;
		physics.Clear();
		interactions.Clear();
		trajectories.Clear();
		seen.Clear();
		bool host = !scene.IsEditor && (Networking.IsHost || Application.IsDedicatedServer);
		if ( world.PhysicsFocus.IsValid() )
			AddFocus( world.PhysicsFocus, world.PhysicsFocus.GetComponent<Rigidbody>(), true );
		if ( !scene.IsEditor && (host || !world.PhysicsFocus.IsValid()) )
			foreach ( var player in scene.GetAllComponents<PlayerController>() )
				if ( host || !player.IsProxy ) AddFocus( player.GameObject, player.GetComponent<Rigidbody>(), true );
		if ( host )
			foreach ( var body in scene.GetAllComponents<Rigidbody>() )
				if ( body.MotionEnabled && !body.Sleeping && !body.IsProxy ) AddFocus( body.GameObject, body, false );
		if ( physics.Count == 0 && camera.IsValid() && scene.IsEditor )
			AddFocus( camera.GameObject, null, true );
		session ??= source.StartStreaming( world.GameObject );
		session.SetCameraView( camera.IsValid() ? camera.WorldRotation.Forward : default );
		session.SetCollisionTrajectories( trajectories );
		session.SetWorldVariants( world.ActiveVariant, world.Variants );
		session.Update( camera.IsValid() ? camera.WorldPosition : null, physics, interactions );
	}
}
