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
	protected override void BuildScene()
	{
		new GameObject( true, "GTA World" ).AddComponent<GtaWorld>();
		var light = new GameObject( true, "Daylight" );
		light.AddComponent<SkyBox2D>();
		light.WorldRotation = Rotation.From( new Angles( 50, 35, 0 ) );
		light.AddComponent<DirectionalLight>();
		var view = new GameObject( true, "Camera" );
		view.WorldPosition = new Vector3( 202.8f, -1327.65f, 32 ) * PacketReader.UnitsPerMetre;
		view.AddComponent<CameraComponent>();
	}
}

internal sealed record WorldArea( uint File, uint Name, uint Parent, uint Flags, uint Content, BBox Streaming, BBox Entities, string Collision );

internal sealed class AreaIndex
{
	const int CellSize = 256;
	readonly Dictionary<(int, int), List<WorldArea>> cells = new();
	readonly List<WorldArea> large = new();

	static int Cell( float value ) => (int)MathF.Floor( value / CellSize );

	public void Add( WorldArea area )
	{
		var box = area.Entities;
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

	public HashSet<WorldArea> Query( Vector3 p, float radius )
	{
		var result = new HashSet<WorldArea>();
		void Add( List<WorldArea> list )
		{
			foreach ( var area in list )
				if ( area.Entities.ClosestPoint( p ).DistanceSquared( p ) <= radius * radius ) result.Add( area );
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
	public static WorldCatalog Read( ArchiveReader archive )
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
			if ( readable != 0 && group == 0 && (flags & 1) == 0 )
				result.Maps.Add( new( file, name, parent, flags, content, stream, entities, null ) );
		}
		count = r.ReadInt32();
		for ( int i = 0; i < count; i++ )
		{
			uint file = r.ReadUInt32();
			string name = Path.GetFileNameWithoutExtension( r.Text() );
			uint interior = r.ReadUInt32();
			var box = new BBox( r.Vector(), r.Vector() );
			if ( interior == 0 ) result.Collision.Add( new( file, 0, 0, 0, 0, box, box, name ) );
		}
		return result;
	}
}

internal readonly record struct WorldEntity( uint Hash, Transform Transform, int Parent, int Level, float Distance, float ChildDistance, uint Children, uint Flags, string ModelPath, IReadOnlyList<ArchetypeExtension> Extensions )
{
	internal static MapRecords Read( ArchiveReader archive, uint file )
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
			uint children = r.ReadUInt32(), flags = r.ReadUInt32(), dictionary = r.ReadUInt32();
			var transform = new Transform( p * PacketReader.UnitsPerMetre, q, new Vector3( xy, xy, z ) );
			entities[i] = new( hash, transform, parent, level, distance, childDistance, children, flags, r.Text(), archive.GetExtensions( dictionary, hash ) );
		}
		var dependencies = new uint[r.ReadInt32()];
		for ( int i = 0; i < dependencies.Length; i++ ) dependencies[i] = r.ReadUInt32();
		return new MapRecords( entities, dependencies );
	}
}

internal sealed record MapRecords( WorldEntity[] Entities, uint[] PhysicsDictionaries );

internal sealed class PlacementCache( ArchiveReader archive )
{
	readonly Dictionary<uint, (Task<MapRecords> Task, int Users)> entries = new();

	internal Task<MapRecords> Acquire( uint file )
	{
		if ( !entries.TryGetValue( file, out var entry ) )
			entry = (Task.Run( () => WorldEntity.Read( archive, file ) ), 0);
		entries[file] = (entry.Task, entry.Users + 1);
		return entry.Task;
	}

	internal void Release( uint file )
	{
		var entry = entries[file];
		if ( entry.Users == 1 ) entries.Remove( file );
		else entries[file] = (entry.Task, entry.Users - 1);
	}
}

internal interface ISubStreamSystem : IDisposable
{
	void Update( WorldCatalog catalog, Vector3 position );
}

