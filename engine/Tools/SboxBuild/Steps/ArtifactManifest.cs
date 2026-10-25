using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Facepunch.Steps;

internal record ArtifactFileInfo
{
	[JsonPropertyName( "path" )]
	public string Path { get; init; }

	[JsonPropertyName( "sha256" )]
	public string Sha256 { get; init; }

	/// <summary>
	/// Optional relative file alias. Hash and size describe the resolved payload so older
	/// downloaders can still read the manifest (but will restore aliases as ordinary files).
	/// </summary>
	[JsonPropertyName( "linkTarget" )]
	[JsonIgnore( Condition = JsonIgnoreCondition.WhenWritingNull )]
	public string LinkTarget { get; init; }

	[JsonPropertyName( "size" )]
	public long Size { get; init; }
}

internal record ArtifactManifest
{
	[JsonPropertyName( "commit" )]
	public string Commit { get; init; }

	[JsonPropertyName( "timestamp" )]
	public string Timestamp { get; init; }

	[JsonPropertyName( "files" )]
	public List<ArtifactFileInfo> Files { get; init; }
}
