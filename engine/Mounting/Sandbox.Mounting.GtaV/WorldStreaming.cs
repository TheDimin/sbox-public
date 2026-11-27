using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Sandbox;
using Sandbox.Mounting;
using Sandbox.Diagnostics;

namespace GtaV;

internal sealed class WorldLoader : SceneLoader<GtaMount>
{
	static readonly Vector3 StartPosition = new Vector3( 202.8f, -1327.65f, 32 ) * PacketReader.UnitsPerMetre;

	protected override void BuildScene()
	{
		new GameObject( true, "GTA World" ).AddComponent<GtaWorld>();
		var spawn = new GameObject( true, "Player Spawn" ) { WorldPosition = StartPosition };
		spawn.AddComponent<SpawnPoint>();
	}
}

internal sealed record WorldArea( uint File, uint Name, uint Parent, uint Flags, uint Content, BBox Streaming, BBox Entities, string Collision )
{
	// Archive file indices uniquely identify areas within a streaming session.
	public override int GetHashCode() => (int)File;
}

internal sealed class AreaIndex( Func<WorldArea, BBox> queryBounds = null )
{
	const int CellSize = 256;
	readonly Dictionary<(int, int), List<WorldArea>> cells = new();
	readonly List<WorldArea> large = new();

	static int Cell( float value ) => (int)MathF.Floor( value / CellSize );

	public void Add( WorldArea area )
	{
		var box = queryBounds?.Invoke( area ) ?? area.Entities;
		int x0 = Cell( box.Mins.x ), x1 = Cell( box.Maxs.x ), y0 = Cell( box.Mins.y ), y1 = Cell( box.Maxs.y );
		if ( ((long)x1 - x0 + 1) * ((long)y1 - y0 + 1) > 256 )
		{
			large.Add( area );
			return;
		}
		for ( int x = x0; x <= x1; x++ )
			for ( int y = y0; y <= y1; y++ )
			{
				if ( !cells.TryGetValue( (x, y), out var list ) ) cells[(x, y)] = list = new();
				list.Add( area );
			}
	}

	// Test vertical clearance against nearby retail collision bounds without loading collision models.
	public bool NearSurface( Vector3 p, float clearance )
	{
		bool ContainsSurface( WorldArea area )
		{
			var bounds = area.Entities;
			return p.x >= bounds.Mins.x && p.x <= bounds.Maxs.x && p.y >= bounds.Mins.y && p.y <= bounds.Maxs.y
				&& p.z >= bounds.Mins.z - clearance && p.z <= bounds.Maxs.z + clearance;
		}
		return (cells.TryGetValue( (Cell( p.x ), Cell( p.y )), out var local ) && local.Any( ContainsSurface ))
			|| large.Any( ContainsSurface );
	}

	public HashSet<WorldArea> Query( Vector3 p, float radius )
	{
		var result = new HashSet<WorldArea>();
		void Add( List<WorldArea> list )
		{
			foreach ( var area in list )
				if ( (queryBounds?.Invoke( area ) ?? area.Entities).ClosestPoint( p ).DistanceSquared( p ) <= radius * radius ) result.Add( area );
		}
		Add( large );
		for ( int x = Cell( p.x - radius ); x <= Cell( p.x + radius ); x++ )
			for ( int y = Cell( p.y - radius ); y <= Cell( p.y + radius ); y++ )
				if ( cells.TryGetValue( (x, y), out var list ) ) Add( list );
		return result;
	}
}

internal sealed record WorldCatalog( AreaIndex Maps, AreaIndex Collision )
{
	internal readonly List<WorldArea> RenderMaps = new();
	readonly List<(WorldArea Area, bool Default)> allMaps = new();
	readonly List<WorldArea> exteriorCollision = new();
	internal readonly AreaIndex AuthoredRenderIndex = new( area => area.Streaming.AddBBox( area.Entities ) );

	void AddMap( WorldArea area )
	{
		Maps.Add( area );
		RenderMaps.Add( area );
		AuthoredRenderIndex.Add( area );
	}

	internal WorldCatalog WithVariants( GtaMount mount, string active, IReadOnlyList<GtaWorldVariant> variants )
	{
		if ( variants is not { Count: > 0 } ) return this;
		var maps = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );
		var collision = new Dictionary<string, bool>( StringComparer.OrdinalIgnoreCase );
		foreach ( var variant in variants )
		{
			if ( variant is null ) continue;
			bool enabled = string.Equals( variant.Name, active, StringComparison.OrdinalIgnoreCase );
			foreach ( var name in variant.Maps ?? [] )
				if ( !string.IsNullOrWhiteSpace( name ) ) maps[name] = enabled || maps.GetValueOrDefault( name );
			foreach ( var name in variant.Collision ?? [] )
				if ( !string.IsNullOrWhiteSpace( name ) ) collision[name] = enabled || collision.GetValueOrDefault( name );
		}
		var result = new WorldCatalog( new(), new() ) { Coast = Coast };
		foreach ( var (area, enabled) in allMaps )
			if ( maps.GetValueOrDefault( mount.MapNames.GetValueOrDefault( area.File ) ?? "", enabled ) ) result.AddMap( area );
		foreach ( var area in exteriorCollision )
			if ( collision.GetValueOrDefault( area.Collision, true ) ) result.Collision.Add( area );
		return result;
	}

	internal AreaIndex BuildRenderIndex( float scale )
	{
		if ( scale <= 1 ) return AuthoredRenderIndex;
		var index = new AreaIndex( area =>
		{
			var bounds = area.Streaming.AddBBox( area.Entities );
			// Expand about the opposite entity-bound corner to include every possible placement pivot.
			return new BBox( bounds.Mins + (bounds.Mins - area.Entities.Maxs) * (scale - 1),
				bounds.Maxs + (bounds.Maxs - area.Entities.Mins) * (scale - 1) );
		} );
		foreach ( var map in RenderMaps ) index.Add( map );
		return index;
	}
	internal Shoreline[] Coast { get; private set; } = [];

	public static WorldCatalog Read( MountArchive archive )
	{
		using var r = archive.Read( -3, 0, 0 );
		var result = new WorldCatalog( new(), new() );
		int count = r.ReadInt32();
		for ( int i = 0; i < count; i++ )
		{
			uint file = r.ReadUInt32(), name = r.ReadUInt32(), parent = r.ReadUInt32(), flags = r.ReadUInt32(), content = r.ReadUInt32();
			byte group = r.ReadByte(), readable = r.ReadByte();
			var stream = new BBox( r.Vector(), r.Vector() );
			var entities = new BBox( r.Vector(), r.Vector() );
			if ( readable != 0 )
			{
				var area = new WorldArea( file, name, parent, flags, content, stream, entities, null );
				bool enabled = group == 0 && (flags & 1) == 0;
				result.allMaps.Add( (area, enabled) );
				if ( enabled ) result.AddMap( area );
			}
		}
		count = r.ReadInt32();
		for ( int i = 0; i < count; i++ )
		{
			uint file = r.ReadUInt32();
			string name = Path.GetFileNameWithoutExtension( r.Text() );
			uint interior = r.ReadUInt32();
			var box = new BBox( r.Vector(), r.Vector() );
			if ( interior == 0 )
			{
				var area = new WorldArea( file, 0, 0, 0, 0, box, box, name );
				result.exteriorCollision.Add( area );
				result.Collision.Add( area );
			}
		}
		if ( r.BaseStream.Position < r.BaseStream.Length ) result.Coast = Shoreline.Read( r );
		return result;
	}
}

internal readonly record struct WorldEntity( uint Hash, Transform Transform, int Parent, int Level, float Distance, float ChildDistance, uint Children, uint Flags, bool IsStatic, int Priority, bool IsInterior, bool IsStructural, string ModelPath, IReadOnlyList<ArchetypeExtension> Extensions )
{
	internal uint ArchetypeMask { get; init; }
	internal bool IsObject { get; init; }
	internal float RenderRadius { get; init; }
	internal string[] DefaultInteriorSets { get; init; }
	internal bool UsesProp => IsObject || !IsStatic || Extensions.Contains( DestroyExplosion.Instance );

	internal static MapRecords Read( MountArchive archive, uint file )
	{
		using var r = archive.Read( -4, file, 0 );
		var entities = new WorldEntity[r.ReadInt32()];
		for ( int i = 0; i < entities.Length; i++ )
		{
			uint hash = r.ReadUInt32();
			var p = r.Vector();
			var q = new Rotation( r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle() );
			float xy = r.ReadSingle(), z = r.ReadSingle();
			int parent = r.ReadInt32(), level = r.ReadInt32();
			float distance = r.ReadSingle(), childDistance = r.ReadSingle();
			uint children = r.ReadUInt32(), flags = r.ReadUInt32();
			bool isStatic = r.ReadBoolean();
			bool isObject = r.ReadBoolean();
			int priority = r.ReadInt32();
			bool interior = r.ReadBoolean();
			bool structural = r.ReadBoolean();
			float renderRadius = r.ReadSingle();
			uint dictionary = r.ReadUInt32();
			var transform = new Transform( p * PacketReader.UnitsPerMetre, q, new Vector3( xy, xy, z ) );
			string modelPath = r.Text();
			string[] defaults = interior ? new string[r.ReadInt32()] : [];
			for ( int j = 0; j < defaults.Length; j++ ) defaults[j] = r.ReadUInt32().ToString( "x8" );
			entities[i] = new( hash, transform, parent, level, distance, childDistance, children, flags, isStatic, priority, interior, structural, modelPath, archive.GetExtensions( dictionary, hash ) )
			{
				IsObject = isObject,
				RenderRadius = renderRadius,
				DefaultInteriorSets = defaults
			};
		}
		var dependencies = new uint[r.ReadInt32()];
		for ( int i = 0; i < dependencies.Length; i++ ) dependencies[i] = r.ReadUInt32();
		var batches = new MapInstanceBatch[r.ReadInt32()];
		for ( int i = 0; i < batches.Length; i++ ) batches[i] = MapInstanceBatch.Read( r );
		if ( r.BaseStream.Position < r.BaseStream.Length )
		{
			if ( r.BaseStream.Length - r.BaseStream.Position != entities.Length * sizeof( uint ) )
				throw new InvalidDataException( "Invalid placement flag table." );
			for ( int i = 0; i < entities.Length; i++ )
				entities[i] = entities[i] with { ArchetypeMask = r.ReadUInt32() };
		}
		return new MapRecords( entities, dependencies, batches );
	}
}

internal sealed record MapRecords( WorldEntity[] Entities, uint[] PhysicsDictionaries, MapInstanceBatch[] Batches );

internal sealed class StreamingIdleBudget
{
	const long Capacity = 32 * 1024 * 1024;
	readonly LinkedList<(long Bytes, Action Evict)> entries = new();
	long bytes;

	internal LinkedListNode<(long Bytes, Action Evict)> Add( long size, Action evict )
	{
		bytes += size;
		return entries.AddLast( (size, evict) );
	}

	internal void Remove( LinkedListNode<(long Bytes, Action Evict)> node )
	{
		bytes -= node.Value.Bytes;
		entries.Remove( node );
	}

