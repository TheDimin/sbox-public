using Sandbox.Gltf;
using Sandbox.Gltf.DirectGpu;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox;

public sealed partial class SkinnedModelRenderer
{
	private GltfAnimationSequence gltfSequence;
	private GltfManagedModelResource gltfAnimationResource;
	private GltfGpuInstance gltfAnimationInstance;
	private GltfPose gltfPose;
	private double gltfPendingDelta;
	private bool gltfPendingUpdate;
	private bool gltfPendingBindPose;
	private bool gltfShowingRestPose;
	private GltfAnimationController gltfController;
	private GltfAnimationMixer gltfMixer;
	private Model gltfControllerModel;
	private sealed record GltfAnimationState( GltfAnimationSequence Sequence, GltfManagedModelResource Resource,
		GltfGpuInstance Instance, GltfPose Pose, double Delta, bool Pending, bool BindPose, bool ShowingRest, Dictionary<int, GltfMorphFade> MorphOverrides,
		GltfAnimationController Controller, GltfAnimationMixer Mixer, Model ControllerModel );
	private GltfAnimationState CaptureGltfAnimationState() => new( gltfSequence, gltfAnimationResource,
		gltfAnimationInstance, gltfPose, gltfPendingDelta, gltfPendingUpdate, gltfPendingBindPose, gltfShowingRestPose, gltfMorphOverrides, gltfController, gltfMixer, gltfControllerModel );
	internal override object CaptureGltfReloadAnimation() => CaptureGltfAnimationState();
	internal override void RestoreGltfReloadAnimation( object state )
	{
		RestoreGltfAnimationState( (GltfAnimationState)state );
		// Model cache invalidation recreates Bone wrappers. Keep the same proxy
		// objects, but rekey them to this restored generation's bone wrappers.
		if ( Model is null || boneToGameObject.Count == 0 ) return;
		var previous = boneToGameObject.ToArray();
		var bones = Model.Bones.AllBones;
		boneToGameObject.Clear();
		foreach ( var (bone, gameObject) in previous )
			if ( (uint)bone.Index < (uint)bones.Count ) boneToGameObject[bones[bone.Index]] = gameObject;
	}
	internal override void CommitGltfReloadAnimation( bool applied, SceneObject previous )
	{
		if ( !applied ) return;
		ReleaseVoiceMorphs( previous as SceneModel );
		BuildBoneHierarchy();
		UpdateGltfBoneObjects();
	}
	private void RestoreGltfAnimationState( GltfAnimationState state )
	{
		gltfSequence = state.Sequence; gltfAnimationResource = state.Resource;
		gltfAnimationInstance = state.Instance; gltfPose = state.Pose;
		gltfPendingDelta = state.Delta; gltfPendingUpdate = state.Pending; gltfPendingBindPose = state.BindPose;
		gltfShowingRestPose = state.ShowingRest;
		gltfMorphOverrides = state.MorphOverrides;
		gltfController = state.Controller; gltfMixer = state.Mixer; gltfControllerModel = state.ControllerModel;
		_boneSnapshotFrame = -1;
	}

	protected override SceneObject CreateSceneObject( Model model )
	{
		if ( model?.GltfResource is not null ) return base.CreateSceneObject( model );
		var sceneModel = new SceneModel( Scene.SceneWorld, model, WorldTransform ) { UseAnimGraph = UseAnimGraph, PlaybackRate = PlaybackRate };
		if ( AnimationGraph is not null ) sceneModel.AnimationGraph = AnimationGraph;
		return sceneModel;
	}

	private void InitializeGltfAnimation()
	{
		ThreadSafe.AssertIsMainThread();
		if ( AnimationGraph is not null || StoredParameterCount > 0 )
			throw new NotSupportedException( "Native animation graphs and graph parameters are not implemented for managed glTF renderers." );
		if ( BoneMergeTarget is not null )
			throw new NotSupportedException( "Bone merging is not implemented for managed glTF renderers." );
		if ( ReferenceEquals( gltfAnimationInstance, GltfInstance ) && ReferenceEquals( gltfAnimationResource, Model?.GltfResource ) ) return;
		ResetGltfAnimation();
		gltfAnimationResource = Model.GltfResource;
		gltfAnimationInstance = GltfInstance;
		gltfSequence = new GltfAnimationSequence( this, gltfAnimationResource.Metadata.Animation );
		ApplyGltfPose( rest: true );
	}

	private void ResetGltfAnimation()
	{
		lock ( this )
		{
			gltfPendingDelta = 0; gltfPendingUpdate = false; gltfPendingBindPose = false;
			gltfSequence = null; gltfPose = null; gltfAnimationResource = null; gltfAnimationInstance = null;
			gltfController = null; gltfMixer = null; gltfControllerModel = null;
			gltfShowingRestPose = false;
			gltfMorphOverrides = new();
			_boneSnapshotFrame = -1; _boneSnapshot = null; _boneSnapshotPrevious = null;
		}
	}

	private void CancelPendingGltfTime()
	{
		// An absolute public seek supersedes work scheduled for its old phase.
		// Updates queued after the seek still contribute their entire signed delta.
		lock ( this ) { gltfPendingDelta = 0; gltfPendingUpdate = false; }
	}

	/// <summary>The parallel animation pass records work only; no document read or GPU upload.</summary>
	private void QueueGltfAnimation( double delta, bool previewBindPose )
	{
		lock ( this )
		{
			if ( gltfSequence is null || !ReferenceEquals( gltfAnimationInstance, GltfInstance ) ) return;
			if ( !double.IsFinite( delta ) || !double.IsFinite( gltfPendingDelta + delta ) ) throw new ArgumentOutOfRangeException( nameof( delta ) );
			gltfPendingDelta += delta;
			gltfPendingUpdate = true;
			gltfPendingBindPose = previewBindPose || (Scene.IsEditor && !CanUpdateInEditor());
		}
	}

