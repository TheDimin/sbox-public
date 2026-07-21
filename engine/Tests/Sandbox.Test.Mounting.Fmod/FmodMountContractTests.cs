using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Mounting;

namespace Sandbox.Test.Mounting.Fmod;

[TestClass]
public class FmodMountContractTests
{
	[TestMethod]
	public void DedicatedAssemblyImplementsSboxMountContract()
	{
		Assert.AreEqual( "fmod_mount", typeof( FmodMount ).Assembly.GetName().Name );
		Assert.IsTrue( typeof( BaseGameMount ).IsAssignableFrom( typeof( FmodMount ) ) );

		var mount = new FmodMount();
		Assert.AreEqual( "fmod", mount.Ident );
		Assert.AreEqual( "FMOD", mount.Title );
		Assert.IsNull( mount.Manager );
	}
}
