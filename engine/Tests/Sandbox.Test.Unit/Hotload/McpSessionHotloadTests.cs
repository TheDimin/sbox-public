using Editor.Mcp;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;

namespace HotloadTests;

[TestClass]
[DoNotParallelize]
public class McpSessionHotloadTests : HotloadTestBase
{
	[Reset]
	public static object SessionRoot;

	[TestMethod]
	public async Task PendingPipeReadSurvivesHotload()
	{
		var name = "sbox-mcp-hotload-test-" + Guid.NewGuid().ToString( "N" );
		using var cancel = new CancellationTokenSource();
		using var server = new NamedPipeServerStream( name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous );
		using var client = new NamedPipeClientStream( ".", name, PipeDirection.InOut, PipeOptions.Asynchronous );
		using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
		var accepting = server.WaitForConnectionAsync( timeout.Token );
		await client.ConnectAsync( timeout.Token );
		await accepting;
		var session = new McpSession( cancel.Token );
		SessionRoot = session;
		var data = new byte[1];
		var read = server.ReadAsync( data.AsMemory(), session.Cancellation ).AsTask();
		Assert.IsFalse( read.IsCompleted, "The pipe read must be pending during hotload." );
		var result = Hotload( true );
		Assert.IsFalse( result.HasErrors, "Hotload must not traverse runtime cancellation callbacks." );
		Assert.AreSame( session, SessionRoot );
		await client.WriteAsync( new byte[] { 42 }, timeout.Token );
		Assert.AreEqual( 1, await read.WaitAsync( timeout.Token ) );
		Assert.AreEqual( (byte)42, data[0] );
		var secondRead = server.ReadAsync( data.AsMemory(), session.Cancellation ).AsTask();
		cancel.Cancel();
		try
		{
			await secondRead.WaitAsync( TimeSpan.FromSeconds( 10 ) );
			Assert.Fail( "The pending pipe read should be cancelled." );
		}
		catch ( OperationCanceledException ) { }
		Assert.IsTrue( secondRead.IsCanceled );
	}
}
