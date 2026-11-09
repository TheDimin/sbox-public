using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Sandbox.Gltf.DirectGpu.TextureSettingsValidation;

/// <summary>Runs the active supplied graph compiler without creating texture nodes or compiling GPU code.</summary>
public static class SamplerAttributeGenerationTests
{
	public sealed record Result( string Name, bool Passed, string Error );
	public static Result[] RunAll()
	{
		var results = new List<Result>();
		foreach ( var stage in new[] { "Vertex", "Pixel" } )
		{
			Run( stage + "_omitted_preserves_legacy_declaration", () =>
			{
				var context = Context( stage ); Register( context, null );
				Require( Globals( context ).Contains( "SamplerState g_sSampler0 < Filter( ANISO ); AddressU( WRAP ); AddressV( WRAP ); >;", StringComparison.Ordinal ), "Unannotated existing graph changed." );
				Require( !Globals( context ).Contains( "Attribute(\"", StringComparison.Ordinal ), "Omitted sampler gained a runtime binding." );
			} );
			Run( stage + "_attribute_emitted_and_serialized", () =>
			{
				var context = Context( stage ); var sampler = Register( context, "TextureSampler_0" );
				Require( Globals( context ).Contains( "Attribute( \"TextureSampler_0\" );", StringComparison.Ordinal ), "Requested sampler attribute was not emitted." );
				var options = (JsonSerializerOptions)context.Graph.GetMethod( "SerializerOptions", BindingFlags.NonPublic | BindingFlags.Static )!.Invoke( null, new object[] { false } );
				var json = JsonSerializer.Serialize( sampler, sampler.GetType(), options );
				var roundtrip = JsonSerializer.Deserialize( json, sampler.GetType(), options );
				Require( (string)sampler.GetType().GetProperty( "Attribute" )!.GetValue( roundtrip ) == "TextureSampler_0", "Graph serialization lost sampler binding name." );
			} );
			Run( stage + "_different_bindings_not_deduplicated", () =>
			{
				var context = Context( stage ); Register( context, "SamplerA" ); Register( context, "SamplerB" ); Register( context, "SamplerA" );
				var text = Globals( context );
				Require( text.Contains( "SamplerState g_sSampler1 <", StringComparison.Ordinal ) && !text.Contains( "SamplerState g_sSampler2 <", StringComparison.Ordinal ), "Sampler identity must include optional attribute name but deduplicate identical names/state." );
			} );
		}
		foreach ( var invalid in new[] { " ", "0invalid", "a\" ); >;", "line\nbreak", "has-hyphen", "é" } )
			Run( "reject_invalid_attribute_" + JsonSerializer.Serialize( invalid ), () =>
			{
				var context = Context( "Pixel" ); var rejected = false;
				try { Register( context, invalid ); _ = Globals( context ); }
				catch ( TargetInvocationException ex ) when ( ex.InnerException is ArgumentException ) { rejected = true; }
				Require( rejected, "Malformed nonempty name must reject before shader text emission." );
			} );
		return results.ToArray();
		void Run( string name, Action test ) { try { test(); results.Add( new( name, true, null ) ); } catch ( Exception e ) { results.Add( new( name, false, e.ToString() ) ); } }
	}
	private sealed record CompilerContext( Type Graph, Type Compiler, object Instance );
	private static CompilerContext Context( string stage )
	{
		var graph = ActiveType( "Editor.ShaderGraph.ShaderGraph" ); var compiler = ActiveType( "Editor.ShaderGraph.GraphCompiler" );
		var instance = Activator.CreateInstance( compiler, Activator.CreateInstance( graph ), false );
		var property = compiler.GetProperty( "Stage" ); property!.SetValue( instance, Enum.Parse( property.PropertyType, stage ) );
		return new( graph, compiler, instance );
	}
	private static object Register( CompilerContext context, string attribute )
	{
		var samplerType = ActiveType( "Editor.ShaderGraph.Sampler" ); var sampler = Activator.CreateInstance( samplerType );
		if ( attribute is not null )
		{
			var property = samplerType.GetProperty( "Attribute" ); Require( property is not null, "The active sampler type has no optional Attribute property." ); property.SetValue( sampler, attribute );
		}
		var input = Activator.CreateInstance( ActiveType( "Editor.ShaderGraph.TextureInput" ) );
		context.Compiler.GetMethod( "ResultTexture" )!.Invoke( context.Instance, new[] { sampler, input, null } );
		return sampler;
	}
	private static string Globals( CompilerContext c ) => (string)c.Compiler.GetMethod( "GenerateGlobals", BindingFlags.NonPublic | BindingFlags.Instance )!.Invoke( c.Instance, null );
	private static Type ActiveType( string name )
	{
		var globals = typeof( Editor.SceneRenderingWidget ).Assembly.GetType( "Sandbox.Internal.GlobalToolsNamespace", true );
		var library = globals!.GetProperty( "EditorTypeLibrary", BindingFlags.Public | BindingFlags.Static )!.GetValue( null );
		var lookup = library!.GetType().GetMethods( BindingFlags.Public | BindingFlags.Instance ).Single( method => method.Name == "GetType" && !method.IsGenericMethod && method.GetParameters().Select( p => p.ParameterType ).SequenceEqual( new[] { typeof( string ), typeof( bool ) } ) );
		var description = lookup.Invoke( library, new object[] { name, true } );
		var type = description?.GetType().GetProperty( "TargetType" )?.GetValue( description ) as Type;
		Require( type is not null && type.FullName == name, "Missing active editor type " + name ); return type;
	}
	private static void Require( bool value, string message ) { if ( !value ) throw new InvalidOperationException( message ); }
}
