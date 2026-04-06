using System.Runtime.InteropServices;
using Sandbox;

namespace Mounting.Sims4;

/// <summary>
/// Shared vertex layout used by model builders for Sims 4 mesh data.
/// </summary>
public static class GeomModelBuilder
{
	[StructLayout( LayoutKind.Sequential )]
	public struct GeomVertex
	{
		public static readonly VertexAttribute[] Layout =
		{
			new VertexAttribute( VertexAttributeType.Position, VertexAttributeFormat.Float32, 3 ),
			new VertexAttribute( VertexAttributeType.Normal, VertexAttributeFormat.Float32, 3 ),
			new VertexAttribute( VertexAttributeType.Tangent, VertexAttributeFormat.Float32, 4 ),
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 0 ),
			new VertexAttribute( VertexAttributeType.TexCoord, VertexAttributeFormat.Float32, 2, 1 ),
			new VertexAttribute( VertexAttributeType.Color, VertexAttributeFormat.Float32, 4 ),
		};

		[VertexLayout.Position]
		public Vector3 Position;
		[VertexLayout.Normal]
		public Vector3 Normal;
		[VertexLayout.Tangent]
		public Vector4 Tangent;
		[VertexLayout.TexCoord( 0 )]
		public Vector2 TexCoord0;
		[VertexLayout.TexCoord( 1 )]
		public Vector2 TexCoord1;
		[VertexLayout.Color]
		public Vector4 Color;
	}
}
