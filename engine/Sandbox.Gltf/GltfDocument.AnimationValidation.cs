namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private void ValidateNonVertexAccessor( AccessorLayout accessor, string description )
	{
		if ( !accessor.Json.TryGetProperty( "bufferView", out _ ) ) return;
		var view = ElementAt( "bufferViews", GltfJson.Int( accessor.Json, "bufferView" ) );
		if ( view.TryGetProperty( "byteStride", out _ ) || view.TryGetProperty( "target", out _ ) )
			throw new GltfException( $"{description} buffer views cannot define vertex byteStride or GPU target." );
	}

	private float[] ReadAnimationInput( AccessorLayout accessor, int index )
	{
		ValidateNonVertexAccessor( accessor, "Animation input" );
		var minimum = SceneFloatArray( GltfJson.Required( accessor.Json, "min" ), 1, "animation input minimum" )[0];
		var maximum = SceneFloatArray( GltfJson.Required( accessor.Json, "max" ), 1, "animation input maximum" )[0];
		if ( minimum > maximum ) throw new GltfException( "Animation input minimum exceeds its maximum." );
		var times = ReadFloatAccessor( index );
		// Parse bounds as float32 before comparing to decoded float32 values.
		// Descriptor-only accessors without a base or sparse source are expressly
		// allowed to declare arbitrary representable bounds by the core spec.
		if ( (accessor.Json.TryGetProperty( "bufferView", out _ ) || accessor.Json.TryGetProperty( "sparse", out _ )) &&
			(minimum != times.Min() || maximum != times.Max()) )
			throw new GltfException( "Animation input bounds do not match its float32 key times." );
		return times;
	}
}
