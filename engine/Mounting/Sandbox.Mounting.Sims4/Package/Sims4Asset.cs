using System.Collections.Generic;

namespace Sandbox.Mounting.Sims4;

public readonly record struct Sims4Asset(ResourceType Kind, DbpfRecord Record);

public readonly record struct StblNameEntry(uint Key, string Text);
