using System.Diagnostics;
using System.Threading;

namespace Borealis.Backend;

internal enum GpuDrivenPath
{
	Resident,
	NoResidentList,
	PlansDisabled,
	EmptyResidentPrefix,
	NoPlan,
	ResidentListReplaced,
	FirstUseDelay,
	PlanVersionChanged,
	PlanBatchCountChanged,
	VisibilityEpochChanged,
	PlanInvalid,
	ResidentCullDisabled,
	NoView,
	ResidentPrefixMismatch,
	CullModeMismatch,
	NoResidentAssignment,
	VisibilityMismatch,
	CompactionMismatch,
	ResidentTail,
	Count
}

public sealed partial class BackendDevice
{
	readonly GpuDrivenPathCounter[] gpuDrivenPaths = new GpuDrivenPathCounter[(int)GpuDrivenPath.Count * 2];
	internal volatile bool CaptureGpuDrivenPaths;

	struct GpuDrivenPathCounter
	{
		internal long Calls, Batches, PlannedBatches, RecordingTicks, CullTicks;
	}

	internal void RecordGpuDrivenPath( bool shadow, GpuDrivenPath path, int batches, int plannedBatches, long started, long cullStarted = 0 )
	{
		long finished = Stopwatch.GetTimestamp();
		ref var counter = ref gpuDrivenPaths[(shadow ? (int)GpuDrivenPath.Count : 0) + (int)path];
		Interlocked.Increment( ref counter.Calls );
		Interlocked.Add( ref counter.Batches, batches );
		Interlocked.Add( ref counter.PlannedBatches, plannedBatches );
		Interlocked.Add( ref counter.RecordingTicks, finished - started );
		if ( cullStarted != 0 ) Interlocked.Add( ref counter.CullTicks, finished - cullStarted );
	}

	internal object[] ReadGpuDrivenPaths()
	{
		var rows = new object[gpuDrivenPaths.Length];
		for ( int i = 0; i < rows.Length; i++ )
		{
			ref var row = ref gpuDrivenPaths[i];
			rows[i] = new
			{
				View = i < (int)GpuDrivenPath.Count ? "Main" : "Shadow",
				Path = ((GpuDrivenPath)(i % (int)GpuDrivenPath.Count)).ToString(),
				Calls = Interlocked.Read( ref row.Calls ),
				Batches = Interlocked.Read( ref row.Batches ),
				PlannedBatches = Interlocked.Read( ref row.PlannedBatches ),
				RecordingTicks = Interlocked.Read( ref row.RecordingTicks ),
				CullTicks = Interlocked.Read( ref row.CullTicks )
			};
		}
		return rows;
	}
}
