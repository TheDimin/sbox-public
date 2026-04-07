using Sandbox;
using Sandbox.Diagnostics;
using Sims4Reader.Mesh;

namespace Mounting.Sims4;

/// <summary>
/// Converts <see cref="ResolvedMesh"/> instances (from the MODL → MLOD pipeline) into a
/// sandbox <see cref="Model"/> using the same vertex layout and coordinate transform
/// as <see cref="GeomModelBuilder"/>.
/// Supports building a single model from multiple meshes, each with its own material.
/// </summary>
public static class ModlModelBuilder
{
	static readonly Logger Log = new Logger( "Sims4-ModlModelBuilder" );
	/// <summary>
	/// Build a sandbox Model from a single resolved MODL mesh.
	/// </summary>
	/// <param name="mesh">The resolved mesh with decoded vertices and indices.</param>
	/// <param name="material">Material to apply. Pass null for default white.</param>
	/// <param name="scale">Uniform scale. Default 39.37 converts meters to Source 2 inches.</param>
	/// <param name="addCollision">Whether to add a collision mesh.</param>
	/// <param name="name">Optional model name for resource identification.</param>
	public static Model? Build(
		ResolvedMesh mesh,
		Material? material = null,
		float scale = 39.37f,
		bool addCollision = true,
		string? name = null )
	{
		return Build(
			new[] { (mesh, material) },
			scale,
			addCollision,
			name );
	}

	/// <summary>
	/// Build a sandbox Model from multiple resolved meshes, each with its own material.
	/// All meshes are combined into a single Model with separate draw calls per material.
	/// </summary>
	/// <param name="meshes">Array of (mesh, material) pairs. Each mesh gets its own material.</param>
	/// <param name="scale">Uniform scale. Default 39.37 converts meters to Source 2 inches.</param>
	/// <param name="addCollision">Whether to add a collision mesh from all geometry.</param>
	/// <param name="name">Optional model name for resource identification.</param>
	public static Model? Build(
		IReadOnlyList<(ResolvedMesh Mesh, Material? Material)> meshes,
		float scale = 39.37f,
		bool addCollision = true,
		string? name = null )
	{
		Material? defaultMaterial = null;
		try
		{
			defaultMaterial = Material.Load( "materials/default/white.vmat" );
		}
		catch ( Exception ex )
		{
			Log.Error( $"Failed to load default white material: {ex}" );
			return null;
		}

		var builder = Model.Builder;
		if ( !string.IsNullOrEmpty( name ) )
			builder.WithName( name );
		bool anyMeshAdded = false;

		// Collect all collision data across meshes
		var allCollisionPositions = new List<Vector3>();
		var allCollisionIndices = new List<int>();

		int meshIdx = 0;
		foreach ( var (mesh, material) in meshes )
		{
			meshIdx++;

			if ( mesh.Vertices.Length < 3 || mesh.Indices.Length < 3 )
				continue;

			var mat = material ?? defaultMaterial;

			var vertices = ExtractVertices( mesh.Vertices, scale );
			var indices = (int[])mesh.Indices.Clone();
			FlipWinding( indices );

			// Validate indices are within vertex buffer bounds.
			// Out-of-range indices cause GPU DMA page faults (VK_ERROR_DEVICE_LOST).
			int vertexCount = vertices.Length;
			bool hasInvalidIndices = false;
			for ( int i = 0; i < indices.Length; i++ )
			{
				if ( (uint)indices[i] >= (uint)vertexCount )
				{
					hasInvalidIndices = true;
					break;
				}
			}

			if ( hasInvalidIndices )
			{
				Log.Warning( $"Skipping mesh (name hash 0x{mesh.NameHash:X}): {indices.Length} indices, {vertexCount} vertices — out-of-range index detected" );
				continue;
			}

			// Check for NaN/Infinity in vertex positions — can crash GPU or hang driver
			bool hasBadVertex = false;
			for ( int i = 0; i < vertices.Length; i++ )
			{
				var p = vertices[i].Position;
				if ( float.IsNaN( p.x ) || float.IsNaN( p.y ) || float.IsNaN( p.z ) ||
				     float.IsInfinity( p.x ) || float.IsInfinity( p.y ) || float.IsInfinity( p.z ) )
				{
					hasBadVertex = true;
					break;
				}
			}

			if ( hasBadVertex )
			{
				Log.Warning( $"Skipping mesh #{meshIdx} '{name}' (name hash 0x{mesh.NameHash:X}): NaN/Infinity in vertex positions" );
				continue;
			}

			try
			{
				var bounds = ComputeBounds( vertices );
				var sbMesh = CreateMesh( vertices, indices, bounds, mat );
				builder.AddMesh( sbMesh );
				anyMeshAdded = true;
			}
			catch ( Exception meshEx )
			{
				Log.Error( $"Failed to create mesh #{meshIdx} '{name}' (0x{mesh.NameHash:X}): {meshEx}" );
				continue;
			}

			// Accumulate collision data
			if ( addCollision )
			{
				int baseIndex = allCollisionPositions.Count;
				for ( int i = 0; i < vertices.Length; i++ )
					allCollisionPositions.Add( vertices[i].Position );
				for ( int i = 0; i < indices.Length; i++ )
					allCollisionIndices.Add( indices[i] + baseIndex );
			}
		}

		if ( !anyMeshAdded )
			return null;

		// Add combined collision + trace mesh from all geometry
		if ( addCollision && allCollisionPositions.Count >= 3 && allCollisionIndices.Count >= 3 )
		{
			var positions = allCollisionPositions.ToArray();
			var collisionIndices = allCollisionIndices.ToArray();
			builder.AddCollisionMesh( positions, collisionIndices );
			builder.AddTraceMesh( positions, collisionIndices );

			// Add a physics body with a convex hull so the model works as an interactable prop.
			try
			{
				builder.AddBody()
					.AddHull( positions, Transform.Zero, new PhysicsBodyBuilder.HullSimplify
					{
						AngleTolerance = 0.1f,
						DistanceTolerance = 0.1f,
						Method = PhysicsBodyBuilder.SimplifyMethod.QEM
					} );
			}
			catch ( Exception e )
			{
				Log.Warning( $"Failed to create physics hull for '{name}': {e.Message}" );
			}
		}

		try
		{
			return builder.Create();
		}
		catch ( Exception ex )
		{
			Log.Error( $"Model.Builder.Create() failed for '{name}': {ex}" );
			return null;
		}
	}

