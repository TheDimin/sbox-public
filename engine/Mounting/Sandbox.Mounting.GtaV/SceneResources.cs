using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

		int lightCount = data.ReadInt32();
		if ( lightCount == 0 ) return;
		var glow = GameResource.Load<Sprite>( $"mount://{Host.Ident}/{CoronaLoader.PathName}" );
		for ( int i = 0; i < lightCount; i++ )
		{
			var position = data.Vector() * PacketReader.UnitsPerMetre;
			uint rgbi = data.ReadUInt32();
			var color = Color32.FromRgb( rgbi ).ToColor() * ( (rgbi >> 24) / 255f );
			float radius = data.ReadSingle() * PacketReader.UnitsPerMetre;
			var go = new GameObject( true, $"LOD light {i}" ) { WorldPosition = position };
			var sprite = go.AddComponent<SpriteRenderer>();
			sprite.Sprite = glow;
			sprite.Additive = true;
			sprite.Color = color.WithAlpha( 1 );
			sprite.Size = PacketReader.UnitsPerMetre;
			if ( radius > 0 )
			{
				var light = go.AddComponent<PointLight>();
				light.LightColor = sprite.Color;
				light.Radius = radius;
				light.Shadows = false;
			}
		}
	}
}

internal sealed class CoronaLoader : ResourceLoader<GtaMount>
{
	internal const string PathName = "lights/corona.sprite";

	protected override object Load()
	{
		return Sprite.FromTexture( Texture.Load( $"mount://{Host.Ident}/textures/graphics/corona.vtex" ) );
	}
}

internal static class Placements
{
	internal static void Build( GtaMount host, BinaryReader data, GameObject parent, Action<int, GameObject> placedAt = null )
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
			bool fixedPlacement = data.ReadBoolean();
			if ( host.Prefabs.TryGetValue( hash, out var path ) && PrefabFile.Load( path ) is { } prefab )
			{
				var placed = GameObject.Clone( prefab, new Transform( position, rotation, scale ), parent );
				GtaFlagTags.AddPlacement( placed, (uint)flags );
				GtaFlagTags.ApplyShadows( placed.Components.Get<ModelRenderer>( true ), 0, (uint)flags );
				placedAt?.Invoke( i, placed );
				if ( fixedPlacement || (flags & PlacementFlags.Fixed) != 0 ) placed.IsStatic = true;
				var prop = placed.Components.Get<Prop>();
				if ( prop is not null )
				{
					if ( (flags & PlacementFlags.Fixed) != 0 ) prop.IsStatic = true;
					if ( (flags & PlacementFlags.SuppressEmbeddedCollision) != 0 ) prop.PhysicsEnabled = false;
				}
				else if ( (flags & PlacementFlags.SuppressEmbeddedCollision) != 0 )
				{
					placed.Components.Get<ModelCollider>()?.Destroy();
				}
			}
			else
			{
				var missing = new GameObject( true, $"Missing GTA archetype {hash:x8}" );
				GtaFlagTags.AddPlacement( missing, (uint)flags );
				missing.IsStatic = fixedPlacement || (flags & PlacementFlags.Fixed) != 0;
				missing.Parent = parent;
				missing.LocalTransform = new Transform( position, rotation, scale );
			}
		}
	}
}

internal sealed class ArchetypeLoader( Entry entry ) : ResourceLoader<GtaMount>
{
	internal IReadOnlyList<ArchetypeExtension> Extensions => Host.GetExtensions( entry );
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
			var root = new GameObject( true, entry.Name ) { IsStatic = data.ReadBoolean() || entry.Member == ArchetypePhysics.ParkingHut2 };
			GtaFlagTags.AddArchetype( root, (uint)flags );
			if ( modelName.Length > 0 )
			{
				var path = $"mount://{Host.Ident}/models/{modelName}.vmdl";
				Model model = null;
				if ( Application.IsDedicatedServer )
				{
					if ( Host.GetByPath( path ) is AssetLoader asset && !asset.TryGetPhysics( out model ) )
					{
						using var packet = asset.ReadPhysics();
						model = asset.LoadPhysics( packet );
					}
				}
				else
				{
					model = Model.Load( path );
				}
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
					var renderer = root.AddComponent<ModelRenderer>();
					renderer.Model = model;
					GtaFlagTags.ApplyShadows( renderer, (uint)flags, 0 );
					if ( model.Physics?.Parts.Count > 0 )
						root.AddComponent<ModelCollider>().Model = model;
				}
				else
				{
					// Enable the prop in the finished prefab. Its procedural components belong to the instance.
					var prop = root.Components.Create<Prop>( false );
					enableProp = true;
					prop.IsStatic = root.IsStatic || (flags & ArchetypeFlags.Fixed) != 0 || (flags & ArchetypeFlags.Object) == 0;
					prop.StartAsleep = true;
					prop.Model = model;
					if ( (flags & ArchetypeFlags.Object) != 0 ) prop.Health = ArchetypePhysics.ObjectHealth;
				}
			}
			var interiorObjects = new System.Collections.Generic.Dictionary<int, GameObject>();
			Placements.Build( Host, data, root, (index, child) => interiorObjects.Add( index, child ) );
			if ( data.BaseStream.Position < data.BaseStream.Length )
				InteriorRooms.Read( Host, data, root, interiorObjects );
			ChairArchetypes.Attach( root, entry.Name );
			foreach ( var extension in Host.GetExtensions( entry ) ) extension.Apply( root );
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
