namespace Sandbox.Gltf;

/// <summary>Evaluates glTF animation keyframes without engine or renderer dependencies.</summary>
public static class GltfAnimationSampler
{
	/// <summary>
	/// Samples a channel in its original glTF coordinate space. Cubic values are laid out
	/// as an in-tangent, value and out-tangent for each key, with one element per component.
	/// </summary>
	public static float[] Sample( float[] times, float[] values, int components, string interpolation, float time, bool rotation = false )
	{
		Validate( times, values, components, interpolation, time, rotation );

		var cubic = interpolation == "CUBICSPLINE";
		var keyStride = cubic ? components * 3 : components;
		var valueOffset = cubic ? components : 0;
		var result = new float[components];

		// An exact key is used as authored, including a quaternion's original sign.
		var index = Array.BinarySearch( times, time );
		if ( index >= 0 )
		{
			Array.Copy( values, index * keyStride + valueOffset, result, 0, components );
			return result;
		}

		var next = ~index;
		if ( next == 0 || next == times.Length )
		{
			var key = next == 0 ? 0 : times.Length - 1;
			Array.Copy( values, key * keyStride + valueOffset, result, 0, components );
			return result;
		}

		var previous = next - 1;
		var previousOffset = previous * keyStride + valueOffset;
		var nextOffset = next * keyStride + valueOffset;
		if ( interpolation == "STEP" )
		{
			Array.Copy( values, previousOffset, result, 0, components );
			return result;
		}

		var duration = (double)times[next] - times[previous];
		var t = ((double)time - times[previous]) / duration;
		if ( interpolation == "LINEAR" && rotation )
		{
			InterpolateRotation( values, previousOffset, nextOffset, t, result );
			return result;
		}

		if ( interpolation == "LINEAR" )
		{
			for ( var i = 0; i < components; i++ )
				result[i] = ToFiniteFloat( (1.0 - t) * values[previousOffset + i] + t * values[nextOffset + i] );
			return result;
		}

		var t2 = t * t;
		var t3 = t2 * t;
		var h00 = 2 * t3 - 3 * t2 + 1;
		var h10 = t3 - 2 * t2 + t;
		var h01 = -2 * t3 + 3 * t2;
		var h11 = t3 - t2;
		Span<double> cubicRotation = stackalloc double[4];
		for ( var i = 0; i < components; i++ )
		{
			var value = h00 * values[previousOffset + i]
				+ h10 * duration * values[previousOffset + components + i]
				+ h01 * values[nextOffset + i]
				+ h11 * duration * values[nextOffset - components + i];
			if ( rotation )
				cubicRotation[i] = value;
			else
				result[i] = ToFiniteFloat( value );
		}

		if ( rotation )
		{
			var length = Math.Sqrt( cubicRotation[0] * cubicRotation[0] + cubicRotation[1] * cubicRotation[1]
				+ cubicRotation[2] * cubicRotation[2] + cubicRotation[3] * cubicRotation[3] );
			if ( length == 0 || !double.IsFinite( length ) )
				throw new GltfException( "Animation rotation evaluated to an invalid quaternion." );
			for ( var i = 0; i < 4; i++ )
				result[i] = (float)(cubicRotation[i] / length);
		}
		return result;
	}

	private static void Validate( float[] times, float[] values, int components, string interpolation, float time, bool rotation )
	{
		if ( times is null || values is null || times.Length == 0 || components <= 0 )
			throw new GltfException( "Animation sampler needs keys and a positive component count." );
		if ( !float.IsFinite( time ) )
			throw new GltfException( "Animation sample time must be finite." );
		if ( interpolation is not ("STEP" or "LINEAR" or "CUBICSPLINE") )
			throw new GltfException( $"Unsupported animation interpolation '{interpolation}'." );
		if ( interpolation == "CUBICSPLINE" && times.Length < 2 )
			throw new GltfException( "Cubic spline interpolation requires at least two keys." );
		if ( rotation && components != 4 )
			throw new GltfException( "Rotation channels require four quaternion components." );

		var multiplier = interpolation == "CUBICSPLINE" ? 3L : 1L;
		var valuesPerKey = components * multiplier;
		if ( values.LongLength / valuesPerKey != times.LongLength || values.LongLength % valuesPerKey != 0 )
			throw new GltfException( "Animation sampler output length does not match its key count and components." );

		for ( var i = 0; i < times.Length; i++ )
		{
			if ( !float.IsFinite( times[i] ) || times[i] < 0 || (i > 0 && times[i] <= times[i - 1]) )
				throw new GltfException( "Animation key times must be finite, non-negative and strictly increasing." );
		}
		foreach ( var value in values )
		{
			if ( !float.IsFinite( value ) )
				throw new GltfException( "Animation key values and tangents must be finite." );
		}

		if ( rotation )
		{
			var stride = interpolation == "CUBICSPLINE" ? 12 : 4;
			var offset = interpolation == "CUBICSPLINE" ? 4 : 0;
			for ( var i = 0; i < times.Length; i++ )
			{
				var start = i * stride + offset;
				if ( values[start] == 0 && values[start + 1] == 0 && values[start + 2] == 0 && values[start + 3] == 0 )
					throw new GltfException( "Animation rotation key cannot be a zero quaternion." );
			}
		}
	}

	private static void InterpolateRotation( float[] values, int a, int b, double t, float[] result )
	{
		var aLength = QuaternionLength( values, a );
		var bLength = QuaternionLength( values, b );
		var dot = 0.0;
		for ( var i = 0; i < 4; i++ )
			dot += values[a + i] / aLength * (values[b + i] / bLength);

		var sign = dot < 0 ? -1.0 : 1.0;
		dot = Math.Clamp( Math.Abs( dot ), 0.0, 1.0 );
		double aWeight;
		double bWeight;
		if ( dot > 0.9995 )
		{
			aWeight = 1.0 - t;
			bWeight = t * sign;
		}
		else
		{
			var angle = Math.Acos( dot );
			var denominator = Math.Sin( angle );
			aWeight = Math.Sin( (1.0 - t) * angle ) / denominator;
			bWeight = Math.Sin( t * angle ) / denominator * sign;
		}

		// Normalize before the only float rounding. An intermediate float
		// quaternion can add angular error through translated joint chains.
		Span<double> interpolated = stackalloc double[4];
		var squaredLength = 0.0;
		for ( var i = 0; i < 4; i++ )
		{
			interpolated[i] = aWeight * values[a + i] / aLength + bWeight * values[b + i] / bLength;
			squaredLength += interpolated[i] * interpolated[i];
		}
		var length = Math.Sqrt( squaredLength );
		if ( length == 0 || !double.IsFinite( length ) ) throw new GltfException( "Animation rotation evaluated to an invalid quaternion." );
		for ( var i = 0; i < 4; i++ ) result[i] = (float)(interpolated[i] / length);
	}

	private static double QuaternionLength( float[] values, int offset )
	{
		var sum = 0.0;
		for ( var i = 0; i < 4; i++ )
			sum += (double)values[offset + i] * values[offset + i];
		return Math.Sqrt( sum );
	}

	private static float ToFiniteFloat( double value )
	{
		var result = (float)value;
		if ( !float.IsFinite( result ) )
			throw new GltfException( "Animation interpolation produced a non-finite value." );
		return result;
	}
}
