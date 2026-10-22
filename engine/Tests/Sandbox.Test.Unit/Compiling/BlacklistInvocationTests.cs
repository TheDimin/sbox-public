namespace CompilingTests;

public partial class BlacklistTest
{
	[TestMethod]
	[DataRow( "System.Runtime.CompilerServices.Unsafe.SizeOf<int>()", 0 )]
	[DataRow( "System.Runtime.CompilerServices.Unsafe.As<object>(new object())", 1 )]
	[DataRow( "Identity(System.Runtime.CompilerServices.Unsafe.As<object>(new object()))", 1 )]
	[DataRow( "((System.Func<object, object>)System.Runtime.CompilerServices.Unsafe.As<object>)(new object())", 1 )]
	[DataRow( "System.Runtime.CompilerServices.Unsafe.SizeOf<System.Runtime.CompilerServices.InlineArray4<int>>()", 1 )]
	public void InvocationChecksRetainArgumentsTypeArgumentsAndDelegateTargets( string expression, int expectedErrors )
	{
		var source = $$"""
			static T Identity<T>(T value) => value;
			var result = {{expression}};
			""";

		CompileAndWalk( source, out var diagnostics );

		Assert.AreEqual( expectedErrors, diagnostics.Count,
			string.Join( ", ", diagnostics.Select( x => x.GetMessage() ) ) );
		Assert.IsTrue( diagnostics.All( x => x.Id == "SB500" ) );
	}

	[TestMethod]
	public void RepeatedSymbolsKeepGenericDisplayNamesAndDiagnosticLocations()
	{
		CompileAndWalk( """
			using static System.Runtime.CompilerServices.Unsafe;
			class Consumer
			{
				void Run()
				{
					var first = As<object>( new object() ); var second = As<dynamic>( new object() );
					var third = As<object>( new object() ); var fourth = As<object>( new object() );
					var allowed = SizeOf<int>();
					var alsoAllowed = SizeOf<long>();
				}
			}
			""", out var diagnostics );

		Assert.AreEqual( 3, diagnostics.Count, string.Join( "\n", diagnostics ) );
		Assert.AreEqual( 2, diagnostics.Count( x => x.GetMessage().Contains( "As<object>" ) ) );
		Assert.AreEqual( 1, diagnostics.Count( x => x.GetMessage().Contains( "As<dynamic>" ) ) );
		Assert.AreEqual( 2, diagnostics.Select( x => x.Location.GetMappedLineSpan().StartLinePosition.Line ).Distinct().Count() );
	}
}
