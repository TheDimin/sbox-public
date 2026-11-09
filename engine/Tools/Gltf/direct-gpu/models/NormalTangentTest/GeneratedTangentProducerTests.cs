using NVector2 = System.Numerics.Vector2;
using NVector3 = System.Numerics.Vector3;
using NVector4 = System.Numerics.Vector4;

namespace Sandbox.Gltf.DirectGpu.NormalTangentTestValidation;

/// <summary>Runs the existing independent seam/UV oracle against the actual producer.</summary>
internal static class GeneratedTangentProducerTests
{
	public static int Run() => GeneratedTangentOracleTests.Run( input =>
		GltfGeneratedTangents.Generate( input.Positions, input.Normals, input.Uvs, input.Indices, 4,
			new NVector3( 1, 0, 0 ), new NVector3( 0, 1, 0 ) ).CornerTangents );
}