	private static Mesh CreateMesh( GeomModelBuilder.GeomVertex[] vertices, int[] indices, BBox bounds, Material material )
	{
		var mesh = new Mesh( material );

		mesh.CreateVertexBuffer( vertices.Length, vertices );
		mesh.CreateIndexBuffer( indices.Length, indices );
		mesh.Bounds = bounds;

		return mesh;
	}

	private static GeomModelBuilder.GeomVertex[] ExtractVertices( Sims4Reader.Mesh.Vertex[] srcVertices, float scale )
	{
		var output = new GeomModelBuilder.GeomVertex[srcVertices.Length];

		for ( int i = 0; i < srcVertices.Length; i++ )
		{
			ref readonly var s = ref srcVertices[i];

			var vert = new GeomModelBuilder.GeomVertex { Color = Vector4.One };

			if ( s.Position != null && s.Position.Length >= 3 )
				vert.Position = SwapYZ( s.Position[0], s.Position[1], s.Position[2] ) * scale;

			if ( s.Normal != null && s.Normal.Length >= 3 )
				vert.Normal = SwapYZ( s.Normal[0], s.Normal[1], s.Normal[2] ).Normal;

			if ( s.Tangent != null && s.Tangent.Length >= 3 )
				vert.Tangent = new Vector4( s.Tangent[0], s.Tangent[2], s.Tangent[1], 1f );

			if ( s.UV != null )
			{
				if ( s.UV.Length > 0 && s.UV[0] != null && s.UV[0].Length >= 2 )
					vert.TexCoord0 = new Vector2( s.UV[0][0], s.UV[0][1] );
				if ( s.UV.Length > 1 && s.UV[1] != null && s.UV[1].Length >= 2 )
					vert.TexCoord1 = new Vector2( s.UV[1][0], s.UV[1][1] );
			}

			if ( s.HasColor )
			{
				vert.Color = new Vector4(
					(s.Color & 0xFF) / 255f,
					((s.Color >> 8) & 0xFF) / 255f,
					((s.Color >> 16) & 0xFF) / 255f,
					((s.Color >> 24) & 0xFF) / 255f );
			}

			output[i] = vert;
		}

		return output;
	}

	private static void FlipWinding( int[] indices )
	{
		for ( int i = 0; i + 2 < indices.Length; i += 3 )
			(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
	}

	private static Vector3 SwapYZ( float x, float y, float z ) => new( x, z, y );

	private static BBox ComputeBounds( GeomModelBuilder.GeomVertex[] vertices )
	{
		if ( vertices.Length == 0 )
			return default;

		var mins = new Vector3( float.MaxValue );
		var maxs = new Vector3( float.MinValue );

		for ( int i = 0; i < vertices.Length; i++ )
		{
			mins = Vector3.Min( mins, vertices[i].Position );
			maxs = Vector3.Max( maxs, vertices[i].Position );
		}

		return new BBox( mins, maxs );
	}
}
