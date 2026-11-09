using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.TextureTransformTestValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

/// <summary>
/// Literal independent reference triangles with texture transforms already
/// applied. The declared Vertex layout is diagnostic data, never a glTF input
/// layout. Borrowed stock materials isolate UV/geometry agreement; sharing those
/// materials means this reference does NOT independently prove sampler fidelity.
/// </summary>
public sealed class TextureTransformReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int drawCalls;
	private bool disposed;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => errors.ToArray();

	public TextureTransformReferenceScene( SceneWorld world, NMatrix placement, Func<int?, Material> materialResolver, bool omitTransforms = false )
	{
		ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( materialResolver );
		if ( !NMatrix.Invert( placement, out var inverse ) ) throw new InvalidOperationException( "Reference placement is singular." );
		var normal = NVector3.Normalize( NVector3.TransformNormal( NVector3.UnitX, NMatrix.Transpose( inverse ) ) );
		var tangent = NVector3.TransformNormal( NVector3.UnitY, placement ); tangent = NVector3.Normalize( tangent - normal * NVector3.Dot( tangent, normal ) );
		var draws = new List<(Vertex[] Vertices, Material Material)>();
		for ( var node = 0; node < 12; node++ )
		{
			var mesh = TextureTransformReference.NodeMeshes[node]; var indices = (uint[])TextureTransformReference.Indices.Clone();
			if ( placement.GetDeterminant() < 0 ) for ( var triangle = 0; triangle < indices.Length; triangle += 3 )
				(indices[triangle + 1], indices[triangle + 2]) = (indices[triangle + 2], indices[triangle + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var corner = 0; corner < vertices.Length; corner++ )
			{
				var source = (int)indices[corner]; var p = NVector3.Transform( TextureTransformReference.EnginePosition( node, source ), placement );
				var rawUv = TextureTransformReference.SourceUv( mesh, source );
				var uv = omitTransforms ? new[] { (double)rawUv.X, rawUv.Y } : TextureTransformReference.SampleUv( mesh, rawUv.X, rawUv.Y );
				vertices[corner] = new Vertex( new global::Vector3( p.X, p.Y, p.Z ) )
				{
					Normal = new global::Vector3( normal.X, normal.Y, normal.Z ),
					Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, placement.GetDeterminant() < 0 ? -1 : 1 ),
					TexCoord0 = new global::Vector4( (float)uv[0], (float)uv[1], 0, 0 ), Color = Color32.White
				};
			}
			var material = materialResolver( mesh );
			if ( material is null || !material.IsValid || material.IsError ) throw new InvalidOperationException( "Missing borrowed stock reference material." );
			draws.Add( (vertices, material) );
		}
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = BBox.FromPoints( draws.SelectMany( value => value.Vertices ).Select( vertex => vertex.Position ) );
		sceneObject.RenderOverride = _ =>
		{
			RenderAttributes attributes = null;
			try
			{
				attributes = RenderAttributes.Pool.Get();
				Interlocked.Increment( ref drawCalls );
				foreach ( var draw in draws )
				{
					attributes.Clear( false );
					// Graphics.Draw does not merge Material.Attributes itself. Carry
					// typed role samplers through this independent literal path too,
					// using the same material-before-view precedence as the raw draw.
					draw.Material.Attributes.MergeTo( attributes );
					Graphics.Attributes.MergeTo( attributes );
					Graphics.Draw( draw.Vertices.AsSpan(), draw.Vertices.Length, draw.Material, attributes );
				}
			}
			catch ( Exception error ) { errors.Enqueue( error.ToString() ); }
			finally { if ( attributes is not null ) RenderAttributes.Pool.Return( attributes ); }
		};
	}

	public void Dispose()
	{
		if ( disposed ) return; disposed = true;
		sceneObject.RenderOverride = null;
		if ( sceneObject.IsValid() ) sceneObject.Delete();
	}
}
