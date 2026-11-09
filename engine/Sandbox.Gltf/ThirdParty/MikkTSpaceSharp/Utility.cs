// s&box modification: isolated namespace and internal top-level visibility; see NOTICE.md.
using System;

namespace Sandbox.Gltf.Internal.MikkTSpaceSharp
{
	internal static class Utility
	{
		public static double ToRadians(double angles)
		{
			return angles * Math.PI / 180.0;
		}

		public static bool NotZero(this float fX)
		{
			return Math.Abs(fX) > 1.1754943508222875E-38;
		}
	}
}
