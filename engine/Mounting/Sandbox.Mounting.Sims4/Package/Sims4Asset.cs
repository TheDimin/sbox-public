using System.Collections.Generic;

namespace Sandbox.Mounting.Sims4;

public enum Sims4AssetKind
{
    Model,
    Texture,
}

public readonly record struct Sims4Asset(Sims4AssetKind Kind, DbpfRecord Record);

public readonly record struct StblNameEntry(uint Key, string Text);