	internal void Trim()
	{
		while ( (bytes > Capacity || entries.Count > 256) && entries.First is { } oldest )
		{
			Remove( oldest );
			oldest.Value.Evict();
		}
	}

	internal void Clear()
	{
		while ( entries.First is { } oldest )
		{
			Remove( oldest );
			oldest.Value.Evict();
		}
	}
}

internal sealed class PlacementCache( MountArchive archive, StreamingIdleBudget budget )
{
	readonly Dictionary<uint, Entry> entries = new();

	sealed class Entry( Task<MapRecords> task )
	{
		internal readonly Task<MapRecords> Task = task;
		internal int Users;
		internal long Bytes;
		internal LinkedListNode<(long Bytes, Action Evict)> IdleNode;
	}

	internal Task<MapRecords> Acquire( uint file )
	{
		if ( !entries.TryGetValue( file, out var entry ) )
			entries.Add( file, entry = new( Task.Run( () => WorldEntity.Read( archive, file ) ) ) );
		if ( entry.IdleNode is { } node )
		{
			budget.Remove( node );
			entry.IdleNode = null;
		}
		entry.Users++;
		return entry.Task;
	}

	internal void Release( uint file )
	{
		var entry = entries[file];
		if ( --entry.Users != 0 ) return;
		if ( !entry.Task.IsCompletedSuccessfully )
		{
			entries.Remove( file );
			_ = entry.Task.ContinueWith( task => { _ = task.Exception; }, TaskScheduler.Default );
			return;
		}

		if ( entry.Bytes == 0 )
		{
			var records = entry.Task.Result;
			// Archetype extensions are shared by the archive. Charge only map-owned data here.
			entry.Bytes = 64L + records.Entities.LongLength * System.Runtime.CompilerServices.Unsafe.SizeOf<WorldEntity>()
				+ records.PhysicsDictionaries.LongLength * sizeof( uint );
			foreach ( var entity in records.Entities )
				if ( entity.ModelPath.Length != 0 ) entry.Bytes += 24L + entity.ModelPath.Length * sizeof( char );
			foreach ( var batch in records.Batches )
				entry.Bytes += 96L + batch.Transforms.LongLength * System.Runtime.CompilerServices.Unsafe.SizeOf<Transform>();
		}
		entry.IdleNode = budget.Add( entry.Bytes, () => entries.Remove( file ) );
		budget.Trim();
	}
}

internal sealed class FocusSet : List<Vector3>
{
	internal bool UpdateQuery( IReadOnlyList<Vector3> next, float step )
	{
		bool moved = Count != next.Count;
		for ( int i = 0; !moved && i < Count; i++ )
			moved = this[i] != next[i] && this[i].DistanceSquared( next[i] ) >= step * step;
		if ( !moved ) return false;
		Clear();
		AddRange( next );
		return true;
	}

	internal HashSet<WorldArea> Query( AreaIndex index, float radius )
	{
		var wanted = new HashSet<WorldArea>();
		foreach ( var p in this ) wanted.UnionWith( index.Query( p, radius ) );
		return wanted;
	}

	internal float DistanceSquared( BBox bounds )
	{
		float best = float.PositiveInfinity;
		foreach ( var p in this ) best = Math.Min( best, bounds.ClosestPoint( p ).DistanceSquared( p ) );
		return best;
	}

	internal float DistanceSquared( Vector3 point )
	{
		float best = float.PositiveInfinity;
		foreach ( var p in this ) best = Math.Min( best, point.DistanceSquared( p ) );
		return best;
	}
}

internal interface ISubStreamSystem : IDisposable
{
	bool WorkReady { get; }
	void Update( WorldCatalog catalog, IReadOnlyList<Vector3> positions );
}

internal sealed class WorldSession : IGtaWorldSession
{
	internal static readonly Logger StreamingLog = new( "GTA world" );
	readonly GameObject owner;
	readonly Scene scene;
	readonly Task<WorldCatalog> catalog;
	readonly GtaMount host;
	WorldCatalog selectedCatalog;
	int variantSignature;
	bool catalogChanged;

	public void SetWorldVariants( string active, IReadOnlyList<GtaWorldVariant> variants )
	{
		if ( !catalog.IsCompletedSuccessfully ) return;
		var signature = new HashCode();
		signature.Add( active, StringComparer.OrdinalIgnoreCase );
		foreach ( var variant in variants ?? [] )
		{
			if ( variant is null ) continue;
			signature.Add( variant.Name, StringComparer.OrdinalIgnoreCase );
			signature.Add( variant.Maps?.Count ?? 0 );
			foreach ( var name in variant.Maps ?? [] ) signature.Add( name, StringComparer.OrdinalIgnoreCase );
			signature.Add( variant.Collision?.Count ?? 0 );
			foreach ( var name in variant.Collision ?? [] ) signature.Add( name, StringComparer.OrdinalIgnoreCase );
		}
		int next = signature.ToHashCode();
		if ( selectedCatalog is not null && next == variantSignature ) return;
		variantSignature = next;
		selectedCatalog = catalog.Result.WithVariants( host, active, variants );
		catalogChanged = true;
	}
	readonly Dictionary<Shoreline, GameObject> waterBodies = new();
	CoastalSurface distantSea;
	Material seaMaterial;
	WaterSurfaceBatch nearSea;
	readonly ShoreFoam surf = new();
	readonly PriorityQueue<Shoreline, float> pendingWater = new();
	readonly FocusSet waterCamera = new(), waterPlayers = new();
	readonly RenderStreamSystem render;
	readonly PhysicsStreamSystem physics;
	readonly ISubStreamSystem props;
	readonly FocusSet renderOrigin = new(), collisionOrigin = new(), propOrigin = new(), ladderOrigin = new();
	readonly LadderStreamSystem ladders;
	readonly PlacementHierarchy placements;

	internal WorldSession( GtaMount host, MountArchive archive, GameObject root )
	{
		this.host = host;
		owner = root;
		scene = root.Scene;
		catalog = host.GetWorldCatalog( archive );
		var idleBudget = host.IdleBudget;
		var maps = new PlacementCache( archive, idleBudget );
		var destructibles = new StreamedDestructibles();
		root.Flags |= GameObjectFlags.GroupRoots;
		placements = new PlacementHierarchy( root, destructibles );
		render = Application.IsDedicatedServer ? null : new RenderStreamSystem( host, maps, root, placements );
		ladders = new LadderStreamSystem( maps, root );
		physics = new PhysicsStreamSystem( archive, root, idleBudget );
		props = new PhysicsPlacementStream( host, maps, root, ladders, placements );
	}

	public bool IsCollisionReady( Vector3 worldPosition ) => catalog.IsCompletedSuccessfully
		&& physics.IsReady( worldPosition / PacketReader.UnitsPerMetre );

	bool failed;
	readonly FocusSet physical = new(), interaction = new(), visual = new();
	public void SetCameraView( Vector3 forward ) => render?.SetViewDirection( forward );
	public void SetCollisionTrajectories( IReadOnlyList<(Vector3 Position, Vector3 Velocity)> trajectories )
		=> physics.SetTrajectories( trajectories );
	public void Update( Vector3? camera, IReadOnlyList<Vector3> focuses, IReadOnlyList<Vector3> interactions )
	{
		if ( failed || owner.IsDestroyed || owner.Scene != scene || scene.IsDestroyed || !catalog.IsCompleted ) return;
		using var scope = scene.Push();
		if ( catalog.IsFaulted )
		{
			failed = true;
			StreamingLog.Warning( catalog.Exception );
			return;
		}
		physical.Clear();
		foreach ( var point in focuses ) physical.Add( point / PacketReader.UnitsPerMetre );
		Dispatch( physics, physical, collisionOrigin, 2 );
		Dispatch( props, physical, propOrigin, 4 );
		interaction.Clear();
		foreach ( var point in interactions ) interaction.Add( point / PacketReader.UnitsPerMetre );
		Dispatch( ladders, interaction, ladderOrigin, ladders.WakeDistance );
		visual.Clear();
		if ( camera.HasValue ) visual.Add( camera.Value / PacketReader.UnitsPerMetre );
		if ( render is not null )
		{
			render.TrackCamera( visual.Count == 0 ? null : visual[0], catalog.Result.Collision );
			Dispatch( render, visual, renderOrigin, 4 );
			if ( GtaWorldStreamingSystem.LodDebug && camera is { } eye ) render.DrawLodOverlay( eye );
		}
		if ( render is not null && catalog.Result.Coast.Length > 0 )
			distantSea ??= new CoastalSurface( scene, catalog.Result.Coast );
		if ( waterCamera.UpdateQuery( visual, 16 ) | waterPlayers.UpdateQuery( physical, 16 ) )
		{
			pendingWater.Clear();
			foreach ( var region in catalog.Result.Coast )
			{
				float distance = Math.Min( physical.DistanceSquared( region.Bounds ), visual.DistanceSquared( region.Bounds ) );
				// Only the local LOD needs a wave mesh and a fluid volume for each region.
				// Keep the elevated, spatially batched surface visible until that mesh is ready.
				if ( distance <= 160f * 160f )
				{
					if ( !waterBodies.ContainsKey( region ) ) pendingWater.Enqueue( region, distance );
				}
				else if ( distance > 200f * 200f && waterBodies.Remove( region, out var previous ) )
				{
					distantSea?.Show( region );
					previous.Destroy();
				}
			}
		}
		if ( pendingWater.TryDequeue( out var next, out _ ) && !waterBodies.ContainsKey( next ) )
		{
			if ( render is not null && seaMaterial is null )
			{
				seaMaterial = Material.Load( "materials/water/water.vmat" ).CreateCopy();
				seaMaterial.Set( "g_fRoughness", 0.16f );
			}
			if ( render is not null && WaterSurfaceBatch.UseForGtaWorld && !nearSea.IsValid() )
				nearSea = new GameObject( owner, true, "Near water surface" ).Components.Create<WaterSurfaceBatch>();
			waterBodies.Add( next, next.Create( owner, seaMaterial, nearSea ) );
			distantSea?.Hide( next );
		}
		if ( distantSea is not null ) surf.Update( owner, waterBodies.Count > 0 ? camera : null );
		placements.UpdateMoved( physical, camera );
		catalogChanged = false;
	}

	void Dispatch( ISubStreamSystem stream, FocusSet focus, FocusSet origin, float step )
	{
		bool moved = origin.UpdateQuery( focus, step );
		if ( !moved && !catalogChanged && !stream.WorkReady ) return;
		if ( !moved ) { origin.Clear(); origin.AddRange( focus ); }
		stream.Update( selectedCatalog ?? catalog.Result, focus );
	}
	public void Dispose()
	{
		surf.Dispose();
		distantSea?.Dispose();
		if ( nearSea.IsValid() ) nearSea.GameObject.Destroy();
		foreach ( var water in waterBodies.Values ) water.Destroy();
		waterBodies.Clear();
		pendingWater.Clear();
		render?.Dispose();
		props.Dispose();
		physics.Dispose();
		ladders.Dispose();
		placements.Dispose();
	}
}

