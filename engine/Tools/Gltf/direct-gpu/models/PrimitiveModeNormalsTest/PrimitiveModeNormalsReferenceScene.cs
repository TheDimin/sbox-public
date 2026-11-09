using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.PrimitiveModeNormalsValidation;

using Matrix4x4 = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;
using static PrimitiveModeNormalsReference;

/// <summary>
/// Independent native primitive drawing from literal source geometry. No Model,
/// Mesh, producer reader/evaluator or shared GPU backend supplies expected data.
/// The coordinator owns material, camera, native captures and pixel acceptance.
/// </summary>
public sealed class PrimitiveModeNormalsReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int drawCalls;
	private bool disposed;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => errors.ToArray();
	public int[] Meshes { get; }
	public PrimitiveModeNormalsReferenceScene( PrimitiveModeNormalsReference reference, SceneWorld world, Matrix4x4 placement,
		Func<int, int?, Material> materialForPrimitiveContext, int[] selectedMeshes = null, bool deliberatelyWrongPrimitiveTypes = false )
	{
		ArgumentNullException.ThrowIfNull( reference ); ArgumentNullException.ThrowIfNull( world ); ArgumentNullException.ThrowIfNull( materialForPrimitiveContext );
		Meshes = selectedMeshes is null ? Enumerable.Range( 0, 25 ).ToArray() : (int[])selectedMeshes.Clone();
		Require( Meshes.Length > 0 && Meshes.All( x => x is >= 0 and < 25 ) && Meshes.Distinct().Count() == Meshes.Length, "Reference selection must identify nonempty unique model meshes; unselected renders remain pending." );
		var draws = new List<(Vertex[] Vertices, Material Material, Graphics.PrimitiveType Mode)>();
		var minimum = new NVector3( float.PositiveInfinity ); var maximum = new NVector3( float.NegativeInfinity );
		foreach ( var mesh in Meshes )
		{
			var expanded = reference.ExpandedIndices( mesh ); var indices = (uint[])expanded.Clone();
			if ( DrawMode( mesh ) == 4 && placement.GetDeterminant() < 0 )
				for ( var i = 0; i < indices.Length; i += 3 ) (indices[i + 1], indices[i + 2]) = (indices[i + 2], indices[i + 1]);
			var vertices = new Vertex[indices.Length];
			for ( var corner = 0; corner < vertices.Length; corner++ )
			{
				var source = (int)indices[corner]; var p = NVector3.Transform( reference.EnginePosition( mesh, source ), placement );
				// For missing point/line normals only, this finite placeholder is a
				// diagnostic policy. Compare geometry with a normal-independent
				// material, never invent a normal-direction conformance image.
				var sourceNormal = HasNormal( mesh ) ? reference.AuthoredEngineNormal( mesh, source ) : Flat( mesh ) ? reference.FlatEngineNormal( mesh, expanded, corner ) : NVector3.UnitX;
				var n = TransformNormal( sourceNormal, placement ); var tangent = NVector3.Normalize( NVector3.Cross( n, MathF.Abs( n.X ) < .7f ? NVector3.UnitX : NVector3.UnitY ) );
				byte ColorComponent( int component ) => HasColor( mesh ) ? reference.Blobs["Colors.bin"][source * 4 + component] : (byte)255;
				vertices[corner] = new Vertex( new global::Vector3( p.X, p.Y, p.Z ) )
				{
					Normal = new global::Vector3( n.X, n.Y, n.Z ), Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, placement.GetDeterminant() < 0 ? -1 : 1 ),
					TexCoord0 = new global::Vector4( reference.Uv( mesh, source, 0 ), reference.Uv( mesh, source, 1 ), 0, 0 ),
					Color = new global::Color32( ColorComponent( 0 ), ColorComponent( 1 ), ColorComponent( 2 ), ColorComponent( 3 ) )
				};
				minimum = NVector3.Min( minimum, p ); maximum = NVector3.Max( maximum, p );
			}
			var material = materialForPrimitiveContext( mesh, Material( mesh ) ); Require( material is { IsValid: true, IsError: false }, "Independent reference material is unavailable." );
			var mode = DrawMode( mesh ) == 0 ? Graphics.PrimitiveType.Points : DrawMode( mesh ) == 1 ? Graphics.PrimitiveType.Lines : Graphics.PrimitiveType.Triangles;
			if ( deliberatelyWrongPrimitiveTypes && mode != Graphics.PrimitiveType.Triangles )
			{
				mode = Graphics.PrimitiveType.Triangles; Array.Resize( ref vertices, vertices.Length / 3 * 3 );
			}
			draws.Add( (vertices, material, mode) );
		}
		minimum -= new NVector3( .001f ); maximum += new NVector3( .001f );
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = new global::BBox( new global::Vector3( minimum.X, minimum.Y, minimum.Z ), new global::Vector3( maximum.X, maximum.Y, maximum.Z ) );
		sceneObject.RenderOverride = _ =>
		{
			try
			{
				foreach ( var draw in draws )
				{
					// Bounded even point/line chunks avoid Graphics.Draw's general
					// large-span chunker splitting a line pair at a vertex boundary.
					var chunk = draw.Mode == Graphics.PrimitiveType.Triangles ? 2046 : 2048;
					for ( var start = 0; start < draw.Vertices.Length; start += chunk )
					{
						var count = Math.Min( chunk, draw.Vertices.Length - start );
						Graphics.Draw( draw.Vertices.AsSpan( start, count ), count, draw.Material, primitiveType: draw.Mode ); Interlocked.Increment( ref drawCalls );
					}
				}
			}
			catch ( Exception error ) { if ( errors.Count < 16 ) errors.Enqueue( error.ToString() ); }
		};
	}
	public void Dispose()
	{
		if ( disposed ) return; disposed = true;
		sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete();
	}
}
