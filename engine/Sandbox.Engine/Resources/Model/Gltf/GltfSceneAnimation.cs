using Sandbox.Gltf;

namespace Sandbox;

/// <summary>Independent exact glTF clip playback behind the existing sequence API.</summary>
internal sealed class GltfSceneAnimation : AnimationSequence
{
	private readonly GltfModelAnimation model;
	private GltfModelAnimation.Clip? clip;
	private readonly GltfPlaybackClock clock = new() { Looping = true };

	internal GltfSceneAnimation( GltfModelAnimation model )
	{
		ArgumentNullException.ThrowIfNull( model );
		this.model = model;
	}

	public override float Duration => clip?.Duration ?? 0;
	public override IReadOnlyList<string> SequenceNames => model.SequenceNames;
	public override bool IsFinished => !clip.HasValue || clock.IsFinished;

	public override string Name
	{
		get => clip?.Name ?? "";
		set
		{
			if ( Name == (value ?? "") ) return;
			if ( string.IsNullOrEmpty( value ) ) clip = null;
			else
			{
				var index = -1;
				for ( var i = 0; i < model.Clips.Count; i++ )
					if ( model.Clips[i].Name == value ) { index = i; break; }
				if ( index < 0 ) throw new ArgumentException( $"glTF animation '{value}' does not exist on this model.", nameof( value ) );
				clip = model.Clips[index];
			}
			clock.Reset( Duration );
		}
	}

	public override float Time
	{
		get => (float)clock.Time;
		set
		{
			if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ), "Animation time must be finite." );
			clock.Seek( value );
		}
	}

	public override float TimeNormalized
	{
		get => Duration > 0 ? Time / Duration : 0;
		set
		{
			if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ), "Animation cycle must be finite." );
			Time = Math.Clamp( value, 0, 1 ) * Duration;
		}
	}

	internal override bool Looping { set => clock.Looping = value; }
	internal override bool Blending
	{
		set
		{
			if ( value ) throw new NotSupportedException( "glTF sequence transition blending is not implemented. Exact clip playback supports Blending=false." );
		}
	}

	internal void Advance( float delta, float playbackRate )
	{
		clock.Advance( delta, playbackRate );
	}

	internal GltfPose EvaluatePose() => model.EvaluatePose( clip?.Index, Time );
	internal GltfPose EvaluateBindPose() => model.EvaluatePose( null, 0 );
	internal bool IsFor( GltfModelAnimation metadata ) => ReferenceEquals( model, metadata );
}

public sealed partial class SceneModel
{
	private GltfSceneAnimation gltfSequence;
	// Native scene objects own native handles, but their managed Model lookup is
	// weak. Keep the exact wrapper that owns the document, material leases and
	// declaration metadata alive for this glTF instance's lifetime.
	private Model gltfModelOwner;

	private GltfSceneAnimation GetGltfSequence()
	{
		var model = Model;
		var metadata = model is { IsValid: true } ? model.GltfAnimation : null;
		gltfModelOwner = metadata is not null ? model : null;
		if ( gltfModelOwner is null ) return null;
		if ( gltfSequence is null || !gltfSequence.IsFor( metadata ) ) gltfSequence = new GltfSceneAnimation( metadata );
		return gltfSequence;
	}

	private GltfPose PrepareGltfAnimation( float delta, bool bindPose )
	{
		var sequence = GetGltfSequence();
		if ( sequence is null ) return null;
		var metadata = Model.GltfAnimation;
		if ( metadata.Rig is null ) throw new InvalidOperationException( "The loaded glTF model has no runtime rig." );
		if ( !bindPose ) sequence.Advance( delta, PlaybackRate );
		var pose = bindPose ? sequence.EvaluateBindPose() : sequence.EvaluatePose();
		metadata.Rig.ApplyMorphWeights( this, pose );
		return pose;
	}

	private void ApplyGltfAnimation( GltfPose pose )
	{
		if ( pose is null ) return;
		gltfModelOwner.GltfAnimation.Rig.ApplyPose( this, pose );
	}
}
