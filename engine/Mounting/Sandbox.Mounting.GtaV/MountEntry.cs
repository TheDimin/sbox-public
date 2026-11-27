namespace GtaV;

internal enum Kind { Invalid = -2, Scene = 0, Prefab, Model, DictionaryModel, Texture, Collision, Fragment, FragmentGroup, VehicleBody, VehicleWheel }

internal readonly record struct Entry( Kind Kind, uint File, uint Member, string Name );
