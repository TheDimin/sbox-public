using Sandbox.Gltf;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	internal bool HasGltfController( GltfAnimationController owner ) => ReferenceEquals( gltfController, owner ) &&
		gltfMixer is not null && ReferenceEquals( gltfAnimationResource, Model?.GltfResource );

	internal void BindGltfController( GltfAnimationController owner, IReadOnlyList<string> names, bool looping )
	{
		ThreadSafe.AssertIsMainThread();
		if ( Model?.GltfResource is null || gltfSequence is null || !ReferenceEquals( gltfAnimationInstance, GltfInstance ) )
			throw new InvalidOperationException( "The target needs an active managed glTF renderer." );
		if ( gltfController is not null && !ReferenceEquals( gltfController, owner ) )
			throw new InvalidOperationException( "Another glTF animation controller already owns this renderer." );
		if ( HasGltfController( owner ) ) return;
		var candidate = new GltfAnimationMixer( gltfAnimationResource.Metadata.Animation ) { Looping = looping };
		candidate.Select( names );
		var previous = CaptureGltfAnimationState();
		CancelPendingGltfTime();
		gltfController = owner; gltfMixer = candidate; gltfControllerModel = Model;
		try { ApplyGltfPose( rest: Scene.IsEditor && !CanUpdateInEditor() ); }
		catch { RestoreGltfAnimationState( previous ); throw; }
	}

	internal void ReleaseGltfController( GltfAnimationController owner )
	{
		ThreadSafe.AssertIsMainThread();
		if ( !ReferenceEquals( gltfController, owner ) ) return;
		var previous = CaptureGltfAnimationState();
		CancelPendingGltfTime();
		gltfController = null; gltfMixer = null; gltfControllerModel = null;
		try
		{
			if ( gltfSequence is not null && ReferenceEquals( gltfAnimationResource, Model?.GltfResource ) )
				ApplyGltfPose( rest: Scene.IsEditor && !CanUpdateInEditor() );
		}
		catch { RestoreGltfAnimationState( previous ); throw; }
	}

	private GltfAnimationMixer RequireGltfController( GltfAnimationController owner ) => HasGltfController( owner )
		? gltfMixer : throw new InvalidOperationException( "The glTF animation controller is not bound to this renderer." );
	internal float GltfControllerTime( GltfAnimationController owner ) => RequireGltfController( owner ).Time;
	internal float GltfControllerDuration( GltfAnimationController owner ) => RequireGltfController( owner ).Duration;
	internal bool GltfControllerFinished( GltfAnimationController owner ) => RequireGltfController( owner ).IsFinished;
	internal void SetGltfControllerLooping( GltfAnimationController owner, bool value ) => RequireGltfController( owner ).Looping = value;
	internal void SeekGltfController( GltfAnimationController owner, float time )
	{
		ThreadSafe.AssertIsMainThread();
		var mixer = RequireGltfController( owner );
		var previous = CaptureGltfAnimationState(); var previousTime = mixer.Time;
		mixer.Time = time; CancelPendingGltfTime();
		try { ApplyGltfPose( rest: Scene.IsEditor && !CanUpdateInEditor() ); }
		catch { mixer.Time = previousTime; RestoreGltfAnimationState( previous ); throw; }
	}
	internal void SelectGltfControllerClips( GltfAnimationController owner, IReadOnlyList<string> names, bool looping )
	{
		ThreadSafe.AssertIsMainThread();
		_ = RequireGltfController( owner );
		var candidate = new GltfAnimationMixer( gltfAnimationResource.Metadata.Animation ) { Looping = looping };
		candidate.Select( names );
		var previous = CaptureGltfAnimationState();
		CancelPendingGltfTime(); gltfMixer = candidate;
		try { ApplyGltfPose( rest: Scene.IsEditor && !CanUpdateInEditor() ); }
		catch { RestoreGltfAnimationState( previous ); throw; }
	}

	private void RebindGltfController( GltfAnimationState previous )
	{
		if ( previous.Controller is null || previous.Mixer is null || ReferenceEquals( gltfMixer, previous.Mixer ) ) return;
		// The old instance still holds its asset/document lease until the caller
		// commits replacement; all validation happens before that lease is released.
		if ( !ReferenceEquals( previous.ControllerModel, Model ) )
		{
			GltfControllerBindingLost( previous.Controller, "The target model changed; bind the controller explicitly again." );
			return;
		}
		if ( previous.Sequence is not null ) gltfSequence.Time = previous.Sequence.Time;
		var animation = gltfAnimationResource.Metadata.Animation;
		if ( !previous.Mixer.CanRebindTo( animation ) )
		{
			GltfControllerBindingLost( previous.Controller, "The selected clip aliases are missing or ambiguous after reload." );
			return;
		}
		try
		{
			var candidate = new GltfAnimationMixer( animation ) { Looping = previous.Mixer.Looping };
			candidate.Select( previous.Mixer.Names ); candidate.Time = previous.Mixer.Time;
			gltfController = previous.Controller; gltfMixer = candidate; gltfControllerModel = Model;
			ApplyGltfPose( rest: Scene.IsEditor && !CanUpdateInEditor() );
		}
		catch ( Exception error ) when ( error is ArgumentException or GltfException )
		{
			gltfController = null; gltfMixer = null; gltfControllerModel = null;
			GltfControllerBindingLost( previous.Controller, "Could not rebind glTF animations after reload: " + error.Message );
		}
	}
}