internal sealed partial class PhysicsStreamSystem( MountArchive archive, GameObject root, StreamingIdleBudget budget ) : ISubStreamSystem
{
	readonly float radius = root.Scene.IsEditor ? 40f : 80f;
	const int CellSize = 32;
	const int MaxTriangles = 512;
	static readonly uint[] ChunkIndices = Enumerable.Range( 0, MaxTriangles * 3 ).Select( i => (uint)i ).ToArray();

	sealed class Chunk( BBox bounds, PhysicsBodyBuilder body, int vertices )
	{
		internal readonly BBox Bounds = bounds;
		internal PhysicsBodyBuilder Body = body;
		internal int Vertices = vertices;
		internal string CacheKey;
		internal bool Cached;
		internal PhysicsGroupDescription Physics;
		internal LinkedListNode<(long Bytes, Action Evict)> IdlePhysics;
		internal GameObject Object;
		internal bool Failed;
	}

	readonly Dictionary<WorldArea, Chunk[]> loaded = new();
	readonly StreamingIdleBudget nativeIdle = new();
	readonly Dictionary<uint, (Chunk[] Chunks, LinkedListNode<(long Bytes, Action Evict)> Node)> idle = new();
	readonly PriorityQueue<(WorldArea Area, int Index), (float Deadline, float Distance)> queue = new();
	HashSet<WorldArea> wanted = new();
	readonly FocusSet last = new();
	readonly Dictionary<WorldArea, Task<Chunk[]>> requests = new();
	readonly List<WorldArea> completed = new();
	WorldCatalog collisionCatalog;
	IReadOnlyList<(Vector3 Position, Vector3 Velocity)> trajectories = [];

	internal void SetTrajectories( IReadOnlyList<(Vector3 Position, Vector3 Velocity)> value ) => trajectories = value;

	public bool WorkReady => queue.Count > 0 || requests.Values.Any( task => task.IsCompleted ) ||
		(requests.Count < 2 && wanted.Count > loaded.Count + requests.Count);

	(float Deadline, float Distance) Priority( BBox bounds )
	{
		const float lookAhead = 1.5f;
		const float corridor = 3f;
		float best = float.PositiveInfinity;
		foreach ( var (worldPosition, worldVelocity) in trajectories )
		{
			var position = worldPosition / PacketReader.UnitsPerMetre;
			var velocity = worldVelocity / PacketReader.UnitsPerMetre;
			float enter = 0, exit = lookAhead;
			if ( IntersectAxis( position.x, velocity.x, bounds.Mins.x - corridor, bounds.Maxs.x + corridor, ref enter, ref exit )
				&& IntersectAxis( position.y, velocity.y, bounds.Mins.y - corridor, bounds.Maxs.y + corridor, ref enter, ref exit )
				&& IntersectAxis( position.z, velocity.z, bounds.Mins.z - 4, bounds.Maxs.z + 4, ref enter, ref exit ) )
				best = Math.Min( enter, best );
		}
		return (best, last.DistanceSquared( bounds ));
	}

	static bool IntersectAxis( float point, float speed, float min, float max, ref float enter, ref float exit )
	{
		if ( MathF.Abs( speed ) < 0.001f ) return point >= min && point <= max;
		float a = (min - point) / speed, b = (max - point) / speed;
		enter = Math.Max( enter, Math.Min( a, b ) );
		exit = Math.Min( exit, Math.Max( a, b ) );
		return enter <= exit;
	}

	internal bool IsReady( Vector3 point )
	{
		if ( last.DistanceSquared( point ) > radius * radius ) return false;
		bool BelowFocus( BBox bounds )
		{
			var nearest = bounds.ClosestPoint( point );
			return nearest.x >= point.x - 2 && nearest.x <= point.x + 2
				&& nearest.y >= point.y - 2 && nearest.y <= point.y + 2
				&& bounds.Mins.z <= point.z + 2 && nearest.DistanceSquared( point ) <= radius * radius;
		}

		bool hasSurface = false;
		foreach ( var area in wanted )
		{
			if ( !BelowFocus( area.Entities ) ) continue;
			if ( !loaded.TryGetValue( area, out var chunks ) || chunks is null ) return false;
			foreach ( var chunk in chunks )
			{
				if ( !BelowFocus( chunk.Bounds ) ) continue;
				if ( !chunk.Object.IsValid() ) return false;
				hasSurface = true;
			}
		}
		return hasSurface;
	}

	public void Update( WorldCatalog catalog, IReadOnlyList<Vector3> positions )
	{
		bool moved = last.UpdateQuery( positions, 2 );
		moved |= !ReferenceEquals( collisionCatalog, catalog );
		collisionCatalog = catalog;
		if ( moved )
		{
			wanted = last.Query( catalog.Collision, radius );
			foreach ( var (position, velocity) in trajectories )
			{
				var origin = position / PacketReader.UnitsPerMetre;
				var step = (velocity / PacketReader.UnitsPerMetre * 0.5f).ClampLength( radius );
				for ( int i = 1; i <= 3; ++i )
					wanted.UnionWith( catalog.Collision.Query( origin + step * i, radius ) );
			}
			queue.Clear();
			foreach ( var area in loaded.Keys.ToArray() )
			{
				if ( !wanted.Contains( area ) )
				{
					Retain( area.File, loaded[area] );
					loaded.Remove( area );
				}
				else Select( area );
			}
		}
		foreach ( var (area, task) in requests )
			if ( task.IsCompleted ) completed.Add( area );
		foreach ( var area in completed )
		{
			requests.Remove( area, out var task );
			if ( task.IsFaulted ) WorldSession.StreamingLog.Warning( task.Exception );
			if ( wanted.Contains( area ) )
			{
				if ( loaded.GetValueOrDefault( area ) is { } previous )
				{
					if ( task.IsCompletedSuccessfully && task.Result.Length == previous.Length )
					{
						for ( int i = 0; i < previous.Length; i++ )
						{
							if ( previous[i].Physics.IsValid() || previous[i].Cached ) continue;
							previous[i].Body = task.Result[i].Body;
							previous[i].Vertices = task.Result[i].Vertices;
						}
					}
					else
					{
						// Keep any working cached chunks when the source cannot be read.
						foreach ( var chunk in previous )
							if ( chunk.Body is null && !chunk.Cached && !chunk.Physics.IsValid() ) chunk.Failed = true;
					}
				}
				else loaded[area] = task.IsCompletedSuccessfully ? task.Result : null;
				Select( area );
			}
		}
		completed.Clear();
		while ( requests.Count < 2 )
		{
			var pending = wanted.Where( x => !loaded.ContainsKey( x ) && !requests.ContainsKey( x ) )
				.MinBy( x => (Priority( x.Entities ), x.File) );
			if ( pending is null ) break;
			uint file = pending.File;
			if ( idle.Remove( file, out var retained ) )
			{
				budget.Remove( retained.Node );
				loaded[pending] = retained.Chunks;
				Select( pending );
			}
			else
			{
				bool cache = PhysicsChunkCache.Enabled;
				bool direct = DirectCollision && !cache;
				bool authored = AuthoredCollision && direct;
				requests.Add( pending, Task.Run( () => ReadArea( archive, file, cache, direct, authored ) ) );
			}
		}

		long start = System.Diagnostics.Stopwatch.GetTimestamp();
		while ( queue.TryDequeue( out var item, out _ ) )
		{
			var chunk = loaded[item.Area][item.Index];
			if ( chunk.Object.IsValid() || chunk.Failed ) continue;
			try
			{
				if ( chunk.IdlePhysics is { } idlePhysics )
				{
					nativeIdle.Remove( idlePhysics );
					chunk.IdlePhysics = null;
				}
				if ( chunk.Cached && !chunk.Physics.IsValid() )
				{
					chunk.Physics = PhysicsChunkCache.Load( chunk.CacheKey, item.Index );
					chunk.Cached = chunk.Physics.IsValid();
				}
				if ( !chunk.Physics.IsValid() && chunk.Body is null )
				{
					if ( !requests.ContainsKey( item.Area ) )
					{
						if ( requests.Count >= 2 )
						{
							queue.Enqueue( item, Priority( chunk.Bounds ) );
							break;
						}
						uint file = item.Area.File;
						requests.Add( item.Area, Task.Run( () => ReadChunks( archive, file ) ) );
					}
					continue;
				}
				var physics = chunk.Physics;
				if ( !physics.IsValid() && chunk.CacheKey is { } key )
				{
					physics = PhysicsChunkCache.Cook( key, item.Index, chunk.Body );
					chunk.Physics = physics;
					chunk.Cached = physics.IsValid() && PhysicsChunkCache.HasShape( key, item.Index );
				}
				if ( chunk.Cached ) chunk.Body = null;
				if ( !physics.IsValid() )
				{
					using var stage = new StreamMeasurements.Stage( "collision.build_runtime" );
					physics = PhysicsGroupDescription.Create( $"gtav/{item.Area.Collision}/{item.Index}.vphys", [chunk.Body] );
					if ( RetainNativeCollision ) chunk.Physics = physics;
				}
				chunk.Object = new GameObject( root, false, item.Area.Collision );
				chunk.Object.WorldPosition = chunk.Bounds.Center * PacketReader.UnitsPerMetre;
				chunk.Object.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
				var collider = chunk.Object.AddComponent<PhysicsCollider>();
				collider.Static = true;
				collider.Physics = physics;
				using ( new StreamMeasurements.Stage( "collision.attach" ) )
					chunk.Object.Enabled = true;
			}
			catch ( Exception e )
			{
				Detach( chunk );
				chunk.Failed = true;
				WorldSession.StreamingLog.Warning( e );
			}
			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= 1 ) break;
		}
	}

	void Select( WorldArea area )
	{
		if ( loaded[area] is not { } chunks ) return;
		for ( int i = 0; i < chunks.Length; i++ )
		{
			var chunk = chunks[i];
			float distance = last.DistanceSquared( chunk.Bounds );
			var priority = Priority( chunk.Bounds );
			if ( distance > radius * radius && !float.IsFinite( priority.Deadline ) )
			{
				Detach( chunk );
			}
			else if ( chunk.Object is null && !chunk.Failed &&
				(chunk.Body is not null || chunk.Cached || chunk.Physics.IsValid() || !requests.ContainsKey( area )) )
				queue.Enqueue( (area, i), priority );
		}
	}

	static Chunk[] ReadArea( MountArchive archive, uint file, bool cache, bool direct, bool authored )
	{
		if ( !cache ) return DecodeChunks( archive, file, direct, authored );
		var key = PhysicsChunkCache.Key( archive, file );
		if ( PhysicsChunkCache.ReadBounds( key ) is { } bounds )
		{
			var cached = new Chunk[bounds.Length];
			for ( int i = 0; i < bounds.Length; i++ )
				cached[i] = new( bounds[i].Bounds, null, bounds[i].Vertices ) { CacheKey = key, Cached = PhysicsChunkCache.HasShape( key, i ) };
			return cached;
		}

		var chunks = ReadChunks( archive, file );
		foreach ( var chunk in chunks ) chunk.CacheKey = key;
		PhysicsChunkCache.WriteBounds( key, chunks.Select( x => (x.Bounds, x.Vertices) ).ToArray() );
		return chunks;
	}

	static Chunk[] ReadChunks( MountArchive archive, uint file )
	{
		using var stage = new StreamMeasurements.Stage( "collision.decode_chunks" );
		using var r = archive.Read( (int)Kind.Collision, file, 0 );
		int count = r.ReadInt32();
		var trianglesByCell = new Dictionary<(int, int), List<int>>();
		var firstTriangle = r.BaseStream.Position;
		for ( int i = 0; i < count; i++ )
		{
			var centre = (r.Vector() + r.Vector() + r.Vector()) / 3f;
			var key = ((int)MathF.Floor( centre.x / CellSize ), (int)MathF.Floor( centre.y / CellSize ));
			if ( !trianglesByCell.TryGetValue( key, out var triangles ) ) trianglesByCell[key] = triangles = new();
			triangles.Add( i );
		}

		// AddMesh copies its spans. Reuse one vertex buffer for every chunk in the packet.
		var vertices = new Vector3[Math.Min( count, MaxTriangles ) * 3];
		var chunks = new List<Chunk>();
		foreach ( var triangles in trianglesByCell.Values )
		{
			for ( int offset = 0; offset < triangles.Count; offset += MaxTriangles )
			{
				int triangleCount = Math.Min( triangles.Count - offset, MaxTriangles );
				int length = triangleCount * 3;
				for ( int i = 0; i < triangleCount; i++ )
				{
					r.BaseStream.Position = firstTriangle + (long)triangles[offset + i] * 9 * sizeof( float );
					vertices[i * 3] = r.Vector();
					vertices[i * 3 + 1] = r.Vector();
					vertices[i * 3 + 2] = r.Vector();
				}
				var bounds = new BBox( vertices[0], vertices[0] );
				for ( int i = 1; i < length; i++ )
					bounds = bounds.AddPoint( vertices[i] );
				var centre = bounds.Center;
				for ( int i = 0; i < length; i++ )
					vertices[i] = (vertices[i] - centre) * PacketReader.UnitsPerMetre;
				chunks.Add( new( bounds, new PhysicsBodyBuilder().AddMesh( vertices.AsSpan( 0, length ), ChunkIndices.AsSpan( 0, length ), default ), length ) );
			}
		}
		return chunks.ToArray();
	}

	static void Release( Chunk[] chunks )
	{
		if ( chunks is null ) return;
		foreach ( var chunk in chunks ) chunk.Object?.Destroy();
	}

	void Detach( Chunk chunk )
	{
		chunk.Object?.Destroy();
		chunk.Object = null;
		if ( chunk.CacheKey is not null || !chunk.Physics.IsValid() || chunk.IdlePhysics is not null ) return;
		if ( !RetainNativeCollision )
		{
			chunk.Physics = null;
			return;
		}

		// Native resource memory is opaque. Budget for the mesh plus its acceleration data.
		long bytes = 2048 + chunk.Vertices * 64L;
		chunk.IdlePhysics = nativeIdle.Add( bytes, () =>
		{
			chunk.Physics = null;
			chunk.IdlePhysics = null;
		} );
		nativeIdle.Trim();
	}

	void EvictIdleArea( uint file, Chunk[] chunks )
	{
		idle.Remove( file );
		foreach ( var chunk in chunks )
		{
			if ( chunk.IdlePhysics is not { } node ) continue;
			nativeIdle.Remove( node );
			chunk.IdlePhysics = null;
			chunk.Physics = null;
		}
	}

	void Retain( uint file, Chunk[] chunks )
	{
		if ( chunks is null ) return;
		long bytes = 24;
		foreach ( var chunk in chunks )
		{
			Detach( chunk );
			chunk.Failed = false;
			bytes += 256L + chunk.Vertices * 16L;
		}
		if ( idle.TryGetValue( file, out var previous ) )
		{
			budget.Remove( previous.Node );
			EvictIdleArea( file, previous.Chunks );
		}
		idle.Add( file, (chunks, budget.Add( bytes, () => EvictIdleArea( file, chunks ) )) );
		budget.Trim();
	}

	public void Dispose()
	{
		foreach ( var chunks in loaded.Values ) Release( chunks );
		nativeIdle.Clear();
		foreach ( var item in idle.Values ) budget.Remove( item.Node );
		idle.Clear();
		foreach ( var task in requests.Values )
			_ = task.ContinueWith( finished => { _ = finished.Exception; }, TaskScheduler.Default );
		requests.Clear();
		loaded.Clear();
		queue.Clear();
	}
}

