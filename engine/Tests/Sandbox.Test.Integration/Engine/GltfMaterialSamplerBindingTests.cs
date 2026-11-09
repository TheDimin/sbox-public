using NativeEngine;
using Sandbox.Rendering;
using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace EngineTests;

/// <summary>
/// Diagnostic acceptance for ordinary material constants selecting bindless
/// samplers. This does not establish stock-shader or glTF loader support.
/// The coordinator must run anonymous compilation in its guarded engine session.
/// </summary>
[TestClass]
public sealed class GltfMaterialSamplerBindingTests
{
	[TestMethod]
	[DataRow( false )]
	[DataRow( true )]
	public async Task MaterialScalarSelectsOnlyExplicitlyAllocatedSamplers( bool floatUniform )
	{
		if ( !Graphics.IsAvailable ) Assert.Inconclusive( "Requires an authorized in-engine graphics session." );
		Assert.IsTrue( ThreadSafe.IsMainThread, "Compile and allocate only on the engine main thread." );
		Assert.IsFalse( Graphics.IsActive, "Run at a safe boundary outside rendering." );
		var pointWrap = new SamplerState { Filter = FilterMode.Point, MaxAnisotropy = 1 };
		var pointClamp = pointWrap with { AddressModeU = TextureAddressMode.Clamp };
		var linearWrap = pointWrap with { Filter = FilterMode.Bilinear };
		int[] allowed = [pointClamp.Index, pointWrap.Index, linearWrap.Index];
		Assert.AreNotEqual( allowed[0], allowed[1], "The allocated wrap/clamp descriptors must differ." );
		Assert.AreNotEqual( allowed[1], allowed[2], "The allocated point/linear descriptors must differ." );
		foreach ( var index in allowed )
			Assert.IsTrue( index >= 0 && (int)(float)index == index, "The existing Vector4 material setter must represent each allocated index exactly." );

		Shader shader = null;
		Material first = null, second = null;
		using var texture = Texture.Create( 2, 1, ImageFormat.RGBA8888 ).WithMips( 1 )
			.WithData( new byte[] { 255, 0, 0, 255, 0, 0, 255, 255 } ).WithStaticUsage().Finish();
		try
		{
			using var cancellation = new CancellationTokenSource( TimeSpan.FromSeconds( 90 ) );
			shader = await Shader.CompileAsync( Source( floatUniform, allowed ), cancellation.Token );
			await MainThread.Wait();
			Assert.IsTrue( shader is { IsValid: true }, "The anonymous diagnostic must compile and load." );
			Assert.IsTrue( shader.IsAnonymous, "The diagnostic must not replace a watched or packaged shader." );
			first = Material.Create( "sampler_scalar_" + Guid.NewGuid().ToString( "N" ), shader );
			Assert.IsTrue( first is { IsValid: true, IsError: false } );
			Assert.IsTrue( first.HasParameter( "g_xMaterialSamplerIndex" ), "An undeclared native property is not a successful binding." );
			Set( first, allowed[0], floatUniform );
			second = first.CreateCopy();
			Assert.IsTrue( second is { IsValid: true, IsError: false } );
			Assert.AreEqual( (float)allowed[0], second.GetVector4( "g_xMaterialSamplerIndex" ).x, "Copy must retain the ordinary material value." );
			Set( second, allowed[2], floatUniform );
			Assert.AreEqual( (float)allowed[0], first.GetVector4( "g_xMaterialSamplerIndex" ).x, "Editing a copy must leave its source material unchanged." );

			AssertPixel( new Vector3( 0, 0, 1 ), Sample( first, texture, 1.25f, allowed[1] ), "Material A clamp; conflicting draw attribute must not replace the plain material constant." );
			AssertPixel( new Vector3( .7f, 0, .3f ), Sample( second, texture, .4f, allowed[0] ), "Material B linear; no material-to-object attribute copying is allowed." );
			AssertPixel( new Vector3( 0, 0, 1 ), Sample( first, texture, 1.25f, allowed[2] ), "Material A must remain independent after drawing B." );
			Set( second, allowed[1], floatUniform );
			AssertPixel( new Vector3( 1, 0, 0 ), Sample( second, texture, .4f, allowed[0] ), "Changing B's scalar must visibly select point sampling." );
			Set( first, -123, floatUniform );
			AssertPixel( new Vector3( 1, 0, 1 ), Sample( first, texture, .4f, allowed[1] ), "Unallocated indices must return the bounded diagnostic color before descriptor access." );
		}
		finally
		{
			await MainThread.Wait();
			second?.Destroy();
			first?.Destroy();
			shader?.Destroy();
		}
	}

