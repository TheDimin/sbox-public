namespace Sandbox;

/// <summary>Plays non-overlapping glTF sequence aliases together on one authored-time clock.</summary>
[Expose, Title( "glTF Animation Controller" ), Category( "Animation" )]
public sealed class GltfAnimationController : Component, Component.ExecuteInEditor
{
	private SkinnedModelRenderer target;
	private List<string> clips = new();
	private string[] attempted = [];
	private float playbackRate = 1;
	private bool looping = true;
	private bool waitingForRenderer;

	[Property]
	public SkinnedModelRenderer Target
	{
		get => target.IsValid() ? target : null;
		set
		{
			if ( ReferenceEquals( target, value ) ) return;
			if ( target.IsValid() ) target.ReleaseGltfController( this );
			target = value;
			waitingForRenderer = false;
			ValidationError = null;
			if ( Active ) TryBind();
		}
	}

	/// <summary>SequenceNames aliases; the list is serialized with the component.</summary>
	[Property]
	public List<string> Clips { get => clips; set => clips = value ?? new(); }
	[Property]
	public bool Looping
	{
		get => looping;
		set { looping = value; if ( IsBound ) target.SetGltfControllerLooping( this, value ); }
	}
	[Property]
	public float PlaybackRate
	{
		get => playbackRate;
		set { if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) ); playbackRate = value; }
	}
	[Hide] public bool IsBound => target.IsValid() && target.HasGltfController( this );
	[Hide] public string ValidationError { get; private set; }
	[Hide] public float Duration => IsBound ? target.GltfControllerDuration( this ) : 0;
	[Hide] public bool IsFinished => !IsBound || target.GltfControllerFinished( this );
	[Hide] public float Time
	{
		get => IsBound ? target.GltfControllerTime( this ) : 0;
		set
		{
			if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
			if ( !IsBound ) throw new InvalidOperationException( "The glTF animation controller is not bound." );
			target.SeekGltfController( this, value );
		}
	}

	/// <summary>Atomically selects aliases; duplicate and overlapping targets are rejected.</summary>
	public void SetClips( IReadOnlyList<string> value )
	{
		ArgumentNullException.ThrowIfNull( value );
		var copy = value.ToArray();
		if ( IsBound ) target.SelectGltfControllerClips( this, copy, looping );
		clips = new List<string>( copy ); attempted = copy; ValidationError = null;
	}

	protected override void OnEnabled() { base.OnEnabled(); TryBind(); }
	protected override void OnDisabled()
	{
		if ( target.IsValid() ) target.ReleaseGltfController( this );
		base.OnDisabled();
	}
	protected override void OnUpdate()
	{
		base.OnUpdate();
		if ( !SameClips() )
		{
			var next = clips.ToArray(); attempted = next;
			try { if ( IsBound ) target.SelectGltfControllerClips( this, next, looping ); ValidationError = null; }
			catch ( Exception error ) when ( error is ArgumentException or Sandbox.Gltf.GltfException ) { ValidationError = error.Message; }
		}
		if ( !IsBound && (ValidationError is null || waitingForRenderer && target.IsValid() && target.GltfInstance is not null) ) TryBind();
	}
	private bool SameClips()
	{
		if ( attempted.Length != clips.Count ) return false;
		for ( var i = 0; i < attempted.Length; i++ ) if ( attempted[i] != clips[i] ) return false;
		return true;
	}
	private void TryBind()
	{
		waitingForRenderer = false;
		if ( !target.IsValid() ) { ValidationError = "A target renderer is required."; return; }
		if ( target.Model?.GltfResource is not null && target.GltfInstance is null )
		{
			waitingForRenderer = true; ValidationError = "Waiting for the target renderer to become active."; return;
		}
		try { target.BindGltfController( this, clips, looping ); attempted = clips.ToArray(); ValidationError = null; }
		catch ( Exception error ) when ( error is ArgumentException or InvalidOperationException or Sandbox.Gltf.GltfException ) { ValidationError = error.Message; }
	}
	internal void BindingLost( string reason ) { waitingForRenderer = false; ValidationError = reason; }
}