internal abstract class PlacementStream( GtaMount host, PlacementCache cache, GameObject root, float radius ) : ISubStreamSystem
{
	protected virtual (float Radius, float Scale) Selection => (radius, 1f);
	(float Radius, float Scale) appliedSelection;
	protected virtual float QueryStep => 4f;
	protected virtual bool RequiredOnly => false;
	bool appliedCap;
	public virtual bool WorkReady => RequiredOnly != appliedCap || Selection != appliedSelection || request?.IsCompleted == true || retiring.Count > 0 ||
		(pendingPlacements.Values.Any( x => x.Ready ) || (pendingPlacements.Count < PlacementReadWindow && queue.Count > 0));
	protected virtual bool TryGetModel( AssetLoader asset, WorldEntity entity, out Model model )
	{
		model = asset.PreparedModel;
		return model is { IsValid: true };
	}
	protected virtual PreparedModelData Prepare( AssetLoader asset, WorldEntity entity ) => asset.ReadModel();
	protected virtual Model Complete( AssetLoader asset, PreparedModelData packet, WorldEntity entity ) => asset.LoadPrepared( packet );
	protected abstract GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner, LodEntityId id );
	protected abstract GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner );
	protected virtual bool Eligible( WorldEntity e ) => !appliedCap || e.Priority == 0 || e.IsStatic || e.IsInterior || e.Children > 0;
	protected virtual bool InRange( WorldEntity e, float d ) => e.Level is 0 or 5;
	protected readonly Dictionary<WorldArea, WorldEntity[]> loaded = new();
	protected virtual void ResetGroups() { }
	protected virtual void IncludeRelated( WorldArea map, int index, float distance ) { }
	protected virtual void ResolveLinks( WorldArea map, int index ) { }
	protected virtual void MapLoaded( WorldArea map, MapRecords records ) { }
	protected virtual void MapRemoved( WorldArea map ) { }
	protected GameObject WorldRoot => root;
	protected readonly Dictionary<(WorldArea, int), GameObject> active = new();
	readonly PriorityQueue<(WorldArea, int), (int Rank, float ViewCost, float Distance, uint File, int Index)> queue = new();
	readonly Queue<GameObject> retiring = new();
	protected virtual void Retire( GameObject obj ) => retiring.Enqueue( obj );
	protected virtual long StartLoad() => 0;
	protected virtual bool PipelineModelReads => false;
	sealed class UpcomingModel( Task<PreparedModelData> packet, WorldEntity entity )
	{
		internal readonly Task<PreparedModelData> Packet = packet;
		internal readonly WorldEntity Entity = entity;
		internal Queue<(AssetLoader Asset, Task<BinaryReader> Read)> Textures;
		internal void Release()
		{
			ReleaseRead( Packet );
			if ( Textures is not null )
				foreach ( var item in Textures ) ReleaseRead( item.Read );
		}
	}
	const int ModelReadWindow = 8;
	const int QueueLookahead = 24;
	readonly HashSet<WorldArea> reservedMaps = new();
	readonly Dictionary<string, AssetLoader> modelAssets = new();
	protected AssetLoader ModelAsset( WorldEntity entity )
	{
		if ( entity.ModelPath.Length == 0 ) return null;
		if ( !modelAssets.TryGetValue( entity.ModelPath, out var asset ) )
			modelAssets[entity.ModelPath] = asset = Mount.GetByPath( $"mount://gtav/models/{entity.ModelPath}.vmdl" ) as AssetLoader;
		return asset;
	}
	readonly Dictionary<AssetLoader, UpcomingModel> upcoming = new();
	readonly Dictionary<uint, Dictionary<AssetLoader, WorldEntity>> dictionaryDemand = new();
	readonly Dictionary<uint, DictionaryModelRead> dictionaryReads = new();
	readonly List<((WorldArea, int) Key, (int Rank, float ViewCost, float Distance, uint File, int Index) Priority)> inspected = new( QueueLookahead );

	static void ReleaseRead<T>( Task<T> read ) where T : IDisposable
	{
		if ( read is null ) return;
		if ( read.IsCompletedSuccessfully ) { read.Result.Dispose(); return; }
		_ = read.ContinueWith( t =>
		{
			if ( t.IsCompletedSuccessfully ) t.Result.Dispose();
			else _ = t.Exception;
		}, TaskScheduler.Default );
	}

	void ClearUpcoming()
	{
		foreach ( var model in upcoming.Values ) model.Release();
		upcoming.Clear();
	}

	void StartUpcoming()
	{
		// Only inspect the front of the current demand queue. Never reorder requests or create engine objects here.
		if ( !PipelineModelReads || upcoming.Count >= ModelReadWindow ) return;
		foreach ( var (file, read) in dictionaryReads )
			if ( read.IsClosed ) dictionaryReads.Remove( file );
		try
		{
			while ( inspected.Count < QueueLookahead && upcoming.Count < ModelReadWindow && queue.TryDequeue( out var key, out var priority ) )
			{
				inspected.Add( (key, priority) );
				if ( active.ContainsKey( key ) || !desired.TryGetValue( key, out var d ) || d != priority.Distance ) continue;
				var entity = loaded[key.Item1][key.Item2];
				if ( ModelAsset( entity ) is not { } asset ||
					pendingPlacements.Values.Any( x => x.Asset == asset ) || upcoming.ContainsKey( asset ) || TryGetModel( asset, entity, out _ ) ) continue;
				if ( asset.SourceEntry.Kind == Kind.DictionaryModel ) StartDictionary( asset, entity );
				else upcoming.Add( asset, new( Task.Run( asset.ReadModel ), entity ) );
			}
		}
		finally
		{
			foreach ( var item in inspected ) queue.Enqueue( item.Key, item.Priority );
			inspected.Clear();
		}
	}

	void StartDictionary( AssetLoader first, WorldEntity entity )
	{
		uint file = first.SourceEntry.File;
		dictionaryReads.TryGetValue( file, out var read );
		DictionaryModelRead start = null;
		void Add( AssetLoader asset, WorldEntity placement )
		{
			var member = new DictionaryModelRead.Member( asset.SourceEntry );
			if ( read is null || !read.TryAdd( member ) )
			{
				dictionaryReads[file] = read = start = new( file );
				read.TryAdd( member );
			}
			upcoming.Add( asset, new( member.Completion.Task, placement ) );
		}

		Add( first, entity );
		if ( dictionaryDemand.TryGetValue( file, out var members ) )
		{
			foreach ( var (asset, placement) in members )
			{
				if ( upcoming.ContainsKey( asset ) || pendingPlacements.Values.Any( x => x.Asset == asset ) ||
					TryGetModel( asset, placement, out _ ) ) continue;
				Add( asset, placement );
			}
		}
		if ( start is not null ) host.StartDictionaryModels( start );
	}
	long loadStarted;
	protected long LoadStarted => loadStarted;
	protected virtual void OnAdded( WorldArea map, int index, WorldEntity entity, GameObject obj ) { }
	protected virtual void OnRemoved( WorldArea map, int index ) { }
	HashSet<WorldArea> wanted = new();
	Task<MapRecords> request;
	WorldArea pending;
	readonly PriorityQueue<WorldArea, (float Distance, uint File)> missingMaps = new();
	readonly Queue<(WorldArea Map, Task<MapRecords> Read)> nextMaps = new();
	protected readonly FocusSet last = new();
	protected GtaMount Mount => host;
	protected bool CoverageQueued => queue.TryPeek( out _, out var priority ) && priority.Rank == 0;

	WorldCatalog placementCatalog;
	public virtual void Update( WorldCatalog catalog, IReadOnlyList<Vector3> positions )
	{
		bool moved = last.UpdateQuery( positions, QueryStep );
		moved |= !ReferenceEquals( placementCatalog, catalog );
		placementCatalog = catalog;
		var selection = Selection;
		moved |= selection != appliedSelection || RequiredOnly != appliedCap || MapQueryChanged;
		appliedCap = RequiredOnly;
		appliedSelection = selection;
		if ( moved )
		{
			wanted = QueryMaps( catalog, selection.Radius );
			foreach ( var map in loaded.Keys.Where( x => !wanted.Contains( x ) ).ToArray() )
			{
				cache.Release( map.File );
				MapRemoved( map );
				loaded.Remove( map );
			}
			// Release old reservations before ordering reads for the new focus.
			reservedMaps.Clear();
			for ( int i = nextMaps.Count; i > 0; --i )
			{
				var next = nextMaps.Dequeue();
				if ( wanted.Contains( next.Map ) )
				{
					reservedMaps.Add( next.Map );
					nextMaps.Enqueue( next );
				}
				else cache.Release( next.Map.File );
			}
			if ( request is not null && !wanted.Contains( pending ) )
			{
				cache.Release( pending.File );
				request = null;
			}
			missingMaps.Clear();
			foreach ( var map in wanted )
				if ( !loaded.ContainsKey( map ) && (request is null || map != pending) && !reservedMaps.Contains( map ) )
					missingMaps.Enqueue( map, (MapRequestDistance( map ), map.File) );
			RebuildQueue();
			foreach ( var (asset, model) in upcoming )
			{
				if ( asset.SourceEntry.Kind == Kind.DictionaryModel &&
					dictionaryDemand.TryGetValue( asset.SourceEntry.File, out var members ) && members.ContainsKey( asset ) ) continue;
				model.Release();
				upcoming.Remove( asset );
			}
			foreach ( var key in pendingPlacements.Keys.Where( key => !desired.ContainsKey( key ) ).ToArray() )
			{
				pendingPlacements.Remove( key, out var stale );
				stale.Release();
			}
		}
		int window = PipelineModelReads ? 4 : 1;
		bool receivedMap = false;
		long mapsStart = System.Diagnostics.Stopwatch.GetTimestamp();
		for ( int i = 0; i < window; i++ )
		{
			if ( request?.IsCompleted == true )
			{
				if ( request.IsFaulted ) WorldSession.StreamingLog.Warning( request.Exception );
				var records = request.IsCompletedSuccessfully ? request.Result : new MapRecords( [], [], [] );
				loaded[pending] = records.Entities;
				receivedMap = true;
				MapLoaded( pending, records );
				SelectMap( pending, resolvePendingLinks: true );
				request = null;
			}
			while ( nextMaps.Count + (request is null ? 0 : 1) < window && missingMaps.TryDequeue( out var area, out _ ) )
				nextMaps.Enqueue( (area, cache.Acquire( area.File )) );
			if ( request is null && nextMaps.TryDequeue( out var next ) )
				(pending, request) = next;
			if ( request?.IsCompleted != true || System.Diagnostics.Stopwatch.GetElapsedTime( mapsStart ).TotalMilliseconds >= 1 ) break;
		}
		// Once all parent links are known, queued sibling promotions can refer to the same placement many times.
		if ( receivedMap && request is null && queue.Count > desired.Count ) RankPending();
		for ( int i = 0; i < 4 && retiring.TryDequeue( out var old ); i++ ) old.Destroy();
		// Drain cheap cache hits together. Check the time between activations because
		// a single engine model upload cannot be interrupted.
		var start = System.Diagnostics.Stopwatch.GetTimestamp();
		StartUpcoming();
		foreach ( var (asset, model) in upcoming )
		{
			if ( model.Textures is not null || !model.Packet.IsCompletedSuccessfully ) continue;
			model.Textures = new();
			try { QueueInitialTextures( asset, model.Packet.Result, model.Entity, model.Textures ); }
			catch ( Exception ex )
			{
				// The normal activation path reports malformed packets. Keep its reader at the header.
				if ( model.Packet.Result.Packet is { } packet ) packet.BaseStream.Position = 0;
				WorldSession.StreamingLog.Warning( ex );
			}
			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= 1 ) break;
		}
		double budgetMs = PipelineModelReads ? Math.Clamp( RealTime.Delta * 160.0, 1.0, 12.0 ) : 1.0;
		for ( int i = 0; i < (PipelineModelReads ? 64 : 8); i++ )
		{
			if ( queue.Count == 0 && pendingPlacements.Count == 0 )
			{
				ClearUpcoming();
				break;
			}
			bool hasReady = false;
			(WorldArea, int) ready = default;
			(int Rank, float ViewCost, float Distance, uint File, int Index) best = default;
			foreach ( var (key, placement) in pendingPlacements )
			{
				if ( !placement.Ready ) continue;
				var priority = PlacementPriority( key.Item1, key.Item2, desired[key] );
				if ( hasReady && priority.CompareTo( best ) >= 0 ) continue;
				hasReady = true;
				ready = key;
				best = priority;
			}
			if ( hasReady ) AdvancePlacement( ready, pendingPlacements[ready] );
			else if ( pendingPlacements.Count < PlacementReadWindow && queue.Count > 0 ) ActivateNext();
			else break;
			if ( System.Diagnostics.Stopwatch.GetElapsedTime( start ).TotalMilliseconds >= budgetMs ) break;
		}
	}

	void AdvancePlacement( (WorldArea, int) key, PendingPlacement placement )
	{
		var entity = loaded[key.Item1][key.Item2];
		try
		{
			if ( !placement.Packet.IsCompletedSuccessfully )
			{
				WorldSession.StreamingLog.Warning( placement.Packet.Exception );
				pendingPlacements.Remove( key );
				placement.Release();
				return;
			}
			placement.Textures ??= new();
			if ( !placement.TexturesQueued )
			{
				QueueInitialTextures( placement.Asset, placement.Packet.Result, entity, placement.Textures );
				placement.TexturesQueued = true;
			}
			if ( placement.Textures.TryPeek( out var head ) && head.Read.IsCompleted )
			{
				placement.Textures.Dequeue();
				if ( head.Read.IsCompletedSuccessfully )
				{
					using var packet = head.Read.Result;
					if ( !head.Asset.HasStreamingTexture ) head.Asset.UploadStage( packet, 256 );
				}
				else WorldSession.StreamingLog.Warning( head.Read.Exception );
			}
			if ( placement.Textures.Count > 0 ) return;

			loadStarted = placement.Started;
			var model = TryGetModel( placement.Asset, entity, out var cached )
				? cached : Complete( placement.Asset, placement.Packet.Result, entity );
			Finish( key, entity, ActivateModel( model, entity, root, new( key.Item1.Name, key.Item2 ) ) );
			pendingPlacements.Remove( key );
			placement.Release();
		}
		catch
		{
			pendingPlacements.Remove( key );
			placement.Release();
			throw;
		}
	}

	void ActivateNext()
	{
		if ( !queue.TryDequeue( out var key, out var priority ) ) return;
		if ( active.ContainsKey( key ) || pendingPlacements.ContainsKey( key ) || !desired.TryGetValue( key, out var distance ) || distance != priority.Distance ) return;
		// New parent records can turn a pending coverage request into a refinement.
		int rank = RequestPriority( key.Item1, key.Item2 );
		if ( rank != priority.Rank )
		{
			queue.Enqueue( key, PlacementPriority( key.Item1, key.Item2, distance ) );
			return;
		}
		loadStarted = StartLoad();
		var entity = loaded[key.Item1][key.Item2];
		if ( ModelAsset( entity ) is { } asset )
		{
			if ( TryGetModel( asset, entity, out var model ) )
			{
				if ( upcoming.Remove( asset, out var unused ) ) unused.Release();
				Finish( key, entity, ActivateModel( model, entity, root, new( key.Item1.Name, key.Item2 ) ) );
			}
			else
			{
				if ( PipelineModelReads && asset.SourceEntry.Kind == Kind.DictionaryModel && !upcoming.ContainsKey( asset ) )
				{
					if ( pendingPlacements.Values.Any( x => x.Asset == asset ) )
					{
						queue.Enqueue( key, priority );
						return;
					}
					StartDictionary( asset, entity );
				}
				upcoming.Remove( asset, out var modelRead );
				var read = modelRead?.Packet ?? Task.Run( () => Prepare( asset, entity ) );
				pendingPlacements.Add( key, new( asset, read, modelRead?.Textures, loadStarted ) );
				StartUpcoming();
			}
			return;
		}
		if ( host.Prefabs.TryGetValue( entity.Hash, out var path ) && PrefabFile.Load( path ) is { } prefab )
			Finish( key, entity, Activate( prefab, entity, root ) );
	}


	sealed class PendingPlacement( AssetLoader asset, Task<PreparedModelData> packet, Queue<(AssetLoader Asset, Task<BinaryReader> Read)> textures, long started )
	{
		internal readonly AssetLoader Asset = asset;
		internal readonly Task<PreparedModelData> Packet = packet;
		internal readonly long Started = started;
		internal Queue<(AssetLoader Asset, Task<BinaryReader> Read)> Textures = textures;
		internal bool TexturesQueued = textures is not null;
		internal bool Ready => Packet.IsCompleted && (Textures is null || !Textures.TryPeek( out var head ) || head.Read.IsCompleted);

		internal void Release()
		{
			ReleaseRead( Packet );
			if ( Textures is not null )
				while ( Textures.TryDequeue( out var texture ) ) ReleaseRead( texture.Read );
		}
	}

	int PlacementReadWindow => PipelineModelReads ? ModelReadWindow : 1;
	readonly Dictionary<(WorldArea, int), PendingPlacement> pendingPlacements = new();

	void QueueInitialTextures( AssetLoader asset, PreparedModelData packet, WorldEntity entity, Queue<(AssetLoader, Task<BinaryReader>)> reads )
	{
		if ( GetTextures( asset, packet, entity ) is { } textures )
			host.Textures.ReadMany( textures, reads );
	}
	protected virtual Queue<AssetLoader> GetTextures( AssetLoader asset, PreparedModelData packet, WorldEntity entity ) => null;

	protected readonly Dictionary<(WorldArea, int), float> desired = new();

	void Finish( (WorldArea, int) key, WorldEntity e, GameObject obj )
	{
		if ( obj.IsValid() ) obj.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		if ( obj.IsValid() )
		{
			GtaFlagTags.AddPlacement( obj, e.Flags );
			GtaFlagTags.AddArchetype( obj, e.ArchetypeMask );
		}
		if ( e.IsInterior && obj.IsValid() && obj.GetComponent<GtaInteriorVisibility>() is { } interior )
			interior.EnabledSets.AddRange( e.DefaultInteriorSets ?? [] );
		active[key] = obj;
		if ( obj.IsValid() ) OnAdded( key.Item1, key.Item2, e, obj );
	}

	protected virtual int RequestPriority( WorldArea map, int index ) => 0;
	protected virtual float ViewCost( WorldEntity entity, float distance ) => distance;
	(int Rank, float ViewCost, float Distance, uint File, int Index) PlacementPriority( WorldArea map, int index, float distance )
		=> (RequestPriority( map, index ), ViewCost( loaded[map][index], distance ), distance, map.File, index);

	protected void Request( WorldArea map, int index, float distance, bool promoted = false )
	{
		var key = (map, index);
		if ( !Eligible( loaded[map][index] ) || (desired.TryGetValue( key, out var previous ) && previous <= distance && !promoted) ) return;
		if ( promoted && desired.TryGetValue( key, out previous ) ) distance = Math.Min( previous, distance );
		desired[key] = distance;
		if ( PipelineModelReads && ModelAsset( loaded[map][index] ) is { SourceEntry.Kind: Kind.DictionaryModel } asset )
		{
			uint file = asset.SourceEntry.File;
			if ( !dictionaryDemand.TryGetValue( file, out var members ) ) dictionaryDemand.Add( file, members = new() );
			var entity = loaded[map][index];
			if ( members.TryAdd( asset, entity ) && dictionaryReads.TryGetValue( file, out var read ) && !read.IsClosed &&
				!upcoming.ContainsKey( asset ) && !pendingPlacements.Values.Any( x => x.Asset == asset ) && !TryGetModel( asset, entity, out _ ) )
			{
				var member = new DictionaryModelRead.Member( asset.SourceEntry );
				if ( read.TryAdd( member ) ) upcoming.Add( asset, new( member.Completion.Task, entity ) );
			}
		}
		if ( !rebuilding && !active.ContainsKey( key ) )
			queue.Enqueue( key, PlacementPriority( map, index, distance ) );
	}

	protected virtual bool MapQueryChanged => false;
	protected virtual float MapRequestDistance( WorldArea map ) => last.DistanceSquared( map.Entities );
	protected virtual bool IsDirectMap( WorldArea map ) => true;

	protected virtual HashSet<WorldArea> QueryMaps( WorldCatalog catalog, float radius ) => last.Query( catalog.Maps, radius );

	protected virtual float DistanceLimit( WorldEntity entity, float radius, float scale ) =>
		entity.Distance > 0 ? Math.Min( radius, entity.Distance * scale ) : radius;

	void SelectMap( WorldArea map, bool resolvePendingLinks = false )
	{
		var entities = loaded[map];
		for ( int i = 0; i < entities.Length; i++ )
		{
			// A full rebuild propagates demand through the complete resident sibling index.
			if ( resolvePendingLinks ) ResolveLinks( map, i );
			var e = entities[i];
			if ( !IsDirectMap( map ) || !Eligible( e ) ) continue;
			float d = last.DistanceSquared( e.Transform.Position / PacketReader.UnitsPerMetre );
			float limit = DistanceLimit( e, appliedSelection.Radius, appliedSelection.Scale );
			if ( d > limit * limit || !InRange( e, d / (appliedSelection.Scale * appliedSelection.Scale) ) ) continue;
			Request( map, i, d );
			IncludeRelated( map, i, d );
		}
	}

	bool rebuilding;

	protected void RankPending()
	{
		queue.Clear();
		foreach ( var (key, distance) in desired )
			if ( !active.ContainsKey( key ) && !pendingPlacements.ContainsKey( key ) )
				queue.Enqueue( key, PlacementPriority( key.Item1, key.Item2, distance ) );
	}

	void RebuildQueue()
	{
		queue.Clear();
		desired.Clear();
		dictionaryDemand.Clear();
		ResetGroups();
		rebuilding = true;
		try
		{
			foreach ( var map in loaded.Keys ) SelectMap( map );
		}
		finally
		{
			rebuilding = false;
		}
		RankPending();
		foreach ( var key in active.Keys.Where( x => !desired.ContainsKey( x ) ).ToArray() )
		{
			OnRemoved( key.Item1, key.Item2 );
			if ( active[key] is { } old ) Retire( old );
			active.Remove( key );
		}
	}

	public virtual void Dispose()
	{
		foreach ( var obj in active.Values ) if ( obj.IsValid() ) Retire( obj );
		while ( retiring.TryDequeue( out var old ) ) old.Destroy();
		foreach ( var map in loaded.Keys ) cache.Release( map.File );
		if ( request is not null ) cache.Release( pending.File );
		while ( nextMaps.TryDequeue( out var next ) ) cache.Release( next.Map.File );
		missingMaps.Clear();
		ClearUpcoming();
		dictionaryReads.Clear();
		dictionaryDemand.Clear();
		foreach ( var placement in pendingPlacements.Values ) placement.Release();
		pendingPlacements.Clear();
		request = null;
		loaded.Clear();
		active.Clear();
		queue.Clear();
	}
}

