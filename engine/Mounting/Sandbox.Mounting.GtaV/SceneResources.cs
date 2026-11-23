using System;
using System.IO;
using Sandbox;
using Sandbox.Mounting;

namespace GtaV;

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
			var position = data.Vector() * PacketReader.UnitsPerMetre;
			var rotation = new Rotation( data.ReadSingle(), data.ReadSingle(), data.ReadSingle(), data.ReadSingle() );
			float xy = data.ReadSingle();
			var scale = new Vector3( xy, xy, data.ReadSingle() );
			if ( host.Prefabs.TryGetValue( hash, out var path ) && PrefabFile.Load( path ) is { } prefab )
			{
				GameObject.Clone( prefab, new Transform( position, rotation, scale ), parent );
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
		var builder = new PrefabBuilder().WithName( Path );
		using ( builder.Scope() )
		{
			var root = new GameObject( true, entry.Name );
			if ( modelName.Length > 0 )
			{
				var model = Model.Load( $"mount://{Host.Ident}/models/{modelName}.vmdl" );
				root.AddComponent<ModelRenderer>().Model = model;
				if ( model.Physics?.Parts.Count > 0 )
				{
					var collider = root.AddComponent<ModelCollider>( false );
					collider.Model = model;
					collider.Static = true;
				}
			}
			Placements.Build( Host, data, root );
			foreach ( var extension in Host.GetExtensions( entry ) ) extension.Apply( root, false );
		}
		return prefab = builder.Create();
	}

	protected override void Shutdown() => PrefabBuilder.Destroy( prefab );
}
