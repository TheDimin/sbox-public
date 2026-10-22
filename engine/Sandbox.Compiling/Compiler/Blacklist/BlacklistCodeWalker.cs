using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sandbox;

internal class BlacklistCodeWalker( SemanticModel semanticModel ) : CSharpSyntaxWalker
{
	public static readonly DiagnosticDescriptor BlacklistDescriptor = new( "SB500", "Blacklist Error", "Prohibited type '{0}' used", "Sandbox.Compiling", DiagnosticSeverity.Error, true );

	public List<Diagnostic> Diagnostics { get; set; } = [];

	record LineSymbol( int Line, ISymbol Symbol );
	HashSet<LineSymbol> LineSymbols = [];

	private readonly SemanticModel _semanticModel = semanticModel;
	private readonly Dictionary<SyntaxNode, IMethodSymbol> _invokedMethods = [];
	private readonly Dictionary<ISymbol, string> _blockedSymbols = new( ReferenceEqualityComparer.Instance );

	static readonly SymbolDisplayFormat FullyQualifiedSymbolFormat = new SymbolDisplayFormat(
		globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
		typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
		genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
		memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType,
		parameterOptions: SymbolDisplayParameterOptions.IncludeType,
		miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
	);

	/// <summary>
	/// Visits every node type, unless that node type has been explicitly overridden.
	/// This gives us complete coverage of every type of possible syntax node.
	/// From there we can see if it references any blacklisted symbols.
	/// </summary>
	public override void DefaultVisit( SyntaxNode node )
	{
		ISymbol symbol;
		if ( _invokedMethods.TryGetValue( node, out var invokedMethod ) )
		{
			symbol = invokedMethod;
		}
		else
		{
			var symbolInfo = _semanticModel.GetSymbolInfo( node );

			// Unresolved calls and method references still need the original candidate checks.
			symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

			if ( node is InvocationExpressionSyntax invocation &&
				symbolInfo.Symbol is IMethodSymbol { MethodKind: not MethodKind.DelegateInvoke } method )
			{
				// The invocation already resolved this method. Binding its method group again
				// makes Roslyn search all extension containers for each spelling of the call.
				// Continue visiting receivers, arguments and generic type arguments normally.
				_invokedMethods[invocation.Expression] = method;
				if ( invocation.Expression is MemberAccessExpressionSyntax access )
					_invokedMethods[access.Name] = method;
				else if ( invocation.Expression is MemberBindingExpressionSyntax binding )
					_invokedMethods[binding.Name] = method;
			}
		}

		if ( symbol is null )
		{
			base.DefaultVisit( node );
			return;
		}

		// The same constructed method is visited at its invocation, method group and name,
		// and often at many other call sites. Keep only its decision for this tree walk.
		// Do not use OriginalDefinition: generic arguments can change the rule match.
		// Reference identity also keeps distinct display forms such as dynamic and object.
		if ( !_blockedSymbols.TryGetValue( symbol, out var fullyQualifiedName ) )
		{
			var displayName = symbol.ToDisplayString( FullyQualifiedSymbolFormat );
			fullyQualifiedName = CompilerRules.IsBlocked( displayName ) ? displayName : null;
			_blockedSymbols.Add( symbol, fullyQualifiedName );
		}

		if ( fullyQualifiedName is null )
		{
			base.DefaultVisit( node );
			return;
		}

		// This isn't strictly needed, it just makes the diagnostics a bit neater and easier to test against
		var lineSymbol = new LineSymbol( node.GetLocation().GetMappedLineSpan().StartLinePosition.Line, symbol );
		if ( LineSymbols.Add( lineSymbol ) == false )
		{
			base.DefaultVisit( node );
			return;
		}

		Diagnostics.Add( Diagnostic.Create( BlacklistDescriptor, node.GetLocation(), fullyQualifiedName ) );

		base.DefaultVisit( node );
	}
}
