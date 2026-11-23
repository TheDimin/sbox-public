using System;
using System.IO;
using Sandbox;
using Sandbox.Mounting;
using System.Text.Json.Nodes;

namespace GtaV;

[Flags]
internal enum PlacementFlags : uint
{
	SuppressEmbeddedCollision = 1u << 2,
	Fixed = 1u << 5
}

[Flags]
internal enum ArchetypeFlags : uint
{
	Fixed = 1u << 5,
	Object = 1u << 17,
	DoorPhysics = 1u << 26
}

internal sealed class MapLoader( Entry entry ) : SceneLoader<GtaMount>
{
	protected override void BuildScene()
	{
		using var data = Host.Read( entry );
		Placements.Build( Host, data, null );
		var daylight = new GameObject( true, "Daylight" );
		daylight.AddComponent<SkyBox2D>();
		daylight.WorldRotation = Rotation.From( new Angles( 50, 35, 0 ) );
		daylight.AddComponent<DirectionalLight>();
		int collisionCount = data.ReadInt32();
		for ( int i = 0; i < collisionCount; i++ )
		{
			string name = data.Text();
			var path = $"mount://{Host.Ident}/collision/{name}.vmdl";
			if ( Host.GetByPath( path ) is null ) continue;
			var collider = new GameObject( true, name ).AddComponent<ModelCollider>();
			collider.Model = Model.Load( path );
			collider.Static = true;
		}
	}
}

internal static class Placements
{
	internal static void Build( GtaMount host, BinaryReader data, GameObject parent )
	{
		int count = data.ReadInt32();
		for ( int i = 0; i < count; i++ )
		{
			uint hash = data.ReadUInt32();
			var flags = (PlacementFlags)data.ReadUInt32();
			var position = data.Vector() * PacketReader.UnitsPerMetre;
			var rotation = new Rotation( data.ReadSingle(), data.ReadSingle(), data.ReadSingle(), data.ReadSingle() );
			float xy = data.ReadSingle();
			var scale = new Vector3( xy, xy, data.ReadSingle() );
			if ( host.Prefabs.TryGetValue( hash, out var path ) && PrefabFile.Load( path ) is { } prefab )
			{
				var placed = GameObject.Clone( prefab, new Transform( position, rotation, scale ), parent );
				var prop = placed.Components.Get<Prop>();
				if ( prop is not null && (flags & PlacementFlags.SuppressEmbeddedCollision) != 0 )
				{
					var model = prop.Model;
					prop.Destroy();
					placed.AddComponent<ModelRenderer>().Model = model;
				}
				else if ( prop is not null && (flags & PlacementFlags.Fixed) != 0 )
				{
					prop.IsStatic = true;
				}
				if ( (flags & PlacementFlags.SuppressEmbeddedCollision) != 0 ) placed.Components.Get<ModelCollider>()?.Destroy();
			}
			else
			{
				var missing = new GameObject( true, $"Missing GTA archetype {hash:x8}" );
				missing.Parent = parent;
				missing.LocalTransform = new Transform( position, rotation, scale );
			}
		}
	}
}

internal sealed class ArchetypeLoader( Entry entry ) : ResourceLoader<GtaMount>
{
	PrefabFile prefab;
	protected override object Load()
	{
		using var data = Host.Read( entry );
		var modelName = data.Text();
		var flags = (ArchetypeFlags)data.ReadUInt32();
		uint doorType = data.ReadUInt32();
		var builder = new PrefabBuilder().WithName( Path );
		bool enableProp = false;
		using ( builder.Scope() )
		{
			var root = new GameObject( true, entry.Name );
			if ( modelName.Length > 0 )
			{
				var model = Model.Load( $"mount://{Host.Ident}/models/{modelName}.vmdl" );
				if ( (flags & ArchetypeFlags.DoorPhysics) != 0 )
				{
					var door = root.AddComponent<Sandbox.Mapping.Door>();
					if ( doorType is 1 or 2 or 4 or 6 or 8 or 10 or 13 )
					{
						door.Mode = Sandbox.Mapping.Door.DoorMode.Sliding;
						var size = model.Bounds.Size;
						door.SlideOffset = doorType == 10 ? Vector3.Up * size.z :
							size.x >= size.y ? Vector3.Forward * size.x : Vector3.Left * size.y;
					}
					root.AddComponent<ModelRenderer>().Model = model;
					if ( model.Physics?.Parts.Count > 0 )
						root.AddComponent<ModelCollider>().Model = model;
				}
				else
				{
					// Enable the prop in the finished prefab. Its procedural components belong to the instance.
					var prop = root.Components.Create<Prop>( false );
					enableProp = true;
					prop.IsStatic = (flags & ArchetypeFlags.Fixed) != 0 || (flags & ArchetypeFlags.Object) == 0;
					prop.StartAsleep = true;
					prop.Model = model;
				}
			}
			Placements.Build( Host, data, root );
		}
		prefab = builder.Create();
		if ( enableProp && prefab.RootObject["Components"] is JsonArray components )
		{
			foreach ( var component in components )
				if ( component["__type"]?.GetValue<string>() == "Sandbox.Prop" )
					component["__enabled"] = true;
		}
		return prefab;
	}

	protected override void Shutdown() => PrefabBuilder.Destroy( prefab );
}
