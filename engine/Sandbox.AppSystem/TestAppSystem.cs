using System;
using System.IO;
using System.Runtime;

namespace Sandbox;

public class TestAppSystem : AppSystem
{
	public override void Init()
	{
		GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
		var gameFolder = Environment.GetEnvironmentVariable( "FACEPUNCH_ENGINE", EnvironmentVariableTarget.Process );
		if ( gameFolder is null ) throw new Exception( "FACEPUNCH_ENGINE not found" );

		var nativeDllPath = Path.Combine( gameFolder, NetCore.NativeDllPath );
		var engineLibrary = OperatingSystem.IsWindows() ? "engine2.dll"
			: OperatingSystem.IsMacOS() ? "libengine2.dylib" : "libengine2.so";
		var engineLibraryPath = Path.Combine( nativeDllPath, engineLibrary );

		// Some tests would set FACEPUNCH_ENGINE to a temp dir that got deleted, make it really clear if that happened

		if ( !File.Exists( engineLibraryPath ) )
		{
			throw new Exception( $"FACEPUNCH_ENGINE ({gameFolder}) doesn't contain {engineLibraryPath}" );
		}

		NetCore.InitializeInterop( gameFolder );

		//
		// Put our native dll path first so that when looking up native dlls we'll
		// always use the ones from our folder first
		//
		var path = Environment.GetEnvironmentVariable( "PATH" );
		path = $"{nativeDllPath}{Path.PathSeparator}{path}";
		Environment.SetEnvironmentVariable( "PATH", path );

		CreateGame();

		var createInfo = new AppSystemCreateInfo()
		{
			Flags = AppSystemFlags.IsGameApp | AppSystemFlags.IsUnitTest
		};

		// Opt in to the real renderer for bitmap comparisons; ordinary tests stay headless.
		InitGame( createInfo, Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS" ) == "1"
			? "-vulkan -noassert -novr " + Environment.GetEnvironmentVariable( "SBOX_TEST_GRAPHICS_ARGS" )
			: "" );
	}
}
