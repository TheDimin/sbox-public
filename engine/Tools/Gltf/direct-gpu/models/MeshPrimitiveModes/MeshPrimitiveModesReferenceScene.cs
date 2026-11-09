using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

using NMatrix = global::System.Numerics.Matrix4x4;
using static MeshPrimitiveModesReference;

/// <summary>Independent literal point/line/triangle reference via Graphics.Draw. No glTF/producer evaluator or native Model/Mesh.</summary>
public sealed class MeshPrimitiveModesReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int draws;
	public int DrawCalls => Volatile.Read( ref draws );
	public string[] CallbackErrors => errors.ToArray();
	public MeshPrimitiveModesReferenceScene( SceneWorld world, int mode, NMatrix placement, Material material, bool corruptTopology = false )
	{
		Require( mode is >= 0 and < 7 && material is { IsValid: true, IsError: false }, "Invalid literal reference mode/material." );
		var indices = (uint[])DrawHexagonIndices[mode].Clone();
		if ( placement.GetDeterminant() < 0 && mode >= 4 ) for ( var i = 0; i < indices.Length; i += 3 )
			(indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		if ( corruptTopology )
		{
			// Points lose all but the center; lines collapse each segment; triangle
			// winding flips. No negative control changes topology class or uses quads.
			if ( mode == 0 ) indices = [0];
			else if ( mode < 4 ) for ( var i = 0; i < indices.Length; i += 2 ) indices[i + 1] = indices[i];
			else for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
		}
		var n = ExpectedTriangleNormal( placement ); var normal = new global::Vector3( n.X, n.Y, n.Z );
		var tangent = global::Vector3.Cross( global::Vector3.Up, normal ); if ( tangent.Length < .1f ) tangent = global::Vector3.Cross( global::Vector3.Forward, normal ); tangent = tangent.Normal;
		var vertices = indices.Select( index =>
		{
			var p = ExpectedPosition( mode, index, placement );
			return new Vertex( new global::Vector3( p.X, p.Y, p.Z ) ) { Normal = normal, Tangent = new global::Vector4( tangent.x, tangent.y, tangent.z, 1 ), Color = global::Color32.White };
		} ).ToArray();
		var type = mode == 0 ? Graphics.PrimitiveType.Points : mode < 4 ? Graphics.PrimitiveType.Lines : Graphics.PrimitiveType.Triangles;
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = BBox.FromPoints( vertices.Select( x => x.Position ) );
		sceneObject.RenderOverride = _ =>
		{
			try { Graphics.Draw( vertices.AsSpan(), vertices.Length, material, primitiveType: type ); Interlocked.Increment( ref draws ); }
			catch ( Exception error ) { errors.Enqueue( error.ToString() ); }
		};
	}
	public void Dispose() { if ( sceneObject.IsValid() ) { sceneObject.RenderOverride = null; sceneObject.Delete(); } }
}
