using System.Threading.Tasks;

namespace EditorTests;

[TestClass]
[DoNotParallelize]
public class AssetPipelineCompatibilityTests
{
	[TestMethod]
	public void FastEditorPathIsOptIn()
	{
		var previous = Editor.AssetPipelineCompatibility.FastEditorEnabled;

		try
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = false;
			Assert.IsFalse( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );

			Editor.AssetPipelineCompatibility.FastEditorEnabled = true;
			Assert.IsTrue( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );
			Assert.IsFalse( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: false ) );
		}
		finally
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = previous;
		}
	}

	[TestMethod]
	public void LegacyScopeOverridesFastEditorMode()
	{
		var previous = Editor.AssetPipelineCompatibility.FastEditorEnabled;

		try
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = true;
			Assert.IsTrue( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );

			using ( Editor.AssetPipelineCompatibility.ForceLegacy() )
			{
				Assert.IsFalse( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );

				using ( Editor.AssetPipelineCompatibility.ForceLegacy() )
				{
					Assert.IsFalse( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );
				}

				Assert.IsFalse( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );
			}

			Assert.IsTrue( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );
		}
		finally
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = previous;
		}
	}

	[TestMethod]
	public async Task LegacyScopeIsVisibleAcrossWorkerContexts()
	{
		var previous = Editor.AssetPipelineCompatibility.FastEditorEnabled;

		try
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = true;

			using ( Editor.AssetPipelineCompatibility.ForceLegacy() )
			{
				var workerObservedFastPath = await Task.Run(
					() => Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );

				Assert.IsFalse( workerObservedFastPath );
			}

			Assert.IsTrue( Editor.AssetPipelineCompatibility.ShouldUseFastEditorPath( isEditor: true ) );
		}
		finally
		{
			Editor.AssetPipelineCompatibility.FastEditorEnabled = previous;
		}
	}
}
