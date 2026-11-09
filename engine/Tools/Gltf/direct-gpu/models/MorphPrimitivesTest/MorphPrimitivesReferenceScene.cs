using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.MorphPrimitivesValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector = System.Numerics.Vector3;
using static MorphPrimitivesReference;

/// <summary>Independent expected geometry via native Graphics.Draw; creates zero native Model/Mesh resources.</summary>
public sealed class MorphPrimitivesReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int drawCalls;
	private bool disposed;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => errors.ToArray();
	public MorphPrimitivesReferenceScene( Form form, SceneWorld world, float weight, NMatrix placement, Func<int?, Material> materialForPrimitive )
	{
		ArgumentNullException.ThrowIfNull( form ); ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( materialForPrimitive );
		var draws = new List<(Vertex[] Vertices, Material Material)>(); var minimum = new NVector( float.PositiveInfinity ); var maximum = new NVector( float.NegativeInfinity );
		for ( var p = 0; p < 2; p++ )
		{
			var indices = Indices( form, p ); if ( placement.GetDeterminant() < 0 ) for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var corner = 0; corner < vertices.Length; corner++ )
			{
				var source = checked((int)indices[corner]); var position = EnginePosition( form, p, source, weight, placement ); var normal = EngineNormal( form, p, source, placement );
				// This source has no normal texture or authored tangent. Tangent
				// direction is not a material-fidelity assertion for this model.
				var tangent = NVector.Normalize( NVector.Cross( normal, MathF.Abs( normal.X ) < .7f ? NVector.UnitX : NVector.UnitY ) );
				vertices[corner] = new Vertex( new global::Vector3( position.X, position.Y, position.Z ) )
				{
					Normal = new global::Vector3( normal.X, normal.Y, normal.Z ), Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, placement.GetDeterminant() < 0 ? -1 : 1 ),
					TexCoord0 = new global::Vector4( Uv( form, p, source, 0 ), Uv( form, p, source, 1 ), 0, 0 ), Color = new global::Color32( 255, 255, 255, 255 )
				};
				minimum = NVector.Min( minimum, position ); maximum = NVector.Max( maximum, position );
			}
			var material = materialForPrimitive( p ); Require( material is { IsValid: true, IsError: false }, "Missing independent source/diagnostic material for primitive " + p ); draws.Add( (vertices, material) );
		}
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		minimum -= new NVector( .001f ); maximum += new NVector( .001f ); sceneObject.Bounds = new global::BBox( new global::Vector3( minimum.X, minimum.Y, minimum.Z ), new global::Vector3( maximum.X, maximum.Y, maximum.Z ) );
		sceneObject.RenderOverride = _ =>
		{
			try { foreach ( var draw in draws ) { Graphics.Draw( draw.Vertices.AsSpan(), draw.Vertices.Length, draw.Material, primitiveType: Graphics.PrimitiveType.Triangles ); Interlocked.Increment( ref drawCalls ); } }
			catch ( Exception error ) { if ( errors.Count < 16 ) errors.Enqueue( error.ToString() ); }
		};
	}
	public void Dispose() { if ( disposed ) return; disposed = true; sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); }
}
