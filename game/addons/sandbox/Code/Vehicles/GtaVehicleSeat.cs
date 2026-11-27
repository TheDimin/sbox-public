/// <summary>A GTA vehicle seat with a separate exterior entry point.</summary>
public sealed class GtaVehicleSeat : BaseChair
{
	[Property] public GameObject Door { get; set; }
	[Property] public bool DrivesVehicle { get; set; }

	[Property] public Vector3 LeftFootPosition { get; set; }
	[Property] public Vector3 RightFootPosition { get; set; }

	SkinnedModelRenderer seatedRenderer;

	public override bool CanEnter( PlayerController player )
	{
		if ( !base.CanEnter( player ) || !Active || !Door.IsValid() ) return false;
		if ( player.GetComponentInParent<BaseChair>( false ).IsValid() ) return false;

		return Vector3.DistanceBetweenSquared( player.EyePosition, Door.WorldPosition ) <= player.ReachLength * player.ReachLength;
	}

	public override void UpdatePlayerAnimator( PlayerController controller, SkinnedModelRenderer renderer )
	{
		base.UpdatePlayerAnimator( controller, renderer );

		if ( seatedRenderer != renderer ) ClearFeet();
		seatedRenderer = renderer;
		SetFoot( renderer, "foot_left", LeftFootPosition );
		SetFoot( renderer, "foot_right", RightFootPosition );
	}

	void SetFoot( SkinnedModelRenderer renderer, string parameter, Vector3 position )
	{
		if ( position == Vector3.Zero ) return;

		var target = new Transform( WorldTransform.PointToWorld( position ), WorldRotation );
		if ( renderer.Animation.Graph is not null )
		{
			renderer.Animation.Parameters.TrySet( $"ik.{parameter}", renderer.WorldTransform.ToLocal( target ) );
			renderer.Animation.Parameters.TrySet( $"ik.{parameter}.enabled", true );
			return;
		}

#pragma warning disable CS0618 // Match BaseChair's legacy animation graph path.
		renderer.SetIk( parameter, target );
#pragma warning restore CS0618
	}

	protected override void OnUpdate()
	{
		if ( seatedRenderer.IsValid() && GetOccupant()?.Renderer != seatedRenderer )
			ClearFeet();
	}

	protected override void OnDisabled() => ClearFeet();

	void ClearFeet()
	{
		if ( !seatedRenderer.IsValid() ) return;

		if ( LeftFootPosition != Vector3.Zero ) ClearFoot( seatedRenderer, "foot_left" );
		if ( RightFootPosition != Vector3.Zero ) ClearFoot( seatedRenderer, "foot_right" );
		seatedRenderer = null;
	}

	static void ClearFoot( SkinnedModelRenderer renderer, string parameter )
	{
		if ( renderer.Animation.Graph is not null )
			renderer.Animation.Parameters.TrySet( $"ik.{parameter}.enabled", false );
		else
		{
#pragma warning disable CS0618 // Match BaseChair's legacy animation graph path.
			renderer.ClearIk( parameter );
#pragma warning restore CS0618
		}
	}
}
