extern alias Before;
extern alias After;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO.Pipes;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace HotloadTests;

[TestClass]
[DoNotParallelize]
public class TimerQueueHotloadTests : HotloadTestBase
{
    [Reset] public static object TimerRoot;
    [TestMethod]
    public async Task TimerOwnCallbackUpgradesWithoutTraversingUnrelatedPipeTimer()
    {
        var queueTimer = typeof( System.Threading.Timer ).Assembly.GetType( "System.Threading.TimerQueueTimer" );
        Assert.IsNotNull( queueTimer, "Review the runtime timer queue boundary for this .NET version." );
        Assert.IsNotNull( queueTimer.GetField( "_prev", BindingFlags.Instance | BindingFlags.NonPublic ) );
        Assert.IsNotNull( queueTimer.GetField( "_next", BindingFlags.Instance | BindingFlags.NonPublic ) );
        Assert.IsNotNull( queueTimer.GetField( "_associatedTimerQueue", BindingFlags.Instance | BindingFlags.NonPublic ) );
        Before::TestClass1.Instance = new Before::TestClass1();
        Action action = Before::TestClass1.Instance.CreateAction1( 50 );
        using var timer = new System.Timers.Timer( 60000 );
        timer.AutoReset = false;
        var fired = new TaskCompletionSource( TaskCreationOptions.RunContinuationsAsynchronously );
        timer.Elapsed += (_,_) => { action(); fired.TrySetResult(); };
        timer.Start(); TimerRoot = timer;
        using var cancel = new CancellationTokenSource( TimeSpan.FromSeconds( 20 ) );
        using var timeout = new CancellationTokenSource( TimeSpan.FromSeconds( 10 ) );
        var name="timer-hotload-"+Guid.NewGuid().ToString("N");
        using var server = new NamedPipeServerStream( name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous );
        using var client = new NamedPipeClientStream( ".", name, PipeDirection.InOut, PipeOptions.Asynchronous );
        var accept = server.WaitForConnectionAsync(timeout.Token); await client.ConnectAsync(timeout.Token); await accept;
        var data = new byte[1]; var pending = server.ReadAsync(data.AsMemory(),cancel.Token).AsTask();
        Assert.IsFalse(pending.IsCompleted);
        var result=Hotload(true);
        Assert.IsFalse(result.HasErrors);
        Assert.AreSame(timer,TimerRoot);
        await client.WriteAsync(new byte[]{42},timeout.Token);
        Assert.AreEqual(1,await pending.WaitAsync(timeout.Token)); Assert.AreEqual((byte)42,data[0]);
        timer.Stop(); timer.Interval=20; timer.Start();
        await fired.Task.WaitAsync(timeout.Token);
        Assert.AreEqual(50,After::TestClass1.Instance.IntField);
        Assert.AreEqual(0,Before::TestClass1.Instance.IntField);
    }
}
