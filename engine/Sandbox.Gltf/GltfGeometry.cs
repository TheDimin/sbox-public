using System.Numerics;

namespace Sandbox.Gltf;

/// <summary>Core glTF primitive and coordinate conversion helpers.</summary>
public static class GltfGeometry
{
	private const float MetersToInches = 1.0f / 0.0254f;
	private const float InchesToMeters = 0.0254f;

	// System.Numerics uses row vectors. A glTF point p becomes p * Basis.
	private static readonly Matrix4x4 Basis = new(
		0, MetersToInches, 0, 0,
		0, 0, MetersToInches, 0,
		MetersToInches, 0, 0, 0,
		0, 0, 0, 1 );
	private static readonly Matrix4x4 InverseBasis = new(
		0, 0, InchesToMeters, 0,
		InchesToMeters, 0, 0, 0,
		0, InchesToMeters, 0, 0,
		0, 0, 0, 1 );

	/// <summary>Expands a glTF topology into point, line or triangle lists.</summary>
	public static uint[] ExpandIndices( uint[] indices, int mode )
	{
		if ( indices is null )
			throw new GltfException( "Primitive indices cannot be null." );
		var count = indices.Length;
		var valid = mode switch
		{
			0 => count > 0,
			1 => count > 0 && count % 2 == 0,
			2 or 3 => count >= 2,
			4 => count > 0 && count % 3 == 0,
			5 or 6 => count >= 3,
			_ => false
		};
		if ( !valid )
			throw new GltfException( $"Invalid index count {count} for primitive mode {mode}." );

		if ( mode is 0 or 1 or 4 )
			return (uint[])indices.Clone();

		var outputLength = mode switch
		{
			2 => (long)count * 2,
			3 => (long)(count - 1) * 2,
			_ => (long)(count - 2) * 3
		};
		if ( outputLength > Array.MaxLength )
			throw new GltfException( "Expanded primitive index count exceeds the supported array length." );
		var result = new uint[(int)outputLength];
		var output = 0;

		if ( mode is 2 or 3 )
		{
			for ( var i = 0; i < count - 1; i++ )
			{
				result[output++] = indices[i];
				result[output++] = indices[i + 1];
			}
			if ( mode == 2 )
			{
				result[output++] = indices[count - 1];
				result[output] = indices[0];
			}
			return result;
		}

		for ( var i = 0; i < count - 2; i++ )
		{
			if ( mode == 6 )
			{
				result[output++] = indices[0];
				result[output++] = indices[i + 1];
			}
			else if ( (i & 1) == 0 )
			{
				result[output++] = indices[i];
				result[output++] = indices[i + 1];
			}
			else
			{
				result[output++] = indices[i + 1];
				result[output++] = indices[i];
			}
			result[output++] = indices[i + 2];
		}
		return result;
	}

	/// <summary>Converts glTF meters and axes to s&amp;box inches and axes.</summary>
	public static Vector3 ConvertPosition( Vector3 value ) => new( value.Z * MetersToInches, value.X * MetersToInches, value.Y * MetersToInches );

	/// <summary>Converts a direction between coordinate bases without changing its magnitude.</summary>
	public static Vector3 ConvertDirection( Vector3 value ) => new( value.Z, value.X, value.Y );

	/// <summary>Converts a glTF row-vector transform to an equivalent s&amp;box row-vector transform.</summary>
	public static Matrix4x4 ConvertTransform( Matrix4x4 value ) => InverseBasis * value * Basis;
}
