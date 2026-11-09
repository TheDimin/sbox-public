using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace EngineToolTests;

[TestClass]
[DoNotParallelize]
public class EngineToolAvailabilityTests
{
	[TestMethod]
	public void LinuxMaterialEditorRequiresBundledNativeLibrary()
	{
		if ( !OperatingSystem.IsLinux() ) Assert.Inconclusive( "Linux native library path test." );
		var original = Environment.CurrentDirectory;
		var root = Path.Combine( Path.GetTempPath(), "sbox-met-test-" + Guid.NewGuid().ToString( "N" ) );
		Directory.CreateDirectory( root );
		try
		{
			Environment.CurrentDirectory = root;
			Assert.IsFalse( Editor.EngineTools.IsAvailable( "met" ) );
			Assert.IsTrue( Editor.EngineTools.IsAvailable( "another-tool" ) );
			var path = Path.Combine( root, NetCore.NativeDllPath, "tools", "libmet.so" );
			Directory.CreateDirectory( Path.GetDirectoryName( path )! );
			File.WriteAllBytes( path, Array.Empty<byte>() );
			Assert.IsTrue( Editor.EngineTools.IsAvailable( "MeT" ) );
			Editor.EngineTools.SetUnavailable( "met" );
			Assert.IsFalse( Editor.EngineTools.IsAvailable( "met" ) );
		}
		finally
		{
			Editor.EngineTools.SetAvailable( "met" );
			Environment.CurrentDirectory = original;
			Directory.Delete( root, true );
		}
	}
}
