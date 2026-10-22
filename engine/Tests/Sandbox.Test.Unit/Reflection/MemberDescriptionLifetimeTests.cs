using Sandbox.Internal;

namespace Sandbox.Tests;

[TestClass]
public class MemberDescriptionLifetimeTests
{
	[Expose]
	public class Parent
	{
		public int Value { get; set; }
		public int Echo( int value ) => value;
		public string Echo( string value ) => value;
	}

	[Expose]
	public sealed class Child : Parent
	{
		public int Field;
		public int Echo( int first, int second ) => first + second;
	}

	[TestMethod]
	public void ActiveMembersDoNotRetainRemovedTypeKeys()
	{
		var library = new TypeLibrary();
		library.AddAssembly( typeof( Child ).Assembly, false );
		var type = library.GetType( typeof( Child ) );
		Assert.IsTrue( type.Members.Length > 0 );
		Assert.IsTrue( type.Members.All( x => x.Ident is null ) );
		Assert.IsTrue( type.Members.All( x => ReferenceEquals( x, library.GetMemberByIdent( x.Identity ) ) ) );
	}

	[TestMethod]
	public void ReloadPreservesInheritedMembersOverloadsAndNumericIdentities()
	{
		var library = new TypeLibrary();
		var assembly = typeof( Child ).Assembly;
		library.AddAssembly( assembly, false );
		var type = library.GetType( typeof( Child ) );
		var members = type.Members.ToArray();
		var identities = members.Select( x => x.Identity ).ToArray();
		var instance = new Child();

		for ( var reload = 0; reload < 3; reload++ )
		{
			library.RemoveAssembly( assembly );
			Assert.IsFalse( type.IsValid );
			Assert.IsTrue( members.All( x => x.MemberInfo is null ) );
			Assert.AreEqual( members.Length, members.Select( x => x.Ident ).Distinct().Count() );
			Assert.IsTrue( members.All( x => !string.IsNullOrEmpty( x.Ident ) ) );

			library.AddAssembly( assembly, false );
			Assert.AreSame( type, library.GetType( typeof( Child ) ) );
			CollectionAssert.AreEquivalent( members, type.Members );
			CollectionAssert.AreEqual( identities, members.Select( x => x.Identity ).ToArray() );
			Assert.IsTrue( members.All( x => x.Ident is null ) );

			var methods = type.Methods.Where( x => x.Name == nameof( Parent.Echo ) ).ToArray();
			Assert.AreEqual( 3, methods.Length );
			Assert.AreEqual( 17, methods.Single( x => x.Parameters.Length == 1 && x.Parameters[0].ParameterType == typeof( int ) ).Invoke( instance, [17] ) );
			Assert.AreEqual( "hello", methods.Single( x => x.Parameters.Length == 1 && x.Parameters[0].ParameterType == typeof( string ) ).Invoke( instance, ["hello"] ) );
			Assert.AreEqual( 5, methods.Single( x => x.Parameters.Length == 2 ).Invoke( instance, [2, 3] ) );
		}
	}
}
