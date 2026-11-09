# Animation review: reproduction and remaining work

The current InterpolationTest capture applies only `Step Scale` out of the nine
requested clips. The pinned asset has nine separate animations, each targeting a
different node, and a static tenth node. The existing public sequence selects a
single animation; `GltfDocument.EvaluatePose` also accepts only one animation.
The previous per-clip numeric results do not establish simultaneous playback.

A general solution needs an explicit composition API and conflict policy, not a
special-case model or nine overlapping copies of the entire scene. For this
sample, gather sampled channel updates from all nine selected clips at the same
authored time into one pose before calculating hierarchy and GPU output. Reject
duplicate node/property targets explicitly unless a separately designed blend
policy is selected. Preserve rest values, source samplers, interpolation types,
per-clip duration, morph data, ownership, and the existing single-sequence path.
Expose actual public component control before using it in `engine/Tools/Gltf/editor/editor_corpus.csx`;
report nine applied clips only when all nine are truly submitted together.

Regression evidence should compare every node against the existing independent
InterpolationTest source oracle at 0, .25, .5, .75, 1, 1.5, and 2 seconds,
including distinct STEP, LINEAR and CUBICSPLINE results and the unchanged plane.
Include conflict rejection and a single-clip control. Then capture all nine via
the public component with actual poses and record submitted revisions and a
negative control. A deterministic seek capture does not prove wall-clock timing.

AnimatedColorsCube's source also animates material baseColorFactor via optional
`KHR_animation_pointer`; the current evaluator skips its node-less channel and
the public material path does not apply that animation. Denser captures can
show its authored translation and rotation but cannot establish animated color.
The color fix needs pointer validation and typed property sampling, per-instance
material state, unchanged shared asset state, authored interpolation and an
independent source-value oracle plus public GPU/material render evidence. Test
red, midpoint, green and subsequent blue values, instance isolation and reset;
do not claim color support based on capture cadence alone.

AnimatedTriangle's existing APNG covers 1.25 seconds of a 1-second clip and
loops from a .20-second pose back to zero. Its frame discontinuity is a capture
plan defect; this evidence does not establish an engine playback-clock defect.
The revised recipe samples one authored cycle. Fox's existing reviewed result is
a still; its prepared dense Survey recipe needs actual capture and user review.
