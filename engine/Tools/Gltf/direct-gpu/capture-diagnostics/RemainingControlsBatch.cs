using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Sandbox;

namespace Sandbox.Gltf.DirectGpu.MeshPrimitiveModesValidation;

/// <summary>Two previously incomplete real-render controls; no prior completed case rerun.</summary>
internal static class RemainingControlsBatch
{
	public sealed record Result( string Name, bool Passed, string Error, object[] Measurements, object Batch = null );
	public static string JournalPath { get; set; }
	public static async Task<List<Result>> Run( string root, CancellationToken cancellation )
	{
		await MainThread.Wait();
		var rows = new List<Result>(); var events = new List<object>(); var clock = Stopwatch.StartNew();
		CaptureShaderBatch batch = null; string terminal = "running"; string cleanupError = null;
		void Publish( string state, string name = null, string error = null )
		{
			events.Add( new { state, name, error, elapsed_ms = clock.Elapsed.TotalMilliseconds, utc = DateTimeOffset.UtcNow.ToString( "O" ) } );
			var document = new { terminal, cleanup_error = cleanupError, completed_cases = rows, events, compile_count = batch?.CompileCount ?? 0,
				reuse_count = batch?.ReuseCount ?? 0, compile_ms = batch?.CompileMilliseconds ?? 0, shader_sha256 = batch?.SourceSha256, shader_released = batch?.Released ?? false,
				scope = "Checkpoint only; final case acceptance requires terminal cleanup and native receipt." };
			Directory.CreateDirectory( Path.GetDirectoryName( JournalPath ) ); var temp = JournalPath + ".tmp";
			File.WriteAllText( temp, JsonSerializer.Serialize( document ) ); File.Move( temp, JournalPath, true );
		}
		async Task Case( string name, Func<Task> body, Func<IReadOnlyList<object>> getMeasurements )
		{
			cancellation.ThrowIfCancellationRequested(); Publish( "case_started", name );
			try { await body(); await MainThread.Wait(); rows.Add( new( name, true, null, getMeasurements().ToArray() ) ); Publish( "case_completed", name ); }
			catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested ) { Publish( "case_cancelled", name ); throw; }
			catch ( Exception e ) { rows.Add( new( name, false, e.ToString(), getMeasurements().ToArray() ) ); Publish( "case_failed", name, e.ToString() ); }
		}
		using var owner = new CaptureShaderBatch( cancellation, state => Publish( state ) );
		batch = owner;
		try
		{
			Publish( "batch_started" ); cancellation.ThrowIfCancellationRequested();
			MergedDrawAttributeTests.Configure( root ); MergedLineBlendTests.Configure( root );
			await Case( "SharedMaterialDistinctDrawAttributeAndMutation", () => new MergedDrawAttributeTests().SharedMaterialDistinctDrawAttributeAndMutation(), () => MergedDrawAttributeTests.Measurements );
			await Case( "FlaggedLineBlendOpacityAndRestore", () => new MergedLineBlendTests().FlaggedLineBlendOpacityAndRestore(), () => MergedLineBlendTests.Measurements );
			if ( rows.TrueForAll( x => x.Passed ) && (batch.CompileCount != 1 || batch.ReuseCount != 1 || batch.SourceSha256 != "bf6c5e6f16c801a9892a485a53bc4c6b6bef85d4a86c98232af8139187630ed9") ) throw new InvalidOperationException( "Expected one compile and one exact-source reuse." );
			terminal = rows.TrueForAll( x => x.Passed ) ? "complete" : "failed";
		}
		catch ( OperationCanceledException ) when ( cancellation.IsCancellationRequested ) { terminal = "cancelled"; throw; }
		catch { terminal = "error"; throw; }
		finally
		{
			await MainThread.Wait();
			try { batch?.Dispose(); } catch ( Exception e ) { terminal = "cleanup_failed"; cleanupError = e.ToString(); throw; }
			finally { Publish( "batch_cleanup_completed" ); }
		}
		var stats = new { compile_count = batch.CompileCount, reuse_count = batch.ReuseCount, compile_ms = batch.CompileMilliseconds, shader_sha256 = batch.SourceSha256, shader_released = batch.Released };
		return rows.Select( x => x with { Batch = stats } ).ToList();
	}
}
