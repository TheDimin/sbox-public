using System;
using System.Threading;
using Sandbox.Utility;

namespace Editor;

/// <summary>
/// Compatibility boundary for experimental editor-only asset acceleration.
/// Publishing and other correctness-critical operations can force the legacy path
/// without changing asset formats, compiled outputs, or public APIs.
/// </summary>
internal static class AssetPipelineCompatibility
{
	private static int legacyScopeDepth;

	[ConVar( "asset_pipeline_fast_editor", ConVarFlags.Protected, Help = "Enable experimental editor-only asset pipeline acceleration. Publishing always uses the legacy path." )]
	internal static bool FastEditorEnabled { get; set; }

	internal static bool IsLegacyForced => Volatile.Read( ref legacyScopeDepth ) != 0;

	internal static bool UseFastEditorPath => ShouldUseFastEditorPath( Sandbox.Application.IsEditor );

	internal static bool ShouldUseFastEditorPath( bool isEditor )
	{
		return isEditor && FastEditorEnabled && Volatile.Read( ref legacyScopeDepth ) == 0;
	}

	internal static IDisposable ForceLegacy()
	{
		Interlocked.Increment( ref legacyScopeDepth );
		return new LegacyScope();
	}

	private sealed class LegacyScope : IDisposable
	{
		private int disposed;

		public void Dispose()
		{
			if ( Interlocked.Exchange( ref disposed, 1 ) != 0 )
				return;

			Interlocked.Decrement( ref legacyScopeDepth );
		}
	}
}
