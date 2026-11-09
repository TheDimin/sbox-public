using System;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

/// <summary>Request-scoped borrowed anonymous shader; never a process-wide cache.</summary>
internal sealed class CaptureShaderBatch : IDisposable
{
	private readonly CancellationToken cancellation;
	private readonly Action<string> progress;
	private Shader shader;
	private byte[] source;
	private bool disposed;
	private static CaptureShaderBatch active;
	public static CancellationToken Cancellation => active?.cancellation ?? throw new InvalidOperationException( "No active validation shader batch." );
	public int CompileCount { get; private set; }
	public int ReuseCount { get; private set; }
	public double CompileMilliseconds { get; private set; }
	public string SourceSha256 { get; private set; }
	public bool Released { get; private set; }
	public CaptureShaderBatch( CancellationToken token, Action<string> report )
	{
		ThreadSafe.AssertIsMainThread();
		if ( active is not null ) throw new InvalidOperationException( "Nested shader batch." );
		cancellation = token; progress = report ?? throw new ArgumentNullException( nameof( report ) ); active = this;
	}
	public static async Task<Shader> AcquireAsync( byte[] bytes )
	{
		var batch = active ?? throw new InvalidOperationException( "No active validation shader batch." );
		await MainThread.Wait(); batch.cancellation.ThrowIfCancellationRequested();
		if ( batch.disposed ) throw new ObjectDisposedException( nameof( CaptureShaderBatch ) );
		if ( batch.shader is not null )
		{
			if ( !bytes.AsSpan().SequenceEqual( batch.source ) ) throw new InvalidOperationException( "Different shader bytes require a separate batch." );
			batch.ReuseCount++; batch.progress( "shader_reused" ); return batch.shader;
		}
		// Native compiler work cannot be interrupted in flight. Await its own
		// completion/cancellation, including worker cleanup, before disposing.
		using var linked = CancellationTokenSource.CreateLinkedTokenSource( batch.cancellation ); linked.CancelAfter( TimeSpan.FromSeconds( 90 ) );
		var watch = Stopwatch.StartNew(); Shader candidate = null;
		try
		{
			batch.CompileCount++; batch.progress( "shader_compile_started" ); candidate = await Shader.CompileAsync( Encoding.UTF8.GetString( bytes ), linked.Token );
			await MainThread.Wait(); batch.cancellation.ThrowIfCancellationRequested();
			if ( candidate is not { IsValid: true, IsAnonymous: true } ) throw new InvalidOperationException( "Anonymous shader compilation failed." );
			batch.source = (byte[])bytes.Clone(); batch.SourceSha256 = Convert.ToHexStringLower( SHA256.HashData( bytes ) );
			batch.shader = candidate; candidate = null; return batch.shader;
		}
		finally { watch.Stop(); batch.CompileMilliseconds += watch.Elapsed.TotalMilliseconds; await MainThread.Wait(); candidate?.Destroy(); batch.progress( "shader_compile_finished" ); }
	}
	public void Dispose()
	{
		ThreadSafe.AssertIsMainThread();
		if ( disposed ) return;
		if ( active != this ) throw new InvalidOperationException( "Shader batch ownership lost." );
		disposed = true;
		try { shader?.Destroy(); }
		finally { shader = null; source = null; active = null; Released = true; }
	}
}
