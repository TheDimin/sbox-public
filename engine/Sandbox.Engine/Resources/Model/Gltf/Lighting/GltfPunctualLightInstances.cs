using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.Lighting;

/// <summary>
/// Per-renderer ownership and pose propagation for punctual lights. A factory
/// must supply an explicitly calibrated native mapping; this class makes no
/// assumptions about native intensity, finite radius or spot falloff.
/// </summary>
internal sealed class GltfPunctualLightInstances : IDisposable
{
	internal interface ILight : IDisposable
	{
		void Apply( GltfPunctualLightPlan.State state, bool visible );
	}

	internal interface IFactory
	{
		ILight Create( GltfPunctualLightPlan.Binding binding );
	}

	private readonly GltfPunctualLightPlan plan;
	private readonly List<ILight> lights = new();
	private bool disposed;

	private GltfPunctualLightInstances( GltfPunctualLightPlan plan ) => this.plan = plan;

	internal static GltfPunctualLightInstances Create( GltfPunctualLightPlan plan, IFactory factory, GltfPose pose, Matrix4x4 placement, IReadOnlyList<bool> visibility )
	{
		ArgumentNullException.ThrowIfNull( plan ); ArgumentNullException.ThrowIfNull( factory );
		var states = Preflight( plan, pose, placement, visibility );
		var owner = new GltfPunctualLightInstances( plan );
		try
		{
			foreach ( var state in states )
			{
				var light = factory.Create( state.Binding ) ?? throw new GltfException( "Punctual light factory returned no light." );
				owner.lights.Add( light );
				light.Apply( state, visibility[state.Binding.Node] );
			}
			return owner;
		}
		catch
		{
			// Preserve the creation error while still attempting every rollback.
			try { owner.Dispose(); } catch { }
			throw;
		}
	}

	internal void Update( GltfPose pose, Matrix4x4 placement, IReadOnlyList<bool> visibility )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		var states = Preflight( plan, pose, placement, visibility );
		for ( var i = 0; i < states.Count; i++ ) lights[i].Apply( states[i], visibility[states[i].Binding.Node] );
	}

	private static IReadOnlyList<GltfPunctualLightPlan.State> Preflight( GltfPunctualLightPlan plan, GltfPose pose, Matrix4x4 placement, IReadOnlyList<bool> visibility )
	{
		ArgumentNullException.ThrowIfNull( visibility );
		var states = plan.Evaluate( pose, placement );
		foreach ( var state in states )
			if ( (uint)state.Binding.Node >= (uint)visibility.Count ) throw new GltfException( "Punctual light visibility does not cover its node." );
		return states;
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		List<Exception> errors = null;
		foreach ( var light in lights )
		{
			try { light.Dispose(); }
			catch ( Exception error ) { (errors ??= new()).Add( error ); }
		}
		lights.Clear();
		if ( errors is not null ) throw new AggregateException( "Punctual light cleanup failed.", errors );
	}
}