internal sealed class WorldSession : IGtaWorldSession
{
	internal static readonly Logger StreamingLog = new( "GTA world" );
	readonly Task<WorldCatalog> catalog;
	readonly ISubStreamSystem render, physics;

	internal WorldSession( GtaMount host, ArchiveReader archive, GameObject root )
	{
		catalog = host.GetWorldCatalog( archive );
		var maps = new PlacementCache( archive );
		render = new RenderStreamSystem( host, maps, root );
		physics = new PhysicsStreamSystem( host, maps, archive, root );
	}

	bool failed;
	public void Update( Vector3 camera, Vector3 focus )
	{
		if ( failed || !catalog.IsCompleted ) return;
		if ( catalog.IsFaulted )
		{
			failed = true;
			StreamingLog.Warning( catalog.Exception );
			return;
		}
		physics.Update( catalog.Result, focus / PacketReader.UnitsPerMetre );
		render.Update( catalog.Result, camera / PacketReader.UnitsPerMetre );
	}

	public void Dispose()
	{
		render.Dispose();
		physics.Dispose();
	}
}

internal sealed class PhysicsStreamSystem( GtaMount host, PlacementCache maps, ArchiveReader archive, GameObject root ) : ISubStreamSystem
{
	readonly PlacementStream props = new PhysicsPlacementStream( host, maps, root );
	readonly Dictionary<WorldArea, GameObject> active = new();
	HashSet<WorldArea> wanted = new();
	Vector3? last;

	public void Update( WorldCatalog catalog, Vector3 position )
	{
		props.Update( catalog, position );
		if ( last is null || last.Value.DistanceSquared( position ) >= 4 )
		{
			last = position;
			wanted = catalog.Collision.Query( position, 80 );
			foreach ( var key in active.Keys.Where( x => !wanted.Contains( x ) ).ToArray() )
			{
				active[key]?.Destroy();
				active.Remove( key );
			}
		}
		if ( request?.IsCompleted == true )
		{
			var area = pending;
			if ( request.IsFaulted ) WorldSession.StreamingLog.Warning( request.Exception );
			if ( wanted.Contains( area ) )
			{
				active[area] = null;
				if ( request.IsCompletedSuccessfully && request.Result.Vertices.Length > 0 )
				{
					var mesh = request.Result;
					var model = Model.Builder.WithName( area.Collision )
						.AddCollisionMesh( mesh.Vertices.AsSpan(), mesh.Indices.AsSpan() ).Create();
					var obj = new GameObject( root, false, area.Collision );
					obj.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
					var collider = obj.AddComponent<ModelCollider>();
					collider.Static = true;
					collider.Model = model;
					obj.Enabled = true;
					active[area] = obj;
				}
			}
			request = null;
		}
		if ( request is not null || active.Count == wanted.Count ) return;
		pending = wanted.Where( x => !active.ContainsKey( x ) ).MinBy( x => x.Entities.ClosestPoint( position ).DistanceSquared( position ) );
		uint file = pending.File;
		request = Task.Run( () => ReadCollision( archive, file ) );
	}

	WorldArea pending;
	Task<(Vector3[] Vertices, int[] Indices)> request;

	static (Vector3[], int[]) ReadCollision( ArchiveReader archive, uint file )
	{
		using var r = archive.Read( (int)Kind.Collision, file, 0 );
		var vertices = new Vector3[checked(r.ReadInt32() * 3)];
		var indices = new int[vertices.Length];
		for ( int i = 0; i < vertices.Length; i++ )
		{
			vertices[i] = r.Vector() * PacketReader.UnitsPerMetre;
			indices[i] = i;
		}
		return (vertices, indices);
	}
	public void Dispose()
	{
		props.Dispose();
		foreach ( var obj in active.Values ) obj?.Destroy();
		active.Clear();
	}
}

