namespace Sandbox.Mounting;

/// <summary>Supplies a scene-local streaming session from the installed GTA mount.</summary>
public interface IGtaWorldSource
{
	IGtaWorldSession StartStreaming( GameObject root );
}

public interface IGtaWorldSession : System.IDisposable
{
	void Update( Vector3 camera, Vector3? physics );
}

public sealed class GtaWorld : Component, Component.ExecuteInEditor
{
	[Property] public GameObject PhysicsFocus { get; set; }

	protected override void OnEnabled() => Scene.GetSystem<GtaWorldStreamingSystem>().Attach( this );
	protected override void OnDisabled() => Scene.GetSystem<GtaWorldStreamingSystem>()?.Detach( this );
}

public sealed class GtaWorldStreamingSystem : GameObjectSystem<GtaWorldStreamingSystem>
{
	GtaWorld world;
	BaseGameMount mount;
	IGtaWorldSession session;

	public GtaWorldStreamingSystem( Scene scene ) : base( scene ) { }

	internal void Attach( GtaWorld component )
	{
		world = component;
		if ( hook is null )
			hook = Scene.AddHook( Stage.FinishUpdate, 0, Update, nameof( GtaWorldStreamingSystem ), "Stream mounted world" );
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
		world = null;
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
		if ( current?.IsMounted != true || current is not IGtaWorldSource source ) return;
		var camera = Scene.IsEditor ? Scene.GetAllComponents<CameraComponent>().FirstOrDefault( x => x.IsSceneEditorCamera ) : Scene.Camera;
		if ( !camera.IsValid() ) return;
		var focus = world.PhysicsFocus;
		if ( !focus.IsValid() && !Scene.IsEditor )
			focus = Scene.GetAllComponents<PlayerController>().FirstOrDefault( x => !x.IsProxy )?.GameObject;
		session ??= source.StartStreaming( world.GameObject );
		session.Update( camera.WorldPosition, focus.IsValid() ? focus.WorldPosition : Scene.IsEditor ? camera.WorldPosition : null );
	}

	public override void Dispose()
	{
		Detach( world );
		base.Dispose();
	}
}
