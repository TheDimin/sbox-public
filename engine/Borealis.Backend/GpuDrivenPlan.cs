using Borealis.Gpu;
using Borealis.Source2.Shaders;
using System.Threading;

namespace Borealis.Backend;

/// <summary>The fixed part of a cull record for one draw in a resident mesh group.</summary>
internal sealed record ResidentDrawPlan( ResidentDrawPlan.Key Inputs, uint First, uint Transform, uint Kind )
{
	// Use the same variant, material version and attribute inputs as the material tables.
	public readonly record struct Key( GpuMaterial Material, int Version, ShaderVariant Variant, bool PixelRecord,
		int VertexAttributes, int PixelAttributes, bool UncompressedTangentFrame, PooledGeometry Geometry,
		uint Flags, uint StartIndex, uint IndexCount, int BaseVertex );

	// A group can draw in both color and depth, from more than one recording worker.
	// Publish each complete record at once and bound the cache to two entries per draw.
	public sealed class Passes
	{
		ResidentDrawPlan color, depth;

		public ResidentDrawPlan Find( bool depthOnly ) => depthOnly ? Volatile.Read( ref depth ) : Volatile.Read( ref color );

		public void Keep( bool depthOnly, ResidentDrawPlan plan )
		{
			if ( depthOnly ) Volatile.Write( ref depth, plan );
			else Volatile.Write( ref color, plan );
		}
	}
}

/// <summary>
/// The plans kept for one resident list (<see cref="Borealis.Gpu.GpuContext.GpuDrivenCull.Plan"/>), one per kind of call:
/// shader mode, targets, and whether it draws ids or lists. Recording threads never wait for publication.
/// </summary>
internal sealed class GpuDrivenPlans
{
	Slot slots;
	uint firstFrame;

	internal sealed class Slot( GpuDrivenPlan.CallKey key, Slot next )
	{
		internal readonly GpuDrivenPlan.CallKey Key = key;
		internal readonly Slot Next = next;
		internal GpuDrivenPlan Plan;
		uint buildFrame;

		internal bool TryBeginBuild( uint frame )
		{
			var previous = Volatile.Read( ref buildFrame );
			return previous != frame && Interlocked.CompareExchange( ref buildFrame, frame, previous ) == previous;
		}

		internal void Keep( GpuDrivenPlan plan )
		{
			if ( Interlocked.Exchange( ref Plan, plan ) is { } previous ) previous.Retired = true;
		}
	}

	static Slot FindSlot( Slot head, in GpuDrivenPlan.CallKey key )
	{
		for ( var slot = head; slot is not null; slot = slot.Next )
		{
			if ( slot.Key == key ) return slot;
		}

		return null;
	}

	/// <summary>
	/// The plan for this kind of call made at <paramref name="version"/> of the list, or null; one whose records name
	/// visibility runs only while <see cref="GpuDrivenTables.VisibilityEpoch"/> is the one it was made in.
	/// </summary>
	public GpuDrivenPlan Find( in GpuDrivenPlan.CallKey key, int version, int batches, int visibilityEpoch )
		=> Find( key, version, batches, visibilityEpoch, out _ );

	internal GpuDrivenPlan Find( in GpuDrivenPlan.CallKey key, int version, int batches, int visibilityEpoch, out GpuDrivenPath reason )
	{
		var slot = FindSlot( Volatile.Read( ref slots ), key );
		var plan = slot is null ? null : Volatile.Read( ref slot.Plan );
		reason = plan is null ? GpuDrivenPath.NoPlan
			: plan.Version != version ? GpuDrivenPath.PlanVersionChanged
			: plan.Batches != batches ? GpuDrivenPath.PlanBatchCountChanged
			: plan.VisibilityEpoch != visibilityEpoch && (key.Ids || key.Lists || key.Visibility) ? GpuDrivenPath.VisibilityEpochChanged
			: GpuDrivenPath.Resident;
		return reason == GpuDrivenPath.Resident ? plan : null;
	}

	/// <summary>Permit one snapshot per call key per frame. Other calls draw without keeping their snapshot.</summary>
	public Slot TryBeginBuild( in GpuDrivenPlan.CallKey key, uint frame, out bool firstUseDelay )
	{
		// A list replaced by streaming before the next frame cannot reuse a snapshot.
		var first = Volatile.Read( ref firstFrame );
		if ( first == 0 ) first = Interlocked.CompareExchange( ref firstFrame, frame, 0 );
		firstUseDelay = first == 0 || first == frame;
		if ( firstUseDelay ) return null;

		var head = Volatile.Read( ref slots );
		var slot = FindSlot( head, key );
		if ( slot is null )
		{
			var next = new Slot( key, head );
			var observed = Interlocked.CompareExchange( ref slots, next, head );
			slot = ReferenceEquals( observed, head ) ? next : FindSlot( observed, key );
		}

		return slot?.TryBeginBuild( frame ) == true ? slot : null;
	}

