using System;
using Sandbox;

namespace Editor;

/// <summary>Bounded caller-owned collision mesh arrays, without unbounded public GetVertices/GetIndices allocations.</summary>
public static class ModelPhysicsMeshReadback
{
	public const int MaxVertices = 100000;
	public const int MaxIndices = 300000;
	public enum Status { Success, WrongThread, InvalidMesh, InvalidLimits, LimitExceeded, InvalidData }
	public sealed record Snapshot( Vector3[] Vertices, uint[] Indices, byte[] Materials, string[] Surfaces );
	public static Status Read( PhysicsGroupDescription.BodyPart.MeshPart shape, int maxVertices, int maxIndices, out Snapshot result )
	{
		result = null;
		if ( !ThreadSafe.IsMainThread ) return Status.WrongThread;
		if ( shape == null || shape.mesh.IsNull ) return Status.InvalidMesh;
		if ( maxVertices < 1 || maxVertices > MaxVertices || maxIndices < 3 || maxIndices > MaxIndices ) return Status.InvalidLimits;
		var mesh = shape.mesh; int nv = mesh.GetVertexCount(), nt = mesh.GetTriangleCount(), nm = mesh.GetMaterialCount();
		if ( nv < 1 || nt < 1 || nm < 0 ) return Status.InvalidData;
		if ( nv > maxVertices || nt > maxIndices / 3 || nm > nt ) return Status.LimitExceeded;
		var surfaces = shape.Surfaces;
		if ( surfaces != null && surfaces.Length > 256 ) return Status.LimitExceeded;
		if ( nm != 0 && surfaces == null ) return Status.InvalidData;
		var names = new string[surfaces?.Length ?? 0];
		for ( int i = 0; i < names.Length; i++ )
		{
			string name = surfaces[i]?.ResourcePath;
			if ( string.IsNullOrWhiteSpace( name ) || name.Length > 1024 ) return Status.InvalidData;
			names[i] = name;
		}
		var vertices = new Vector3[nv]; var indices = new uint[nt * 3]; var materials = nm == 0 ? null : new byte[nt];
		for ( int i = 0; i < nv; i++ )
		{
			var v = mesh.GetVertex( i );
			if ( !float.IsFinite( v.x ) || !float.IsFinite( v.y ) || !float.IsFinite( v.z ) ) return Status.InvalidData;
			vertices[i] = v;
		}
		for ( int i = 0; i < nt; i++ )
		{
			mesh.GetTriangleIndices( i, out var a, out var b, out var c );
			if ( a < 0 || b < 0 || c < 0 || a >= nv || b >= nv || c >= nv ) return Status.InvalidData;
			indices[3 * i] = (uint)a; indices[3 * i + 1] = (uint)b; indices[3 * i + 2] = (uint)c;
			if ( materials != null )
			{
				int material = mesh.GetTriangleMaterial( i );
				if ( material < 0 || material > 255 || material >= names.Length ) return Status.InvalidData;
				materials[i] = (byte)material;
			}
		}
		result = new Snapshot( vertices, indices, materials, names ); return Status.Success;
	}
}
