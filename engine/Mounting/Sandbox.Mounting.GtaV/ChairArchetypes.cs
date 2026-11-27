using Sandbox;

namespace GtaV;

internal static class ChairArchetypes
{
	// Seat surface coordinates in metres, relative to the drawable origin.
	private static float? SeatHeight( string name ) => name switch
	{
		"prop_chair_01a" or "prop_chair_02" or "prop_chair_07" or "prop_chair_09" => 0.45f,
		"prop_chair_03" or "prop_chair_04a" or "prop_chair_04b" or "prop_chair_05" or "prop_chair_06" or "prop_chair_10" => 0.40f,
		"prop_chair_08" => -0.10f,
		"prop_table_01_chr_b" => -0.05f,
		"prop_bench_01a" => 0.479f,
		_ => null
	};

	static string ArchetypeName( string path ) => path.StartsWith( "fragments/", System.StringComparison.Ordinal ) ? path[10..] : path;
	internal static bool IsChair( string path ) => SeatHeight( ArchetypeName( path ) ).HasValue;
	internal const float MinimumMass = 10_000f;

	internal static BaseChair[] Attach( GameObject owner, string path )
	{
		var name = ArchetypeName( path );
		if ( SeatHeight( name ) is not float height ) return [];
		if ( name == "prop_bench_01a" )
		{
			// Each retail scenario point gets its own interaction volume and occupant.
			return [BenchSeat( owner, "Left seat", 0.551f, height ), BenchSeat( owner, "Right seat", -0.509f, height )];
		}
		return [CreateSeat( owner, height, name == "prop_table_01_chr_b" ? Rotation.Identity : Rotation.FromYaw( -90 ) )];
	}

	static BaseChair BenchSeat( GameObject owner, string label, float x, float height )
	{
		var region = new GameObject( owner, true, label );
		region.LocalPosition = new Vector3( x, 0.0167f, 0 ) * PacketReader.UnitsPerMetre;
		var hit = region.AddComponent<BoxCollider>();
		hit.IsTrigger = true;
		hit.Center = new Vector3( 0, -0.05f, height + 0.1f ) * PacketReader.UnitsPerMetre;
		hit.Scale = new Vector3( 1.02f, 0.72f, 0.65f ) * PacketReader.UnitsPerMetre;
		return CreateSeat( region, height, Rotation.FromYaw( -90 ) );
	}

	static BaseChair CreateSeat( GameObject owner, float height, Rotation facing )
	{
		GameObject Marker( string label, float forward, float z, float side = 0 )
		{
			var marker = new GameObject( owner, true, label );
			marker.LocalPosition = facing * new Vector3( forward, side, z ) * PacketReader.UnitsPerMetre;
			marker.LocalRotation = facing;
			return marker;
		}

		var chair = owner.AddComponent<BaseChair>();
		chair.SeatPosition = Marker( "Seat", 0, height - 0.34f );
		chair.EyePosition = Marker( "Seated eyes", 0, height + 0.94f );
		float ground = height - 0.40f;
		chair.ExitPoints = [Marker( "Front exit", 0.85f, ground ), Marker( "Left exit", 0, ground, 0.85f ), Marker( "Right exit", 0, ground, -0.85f )];
		return chair;
	}

	internal static void Eject( BaseChair[] seats )
	{
		foreach ( var chair in seats )
			if ( chair.IsValid() && chair.GetOccupant() is { } player ) chair.Eject( player );
	}
}
