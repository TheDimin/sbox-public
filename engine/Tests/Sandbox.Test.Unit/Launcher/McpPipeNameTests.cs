using Sandbox.Mcp.Transport;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Sandbox.Tests;

[TestClass]
public class McpPipeNameTests
{
	[TestMethod]
	public void LinuxEndpointUsesKernelStartTime()
	{
		if ( !OperatingSystem.IsLinux() )
			Assert.Inconclusive( "Linux process identity regression." );

		using var process = Process.GetCurrentProcess();
		// Read field 22 independently of Process.StartTime's wall-clock conversion.
		var stat = File.ReadAllText( $"/proc/{process.Id}/stat" );
		var match = Regex.Match( stat, @"^\d+ \(.*\) (?:\S+ ){19}(\d+)", RegexOptions.Singleline );
		Assert.IsTrue( match.Success );
		var startTicks = long.Parse( match.Groups[1].Value, CultureInfo.InvariantCulture );
		Assert.AreEqual( $"sbox-mcp-{process.Id}-{startTicks:x}", McpWire.PipeName( process ) );
	}

	[TestMethod]
	public void EndpointDistinguishesReusedProcessIds()
	{
		Assert.AreNotEqual( McpWire.PipeName( 123, 100 ), McpWire.PipeName( 123, 101 ) );
	}
}
