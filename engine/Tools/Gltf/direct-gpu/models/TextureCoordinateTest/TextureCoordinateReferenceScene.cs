using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.TextureCoordinateTestValidation;

using NMatrix = System.Numerics.Matrix4x4;
using NVector3 = System.Numerics.Vector3;

/// <summary>Independent literal source triangles; declared Vertex is a reference draw format, never glTF input layout.</summary>
public sealed class TextureCoordinateReferenceScene : IDisposable
{
	private readonly SceneCustomObject sceneObject;
	private readonly ConcurrentQueue<string> errors = new();
	private int drawCalls;
	public int DrawCalls => Volatile.Read( ref drawCalls );
	public string[] CallbackErrors => errors.ToArray();
	public TextureCoordinateReferenceScene( SceneWorld world, TextureCoordinateReference.Form form, NMatrix placement, Func<int?, Material> borrowMaterial, bool flipV = false )
	{
		TextureCoordinateCoreTests.Require( NMatrix.Invert( placement, out var inverse ), "Reference requires invertible placement." );
		var normalMatrix = NMatrix.Transpose( inverse ); var draws = new List<(Vertex[] Vertices, Material Material)>();
		foreach ( var primitive in form.Expected )
		{
			var indices = primitive.Indices.Values.Select( value => (int)value[0] ).ToArray();
			if ( placement.GetDeterminant() < 0 ) for ( var index = 0; index < indices.Length; index += 3 ) (indices[index + 1], indices[index + 2]) = (indices[index + 2], indices[index + 1]);
			var vertices = indices.Select( source =>
			{
				var p = NVector3.Transform( TextureCoordinateReference.EnginePosition( primitive.Streams["POSITION"].Values[source] ), placement );
				var n = NVector3.Normalize( NVector3.TransformNormal( TextureCoordinateReference.EngineNormal( primitive.Streams["NORMAL"].Values[source] ), normalMatrix ) );
				var tangent = NVector3.TransformNormal( NVector3.UnitY, placement ); tangent = NVector3.Normalize( tangent - n * NVector3.Dot( tangent, n ) );
				var uv0 = primitive.Streams.TryGetValue( "TEXCOORD_0", out var stream0 ) ? stream0.Values[source] : new double[] { 0, 0 };
				var uv1 = primitive.Streams.TryGetValue( "TEXCOORD_1", out var stream1 ) ? stream1.Values[source] : new double[] { 0, 0 };
				return new Vertex( new global::Vector3( p.X, p.Y, p.Z ) )
				{
					Normal = new global::Vector3( n.X, n.Y, n.Z ), Tangent = new global::Vector4( tangent.X, tangent.Y, tangent.Z, placement.GetDeterminant() < 0 ? -1 : 1 ),
					TexCoord0 = new global::Vector4( (float)uv0[0], (float)(flipV && stream0 is not null ? 1 - uv0[1] : uv0[1]), 0, 0 ),
					TexCoord1 = new global::Vector4( (float)uv1[0], (float)(flipV && stream1 is not null ? 1 - uv1[1] : uv1[1]), 0, 0 ), Color = Color32.White
				};
			} ).ToArray();
			var material = borrowMaterial( primitive.Material );
			TextureCoordinateCoreTests.Require( material is { IsValid: true, IsError: false }, "Missing actual source material for independent coordinate reference." ); draws.Add( (vertices, material) );
		}
		sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
		sceneObject.Bounds = BBox.FromPoints( draws.SelectMany( value => value.Vertices ).Select( value => value.Position ) );
		sceneObject.RenderOverride = _ =>
		{
			try { foreach ( var draw in draws ) Graphics.Draw( draw.Vertices.AsSpan(), draw.Vertices.Length, draw.Material ); Interlocked.Increment( ref drawCalls ); }
			catch ( Exception error ) { if ( errors.Count < 8 ) errors.Enqueue( error.ToString() ); }
		};
	}
	public void Dispose() { sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); }
}
