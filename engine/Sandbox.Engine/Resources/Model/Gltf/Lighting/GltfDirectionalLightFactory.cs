namespace Sandbox.Gltf.DirectGpu.Lighting;

/// <summary>
/// Native directional-light realization. The caller must supply a measured
/// conversion from glTF lux to native light color; this class deliberately has
/// no guessed default calibration. Point and spot lights require their own
/// physically validated implementations.
/// </summary>
internal sealed class GltfDirectionalLightFactory : GltfPunctualLightInstances.IFactory
{
	private readonly SceneWorld world;
	private readonly float nativeColorPerLux;

	internal GltfDirectionalLightFactory( SceneWorld world, float nativeColorPerLux )
	{
		ArgumentNullException.ThrowIfNull( world );
		if ( !float.IsFinite( nativeColorPerLux ) || nativeColorPerLux <= 0 )
			throw new ArgumentOutOfRangeException( nameof( nativeColorPerLux ) );
		this.world = world;
		this.nativeColorPerLux = nativeColorPerLux;
	}

	public GltfPunctualLightInstances.ILight Create( GltfPunctualLightPlan.Binding binding )
	{
		ArgumentNullException.ThrowIfNull( binding );
		if ( binding.Kind != GltfPunctualLightPlan.LightKind.Directional )
			throw new GltfException( "This calibrated factory supports directional lights only." );
		return new DirectionalInstance( world, binding, nativeColorPerLux );
	}

	private sealed class DirectionalInstance : GltfPunctualLightInstances.ILight
	{
		private readonly GltfPunctualLightPlan.Binding binding;
		private readonly float calibration;
		private SceneDirectionalLight light;

		internal DirectionalInstance( SceneWorld world, GltfPunctualLightPlan.Binding binding, float calibration )
		{
			this.binding = binding;
			this.calibration = calibration;
			// Start black until a preflighted state is applied.
			light = new SceneDirectionalLight( world, Rotation.Identity, Color.Black ) { ShadowsEnabled = false };
		}

		public void Apply( GltfPunctualLightPlan.State state, bool visible )
		{
			ObjectDisposedException.ThrowIf( light is null, this );
			ArgumentNullException.ThrowIfNull( state );
			if ( state.Binding != binding ) throw new GltfException( "Directional light state belongs to another binding." );
			var scale = visible && state.HasEmissionDirection ? (double)binding.Intensity * calibration : 0;
			var r = scale * binding.LinearColor.X; var g = scale * binding.LinearColor.Y; var b = scale * binding.LinearColor.Z;
			if ( !double.IsFinite( r ) || !double.IsFinite( g ) || !double.IsFinite( b ) || r > float.MaxValue || g > float.MaxValue || b > float.MaxValue )
				throw new GltfException( "Calibrated directional light exceeds the native color domain." );
			var direction = state.EmissionDirection;
			if ( state.HasEmissionDirection ) light.Rotation = Rotation.LookAt( new Vector3( direction.X, direction.Y, direction.Z ) );
			light.LightColor = new Color( (float)r, (float)g, (float)b );
		}

		public void Dispose()
		{
			var owned = light;
			light = null;
			owned?.Delete();
		}
	}
}
