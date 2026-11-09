using System.Numerics;

namespace Sandbox.Gltf.DirectGpu.Lighting;

/// <summary>
/// An explicitly opted-in native directional presentation for a selected glTF
/// scene. The caller retains the document until disposal and owns the chosen
/// native-color multiplier. This is not full KHR_lights_punctual support or a
/// physical calibration of the engine's light response.
/// </summary>
internal sealed class GltfDirectionalLightBinding : IDisposable
{
	private readonly GltfPunctualLightInstances instances;
	private bool disposed;
	internal IReadOnlyList<GltfPunctualLightPlan.Binding> AuthoredLights { get; }

	private GltfDirectionalLightBinding( GltfPunctualLightPlan plan, GltfPunctualLightInstances instances )
	{
		AuthoredLights = plan.Bindings;
		this.instances = instances;
	}

	internal static GltfDirectionalLightBinding Create( GltfDocument document, int? selectedScene, SceneWorld world,
		float nativeColorPerLux, GltfPose pose, Matrix4x4 placement, IReadOnlyList<bool> visibility )
	{
		ArgumentNullException.ThrowIfNull( document );
		var plan = GltfPunctualLightPlan.CreateForScene( document, selectedScene, allowUnselected: true );
		if ( plan.UsesUnsupportedOptionalFallback )
			throw new GltfException( "Directional native presentation requires the punctual-light extension to be selected while reading the document." );
		if ( plan.Bindings.Any( x => x.Kind != GltfPunctualLightPlan.LightKind.Directional ) )
			throw new GltfException( "The opted-in native directional presentation does not support point or spot lights." );
		var factory = new GltfDirectionalLightFactory( world, nativeColorPerLux );
		var instances = GltfPunctualLightInstances.Create( plan, factory, pose, placement, visibility );
		return new( plan, instances );
	}

	internal void Update( GltfPose pose, Matrix4x4 placement, IReadOnlyList<bool> visibility )
	{
		ObjectDisposedException.ThrowIf( disposed, this );
		instances.Update( pose, placement, visibility );
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		instances.Dispose();
	}
}
