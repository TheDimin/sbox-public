using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using NMatrix = System.Numerics.Matrix4x4;

namespace Sandbox.Gltf.DirectGpu.Validation;

/// <summary>
/// Independent image reference: literal oracle triangles use Graphics.Draw's
/// declared diagnostic Vertex layout. No glTF decoding, deformation, GPU-backend
/// transform helper, native Model or Mesh is involved. Materials are borrowed
/// from the coordinator, so this compares geometry, winding and normals rather
/// than independently validating the stock material adapter.
/// </summary>
public sealed class OrientationReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> callbackErrors = new();
	private int drawCalls;
	private bool disposed;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => callbackErrors.ToArray();

	public OrientationReferenceScene( SceneWorld world, JsonElement expected, NMatrix placement, Func<int?, Material> materialResolver, bool reverseWinding = false )
	{
		ArgumentNullException.ThrowIfNull( world );
		ArgumentNullException.ThrowIfNull( materialResolver );
		var draws = new List<(Vertex[] Vertices, Material Material)>();
		foreach ( var row in expected.GetProperty( "primitives" ).EnumerateArray() )
		{
			var positions = row.GetProperty( "engine_world_positions" );
			var normals = row.GetProperty( "engine_world_normals" );
			var indices = row.GetProperty( "indices" ).EnumerateArray().Select( value => value.GetInt32() ).ToArray();
			if ( (placement.GetDeterminant() < 0) != reverseWinding )
				for ( var triangle = 0; triangle < indices.Length; triangle += 3 )
					(indices[triangle + 1], indices[triangle + 2]) = (indices[triangle + 2], indices[triangle + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var corner = 0; corner < indices.Length; corner++ )
			{
				var index = indices[corner];
				var p = System.Numerics.Vector3.Transform( OrientationCoreTests.JsonVector( positions[index] ), placement );
				var n = OrientationGpuChecks.TransformNormal( OrientationCoreTests.JsonVector( normals[index] ), placement );
				var normal = new Vector3( n.X, n.Y, n.Z );
				var tangent = Vector3.Cross( Vector3.Up, normal );
				if ( tangent.Length < .1f ) tangent = Vector3.Cross( Vector3.Forward, normal );
				tangent = tangent.Normal;
				vertices[corner] = new Vertex( new Vector3( p.X, p.Y, p.Z ) )
				{
					Normal = normal,
					Tangent = new Vector4( tangent.x, tangent.y, tangent.z, 1 ),
					Color = Color32.White
				};
			}
			var material = materialResolver( row.GetProperty( "material" ).GetInt32() );
			OrientationCoreTests.Require( material is { IsValid: true, IsError: false }, "Independent reference material is unavailable." );
			draws.Add( (vertices, material) );
		}
		OrientationCoreTests.Require( draws.Count > 0, "Independent reference has no triangles." );
		sceneObject = new SceneCustomObject( world );
		sceneObject.Flags.IsOpaque = true;
		sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = BBox.FromPoints( draws.SelectMany( draw => draw.Vertices ).Select( vertex => vertex.Position ) );
		sceneObject.RenderOverride = _ =>
		{
			try
			{
				Interlocked.Increment( ref drawCalls );
				foreach ( var draw in draws ) Graphics.Draw( draw.Vertices.AsSpan(), draw.Vertices.Length, draw.Material );
			}
			catch ( Exception error ) { callbackErrors.Enqueue( error.ToString() ); }
		};
	}

	public void Dispose()
	{
		if ( disposed ) return;
		disposed = true;
		if ( sceneObject.IsValid() ) sceneObject.Delete();
	}
}