	private static void Set( Material material, int index, bool floatUniform )
	{
		// Both overloads currently forward Vector4 to native. Value readback and
		// rendered texels are required; Material.Set's true return proves nothing.
		if ( floatUniform ) material.Set( "g_xMaterialSamplerIndex", (float)index );
		else material.Set( "g_xMaterialSamplerIndex", index );
		Assert.AreEqual( (float)index, material.GetVector4( "g_xMaterialSamplerIndex" ).x, "Native material value readback must match the allocated index." );
	}

	private static Vector3 Sample( Material material, Texture texture, float u, int conflictingAttribute )
	{
		using var target = Texture.CreateRenderTarget().WithSize( 1, 1 ).WithFormat( ImageFormat.RGBA32323232F ).WithInitialColor( Color.Yellow ).Create();
		using ( var scope = Graphics.Scope.Create() )
		{
			Graphics.RenderTarget = RenderTarget.From( target );
			scope.Context.Clear( Color.Yellow, true, false, false );
			Graphics.Attributes.Set( "ProbeTexture", texture );
			Graphics.Attributes.Set( "ProbeUv", new Vector2( u, .5f ) );
			Graphics.Attributes.Set( "g_xMaterialSamplerIndex", conflictingAttribute );
			Graphics.Blit( material );
		}
		g_pRenderDevice.ForceFlushGPU( default );
		var pixels = new float[4];
		target.GetPixels( (0, 0, 1, 1), 0, 0, pixels.AsSpan(), ImageFormat.RGBA32323232F );
		return new Vector3( pixels[0], pixels[1], pixels[2] );
	}

	private static void AssertPixel( Vector3 expected, Vector3 actual, string message )
	{
		Assert.IsTrue( float.IsFinite( actual.x ) && float.IsFinite( actual.y ) && float.IsFinite( actual.z ), message + " Nonfinite output." );
		Assert.IsTrue( actual.AlmostEqual( expected, .04f ), $"{message} Expected {expected}; actual {actual}." );
	}

	private static string Source( bool floatUniform, int[] allocated )
	{
		var source = """
		HEADER { DevShader = true; Description = "Material scalar bindless diagnostic only"; }
		MODES { Default(); Forward(); }
		COMMON
		{
			#include "postprocess/shared.hlsl"
			#include "common/Bindless.hlsl"
		}
		struct VertexInput { float3 vPositionOs : POSITION < Semantic( PosXyz ); >; };
		struct PixelInput
		{
			#if ( PROGRAM == VFX_PROGRAM_VS )
			float4 vPositionPs : SV_Position;
			#endif
			#if ( PROGRAM == VFX_PROGRAM_PS )
			float4 vPositionSs : SV_Position;
			#endif
		};
		VS
		{
			PixelInput MainVs( VertexInput i ) { PixelInput o; o.vPositionPs=float4(i.vPositionOs.xy,0,1); return o; }
		}
		PS
		{
			RenderState( CullMode, NONE ); RenderState( DepthEnable, false );
			RenderState( DepthWriteEnable, false ); RenderState( BlendEnable, false );
			Texture2D g_tProbe < Attribute("ProbeTexture"); SrgbRead(false); >;
			float2 g_vProbeUv < Attribute("ProbeUv"); >;
			__TYPE__ g_xMaterialSamplerIndex < Default(-1); >;
			float4 MainPs( PixelInput i ) : SV_Target0
			{
				// Do not clamp to a heap capacity: unused descriptors inside that
				// capacity are also unsafe. Only these allocated indices are legal.
				[branch] if ( g_xMaterialSamplerIndex != __A__ && g_xMaterialSamplerIndex != __B__ && g_xMaterialSamplerIndex != __C__ )
					return float4(1,0,1,1);
				int index = (int)g_xMaterialSamplerIndex;
				return g_tProbe.SampleLevel( Bindless::GetSampler(index), g_vProbeUv, 0 );
			}
		}
		""";
		return source.Replace( "__TYPE__", floatUniform ? "float" : "int", StringComparison.Ordinal )
			.Replace( "__A__", allocated[0].ToString( CultureInfo.InvariantCulture ), StringComparison.Ordinal )
			.Replace( "__B__", allocated[1].ToString( CultureInfo.InvariantCulture ), StringComparison.Ordinal )
			.Replace( "__C__", allocated[2].ToString( CultureInfo.InvariantCulture ), StringComparison.Ordinal );
	}
}
