using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.NegativeScaleValidation;
using static NegativeScaleReference;

/// <summary>Independent literal geometry. No reader/evaluator, compute buffer or native Model/Mesh.</summary>
public sealed class NegativeScaleReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int drawCalls;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => errors.ToArray();
	public NegativeScaleReferenceScene( SceneWorld world, JsonElement expected, Matrix4x4 placement, Func<int?,Material> resolve, bool reverseSingleSided = false )
	{
		Require( Determinant( placement ) != 0,"Image reference requires defined normals; singular-collapse evidence uses positions/readback." );
		var draws = new List<(Vertex[] Vertices,Material Material)>();
		foreach ( var row in expected.GetProperty( "rows" ).EnumerateArray() )
		{
			var material = row.GetProperty( "material" ).GetInt32(); var indices = row.GetProperty( "indices" ).EnumerateArray().Select( x => x.GetInt32() ).ToArray();
			var reverse = row.GetProperty( "determinant" ).GetDouble() * Determinant( placement ) < 0;
			if ( reverseSingleSided && material <= 2 ) reverse = !reverse;
			if ( reverse ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1],indices[i + 2]) = (indices[i + 2],indices[i + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var corner = 0; corner < vertices.Length; corner++ )
			{
				var source = indices[corner]; var p = Position( row,source,placement ); var n = Normal( row,source,placement ); var normal = new global::Vector3( (float)n[0],(float)n[1],(float)n[2] );
				var tangent = global::Vector3.Cross( global::Vector3.Up,normal ); if ( tangent.Length < .1f ) tangent = global::Vector3.Cross( global::Vector3.Forward,normal ); tangent = tangent.Normal;
				var uv = row.GetProperty( "attributes" ).TryGetProperty( "TEXCOORD_0",out var accessor ) ? Numbers( expected.GetProperty( "accessors" )[accessor.GetInt32()].GetProperty( "values" )[source] ) : new double[] { 0,0 };
				vertices[corner] = new Vertex( new global::Vector3( (float)p[0],(float)p[1],(float)p[2] ) ) { Normal = normal,Tangent = new global::Vector4( tangent.x,tangent.y,tangent.z,1 ),TexCoord0 = new global::Vector4( (float)uv[0],(float)uv[1],0,0 ),Color = Color32.White };
			}
			var resolved = resolve( material ); Require( resolved is { IsValid: true,IsError: false },"Independent geometry reference material unavailable." ); draws.Add( (vertices,resolved) );
		}
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = BBox.FromPoints( draws.SelectMany( r => r.Vertices ).Select( v => v.Position ) );
		sceneObject.RenderOverride = _ => { try { Interlocked.Increment( ref drawCalls ); foreach ( var draw in draws ) Graphics.Draw( draw.Vertices.AsSpan(),draw.Vertices.Length,draw.Material ); } catch ( Exception e ) { errors.Enqueue( e.ToString() ); } };
	}
	public void Dispose() { if ( sceneObject.IsValid() ) sceneObject.Delete(); }
}
