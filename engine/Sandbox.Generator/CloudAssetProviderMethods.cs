using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace Sandbox.Generator;

/// <summary>
/// A conservative name filter, not a replacement for overload resolution. Most calls cannot
/// refer to a provider. Avoid binding those calls just to check for a provider diagnostic.
/// </summary>
internal sealed class CloudAssetProviderMethods
{
	private static readonly ConditionalWeakTable<PortableExecutableReference, CloudAssetProviderMethods> MetadataNames = new();
	private readonly HashSet<string> names = new( StringComparer.Ordinal );
	private bool complete = true;

	internal static CloudAssetProviderMethods Create( Compilation compilation )
	{
		var result = new CloudAssetProviderMethods();
		result.AddCompilation( compilation, new HashSet<Compilation>() );
		return result;
	}

	private void AddCompilation( Compilation compilation, HashSet<Compilation> visited )
	{
		if ( !visited.Add( compilation ) ) return;

		// Include every attributed declaration. This also covers aliases and attributes from
		// game code without binding their bodies or keeping symbols alive in the cache.
		foreach ( var tree in compilation.SyntaxTrees )
		{
			foreach ( var node in tree.GetRoot().DescendantNodes() )
			{
				if ( node is MethodDeclarationSyntax method && method.AttributeLists.Count > 0 )
					names.Add( method.Identifier.ValueText );
				else if ( node is LocalFunctionStatementSyntax local && local.AttributeLists.Count > 0 )
					names.Add( local.Identifier.ValueText );
			}
		}

		foreach ( var reference in compilation.References )
		{
			if ( reference is CompilationReference source )
			{
				AddCompilation( source.Compilation, visited );
			}
			else if ( reference is PortableExecutableReference portable )
			{
				var metadata = MetadataNames.GetValue( portable, ReadMetadata );
				names.UnionWith( metadata.names );
				complete &= metadata.complete;
			}
			else
			{
				complete = false;
			}
		}
	}

	private static CloudAssetProviderMethods ReadMetadata( PortableExecutableReference reference )
	{
		var result = new CloudAssetProviderMethods();
		try
		{
			using var metadata = reference.GetMetadata();
			if ( metadata is AssemblyMetadata assembly )
			{
				foreach ( var module in assembly.GetModules() ) result.AddModule( module.GetMetadataReader() );
			}
			else if ( metadata is ModuleMetadata module )
			{
				result.AddModule( module.GetMetadataReader() );
			}
			else result.complete = false;
		}
		catch ( BadImageFormatException )
		{
			// Let the compiler report invalid metadata. Do not suppress provider checks.
			result.complete = false;
		}
		catch ( IOException )
		{
			result.complete = false;
		}

		return result;
	}

	private void AddModule( MetadataReader reader )
	{
		foreach ( var handle in reader.CustomAttributes )
		{
			var attribute = reader.GetCustomAttribute( handle );
			if ( attribute.Parent.Kind != HandleKind.MethodDefinition ) continue;

			EntityHandle type;
			if ( attribute.Constructor.Kind == HandleKind.MemberReference )
				type = reader.GetMemberReference( (MemberReferenceHandle)attribute.Constructor ).Parent;
			else if ( attribute.Constructor.Kind == HandleKind.MethodDefinition )
				type = reader.GetMethodDefinition( (MethodDefinitionHandle)attribute.Constructor ).GetDeclaringType();
			else
			{
				complete = false;
				continue;
			}

			StringHandle name;
			if ( type.Kind == HandleKind.TypeReference ) name = reader.GetTypeReference( (TypeReferenceHandle)type ).Name;
			else if ( type.Kind == HandleKind.TypeDefinition ) name = reader.GetTypeDefinition( (TypeDefinitionHandle)type ).Name;
			else
			{
				// An unusual attribute constructor must not cause a missed diagnostic.
				complete = false;
				continue;
			}

			if ( !reader.StringComparer.Equals( name, "CloudAssetProviderAttribute" ) &&
				!reader.StringComparer.Equals( name, "CloudAssetProviderAttributeAttribute" ) ) continue;

			var method = reader.GetMethodDefinition( (MethodDefinitionHandle)attribute.Parent );
			var methodName = reader.GetString( method.Name );
			// Explicit interface implementations have a qualified metadata name.
			names.Add( methodName.Substring( methodName.LastIndexOf( '.' ) + 1 ) );
		}
	}

	internal bool MayContain( ExpressionSyntax expression )
	{
		if ( !complete ) return true;
		return expression switch
		{
			SimpleNameSyntax name => names.Contains( name.Identifier.ValueText ),
			MemberAccessExpressionSyntax member => names.Contains( member.Name.Identifier.ValueText ),
			MemberBindingExpressionSyntax member => names.Contains( member.Name.Identifier.ValueText ),
			_ => true
		};
	}
}
