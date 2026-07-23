using Sandbox;
using Sandbox.Utility;

namespace EngineTests;

[TestClass]
[DoNotParallelize]
public class HeadlessClientOptionsTests
{
	private string _previousCommandLine;

	[TestInitialize]
	public void Initialize()
	{
		_previousCommandLine = CommandLine.CommandLineString;
	}

	[TestCleanup]
	public void Cleanup()
	{
		CommandLine.CommandLineString = _previousCommandLine;
		CommandLine.Parse();
	}

	[TestMethod]
	public void ParsesJoinLocalClient()
	{
		SetCommandLine( "sbox.exe -headless -joinlocal +instanceid 7" );

		Assert.IsTrue( HeadlessClientOptions.TryParse( out var options, out var error ), error );
		Assert.AreEqual( 7, options.InstanceId );
		Assert.AreEqual( "local", options.Target );
	}

	[TestMethod]
	public void ParsesLoopbackConnectClient()
	{
		SetCommandLine( "sbox.exe -headless +connect 127.0.0.1:27015 +instanceid 3" );

		Assert.IsTrue( HeadlessClientOptions.TryParse( out var options, out var error ), error );
		Assert.AreEqual( 3, options.InstanceId );
		Assert.AreEqual( "127.0.0.1:27015", options.Target );
	}

	[TestMethod]
	[DataRow( "local", true )]
	[DataRow( "localhost", true )]
	[DataRow( "localhost:27015", true )]
	[DataRow( "127.0.0.1", true )]
	[DataRow( "127.42.0.1:27015", true )]
	[DataRow( "::1", true )]
	[DataRow( "[::1]:27015", true )]
	[DataRow( "192.168.1.10:27015", false )]
	[DataRow( "example.com", false )]
	[DataRow( "76561198000000000", false )]
	[DataRow( "localhost:0", false )]
	[DataRow( "", false )]
	public void ValidatesLoopbackTargets( string target, bool expected )
	{
		Assert.AreEqual( expected, HeadlessClientOptions.IsLoopbackTarget( target ) );
	}

	[TestMethod]
	[DataRow( "sbox.exe -headless +instanceid 1" )]
	[DataRow( "sbox.exe -headless -joinlocal +connect local +instanceid 1" )]
	[DataRow( "sbox.exe -headless -joinlocal +instanceid 0" )]
	[DataRow( "sbox.exe -headless +connect 192.168.1.10 +instanceid 1" )]
	public void RejectsInvalidStartupOptions( string commandLine )
	{
		SetCommandLine( commandLine );

		Assert.IsFalse( HeadlessClientOptions.TryParse( out _, out var error ) );
		Assert.IsFalse( string.IsNullOrWhiteSpace( error ) );
	}

	[TestMethod]
	public void ClampsAndDefaultsFrameRate()
	{
		SetCommandLine( "sbox.exe -headless" );
		Assert.AreEqual( 60, HeadlessClientOptions.GetFrameRate() );

		SetCommandLine( "sbox.exe -headless +headless_fps 2" );
		Assert.AreEqual( 10, HeadlessClientOptions.GetFrameRate() );

		SetCommandLine( "sbox.exe -headless +headless_fps 144" );
		Assert.AreEqual( 144, HeadlessClientOptions.GetFrameRate() );
	}

	[TestMethod]
	public void CreatesDeterministicIdentity()
	{
		Assert.AreEqual( Steam.BaseFakeSteamId + 12, Steam.GetLocalTestSteamId( 12 ) );
		Assert.AreEqual( "Headless Client 12", Steam.GetHeadlessClientName( 12 ) );
	}

	private static void SetCommandLine( string value )
	{
		CommandLine.CommandLineString = value;
		CommandLine.Parse();
	}
}
