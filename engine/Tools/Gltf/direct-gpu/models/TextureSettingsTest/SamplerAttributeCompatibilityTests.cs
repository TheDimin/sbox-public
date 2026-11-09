using System;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

using Assert = Microsoft.VisualStudio.TestTools.UnitTesting.Assert;

/// <summary>Compatibility edges for the optional provided-graph sampler binding.</summary>
[TestClass]
public sealed class SamplerAttributeCompatibilityTests
{
	[TestMethod]
	public void EmptyAndOmittedAttributesShareDescriptorAndLegacySerialization()
	{
		var context = Call( "Context", "Pixel" );
		var omitted = Call( "Register", context, null ); var empty = Call( "Register", context, "" );
		Assert.AreEqual( omitted, empty, "An empty optional name must normalize to the legacy omitted binding." );
		var source = (string)Call( "Globals", context );
		Assert.IsFalse( source.Contains( "SamplerState g_sSampler1", StringComparison.Ordinal ), "Empty versus omitted must not create duplicate static samplers." );
		Assert.IsFalse( source.Split( '\n' ).Where( line => line.TrimStart().StartsWith( "SamplerState ", StringComparison.Ordinal ) ).Any( line => line.Contains( "Attribute", StringComparison.Ordinal ) ), "Omitted sampler declarations must contain no Attribute annotation, regardless of emitted whitespace." );
		var first = Call( "Context", "Pixel" ); Call( "Register", first, null );
		var second = Call( "Context", "Pixel" ); Call( "Register", second, "" );
		Assert.AreEqual( (string)Call( "Globals", first ), (string)Call( "Globals", second ), "Empty and omitted bindings must generate byte-identical legacy shader text." );
		var json = JsonSerializer.Serialize( empty, empty.GetType(), new JsonSerializerOptions { DefaultIgnoreCondition = JsonIgnoreCondition.Never } );
		Assert.IsFalse( json.Contains( "\"Attribute\"", StringComparison.Ordinal ), "Legacy graphs must not gain null-valued sampler properties when serialized." );
	}

	[TestMethod]
	public void AttributeNamesRemainCaseSensitive()
	{
		var context = Call( "Context", "Pixel" ); Call( "Register", context, "ColorSampler" ); Call( "Register", context, "colorSampler" );
		var source = (string)Call( "Globals", context );
		Assert.IsTrue( source.Contains( "SamplerState g_sSampler1", StringComparison.Ordinal ) );
		Assert.IsTrue( source.Contains( "Attribute( \"ColorSampler\" );", StringComparison.Ordinal ) && source.Contains( "Attribute( \"colorSampler\" );", StringComparison.Ordinal ) );
	}

	[TestMethod]
	public void UnderscoreAndTrailingDigitsAreValidNames()
	{
		var context = Call( "Context", "Vertex" ); var sampler = Call( "Register", context, "_Sampler2" );
		Assert.IsTrue( ((string)Call( "Globals", context )).Contains( "Attribute( \"_Sampler2\" );", StringComparison.Ordinal ) );
		var json = JsonSerializer.Serialize( sampler, sampler.GetType() );
		var roundtrip = JsonSerializer.Deserialize( json, sampler.GetType() );
		Assert.AreEqual( sampler, roundtrip );
	}

	[TestMethod]
	public void InvalidRegistrationDoesNotLeaveSamplerOrTextureEntries()
	{
		var context = Call( "Context", "Pixel" );
		foreach ( var invalid in new[] { " ", "0invalid", "a\" ); >;", "line\nbreak", "has-hyphen", "é" } )
		{
			var rejected = false;
			try { Call( "Register", context, invalid ); }
			catch ( TargetInvocationException error )
			{
				Exception cause = error;
				while ( cause is TargetInvocationException invocation && invocation.InnerException is not null ) cause = invocation.InnerException;
				Assert.IsInstanceOfType<ArgumentException>( cause ); rejected = true;
			}
			Assert.IsTrue( rejected, "Malformed names must reject before graph registration." );
		}
		Call( "Register", context, "ValidSampler" ); var source = (string)Call( "Globals", context );
		Assert.IsTrue( source.Contains( "SamplerState g_sSampler0", StringComparison.Ordinal ) );
		Assert.IsFalse( source.Contains( "SamplerState g_sSampler1", StringComparison.Ordinal ) || source.Contains( "Texture_ps_1", StringComparison.Ordinal ), "Rejected samplers must not partially register textures or samplers." );
	}

	private static object Call( string method, params object[] arguments ) => typeof( SamplerAttributeGenerationTests ).GetMethod( method, BindingFlags.Static | BindingFlags.NonPublic )!.Invoke( null, arguments );
}
