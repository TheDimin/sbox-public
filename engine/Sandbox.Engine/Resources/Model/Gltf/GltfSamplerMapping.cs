using Sandbox.Gltf;
using Sandbox.Rendering;

namespace Sandbox;

/// <summary>
/// Converts glTF sampler data into a managed sampler description. A caller must
/// still bind it to a shader that supports the requested sampling behavior.
/// </summary>
internal static class GltfSamplerMapping
{
	/// <summary>
	/// Base-mip-only modes require a single-mip texture or view. The sampler alone
	/// does not enforce that restriction, and on Vulkan MaxLod=0 can incorrectly
	/// select magnification when glTF specifies independent min/mag filters.
	/// </summary>
	internal readonly record struct Result( SamplerState State, bool RequiresBaseMipOnly );

	/// <summary>
	/// Omitted glTF filters use the explicit caller-provided selection; no engine
	/// or glTF implementation-specific default is assumed here.
	/// </summary>
	internal static Result Map( GltfSampler sampler, int defaultMinFilter, int defaultMagFilter )
	{
		ValidateMin( defaultMinFilter, nameof( defaultMinFilter ) );
		ValidateMag( defaultMagFilter, nameof( defaultMagFilter ) );
		var min = sampler?.MinFilter ?? defaultMinFilter;
		var mag = sampler?.MagFilter ?? defaultMagFilter;
		ValidateMin( min, nameof( sampler ) );
		ValidateMag( mag, nameof( sampler ) );
		var wrapU = MapWrap( sampler?.WrapS ?? 10497 );
		var wrapV = MapWrap( sampler?.WrapT ?? 10497 );
		var minLinear = min is 9729 or 9985 or 9987;
		var magLinear = mag == 9729;
		var mipLinear = min is 9986 or 9987;
		var filter = (minLinear ? 0x10 : 0) | (magLinear ? 0x04 : 0) | (mipLinear ? 0x01 : 0);
		return new Result( new SamplerState
		{
			Filter = (FilterMode)filter,
			AddressModeU = wrapU,
			AddressModeV = wrapV
		}, min is 9728 or 9729 );
	}

	private static void ValidateMin( int value, string parameter )
	{
		if ( value is not (9728 or 9729 or 9984 or 9985 or 9986 or 9987) )
			throw new ArgumentOutOfRangeException( parameter, value, "Invalid glTF minification filter." );
	}

	private static void ValidateMag( int value, string parameter )
	{
		if ( value is not (9728 or 9729) )
			throw new ArgumentOutOfRangeException( parameter, value, "Invalid glTF magnification filter." );
	}

	private static TextureAddressMode MapWrap( int value ) => value switch
	{
		10497 => TextureAddressMode.Wrap,
		33648 => TextureAddressMode.Mirror,
		33071 => TextureAddressMode.Clamp,
		_ => throw new ArgumentOutOfRangeException( nameof( value ), value, "Invalid glTF wrapping mode." )
	};
}
