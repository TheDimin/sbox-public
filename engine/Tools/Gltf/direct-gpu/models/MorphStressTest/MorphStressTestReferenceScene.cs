using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.MorphStressTestValidation;

using Vector3 = System.Numerics.Vector3;
using static MorphStressTestReference;

/// <summary>Independent source geometry drawn through Graphics.Draw. No native Model/Mesh or producer evaluator.</summary>
public sealed class MorphStressTestReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int draws;
	public int DrawCalls => Volatile.Read( ref draws );
	public string[] CallbackErrors => errors.ToArray();
	public MorphStressTestReferenceScene( MorphStressTestReference r, SceneWorld world, double[] weights, Matrix4x4 placement, Func<int?, Material> materialResolver )
	{
		var batches = new List<(Vertex[] Vertices, Material Material)>(); var bounds = new List<global::Vector3>();
		for ( var primitive = 0; primitive < 2; primitive++ )
		{
			var indices = r.Indices( primitive ); if ( placement.GetDeterminant() < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var i = 0; i < vertices.Length; i++ )
			{
				var source = checked((int)indices[i]); var p = r.Engine( primitive, source, weights, placement ); var n = r.Engine( primitive, source, weights, placement, true );
				Require( n is not null, "The lit reference needs defined independent authored normals at every referenced vertex." );
				var normal = new Vector3( (float)n[0], (float)n[1], (float)n[2] ); var t = Vector3.Normalize( Vector3.Cross( normal, Math.Abs( normal.X ) < .7 ? Vector3.UnitX : Vector3.UnitY ) );
				var uv0 = Uv( 0 ); var uv1 = Uv( 1 ); var position = new global::Vector3( (float)p[0], (float)p[1], (float)p[2] ); bounds.Add( position );
				vertices[i] = new Vertex( position ) { Normal = new global::Vector3( normal.X, normal.Y, normal.Z ), Tangent = new global::Vector4( t.X, t.Y, t.Z, placement.GetDeterminant() < 0 ? -1 : 1 ),
					Color = global::Color32.White, TexCoord0 = new global::Vector4( (float)uv0[0], (float)uv0[1], 0, 0 ), TexCoord1 = new global::Vector4( (float)uv1[0], (float)uv1[1], 0, 0 ) };
				double[] Uv( int set ) => r.AccessorValues( r.Primitive( primitive ).GetProperty( "attributes" ).GetProperty( "TEXCOORD_" + set ).GetInt32() )[source];
			}
			var material = materialResolver( primitive ); Require( material is { IsValid: true, IsError: false }, "Independent reference material unavailable." ); batches.Add( (vertices, material) );
		}
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false; sceneObject.Bounds = BBox.FromPoints( bounds );
		sceneObject.RenderOverride = _ =>
		{
			try
			{
				foreach ( var batch in batches ) for ( var start = 0; start < batch.Vertices.Length; start += 2046 )
				{
					var count = Math.Min( 2046, batch.Vertices.Length - start ); Graphics.Draw( batch.Vertices.AsSpan( start, count ), count, batch.Material, primitiveType: Graphics.PrimitiveType.Triangles ); Interlocked.Increment( ref draws );
				}
			}
			catch ( Exception error ) { if ( errors.Count < 16 ) errors.Enqueue( error.ToString() ); }
		};
	}
	public void Dispose() { sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); }
}
