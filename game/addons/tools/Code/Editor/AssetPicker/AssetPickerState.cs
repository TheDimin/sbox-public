using System;

namespace Editor.AssetPickers;

/// <summary>
/// Recent navigation for one asset picker filter combination. Stored in the project cookie.
/// </summary>
internal sealed class AssetPickerState
{
	public DateTime ClosedAt { get; set; }
	public string Tab { get; set; } = "Asset Browser";
	public BrowserState Local { get; set; }
	public BrowserState Mounts { get; set; }
	public string CloudSearch { get; set; }
	public string CloudFilter { get; set; }
	public string Package { get; set; }

	public bool IsRecent( DateTime now ) => now >= ClosedAt && now - ClosedAt < TimeSpan.FromMinutes( 10 );

	public string GetTab( bool enableCloud, bool enableMounts ) => Tab switch
	{
		"Cloud Browser" when enableCloud => Tab,
		"Mounts" when enableMounts => Tab,
		_ => "Asset Browser"
	};

	public sealed class BrowserState
	{
		public string Path { get; set; }
		public string Search { get; set; }
		public string Asset { get; set; }
	}
}