	/// <summary>Forget only the plan whose validation failed.</summary>
	public void Drop( GpuDrivenPlan plan )
	{
		var slot = FindSlot( Volatile.Read( ref slots ), plan.Key );
		if ( slot is not null && ReferenceEquals( Interlocked.CompareExchange( ref slot.Plan, null, plan ), plan ) )
			plan.Retired = true;
	}

	/// <summary>Plans kept, for the tests.</summary>
	public int Count
	{
		get
		{
			var count = 0;
			for ( var slot = Volatile.Read( ref slots ); slot is not null; slot = slot.Next )
			{
				if ( Volatile.Read( ref slot.Plan ) is not null ) count++;
			}

			return count;
		}
	}
}

/// <summary>
/// What a GPU-culled call of a resident list made, kept for the next call of the same kind while the list's version is
/// the same: its buckets, each batch's 32-byte record, its multi-draws, and which batches it drew. Before it's used, each
/// bucket's variant and attribute hashes are worked out again from its first batch (what the pass's attributes decide), and
/// each material's version is compared; any difference makes the plan again. Read by every worker; never changed once made.
/// </summary>
internal sealed class GpuDrivenPlan
{
	/// <summary>What a call's kind is: the same key, the same buckets and records for the same list.</summary>
	public readonly record struct CallKey( uint Mode, Format Color, Format Depth, int Samples, bool NoColor, bool Ids, bool Lists, bool Visibility, bool Positions, bool KeepsOrder, bool Meshlets = false, bool MeshTasks = false );

	public CallKey Key;
	public int Version;
	public int Batches;

	/// <summary>The visibility epoch its records' runs were named in (<see cref="GpuDrivenTables.VisibilityEpoch"/>).</summary>
	public int VisibilityEpoch;

	/// <summary>Its buckets' commands and fragments, its multi-draws' slots, and the compacted slots it uses.</summary>
	public int Commands, Fragments, Written, CompactUsed;

	/// <summary>The call's fragments when it was made, where its compacted multi-draws' slots started.</summary>
	public int CompactBase;

	/// <summary>The buckets, as the context's own were when the plan was made.</summary>
	public List<BackendContext.GpuDrivenBucket> Buckets;

	/// <summary>Per bucket: its first batch's material, object attributes and variant (none for a surface's), and its stages' attribute hashes.</summary>
	public (GpuMaterial Material, ShaderParameters Attributes, ShaderVariant Variant, int VertexAttributes, int PixelAttributes)[] Checks;

	/// <summary>Every material the batches draw with, and its version then.</summary>
	public (GpuMaterial Material, int Version)[] Materials;

	/// <summary>Every set of object attributes the batches draw with (their lighting records come from them), and its version then.</summary>
	public (BackendParameters Attributes, int Version)[] Attributes;

	/// <summary>Every geometry the batches draw: the plan is made again once one of them is freed (<see cref="IBackendGeometry.Use"/>).</summary>
	public IBackendGeometry[] Geometries;

	public uint[] BatchRecords;

	/// <summary>
	/// The resident filter's moved words, one per fragment or mesh. Batch records hold byte offsets relative to this array
	/// or <see cref="GpuDrivenTables.NoVisibility"/> for batches every view keeps. Null when there are no moved words.
	/// </summary>
	public uint[] Moved;
	public (int Bucket, BufferHandle Page, IndexFormat Format, int First, int Count)[] Runs;

	/// <summary>By multi-draw: whether it draws forward's compacted triangles, from the frame's compacted buffer rather than an index page.</summary>
	public bool[] Compacted;
	public bool[] Drawn;

	/// <summary>Its multi-draws' words in a resident cull's view region (first command slot, count), made the first time.</summary>
	public uint[] ResidentRunWords;

	/// <summary>
	/// For the resident cull (<see cref="ResidentCullItem"/>): a buffer of its batch records and entry map, written the first
	/// time a setup culls it, its bindless slot, its entries and the entry map's byte offset. Destroyed once it's retired.
	/// Its entry records (<see cref="ResidentCullEntryRecord"/>) and resident mesh batches' headers follow, written on the GPU
	/// by the same setup from the tables in host memory, so the culls read only video memory.
	/// </summary>
	public BufferHandle Records;
	public int RecordsSlot, Entries, EntryMap, EntryRecords, Headers;

	/// <summary>Whether a setup has recorded the expand dispatch that writes its entry records.</summary>
	public bool EntryRecordsWritten;

	/// <summary>
	/// A forward plan that compacts: the prepass plan whose ids its records' entries name (<see cref="ResidentCullEntry.PrepassIds"/>).
	/// A view whose prepass draws with that plan lists from them in the same dispatch; any other reads its prepass's stamps.
	/// </summary>
	public GpuDrivenPlan IdsFrom;

	/// <summary>Replaced or dropped from its list's plans: no call finds it again.</summary>
	public volatile bool Retired;

	/// <summary>
	/// The frame (<see cref="BackendDevice.GpuDrivenFrame"/>) its <see cref="Materials"/>' and <see cref="Attributes"/>' versions
	/// were last found as it was made at and its <see cref="Geometries"/> in use; 0 for never.
	/// </summary>
	public uint CheckedFrame;
}
