# Authored glTF camera integration

The document reader exposes perspective and orthographic camera parameters; it
does not create engine cameras or import glTF nodes as scene components. The
comparison runner's recipe camera is separately authored presentation state and
is not evidence of authored-camera import.

An exact scene-import contract must preserve the selected scene's camera node,
its animated world transform and authored camera-to-node association. glTF
cameras look along local -Z with +Y up; source metres must be converted to engine
inches. Import must not select or activate a camera implicitly without a defined
policy. Model loading by itself is not a scene import.

Existing CameraComponent fields are insufficient for every valid projection:

- Perspective `yfov` is vertical radians; the camera uses degrees and clamps its
  render FOV to 1–179 degrees. Authored aspect can differ from the viewport and
  needs explicit viewport/aspect treatment. Missing `zfar` specifies an infinite
  perspective projection; substituting a finite distance changes visibility.
- Orthographic `xmag` and `ymag` independently specify horizontal and vertical
  half extents. The component exposes height and derives width from viewport
  aspect; both extents cannot be preserved for arbitrary viewport shapes by
  setting height alone.
- `znear` and finite `zfar` are metres in the asset, while engine values are
  inches. Scaling or shearing inherited from node transforms and animation must
  be handled according to camera transform semantics, rather than discarded by a
  generic affine-to-TRS conversion without validation.

A faithful integration therefore needs a projection/viewport representation
that can express these cases and scene-component lifetime/activation semantics.
No approximation is claimed here. Tests should compare independent analytic
projection matrices and landmarks for aspect mismatch, asymmetric orthographic
magnitudes, infinite far, near/far boundaries, camera node ancestry and animated
camera transforms; then verify actual rendering in the native engine. Reader
parameter tests and recipe captures remain separate evidence.