internal abstract class PlacementStream( GtaMount host, PlacementCache cache, GameObject root, float radius ) : ISubStreamSystem
{
	protected virtual bool TryGetModel( AssetLoader asset, out Model model )
	{
		model = asset.PreparedModel;
		return model is { IsValid: true };
	}
	protected virtual BinaryReader Prepare( AssetLoader asset ) => asset.ReadPacket();
	protected virtual Model Complete( AssetLoader asset, BinaryReader packet ) => asset.LoadPrepared( packet );
	protected abstract GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner );
	protected abstract GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner );
	protected virtual bool InRange( WorldEntity e, float d ) => e.Level is 0 or 5;
	readonly Dictionary<WorldArea, WorldEntity[]> loaded = new();
	readonly Dictionary<(WorldArea, int), GameObject> active = new();
	readonly Queue<(WorldArea, int)> queue = new();
	readonly Queue<GameObject> retiring = new();
	protected virtual void OnAdded( WorldArea map, int index, WorldEntity entity, GameObject obj ) { }
	protected virtual void OnRemoved( WorldArea map, int index ) { }
	HashSet<WorldArea> wanted = new();
	Task<MapRecords> request;
	WorldArea pending;
	Vector3? last;

	public void Update( WorldCatalog catalog, Vector3 position )
	{
		bool moved = last is null || last.Value.DistanceSquared( position ) >= 16;
		if ( moved )
		{
			last = position;
			wanted = catalog.Maps.Query( position, radius );
			foreach ( var map in loaded.Keys.Where( x => !wanted.Contains( x ) ).ToArray() )
			{
				cache.Release( map.File );
				loaded.Remove( map );
			}
		}
		bool received = request?.IsCompleted == true;
		if ( received )
		{
			if ( request.IsFaulted ) WorldSession.StreamingLog.Warning( request.Exception );
			if ( wanted.Contains( pending ) ) loaded[pending] = request.IsCompletedSuccessfully ? request.Result.Entities : [];
			else cache.Release( pending.File );
			request = null;
		}
		if ( moved || received ) RebuildQueue( position );
		if ( request is null && (moved || received) )
		{
			pending = wanted.Where( x => !loaded.ContainsKey( x ) ).MinBy( x => x.Entities.ClosestPoint( position ).DistanceSquared( position ) );
			if ( pending is not null )
			{
				request = cache.Acquire( pending.File );
			}
		}
		for ( int i = 0; i < 4 && retiring.TryDequeue( out var old ); i++ ) old.Destroy();
		if ( preparation is not null && !preparation.IsCompleted ) return;
		if ( preparation is not null )
		{
			var task = preparation;
			preparation = null;
			if ( task.IsFaulted ) WorldSession.StreamingLog.Warning( task.Exception );
			if ( !task.IsCompletedSuccessfully || !loaded.ContainsKey( waiting.Item1 ) || !desired.Contains( waiting ) ) return;
			modelPacket = task.Result;
			textureQueue = GetTextures( waitingAsset, modelPacket );
		}
		if ( modelPacket is not null )
		{
			if ( textureRead is not null && !textureRead.IsCompleted ) return;
			bool needed = desired.Contains( waiting ) && loaded.ContainsKey( waiting.Item1 );
			if ( textureRead is not null )
			{
				if ( textureRead.IsCompletedSuccessfully )
				{
					using var packet = textureRead.Result;
					if ( needed ) texture.UploadTexture( packet );
				}
				else WorldSession.StreamingLog.Warning( textureRead.Exception );
				textureRead = null;
			}
			if ( needed && textureQueue?.TryDequeue( out texture ) == true )
			{
				if ( !texture.TextureReady ) textureRead = Task.Run( texture.ReadPacket );
				return;
			}
			using var ready = modelPacket;
			modelPacket = null;
			if ( needed )
			{
				var e = loaded[waiting.Item1][waiting.Item2];
				Finish( waiting, e, ActivateModel( Complete( waitingAsset, ready ), e, root ) );
			}
			return;
		}
		if ( !queue.TryDequeue( out var key ) ) return;
		if ( active.ContainsKey( key ) ) return;
		var entity = loaded[key.Item1][key.Item2];
		if ( entity.ModelPath.Length > 0 && host.GetByPath( $"mount://gtav/models/{entity.ModelPath}.vmdl" ) is AssetLoader asset )
		{
			if ( TryGetModel( asset, out var model ) )
				Finish( key, entity, ActivateModel( model, entity, root ) );
			else
			{
				waiting = key;
				waitingAsset = asset;
				preparation = Task.Run( () => Prepare( asset ) );
			}
			return;
		}
		if ( host.Prefabs.TryGetValue( entity.Hash, out var path ) && PrefabFile.Load( path ) is { } prefab )
			Finish( key, entity, Activate( prefab, entity, root ) );
	}

	(WorldArea, int) waiting;
	BinaryReader modelPacket;
	Queue<AssetLoader> textureQueue;
	Task<BinaryReader> textureRead;
	AssetLoader texture;
	protected virtual Queue<AssetLoader> GetTextures( AssetLoader asset, BinaryReader packet ) => null;
	Task<BinaryReader> preparation;
	AssetLoader waitingAsset;
	HashSet<(WorldArea, int)> desired = new();

	void Finish( (WorldArea, int) key, WorldEntity e, GameObject obj )
	{
		if ( obj.IsValid() ) obj.Flags |= GameObjectFlags.NotSaved | GameObjectFlags.NotNetworked;
		active[key] = obj;
		if ( obj.IsValid() ) OnAdded( key.Item1, key.Item2, e, obj );
	}

	void RebuildQueue( Vector3 position )
	{
		var candidates = new Dictionary<(WorldArea, int), float>();
		foreach ( var (map, entities) in loaded )
			for ( int i = 0; i < entities.Length; i++ )
			{
				var e = entities[i];
				float d = (e.Transform.Position / PacketReader.UnitsPerMetre).DistanceSquared( position );
				float limit = e.Distance > 0 ? Math.Min( radius, e.Distance ) : radius;
				if ( d <= limit * limit && InRange( e, d ) )
					candidates[(map, i)] = d;
			}
		foreach ( var key in active.Keys.Where( x => !candidates.ContainsKey( x ) ).ToArray() )
		{
			OnRemoved( key.Item1, key.Item2 );
			if ( active[key] is { } old ) retiring.Enqueue( old );
			active.Remove( key );
		}
		desired = candidates.Keys.ToHashSet();
		queue.Clear();
		foreach ( var item in candidates.Where( x => !active.ContainsKey( x.Key ) ).OrderBy( x => x.Value ) ) queue.Enqueue( item.Key );
	}

	public void Dispose()
	{
		foreach ( var obj in active.Values ) obj?.Destroy();
		while ( retiring.TryDequeue( out var old ) ) old.Destroy();
		foreach ( var map in loaded.Keys ) cache.Release( map.File );
		if ( request is not null ) cache.Release( pending.File );
		request = null;
		loaded.Clear();
		active.Clear();
		queue.Clear();
	}
}

