using System.Numerics;

namespace Sandbox.Gltf;

public sealed partial class GltfDocument
{
	private Vector3 EvaluateSkinnedPosition( GltfPrimitive geometry, GltfSkin skin, int vertex, Vector3 position, Func<int, Matrix4x4> jointWorld )
	{
		var result = Vector3.Zero;
		var used = new HashSet<int>();
		var allQuantized = true;
		long normalizedIntegerSum = 0;
		for ( var set = 0; geometry.Attributes.TryGetValue( "JOINTS_" + set, out var jointsAccessor ); set++ )
		{
			if ( !geometry.Attributes.TryGetValue( "WEIGHTS_" + set, out var weightsAccessor ) ) throw new GltfException( "Missing skinning weight set." );
			var joints = ReadFloatAccessor( jointsAccessor );
			var weights = ReadFloatAccessor( weightsAccessor );
			var layout = Accessor( weightsAccessor );
			var quantized = layout.Normalized && layout.ComponentType is 5121 or 5123;
			allQuantized &= quantized;
			var denominator = layout.ComponentType == 5121 ? 255 : 65535;
			for ( var influence = 0; influence < 4; influence++ )
			{
				var offset = vertex * 4 + influence;
				var joint = (int)joints[offset];
				var weight = weights[offset];
				if ( (uint)joint >= (uint)skin.Joints.Length ) throw new GltfException( "Skinning joint index is out of range." );
				if ( weight < 0 || !float.IsFinite( weight ) ) throw new GltfException( "Skinning weights must be finite and non-negative." );
				if ( quantized )
				{
					// At most 16 source bits are present, so float32 dequantization
					// retains enough precision to recover the exact integer uniquely.
					var integer = (long)Math.Round( (double)weight * denominator );
					normalizedIntegerSum += layout.ComponentType == 5121 ? integer * 257 : integer;
				}
				if ( weight == 0 ) continue;
				if ( !used.Add( joint ) ) throw new GltfException( "A joint cannot have multiple nonzero influences for the same vertex." );
				var world = skin.InverseBindMatrices[joint] * jointWorld( skin.Joints[joint] );
				result += Vector3.Transform( position, world ) * weight;
			}
		}
		// A float-weight set makes normalization a recommendation; do not reject
		// or silently renormalize those authored values using an arbitrary epsilon.
		if ( allQuantized && normalizedIntegerSum != 65535 )
			throw new GltfException( "Quantized skinning weights must sum exactly to one across all influence sets." );
		return result;
	}
}