	private void FinishGltfAnimation()
	{
		ThreadSafe.AssertIsMainThread();
		lock ( this )
		{
			if ( gltfSequence is null || GltfInstance is null || !ReferenceEquals( gltfAnimationInstance, GltfInstance ) || !ReferenceEquals( gltfAnimationResource, Model?.GltfResource ) ) return;
			if ( gltfPendingUpdate )
			{
				var delta = gltfPendingDelta; var rest = gltfPendingBindPose;
				gltfPendingDelta = 0; gltfPendingUpdate = false;
				if ( rest )
				{
					if ( !gltfShowingRestPose ) ApplyGltfPose( rest: true );
				}
				else
				{
					var changed = gltfMixer is null ? gltfSequence.Advance( delta, PlaybackRate ) : gltfMixer.Advance( delta, gltfController.PlaybackRate );
					changed |= AdvanceGltfMorphs( delta );
					if ( changed || gltfShowingRestPose ) ApplyGltfPose();
				}
			}
			UpdateGltfBoneObjects();
		}
	}

	private void ApplyGltfPose( bool rest = false )
	{
		ThreadSafe.AssertIsMainThread();
		if ( gltfSequence is null || !ReferenceEquals( gltfAnimationInstance, GltfInstance ) ) return;
		var animation = gltfAnimationResource.Metadata.Animation;
		var pose = rest ? animation.EvaluatePose( null, 0 ) : gltfMixer is not null ? gltfMixer.EvaluatePose() : animation.EvaluatePose( gltfSequence.Clip, gltfSequence.Time );
		if ( !rest ) pose = ApplyGltfMorphs( pose );
		gltfAnimationInstance.SetPose( pose );
		gltfPose = pose;
		gltfShowingRestPose = rest;
		_boneSnapshotFrame = -1;
		UpdateGltfBoneObjects();
	}

	private bool TryGetGltfBoneTransform( BoneCollection.Bone bone, bool world, out Transform transform )
	{
		transform = global::Transform.Zero;
		if ( gltfPose is null || bone is null || Model?.GltfResource is not { } resource ||
			(uint)bone.Index >= (uint)resource.Metadata.Bones.Count || !ReferenceEquals( Model.Bones.AllBones[bone.Index], bone ) ) return false;
		var matrix = GltfGeometry.ConvertTransform( gltfPose.GetWorldTransform( resource.Metadata.Bones[bone.Index].Node ) );
		if ( world ) matrix *= (NMatrix)Matrix.FromTransform( WorldTransform );
		transform = GltfManagedModelMetadata.ToPublicTransform( matrix );
		return true;
	}

	private void UpdateGltfBoneObjects()
	{
		if ( gltfPose is null || IsGltfChangeStaged ) return;
		foreach ( var (bone, gameObject) in boneToGameObject )
		{
			if ( !gameObject.IsValid() || gameObject.Flags.Contains( GameObjectFlags.ProceduralBone ) || gameObject.Flags.Contains( GameObjectFlags.Absolute ) ) continue;
			if ( !TryGetGltfBoneTransform( bone, false, out var transform ) ) continue;
			if ( bone.Parent is { } parent && TryGetGltfBoneTransform( parent, false, out var parentTransform ) ) transform = parentTransform.ToLocal( transform );
			gameObject.LocalTransform = transform;
		}
	}

	private sealed class GltfAnimationSequence : AnimationSequence
	{
		private readonly SkinnedModelRenderer renderer;
		private readonly GltfModelAnimation animation;
		private readonly GltfPlaybackClock clock = new();
		private string name;
		internal int? Clip { get; private set; }
		internal GltfAnimationSequence( SkinnedModelRenderer renderer, GltfModelAnimation animation ) => (this.renderer, this.animation) = (renderer, animation);
		public override float Duration => (float)clock.Duration;
		public override bool IsFinished => !Clip.HasValue || clock.IsFinished;
		public override IReadOnlyList<string> SequenceNames => animation.SequenceNames;
		public override string Name
		{
			get => name ?? "";
			set
			{
				ThreadSafe.AssertIsMainThread();
				if ( name == value ) return;
				GltfModelAnimation.Clip? selected = null;
				if ( !string.IsNullOrEmpty( value ) )
				{
					foreach ( var clip in animation.Clips ) if ( clip.Name == value ) { selected = clip; break; }
					if ( selected is null ) throw new ArgumentException( $"Unknown glTF animation '{value}'.", nameof( value ) );
				}
				renderer.CancelPendingGltfTime();
				name = value; Clip = selected?.Index; clock.Reset( selected?.Duration ?? 0 ); renderer.ApplyGltfPose();
			}
		}
		public override float Time
		{
			get => (float)clock.Time;
			set { ThreadSafe.AssertIsMainThread(); clock.Seek( value ); renderer.CancelPendingGltfTime(); renderer.ApplyGltfPose(); }
		}
		public override float TimeNormalized
		{
			get => Duration > 0 ? Time / Duration : 0;
			set
			{
				if ( !float.IsFinite( value ) ) throw new ArgumentOutOfRangeException( nameof( value ) );
				Time = Math.Clamp( value, 0, 1 ) * Duration;
			}
		}
		internal override bool Looping { set => clock.Looping = value; }
		internal override bool Blending { set { if ( value ) throw new NotSupportedException( "Blending glTF sequences is not implemented." ); } }
		internal bool Advance( double delta, double rate ) => Clip.HasValue && clock.Advance( delta, rate );
	}
}