internal sealed class RenderStreamSystem( GtaMount host, PlacementCache cache, GameObject root ) : PlacementStream( host, cache, root, 350 )
{
	protected override Queue<AssetLoader> GetTextures( AssetLoader asset, BinaryReader packet ) => asset.ExternalTextures( packet );

	// Bit 3 in fwEntityDef places the parent index in the parent YMAP.
	readonly Dictionary<(uint, int), (GameObject Object, uint Children)> representations = new();
	readonly Dictionary<(uint, int), (uint, int)> parents = new();
	readonly Dictionary<(uint, int), int> ready = new();

	protected override bool InRange( WorldEntity e, float d ) => e.Level is 0 or 5 || e.Children != 0 || d >= e.ChildDistance * e.ChildDistance;

	protected override void OnAdded( WorldArea map, int index, WorldEntity entity, GameObject obj )
	{
		var key = (map.Name, index);
		representations[key] = (obj, entity.Children);
		if ( entity.Parent >= 0 )
		{
			var parent = ((entity.Flags & 8) != 0 ? map.Parent : map.Name, entity.Parent);
			parents[key] = parent;
			ready[parent] = ready.GetValueOrDefault( parent ) + 1;
			Refresh( parent );
		}
		Refresh( key );
	}

	protected override void OnRemoved( WorldArea map, int index )
	{
		var key = (map.Name, index);
		representations.Remove( key );
		if ( !parents.Remove( key, out var parent ) ) return;
		if ( --ready[parent] == 0 ) ready.Remove( parent );
		Refresh( parent );
	}

