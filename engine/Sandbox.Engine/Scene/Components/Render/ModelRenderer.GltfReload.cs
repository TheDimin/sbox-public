using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

public partial class ModelRenderer
{
	private sealed class GltfReloadPlan
	{
		internal GltfManagedModelResource Replacement, PreviousResource;
		internal GltfGpuInstance Candidate, PreviousInstance;
		internal SceneObject CandidateObject, PreviousObject;
		internal object Variant, VariantState, Animation;
		internal bool Applied, PreviousRendering;
		internal GltfAnimationController LostController;
		internal string LostReason;
	}
	private GltfReloadPlan gltfReloadPlan;
	protected bool IsGltfChangeStaged => gltfReloadPlan is not null;
	internal virtual object CaptureGltfReloadAnimation() => null;
	internal virtual void RestoreGltfReloadAnimation( object state ) { }
	internal virtual void CommitGltfReloadAnimation( bool applied, SceneObject previous ) { }

	internal void PrepareGltfReload( GltfManagedModelResource replacement ) => PrepareGltfChange( Model, replacement );
	private void PrepareGltfChange( Model target, GltfManagedModelResource replacement )
	{
		if ( gltfReloadPlan is not null ) throw new InvalidOperationException( "A glTF renderer reload is already in progress." );
		var plan = new GltfReloadPlan { Replacement = replacement, PreviousResource = gltfInstanceResource,
			PreviousInstance = gltfInstance, PreviousObject = _sceneObject, Animation = CaptureGltfReloadAnimation(),
			VariantState = CaptureGltfVariantState(), Variant = replacement is null ? null : PrepareGltfVariantReload( replacement ) };
		try
		{
			if ( Active && _sceneObject.IsValid() )
			{
				plan.PreviousRendering = _sceneObject.RenderingEnabled;
				if ( replacement is not null )
				{
					plan.Candidate = replacement.CreateInstance( Scene.SceneWorld, initiallyVisible: false );
					plan.Candidate.Placement = Matrix.FromTransform( Transform.InterpolatedWorld );
					plan.CandidateObject = plan.Candidate.SceneObject;
					ApplyGltfVariantReload( plan.Variant, plan.Candidate );
				}
				else
				{
					plan.CandidateObject = CreateSceneObject( target ?? Model.Load( "models/dev/box.vmdl" ) );
					plan.CandidateObject.RenderingEnabled = false;
				}
			}
			else if ( plan.Variant is not null ) ApplyGltfVariantReload( plan.Variant, null );
			gltfReloadPlan = plan;
		}
		catch ( Exception error )
		{
			GltfGpuCleanup.Rollback( error, [() => { if ( plan.Candidate is not null ) plan.Candidate.Dispose(); else plan.CandidateObject?.Delete(); }] );
			throw;
		}
	}

	internal void ValidateGltfReload()
	{
		var plan = gltfReloadPlan ?? throw new InvalidOperationException( "Missing glTF reload plan." );
		if ( plan.CandidateObject is not null && !plan.Applied ) throw new InvalidOperationException( "A participating glTF renderer did not apply its prepared instance." );
	}
	internal void ApplyGltfReload()
	{
		var plan = gltfReloadPlan ?? throw new InvalidOperationException( "Missing glTF reload plan." );
		if ( plan.CandidateObject is not null ) UpdateObject();
	}
	internal void PublishGltfReload()
	{
		var plan = gltfReloadPlan ?? throw new InvalidOperationException( "Missing glTF reload plan." );
		if ( plan.Variant is not null ) CommitGltfVariantReload( plan.Variant ); else ClearGltfVariantSelection();
		if ( plan.Applied )
		{
			plan.PreviousObject.RenderingEnabled = false;
			plan.CandidateObject.RenderingEnabled = plan.PreviousRendering && (Model ?? Model.Load( "models/dev/box.vmdl" )).HasRenderMeshes();
		}
	}
	internal void FinishGltfReload()
	{
		var plan = gltfReloadPlan ?? throw new InvalidOperationException( "Missing glTF reload plan." );
		gltfReloadPlan = null;
		GltfGpuCleanup.ReleaseAll( [
			() => CommitGltfReloadAnimation( plan.Applied, plan.PreviousObject ),
			() => plan.LostController?.BindingLost( plan.LostReason ),
			() => { if ( plan.Applied ) { if ( plan.PreviousInstance is not null ) plan.PreviousInstance.Dispose(); else plan.PreviousObject?.Delete(); } }
		] );
	}
	internal void RollbackGltfReload()
	{
		var plan = gltfReloadPlan;
		if ( plan is null ) return;
		gltfReloadPlan = null;
		try
		{
			RestoreGltfVariantState( plan.VariantState );
			RestoreGltfReloadAnimation( plan.Animation );
			if ( plan.Applied )
			{
				gltfInstance = plan.PreviousInstance; gltfInstanceResource = plan.PreviousResource; _sceneObject = plan.PreviousObject;
				if ( plan.PreviousObject.IsValid() ) { base.OnSceneObjectCreated( plan.PreviousObject ); plan.PreviousObject.RenderingEnabled = plan.PreviousRendering; }
			}
		}
		finally { if ( plan.Candidate is not null ) plan.Candidate.Dispose(); else plan.CandidateObject?.Delete(); }
	}
	protected void GltfControllerBindingLost( GltfAnimationController controller, string reason )
	{
		if ( gltfReloadPlan is { } plan ) { plan.LostController = controller; plan.LostReason = reason; }
		else controller?.BindingLost( reason );
	}
}
