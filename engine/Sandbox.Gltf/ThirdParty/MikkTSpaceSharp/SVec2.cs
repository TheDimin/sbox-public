// s&box modification: isolated namespace and internal top-level visibility; see NOTICE.md.
namespace Sandbox.Gltf.Internal.MikkTSpaceSharp
{
	internal struct SVec2
	{
		public float x;
		public float y;

		public SVec2(float xx, float yy)
		{
			x = xx;
			y = yy;
		}

		public override string ToString() => $"{x}, {y}";
	}
}