internal sealed class RenderStreamSystem( GtaMount host, PlacementCache cache, GameObject root, PlacementHierarchy placements ) : PlacementStream( host, cache, root, float.PositiveInfinity )
{
	// The map bounds and placement distances are in GTA metres.
	protected override bool PipelineModelReads => true;
	AreaIndex renderIndex;
	WorldCatalog indexedCatalog;
	readonly Dictionary<WorldArea, float?> submergedMaps = new();

	bool DeepMapIsDistant( WorldArea map )
	{
		if ( indexedCatalog is null || last.Count == 0 ) return false;
		if ( !submergedMaps.TryGetValue( map, out var level ) )
		{
			level = null;
			var bounds = map.Entities;
			foreach ( var region in indexedCatalog.Coast )
			{
				var water = region.Bounds;
				if ( bounds.Maxs.z + 12 >= water.Center.z || bounds.Mins.x < water.Mins.x ||
					bounds.Maxs.x > water.Maxs.x || bounds.Mins.y < water.Mins.y || bounds.Maxs.y > water.Maxs.y ) continue;
				level = water.Center.z;
				break;
			}
			submergedMaps.Add( map, level );
		}
		return level.HasValue && last[0].z > level.Value + 2 && last.DistanceSquared( map.Entities ) > 100 * 100;
	}
	float indexScale;
	Vector3 viewDirection, rankedDirection;