	void Refresh( (uint, int) key )
	{
		if ( representations.TryGetValue( key, out var item ) )
			item.Object.Enabled = item.Children == 0 || ready.GetValueOrDefault( key ) < item.Children;
	}

	protected override GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner )
	{
		if ( model is null || model.IsError ) return null;
		var obj = new GameObject( owner, true, entity.ModelPath );
		obj.WorldTransform = entity.Transform;
		obj.AddComponent<ModelRenderer>().Model = model;
		return obj;
	}
	protected override GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner )
	{
		return GameObject.Clone( prefab, entity.Transform, owner );
	}
}

internal sealed class PhysicsPlacementStream( GtaMount host, PlacementCache cache, GameObject root ) : PlacementStream( host, cache, root, 80 )
{
	protected override bool TryGetModel( AssetLoader asset, out Model model ) => asset.TryGetPhysics( out model );
	protected override BinaryReader Prepare( AssetLoader asset ) => asset.ReadPhysics();
	protected override Model Complete( AssetLoader asset, BinaryReader packet ) => asset.LoadPhysics( packet );
	protected override GameObject ActivateModel( Model model, WorldEntity entity, GameObject owner )
	{
		bool solid = model?.Physics?.Parts.Count > 0;
		if ( !solid && entity.Extensions.Count == 0 ) return null;
		var obj = new GameObject( owner, false, "Object collision" );
		obj.WorldTransform = entity.Transform;
		if ( solid )
		{
			var collider = obj.AddComponent<ModelCollider>();
			collider.Static = true;
			collider.Model = model;
		}
		foreach ( var extension in entity.Extensions ) extension.Apply( obj, true );
		obj.Enabled = true;
		return obj;
	}
	protected override GameObject Activate( PrefabFile prefab, WorldEntity entity, GameObject owner )
	{
		var template = SceneUtility.GetPrefabScene( prefab );
		GameObject group = null;
		foreach ( var source in template.GetComponentsInChildren<ModelCollider>( true ) )
		{
			group ??= new GameObject( owner, true, "Object collision" );
			var child = new GameObject( group, false, source.GameObject.Name );
			child.WorldTransform = entity.Transform.ToWorld( source.WorldTransform );
			var collider = child.AddComponent<ModelCollider>();
			collider.Static = true;
			collider.Model = source.Model;
			child.Enabled = true;
		}
		foreach ( var source in template.GetComponentsInChildren<BoxCollider>( true ) )
		{
			if ( !source.IsTrigger || !source.Tags.Has( "ladder" ) ) continue;
			group ??= new GameObject( owner, true, "Object collision" );
			var child = new GameObject( group, false, source.GameObject.Name );
			child.WorldTransform = entity.Transform.ToWorld( source.WorldTransform );
			child.Tags.Add( "ladder" );
			var box = child.AddComponent<BoxCollider>();
			box.Center = source.Center;
			box.Scale = source.Scale;
			box.IsTrigger = true;
			box.Static = true;
			child.Enabled = true;
		}
		return group;
	}
}
