using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox.Mounting;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Sandbox.Test.Mounting.Fmod;

[TestClass]
public class FmodManagerIntegrationTests
{
	[TestMethod]
	[TestCategory( "Integration" )]
	public void InstalledSdkCanLoadPlayUnloadAndReload()
	{
		if ( !OperatingSystem.IsWindows() )
			Assert.Inconclusive( "The FMOD mount currently supports Windows only." );

		var sdkRoot = Environment.GetEnvironmentVariable( "FMOD_SDK_ROOT" );
		if ( string.IsNullOrWhiteSpace( sdkRoot ) )
			sdkRoot = FmodManager.DefaultSdkRoot;

		var bankDirectory = Path.Combine( sdkRoot, "api", "studio", "examples", "media" );
		if ( !Directory.Exists( bankDirectory ) )
			Assert.Inconclusive( $"FMOD example media was not found under '{sdkRoot}'." );

		Assert.AreEqual( IntPtr.Zero, GetModuleHandle( "fmodstudio.dll" ), "FMOD Studio should not be loaded before the manager starts." );
		Assert.AreEqual( IntPtr.Zero, GetModuleHandle( "fmod.dll" ), "FMOD Core should not be loaded before the manager starts." );

		RunManagerOnce( sdkRoot, bankDirectory, true );
		AssertNativeLibrariesUnloaded();

		RunManagerOnce( sdkRoot, bankDirectory, false );
		AssertNativeLibrariesUnloaded();
	}

	static void RunManagerOnce( string sdkRoot, string bankDirectory, bool playDemo )
	{
		using var manager = new FmodManager( sdkRoot );
		manager.Initialize();

		Assert.IsTrue( manager.IsInitialized );
		Assert.AreNotEqual( IntPtr.Zero, GetModuleHandle( "fmod.dll" ) );
		Assert.AreNotEqual( IntPtr.Zero, GetModuleHandle( "fmodstudio.dll" ) );

		manager.LoadDefaultBanks( bankDirectory );
		Assert.AreEqual( 3, manager.LoadedBanks.Count );

		if ( playDemo )
			manager.PlayOneShot( "event:/Weapons/Explosion" );

		manager.Update();
	}

	static void AssertNativeLibrariesUnloaded()
	{
		Assert.AreEqual( IntPtr.Zero, GetModuleHandle( "fmodstudio.dll" ), "FMOD Studio remained loaded after manager disposal." );
		Assert.AreEqual( IntPtr.Zero, GetModuleHandle( "fmod.dll" ), "FMOD Core remained loaded after manager disposal." );
	}

	[DllImport( "kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW" )]
	static extern IntPtr GetModuleHandle( string moduleName );
}