	internal void SetViewDirection( Vector3 forward ) => viewDirection = forward;
	bool ViewChanged => (viewDirection - rankedDirection).LengthSquared >= 0.03f;

	protected override float ViewCost( WorldEntity entity, float distance )
	{
		if ( last.Count == 0 ) return distance;
		var offset = entity.Transform.Position / PacketReader.UnitsPerMetre - last[0];
		float range = MathF.Max( 1f, offset.Length );
		float radius = MathF.Max( 0.25f, entity.RenderRadius );
		float alignment = Math.Clamp( Vector3.Dot( rankedDirection, offset / range ), -1f, 1f );
		return range / radius * (2f - alignment);
	}
	const float EnterFlightHeight = 65f;
	const float LeaveFlightHeight = 40f;

	bool airborne;
	long lastMovement;
	Vector3? movementAnchor, surfaceSample;
	Vector3? cameraSample, predictedFocus, queriedPrediction;
	long cameraSampleTime;
	HashSet<WorldArea> directMaps = new();

	protected override bool MapQueryChanged => predictedFocus.HasValue != queriedPrediction.HasValue ||
		(predictedFocus is { } next && queriedPrediction is { } previous && next.DistanceSquared( previous ) >= 144f);

	protected override float MapRequestDistance( WorldArea map ) => predictedFocus is { } point
		? Math.Min( base.MapRequestDistance( map ), map.Entities.ClosestPoint( point ).DistanceSquared( point ) )
		: base.MapRequestDistance( map );

	protected override bool IsDirectMap( WorldArea map ) => directMaps.Contains( map );

	protected override (float Radius, float Scale) Selection =>
		(float.PositiveInfinity, GtaWorldStreamingSystem.LodScale);
	protected override float QueryStep => RequiredOnly ? 48f : 12f;
	protected override bool RequiredOnly => airborne && System.Diagnostics.Stopwatch.GetElapsedTime( lastMovement ).TotalSeconds < 1.0;

	internal void TrackCamera( Vector3? metres, AreaIndex collision )
	{

		if ( metres is not { } position )
		{
			movementAnchor = surfaceSample = cameraSample = predictedFocus = null;
			airborne = false;
			return;
		}
			long now = System.Diagnostics.Stopwatch.GetTimestamp();
		if ( cameraSample is { } previous )
		{
			float elapsed = (float)System.Diagnostics.Stopwatch.GetElapsedTime( cameraSampleTime, now ).TotalSeconds;
			if ( elapsed >= 0.1f )
			{
				var displacement = position - previous;
				var velocity = displacement / elapsed;
				// Reject jumps and let stationary cameras drop the extra demand.
				predictedFocus = displacement.LengthSquared <= 10000f && velocity.LengthSquared >= 25f
					? position + velocity.ClampLength( 75f )
					: null;
				cameraSample = position;
				cameraSampleTime = now;
			}
		}
		else
		{
			cameraSample = position;
			cameraSampleTime = now;
		}
		if ( movementAnchor is not { } anchor || anchor.DistanceSquared( position ) >= 0.25f )
		{
			movementAnchor = position;
			lastMovement = System.Diagnostics.Stopwatch.GetTimestamp();
		}
		if ( surfaceSample is { } sample && sample.DistanceSquared( position ) < 4 ) return;
		surfaceSample = position;
		float clearance = airborne ? LeaveFlightHeight : EnterFlightHeight;
		airborne = position.z > clearance && !collision.NearSurface( position, clearance );
	}

