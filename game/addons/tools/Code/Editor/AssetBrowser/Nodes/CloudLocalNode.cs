namespace Editor.AssetBrowsing.Nodes;

partial class CloudLocalNode : AssetFilterNode, ResourceLibrary.IEventListener
{
	public CloudLocalNode() : base( "attach_file", "Referenced", "@referenced" )
	{
		EditorEvent.Register( this );
	}

	~CloudLocalNode()
	{
		EditorEvent.Unregister( this );
	}

	void ResourceLibrary.IEventListener.OnSave( GameResource resource ) => InvalidateCount();
	void ResourceLibrary.IEventListener.OnExternalChanges( GameResource resource ) => InvalidateCount();

	bool _countStale = true;

	void InvalidateCount()
	{
		_countStale = true;
		TreeView?.Update();
	}

	public override void OnPaint( VirtualWidget item )
	{
		if ( _countStale )
		{
			_countStale = false;
			Count = CloudAsset.GetAssetReferences( true ).Count;
		}
		base.OnPaint( item );
	}
}
