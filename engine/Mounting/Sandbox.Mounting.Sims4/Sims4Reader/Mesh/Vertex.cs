namespace Sims4Reader.Mesh;

/// <summary>
/// A decoded vertex with all supported attribute channels.
/// Arrays are null if the attribute is not present in the vertex format.
/// </summary>
public struct Vertex
{
    /// <summary>XYZ position.</summary>
    public float[]? Position;

    /// <summary>XYZ normal vector.</summary>
    public float[]? Normal;

    /// <summary>UV texture coordinates. Each element is a float[2] channel.</summary>
    public float[][]? UV;

    /// <summary>Bone indices for skinning (raw bytes, typically 4).</summary>
    public byte[]? BlendIndices;

    /// <summary>Bone weights for skinning.</summary>
    public float[]? BlendWeights;

    /// <summary>XYZ tangent vector (plus optional handedness as 4th component).</summary>
    public float[]? Tangent;

    /// <summary>Vertex color as a packed ARGB uint.</summary>
    public uint Color;

    /// <summary>True if vertex color was present in the format.</summary>
    public bool HasColor;
}