	long nextOverlaySample;
	internal void DrawLodOverlay( Vector3 camera )
	{
		long now = Environment.TickCount64;
		if ( now < nextOverlaySample ) return;
		nextOverlaySample = now + 250;
		Span<int> resident = stackalloc int[8 * 5];
		Span<int> visible = stackalloc int[8 * 5];
		resident.Clear();
		visible.Clear();
		foreach ( var ((map, index), obj) in active )
		{
			if ( !obj.IsValid() || !loaded.TryGetValue( map, out var records ) ) continue;
			int level = records[index].Level;
			float distance = camera.Distance( obj.WorldPosition ) / PacketReader.UnitsPerMetre;
			int band = distance switch { < 100 => 0, < 350 => 1, < 1000 => 2, < 3000 => 3, _ => 4 };
			int slot = (level is >= 0 and <= 6 ? level : 7) * 5 + band;
			resident[slot]++;
			if ( placements.IsVisible( new( map.Name, index ), obj ) ) visible[slot]++;
		}
		var text = new System.Text.StringBuilder( "GTA LOD   selected/resident   |   camera to placement origin (m)\nLOD      <100      100-350      350-1000      1000-3000      3000+" );
		for ( int level = 0; level < 8; level++ )
		{
			if ( level == 7 && !resident.Slice( 35 ).ContainsAnyExcept( 0 ) ) continue;
			text.AppendLine().Append( level == 7 ? "Other" : level.ToString() );
			for ( int band = 0; band < 5; band++ )
			{
				int slot = level * 5 + band;
				text.Append( "       " ).Append( visible[slot] ).Append( "/" ).Append( resident[slot] );
			}
		}
		WorldRoot.DebugOverlay.ScreenText( new Vector2( 18, 85 ), text.ToString(), 15, TextFlag.LeftTop, duration: 0.25f );
	}

	static bool IsSkylineLod( int level ) => level is 3 or 4 or 6; // 5 is orphan HD.

	protected override HashSet<WorldArea> QueryMaps( WorldCatalog catalog, float radius )
	{
		float scale = Selection.Scale;
		if ( renderIndex is null || indexScale != scale || !ReferenceEquals( indexedCatalog, catalog ) )
		{
			renderIndex = catalog.BuildRenderIndex( scale );
			submergedMaps.Clear();
			indexedCatalog = catalog;
			indexScale = scale;
		}
		directMaps = last.Query( renderIndex, 0 );
		var maps = new HashSet<WorldArea>( directMaps );
		queriedPrediction = predictedFocus;
		if ( queriedPrediction is { } point ) maps.UnionWith( renderIndex.Query( point, 0 ) );
		if ( RequiredOnly ) maps.RemoveWhere( map => (map.Content & 4) == 0 );
		return maps;
	}

	// DistanceLimit bounds direct selection. A nearby child can need a sibling
	// beyond that radius before their coarse parent can be hidden.
	protected override bool Eligible( WorldEntity e ) => !RequiredOnly || IsSkylineLod( e.Level );



	protected override Queue<AssetLoader> GetTextures( AssetLoader asset, PreparedModelData packet, WorldEntity entity ) => asset.ExternalTextures( packet, colorOnly: entity.Level is 0 or 5 );

	readonly System.Diagnostics.Metrics.Meter meter = new( "GtaV.WorldStreaming" );
	System.Diagnostics.Metrics.Histogram<double> activationMs;
	System.Diagnostics.Metrics.Counter<long> selected;

	readonly Dictionary<LodEntityId, (string Region, int Level, long Start)> selectedSince = new();
	System.Diagnostics.Metrics.Histogram<double> dwellMs;

	void EndSelection( LodEntityId id, string reason )
	{
		if ( !selectedSince.Remove( id, out var previous ) || !dwellMs.Enabled ) return;
		if ( reason == "removed" ) reason = fades.Visibility.ReplacementAfterRemoval( id );
		var tags = new System.Diagnostics.TagList { { "ymap", previous.Region }, { "lod", previous.Level }, { "exit", reason } };
		dwellMs.Record( System.Diagnostics.Stopwatch.GetElapsedTime( previous.Start ).TotalMilliseconds, tags );
	}

	void CountSelection( LodEntityId id )
	{
		if ( !(selected.Enabled || dwellMs.Enabled) || !residentMaps.TryGetValue( id.Map, out var map ) ) return;
		if ( dwellMs.Enabled ) selectedSince[id] = (Mount.MapNames.GetValueOrDefault( map.File ), loaded[map][id.Index].Level, System.Diagnostics.Stopwatch.GetTimestamp());
		var tags = new System.Diagnostics.TagList
		{
			{ "ymap", Mount.MapNames.GetValueOrDefault( map.File ) },
			{ "lod", loaded[map][id.Index].Level }
		};
		selected.Add( 1, tags );
	}

	protected override long StartLoad()
	{
		if ( activationMs is null )
		{
			selected = meter.CreateCounter<long>( "gtav.render.selected", description: "Render placements selected for display, including reselection." );
			dwellMs = meter.CreateHistogram<double>( "gtav.render.dwell", "ms", "Time a placement was selected before refinement, coarsening, or removal." );
			fades.Visibility.SelectionEnded = EndSelection;
			fades.OnSelected = CountSelection;
			activationMs = meter.CreateHistogram<double>( "gtav.render.activation", "ms", "Elapsed time from dequeue to successful render placement activation." );
		}
		return activationMs.Enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0;
	}

	readonly LodFades fades = new( placements );
	readonly Dictionary<WorldArea, MapInstanceBatch[]> instanceBatches = new();
	readonly Dictionary<MapInstanceBatch, MapInstanceBatch.RenderBatch> renderedBatches = new();
	bool instanceWorkPending;
	readonly Dictionary<LodEntityId, (GameObject Object, AssetLoader Model, float Radius)> textureUsers = new();
	readonly Dictionary<AssetLoader, HashSet<LodEntityId>> usersByTexture = new();
	readonly Queue<LodEntityId> textureScan = new();
	Task<BinaryReader> detailRead;
	AssetLoader detailAsset;
	readonly HashSet<LodEntityId> queuedUsers = new();
	readonly FocusSet textureFocus = new();
	int remaining;
	long lastScan;

	bool ScanDue => remaining > 0 && !RequiredOnly && !CoverageQueued && Environment.TickCount64 - lastScan >= 16;
	public override bool WorkReady => ViewChanged || MapQueryChanged || fades.Pending || base.WorkReady || instanceWorkPending || detailRead?.IsCompleted == true || (detailRead is null && ScanDue);
	public override void Update( WorldCatalog catalog, IReadOnlyList<Vector3> positions )
	{
		if ( ViewChanged )
		{
			rankedDirection = viewDirection;
			RankPending();
			remaining = textureScan.Count;
		}
		fades.Update();
		base.Update( catalog, positions );
		UpdateInstanceBatches();
		if ( textureFocus.UpdateQuery( positions, 8 ) || fades.Pending ) remaining = textureScan.Count;
		Refine();
	}

	void UpdateInstanceBatches()
	{
		instanceWorkPending = false;
		bool created = false;
		foreach ( var batches in instanceBatches.Values )
		foreach ( var batch in batches )
		{
			if ( batch.ModelPath.Length == 0 || batch.Transforms.Length == 0 ) continue;
			float range = batch.Distance * Selection.Scale;
			if ( RequiredOnly || last.DistanceSquared( batch.Bounds ) > range * range )
			{
				if ( renderedBatches.Remove( batch, out var departed ) ) departed?.Delete();
				continue;
			}
			if ( renderedBatches.ContainsKey( batch ) ) continue;
			if ( created ) { instanceWorkPending = true; continue; }
			var model = Model.Load( $"mount://gtav/models/{batch.ModelPath}.vmdl" );
			renderedBatches.Add( batch, model.IsValid() && !model.IsError ? new( WorldRoot.Scene.SceneWorld, batch, model ) : null );
			created = true;
		}
	}

	int Target( LodEntityId id )
	{
		if ( RequiredOnly || !textureUsers.TryGetValue( id, out var use ) || !use.Object.IsValid() || !placements.IsVisible( id, use.Object ) ) return 256;
		if ( last.Count == 0 ) return 256;
		var offset = use.Object.WorldPosition / PacketReader.UnitsPerMetre - last[0];
		float radius = MathF.Max( 0.25f, use.Radius );
		float range = MathF.Max( radius, offset.Length );
		if ( Vector3.Dot( viewDirection, offset ) < -radius ) return 256;
		// Use a reference focal length in pixels to estimate coverage from the authored bounds.
		float pixels = 1600f * radius / range;
		return pixels > 1024f ? int.MaxValue : pixels > 512f ? 1024 : pixels > 256f ? 512 : 256;
	}

	int SharedTarget( AssetLoader asset )
	{
		int target = 256;
		if ( usersByTexture.TryGetValue( asset, out var users ) )
			foreach ( var id in users )
				target = Math.Max( target, Target( id ) );
		return target;
	}

	void RemoveTextureUser( LodEntityId id )
	{
		if ( !textureUsers.Remove( id, out var use ) ) return;
		foreach ( var texture in use.Model.PreviewDependencies )
			if ( usersByTexture.TryGetValue( texture, out var users ) && users.Remove( id ) && users.Count == 0 )
				usersByTexture.Remove( texture );
	}

	void Refine()
	{
		if ( detailRead is not null )
		{
			if ( !detailRead.IsCompleted ) return;
			var read = detailRead;
			detailRead = null;
			try
			{
				using var packet = read.GetAwaiter().GetResult();
				int target = SharedTarget( detailAsset );
				if ( detailAsset.NeedsDetail( target ) ) detailAsset.UploadStage( packet, detailAsset.NextStage( target ) );
				remaining = Math.Max( remaining, textureScan.Count );
			}
			catch ( Exception ex ) { WorldSession.StreamingLog.Warning( ex ); }
			return;
		}
		if ( !ScanDue ) return;
		lastScan = Environment.TickCount64;
		AssetLoader best = null;
		float bestDistance = float.MaxValue;
		for ( int i = Math.Min( 48, Math.Min( remaining, textureScan.Count ) ); i > 0; i-- )
		{
			remaining--;
			var id = textureScan.Dequeue();
			queuedUsers.Remove( id );
			if ( !textureUsers.TryGetValue( id, out var use ) ) continue;
			if ( !use.Model.PreviewDependencies.Any( a => !a.TextureReady ) ) { RemoveTextureUser( id ); continue; }
			if ( queuedUsers.Add( id ) ) textureScan.Enqueue( id );
			int target = Target( id );
			if ( target <= 256 ) continue;
			foreach ( var asset in use.Model.PreviewDependencies )
			{
				if ( !asset.NeedsDetail( target ) ) continue;
				float distance = last.DistanceSquared( use.Object.WorldPosition / PacketReader.UnitsPerMetre );
				if ( best is not null && (asset.Resolution > best.Resolution ||
					(asset.Resolution == best.Resolution && distance >= bestDistance)) ) continue;
				best = asset;
				bestDistance = distance;
			}
		}
		if ( best is not null )
		{
			detailAsset = best;
			detailRead = best.ReadDetail();
		}
		if ( textureScan.Count == 0 ) remaining = 0;
	}
	protected override void Retire( GameObject obj )
	{
		if ( !fades.RetireAfterFade( obj ) && !placements.Release( obj, physics: false ) ) base.Retire( obj );
	}

	public override void Dispose()
	{
		base.Dispose();
		fades.Dispose();
		foreach ( var batch in renderedBatches.Values ) batch?.Delete();
		renderedBatches.Clear();
		instanceBatches.Clear();
		meter.Dispose();
		textureUsers.Clear();
		usersByTexture.Clear();
		textureScan.Clear();
		if ( detailRead is { } read )
			_ = read.ContinueWith( t => { if ( t.IsCompletedSuccessfully ) t.Result.Dispose(); else _ = t.Exception; }, TaskScheduler.Default );
	}

