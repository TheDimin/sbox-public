# SceneLab agent guide

SceneLab exercises the managed scene renderer against native rendering. Read the [renderer guide](../../Sandbox.SceneRenderer/README.md) for architecture, conventions and verification. For SceneLab commands, preset construction and capture details, read [SceneLab reference](REFERENCE.md) when relevant; verify current behavior in source.

Everything the managed world can express must also reach `NativeScene` in `NativeReference.cs`. Mirror new object, lighting and preset settings so parity compares the same scene. A new GameObject preset needs a meaningful change and a negative control showing its comparison can fail.

Captures and benchmarks have mode-specific repeatability limits; compare like-for-like and preserve actual failed or unrun results. A running SceneLab can lock managed DLLs; coordinate with its owner before closing or rebuilding. Follow the current resource guard and preserve other tasks.
