using Sandbox.Engine;
using Sandbox.Gltf.DirectGpu;

namespace Sandbox;

public sealed partial class Model
{
	internal GltfManagedModelResource GltfResource { get; private set; }
	private bool gltfReloading;

	private Model( GltfManagedModelResource resource, string name )
	{
		GltfResource = resource ?? throw new ArgumentNullException( nameof( resource ) );
		Name = name;
		RegisterWeakResourceId( name );
	}

	internal static Model FromGltfResource( GltfManagedModelResource resource, string name ) => new( resource, name );

	/// <returns>Post-commit notification/cleanup errors, or null. Pre-commit failures throw.</returns>
	internal AggregateException ReplaceGltfResource( GltfManagedModelResource replacement ) =>
		ReplaceGltfResources( [(this, replacement)] );

	private sealed class GltfModelReload
	{
		internal Model Model;
		internal GltfManagedModelResource Previous, Replacement;
		internal readonly List<ModelRenderer> Prepared = new();
		internal List<(Scene Scene, IHasModel Component)> Participants;
	}

	/// <summary>
	/// Publish every cached scene view of one source as one generation change.
	/// All previous owners survive until every renderer has applied and validated;
	/// a pre-commit failure restores the whole group before any owner is released.
	/// </summary>
	internal static AggregateException ReplaceGltfResources( IReadOnlyList<(Model Model, GltfManagedModelResource Resource)> replacements )
	{
		ThreadSafe.AssertIsMainThread();
		ArgumentNullException.ThrowIfNull( replacements );
		var transactions = new List<GltfModelReload>();
		var unique = new HashSet<Model>( ReferenceEqualityComparer.Instance );
		var uniqueResources = new HashSet<GltfManagedModelResource>( ReferenceEqualityComparer.Instance );
		foreach ( var (model, replacement) in replacements )
		{
			ArgumentNullException.ThrowIfNull( model ); ArgumentNullException.ThrowIfNull( replacement );
			if ( !replacement.IsValid ) throw new ObjectDisposedException( nameof( replacement ) );
			if ( !uniqueResources.Add( replacement ) ) throw new ArgumentException( "Each model must own a separate prepared generation.", nameof( replacements ) );
			if ( !unique.Add( model ) ) throw new ArgumentException( "A model can occur only once in a glTF reload group.", nameof( replacements ) );
			if ( model.gltfReloading ) throw new InvalidOperationException( "A glTF model reload is already in progress." );
			var previous = model.GltfResource ?? throw new InvalidOperationException( "The model is not backed by glTF." );
			if ( ReferenceEquals( previous, replacement ) ) throw new ArgumentException( "The replacement must be a new resource generation.", nameof( replacements ) );
			transactions.Add( new GltfModelReload { Model = model, Previous = previous, Replacement = replacement } );
		}
		foreach ( var transaction in transactions ) transaction.Model.gltfReloading = true;
		try
		{
			try
			{
				foreach ( var transaction in transactions )
				{
					transaction.Participants = transaction.Model.GltfReloadParticipants();
					foreach ( var renderer in transaction.Participants.Select( entry => entry.Component ).OfType<ModelRenderer>() )
					{
						using var scope = renderer.Scene.Push();
						renderer.PrepareGltfReload( transaction.Replacement ); transaction.Prepared.Add( renderer );
					}
				}
				foreach ( var transaction in transactions )
				{
					transaction.Model.GltfResource = transaction.Replacement;
					transaction.Model.ClearReloadedCaches();
					foreach ( var renderer in transaction.Prepared ) { using var scope = renderer.Scene.Push(); renderer.ApplyGltfReload(); }
				}
				foreach ( var transaction in transactions )
				{
					var identities = new HashSet<IHasModel>( transaction.Participants.Select( entry => entry.Component ), ReferenceEqualityComparer.Instance );
					if ( !identities.SetEquals( transaction.Model.GltfReloadParticipants().Select( entry => entry.Component ) ) )
						throw new InvalidOperationException( "The model's reload participants changed during preparation." );
					foreach ( var renderer in transaction.Prepared ) { using var scope = renderer.Scene.Push(); renderer.ValidateGltfReload(); }
				}
				foreach ( var transaction in transactions )
					foreach ( var renderer in transaction.Prepared ) { using var scope = renderer.Scene.Push(); renderer.PublishGltfReload(); }
			}
			catch ( Exception error )
			{
				foreach ( var transaction in transactions ) transaction.Model.GltfResource = transaction.Previous;
				var releases = new List<Action>();
				foreach ( var transaction in transactions.AsEnumerable().Reverse() )
				{
					releases.Add( transaction.Model.ClearReloadedCaches );
					releases.AddRange( transaction.Prepared.AsEnumerable().Reverse().Select<ModelRenderer, Action>( renderer => () => { using var scope = renderer.Scene.Push(); renderer.RollbackGltfReload(); } ) );
					releases.Add( transaction.Replacement.ReleaseOwner );
				}
				GltfGpuCleanup.Rollback( error, releases );
				throw;
			}
			// Group commit precedes external notifications and all previous-owner release.
			var actions = new List<Action>();
			foreach ( var transaction in transactions )
			{
				actions.AddRange( transaction.Prepared.Select<ModelRenderer, Action>( renderer => () => { using var scope = renderer.Scene.Push(); renderer.FinishGltfReload(); } ) );
				actions.Add( transaction.Previous.ReleaseOwner );
			}
			// Every scene view must finish before the first external observer runs.
			// A callback may inspect any other view or its animation/controller state.
			foreach ( var transaction in transactions )
			{
				actions.Add( () => IToolsDll.Current?.RunEvent( "model.reload", transaction.Model ) );
				foreach ( var entry in transaction.Participants )
				{
					var captured = entry;
					actions.Add( () =>
					{
						if ( captured.Component is not Component component || !component.IsValid || !component.Active || !ReferenceEquals( captured.Component.Model, transaction.Model ) ) return;
						using var scope = captured.Scene.Push();
						if ( captured.Component is ModelRenderer renderer ) renderer.OnModelChanged();
						else captured.Component.OnModelReloaded();
					} );
				}
			}
			try { GltfGpuCleanup.ReleaseAll( actions ); return null; }
			catch ( AggregateException error ) { return error; }
		}
		finally { foreach ( var transaction in transactions ) transaction.Model.gltfReloading = false; }
	}

	private List<(Scene Scene, IHasModel Component)> GltfReloadParticipants()
	{
		var result = new List<(Scene, IHasModel)>();
		foreach ( var scene in Scene.AllActiveIncludingEditor )
		{
			using var scope = scene.Push();
			foreach ( var component in scene.GetAllComponents<IHasModel>() )
				if ( ReferenceEquals( component.Model, this ) ) result.Add( (scene, component) );
		}
		return result;
	}

	private bool DestroyGltfResource()
	{
		if ( GltfResource is not { } resource ) return false;
		using var scope = new GlobalContext.GlobalContextScope( resource.Context );
		try { resource.ReleaseOwner(); }
		finally { base.Destroy(); }
		return true;
	}
}
