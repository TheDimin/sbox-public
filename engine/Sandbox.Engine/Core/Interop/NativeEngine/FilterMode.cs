
namespace Sandbox.Rendering;

/// <summary>
/// Represents filtering modes for texture sampling in the rendering pipeline.
/// </summary>
[Expose]
public enum FilterMode : int
{
	/// <summary>
	/// Uses the nearest texel without interpolation.
	/// Fastest but lowest visual quality.
	/// </summary>
	Point = 0x0,

	/// <summary>Point minification and magnification, with linear interpolation between mip levels.</summary>
	MinMagPointMipLinear = 0x01,

	/// <summary>Point minification and mip selection, with linear magnification.</summary>
	MinPointMagLinearMipPoint = 0x04,

	/// <summary>Point minification, with linear magnification and mip interpolation.</summary>
	MinPointMagMipLinear = 0x05,

	/// <summary>Linear minification, with point magnification and mip selection.</summary>
	MinLinearMagMipPoint = 0x10,

	/// <summary>Linear minification and mip interpolation, with point magnification.</summary>
	MinLinearMagPointMipLinear = 0x11,

	/// <summary>
	/// Interpolates between the four nearest texels in the same mip level.
	/// Smoother than point sampling but does not blend between mip levels.
	/// </summary>
	Bilinear = 0x14,

	/// <summary>
	/// Bilinear sampling with smooth transitions between mipmap levels.
	/// Provides better visual quality across distances.
	/// </summary>
	Trilinear = 0x15,

	/// <summary>
	/// Enhances texture detail on steep viewing angles.
	/// Best visual quality, higher performance cost.
	/// </summary>
	Anisotropic = 0x55,
}
