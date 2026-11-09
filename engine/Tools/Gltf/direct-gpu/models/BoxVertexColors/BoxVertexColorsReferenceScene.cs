using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sandbox.Gltf.DirectGpu.BoxVertexColorsValidation;

using NVector4 = System.Numerics.Vector4;
using static BoxVertexColorsReference;

/// <summary>
/// Explicit reference OUTPUT layout. Literal cube positions, normals, colors and
/// topology come from the independent oracle, never the producer GPU evaluator.
/// FLOAT4 preserves fractional colors and alpha without Color32 quantization.
/// </summary>
internal sealed class BoxVertexColorsReferenceScene : IDisposable
{
	[StructLayout( LayoutKind.Sequential, Pack = 4 )]
	private struct ReferenceVertex
	{
		[VertexLayout.Color( 0 )] public global::Vector4 Color;
		[VertexLayout.Position( 0 )] public global::Vector3 Position;
		[VertexLayout.Normal( 0 )] public global::Vector3 Normal;
		[VertexLayout.TexCoord( 0 )] public global::Vector2 Uv;
		[VertexLayout.Tangent( 0 )] public global::Vector4 Tangent;
	}
	public enum ColorControl { CorrectLinear, MissingColorWhite, IncorrectSrgbDecode }
	private readonly SceneCustomObject sceneObject;
	private readonly GpuBuffer<ReferenceVertex> vertices;
	private readonly ConcurrentQueue<string> errors = new();
	private int draws; private bool disposed;
	public int DrawCalls => Volatile.Read( ref draws );
	public string[] CallbackErrors => errors.ToArray();

	public BoxVertexColorsReferenceScene( BoxVertexColorsReference reference, Form form, SceneWorld world, Material independentLinearColorMaterial, ColorControl colorControl = ColorControl.CorrectLinear )
	{
		Require( independentLinearColorMaterial is { IsValid: true, IsError: false }, "Independent linear-color reference material is unavailable." );
		var data = new ReferenceVertex[36];
		for ( var corner = 0; corner < data.Length; corner++ )
		{
			var source = (int)reference.Indices[corner]; var p = reference.EnginePosition( source ); var n = reference.EngineNormal( source );
			var color = colorControl == ColorControl.MissingColorWhite ? NVector4.One : form.DrawColor( source );
			if ( colorControl == ColorControl.IncorrectSrgbDecode ) for ( var channel = 0; channel < 3; channel++ ) color[channel] = color[channel] <= .04045f ? color[channel] / 12.92f : MathF.Pow( (color[channel] + .055f) / 1.055f, 2.4f );
			// No authored tangent/normal texture exists. Match the explicit
			// generated source basis (1,0,0) -> engine (0,1,0), orthogonalized.
			var tangent = System.Numerics.Vector3.UnitY - n * System.Numerics.Vector3.Dot( System.Numerics.Vector3.UnitY, n );
			if ( tangent.LengthSquared() == 0 ) tangent = System.Numerics.Vector3.Normalize( System.Numerics.Vector3.Cross( n, MathF.Abs( n.X ) < .7f ? System.Numerics.Vector3.UnitX : System.Numerics.Vector3.UnitY ) );
			else tangent = System.Numerics.Vector3.Normalize( tangent );
			data[corner] = new ReferenceVertex { Color = new( color.X, color.Y, color.Z, color.W ), Position = new( p.X, p.Y, p.Z ), Normal = new( n.X, n.Y, n.Z ), Uv = default, Tangent = new( tangent.X, tangent.Y, tangent.Z, 1 ) };
		}
		vertices = new GpuBuffer<ReferenceVertex>( data.Length, GpuBuffer.UsageFlags.Vertex, "BoxVertexColors independent FLOAT4 reference output" );
		try
		{
			vertices.SetData( data ); sceneObject = new SceneCustomObject( world ); sceneObject.Flags.IsOpaque = true; sceneObject.Flags.IsTranslucent = false;
			sceneObject.Bounds = BBox.FromPoints( data.Select( value => value.Position ) );
			sceneObject.RenderOverride = _ =>
			{
				try { Graphics.Draw( vertices, independentLinearColorMaterial, vertexCount: data.Length ); Interlocked.Increment( ref draws ); }
				catch ( Exception error ) { if ( errors.Count < 16 ) errors.Enqueue( error.ToString() ); }
			};
		}
		catch { vertices.Dispose(); throw; }
	}
	public void Dispose()
	{
		if ( disposed ) return; disposed = true; sceneObject.RenderOverride = null; if ( sceneObject.IsValid() ) sceneObject.Delete(); vertices.Dispose();
	}
}
