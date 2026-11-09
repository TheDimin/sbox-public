using Microsoft.VisualStudio.TestTools.UnitTesting;
using Sandbox;
using System;
using System.IO.Pipes;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;

namespace HotloadTests;

[TestClass]
[DoNotParallelize]
public class SocketEngineHotloadTests
{
	sealed class Root
	{
		public object Engines;
	}

	[TestMethod]
	public async Task SocketEventQueuesAndPendingIoSurviveHotload()
	{
		if ( OperatingSystem.IsWindows() )
			Assert.Inconclusive( "The Unix socket event engine is not used on Windows." );

		using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
		var name = "sbox-socket-hotload-" + Guid.NewGuid().ToString( "N" );
		using var server = new NamedPipeServerStream( name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous );
		using var client = new NamedPipeClientStream( ".", name, PipeDirection.InOut, PipeOptions.Asynchronous );
		var accepting = server.WaitForConnectionAsync( timeout.Token );

		var type = typeof( Socket ).Assembly.GetType( "System.Net.Sockets.SocketAsyncEngine" );
		Assert.IsNotNull( type, "Review the socket hotload boundary for this runtime." );
		var enginesField = type.GetField( "s_engines", BindingFlags.Static | BindingFlags.NonPublic );
		var queueField = type.GetField( "_eventQueue", BindingFlags.Instance | BindingFlags.NonPublic );
		Assert.IsNotNull( enginesField );
		Assert.IsNotNull( queueField );
		var engines = ((Array)enginesField.GetValue( null )).Cast<object>().ToArray();
		var queues = engines.Select( queueField.GetValue ).ToArray();
		var root = new Root { Engines = engines };

		void Reload()
		{
			var hotload = new Hotload();
			hotload.AddUpgrader<Sandbox.Upgraders.AutoSkipUpgrader>();
			hotload.WatchInstance( root );
			// A minimal assembly swap triggers traversal without loading the native engine.
			var before = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "SocketBefore" + Guid.NewGuid().ToString( "N" ) ), AssemblyBuilderAccess.RunAndCollect );
			var after = AssemblyBuilder.DefineDynamicAssembly( new AssemblyName( "SocketAfter" + Guid.NewGuid().ToString( "N" ) ), AssemblyBuilderAccess.RunAndCollect );
			hotload.ReplacingAssembly( before, after );
			Assert.IsFalse( hotload.UpdateReferences().HasErrors );
			for ( var i = 0; i < engines.Length; i++ )
				Assert.AreSame( queues[i], queueField.GetValue( engines[i] ), "The socket thread retains the original queue on its stack." );
		}

		Reload();
		await client.ConnectAsync( timeout.Token );
		await accepting;
		var buffer = new byte[1];
		var reading = server.ReadAsync( buffer.AsMemory(), timeout.Token );
		Reload();
		await client.WriteAsync( new byte[] { 42 }, timeout.Token );
		Assert.AreEqual( 1, await reading );
		Assert.AreEqual( (byte)42, buffer[0] );
	}
}