	protected override bool InRange( WorldEntity e, float d ) => e.Level is 0 or 5 || e.Children != 0 || d >= e.ChildDistance * e.ChildDistance;

	readonly Dictionary<LodEntityId, List<(WorldArea Map, int Index)>> siblings = new();
	readonly Dictionary<uint, WorldArea> residentMaps = new();

	static LodEntityId? ParentOf( WorldArea map, WorldEntity entity ) =>
		entity.Parent < 0 ? null : new( (entity.Flags & 8) != 0 ? map.Parent : map.Name, entity.Parent );

	readonly HashSet<LodEntityId> visitedParents = new();

	// A region is an authored LOD branch. Queue its coarsest requested representation
	// before detail on any branch. A map can contain many independent branches.
	protected override int RequestPriority( WorldArea map, int index )
	{
		var entity = loaded[map][index];
		bool coversChildren = entity.Children > 0;
		bool structure = structures.Contains( (map, index) );
		// Group demand inherits the nearest sibling distance. Rank local detail by its own position.
		bool nearDetail = entity.Level is 0 or 5 && last.DistanceSquared( entity.Transform.Position / PacketReader.UnitsPerMetre ) <= NearDetailDistance * NearDetailDistance;
		// Request the authored fallback for nearby children, at any LOD level.
		bool localFallback = coversChildren && groupDemand.TryGetValue( new( map.Name, index ), out var demandDistance ) &&
			demandDistance <= NearStructureDistance * NearStructureDistance;
		int detailRank = nearDetail ? (entity.IsStructural ? 2 : 3) : structure ? 4 : 6;
		if ( !coversChildren && !structure && entity.Level is 0 or 5 && DeepMapIsDistant( map ) ) detailRank = 7;
		var directParent = ParentOf( map, entity );
		bool ancestorRequested = false;
		visitedParents.Clear();
		while ( ParentOf( map, loaded[map][index] ) is { } parent &&
			residentMaps.TryGetValue( parent.Map, out map ) && (uint)parent.Index < loaded[map].Length )
		{
			if ( !visitedParents.Add( parent ) ) break;
			if ( fades.Visibility.IsSelected( parent ) )
			{
				// A nearby intermediate model can replace the skyline before its own HD group is ready.
				if ( localFallback ) return 1;
				// The last sibling gates the whole handoff, even when its own pivot is farther away.
				if ( directParent == parent && structuralGroups.Contains( parent ) ) return 2;
				return detailRank;
			}
			index = parent.Index;
			ancestorRequested |= desired.ContainsKey( (map, index) );
		}
		if ( !ancestorRequested && coversChildren ) return 0;
		if ( localFallback ) return 1;
		return ancestorRequested || detailRank == 7 ? detailRank : Math.Min( detailRank, 5 );
	}

	protected override void MapLoaded( WorldArea map, MapRecords records )
	{
		var entities = records.Entities;
		if ( records.Batches.Length != 0 ) instanceBatches.Add( map, records.Batches );
		residentMaps[map.Name] = map;
		for ( int i = 0; i < entities.Length; i++ )
		{
			if ( ParentOf( map, entities[i] ) is not { } parent ) continue;
			if ( !siblings.TryGetValue( parent, out var group ) ) siblings[parent] = group = new();
			group.Add( (map, i) );
		}
	}

	protected override void MapRemoved( WorldArea map )
	{
		if ( instanceBatches.Remove( map, out var batches ) )
			foreach ( var batch in batches )
				if ( renderedBatches.Remove( batch, out var rendered ) ) rendered?.Delete();
		residentMaps.Remove( map.Name );
		foreach ( var entity in loaded[map] )
		{
			if ( ParentOf( map, entity ) is not { } parent || !siblings.TryGetValue( parent, out var group ) ) continue;
			group.RemoveAll( x => x.Map == map );
			if ( group.Count == 0 ) siblings.Remove( parent );
		}
	}

	// Minimum squared distance requesting each sibling group, including links to maps still in flight.
	readonly Dictionary<LodEntityId, float> groupDemand = new();

	const float NearDetailDistance = 75f;
	const float NearStructureDistance = 120f;
	readonly HashSet<(WorldArea, int)> structures = new();
	readonly HashSet<LodEntityId> structuralGroups = new();

	protected override void ResetGroups()
	{
		groupDemand.Clear();
		structuralGroups.Clear();
		structures.Clear();
	}

	void RequestMember( WorldArea map, int index, float distance, bool structural )
	{
		bool promoted = structural && structures.Add( (map, index) );
		Request( map, index, distance, promoted );
	}

	protected override void ResolveLinks( WorldArea map, int index )
	{
		var entity = loaded[map][index];
		if ( ParentOf( map, entity ) is { } parent && groupDemand.TryGetValue( parent, out var distance ) )
			RequestMember( map, index, distance, structuralGroups.Contains( parent ) );
		if ( groupDemand.TryGetValue( new( map.Name, index ), out distance ) )
		{
			bool structural = structuralGroups.Contains( new( map.Name, index ) );
			RequestMember( map, index, distance, structural );
			ExpandGroup( map, index, distance, structural );
		}
	}

	protected override void IncludeRelated( WorldArea map, int index, float distance )
	{
		var entity = loaded[map][index];
		bool structural = entity.IsStructural && (entity.Level is 0 or 5 || entity.IsInterior) &&
			distance <= NearStructureDistance * NearStructureDistance;
		if ( structural ) RequestMember( map, index, distance, true );
		ExpandGroup( map, index, distance, structural );
	}

	void ExpandGroup( WorldArea map, int index, float distance, bool structural )
	{
		while ( ParentOf( map, loaded[map][index] ) is { } parent )
		{
			bool promoted = structural && structuralGroups.Add( parent );
			if ( groupDemand.TryGetValue( parent, out var previous ) )
			{
				if ( previous <= distance && !promoted ) break;
				distance = Math.Min( distance, previous );
			}
			groupDemand[parent] = distance;
			foreach ( var sibling in siblings[parent] )
				RequestMember( sibling.Map, sibling.Index, distance, structural );

			if ( !residentMaps.TryGetValue( parent.Map, out map ) || (uint)parent.Index >= loaded[map].Length ) break;
			index = parent.Index;
			RequestMember( map, index, distance, structural );
		}
	}

	protected override void OnAdded( WorldArea map, int index, WorldEntity entity, GameObject obj )
	{
		if ( LoadStarted != 0 && activationMs.Enabled )
		{
			var tags = new System.Diagnostics.TagList
			{
				{ "ymap", Mount.MapNames.GetValueOrDefault( map.File ) },
				{ "lod", entity.Level }
			};
			activationMs.Record( System.Diagnostics.Stopwatch.GetElapsedTime( LoadStarted ).TotalMilliseconds, tags );
		}

		// Bit 3 in fwEntityDef selects the parent YMAP.
		var parent = ParentOf( map, entity );
		var id = new LodEntityId( map.Name, index );
		if ( ModelAsset( entity ) is { } asset && asset.PreviewDependencies.Count > 0 )
		{
			RemoveTextureUser( id );
			textureUsers[id] = (obj, asset, entity.RenderRadius);
			foreach ( var texture in asset.PreviewDependencies )
			{
				if ( !usersByTexture.TryGetValue( texture, out var users ) )
					usersByTexture.Add( texture, users = new() );
				users.Add( id );
			}
			if ( queuedUsers.Add( id ) ) textureScan.Enqueue( id );
		}
		remaining = textureScan.Count;
		fades.Reacquire( obj );
		fades.Visibility.Add( id, (id, obj), entity.Children, parent );
	}

	protected override void OnRemoved( WorldArea map, int index )
	{
		var id = new LodEntityId( map.Name, index );
		RemoveTextureUser( id );
		fades.Visibility.Remove( id );
	}
	protected override GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner, LodEntityId id )
	{
		if ( model is null || model.IsError ) return null;
		return placements.AddModel( id, entity, model, physics: false );
	}
	protected override GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner )
	{
		var obj = GameObject.Clone( prefab, entity.Transform, owner );
		obj.IsStatic = entity.IsStatic;
		return obj;
	}
}

internal sealed class PhysicsPlacementStream( GtaMount host, PlacementCache cache, GameObject root, LadderStreamSystem ladders, PlacementHierarchy placements ) : PlacementStream( host, cache, root, 80 )
{
	protected override bool TryGetModel( AssetLoader asset, WorldEntity entity, out Model model ) => entity.UsesProp ? base.TryGetModel( asset, entity, out model ) : asset.TryGetPhysics( out model );
	protected override Queue<AssetLoader> GetTextures( AssetLoader asset, PreparedModelData packet, WorldEntity entity ) => entity.UsesProp ? asset.ExternalTextures( packet, colorOnly: entity.Level is 0 or 5 ) : null;
	protected override PreparedModelData Prepare( AssetLoader asset, WorldEntity entity ) => entity.UsesProp ? asset.ReadModel() : new( asset.ReadPhysics() );
	protected override Model Complete( AssetLoader asset, PreparedModelData packet, WorldEntity entity ) => entity.UsesProp ? asset.LoadPrepared( packet ) : asset.LoadPhysics( packet.Packet );
	protected override GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner, LodEntityId id )
	{
		if ( model?.Physics?.Parts.Count is not > 0 ) return null;
		return placements.AddModel( id, entity, model, physics: true );
	}

	protected override void Retire( GameObject obj )
	{
		if ( !placements.Release( obj, physics: true ) ) base.Retire( obj );
	}

	protected override GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner )
	{
		var template = SceneUtility.GetPrefabScene( prefab );
		GameObject group = null;
		foreach ( var source in template.GetComponentsInChildren<ModelCollider>( true ) )
		{
			group ??= new GameObject( owner, true, prefab.ResourceName ) { WorldTransform = entity.Transform };
			var child = new GameObject( group, false, source.GameObject.Name );
			child.IsStatic = entity.IsStatic || source.GameObject.IsStatic;
			child.WorldTransform = entity.Transform.ToWorld( source.WorldTransform );
			var collider = child.AddComponent<ModelCollider>();
			collider.Model = source.Model;
			child.Enabled = true;
		}
		foreach ( var source in template.GetComponentsInChildren<BoxCollider>( true ) )
		{
			if ( !source.IsTrigger || !source.Tags.Has( "ladder" ) ) continue;
			group ??= new GameObject( owner, true, prefab.ResourceName ) { WorldTransform = entity.Transform };
			var child = new GameObject( group, false, source.GameObject.Name );
			child.IsStatic = entity.IsStatic || source.GameObject.IsStatic;
			child.WorldTransform = entity.Transform.ToWorld( source.WorldTransform );
			child.Tags.Add( "ladder" );
			var box = child.AddComponent<BoxCollider>();
			box.Center = source.Center;
			box.Scale = source.Scale;
			box.IsTrigger = true;
			ladders.TrackInterior( child, box.Scale.z );
		}
		return group;
	}
}
