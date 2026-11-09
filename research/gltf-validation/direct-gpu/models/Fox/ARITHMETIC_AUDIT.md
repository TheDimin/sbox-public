# Fox arithmetic audit

The original CPU acceptance checks remain red for 12 animated source/compiled cases. The fixed world-matrix component threshold is `4e-6` in source units; the sampled vertex-distance threshold is `2e-5`. No tolerance or producer change has been approved.

The exact-key/held-quaternion reference bug was first reproduced (two failing controls), corrected in the independent reference, and rerun successfully. This did not remove the original animated failures. The largest observed matrix errors remain `1.5258789e-5` (Survey/Walk) and `2.86102e-5` (Run). Source units and geometry are unchanged.

Both producer and reference multiply the same child-to-parent sequence of float matrices. The remaining strict-interior rotation paths use a different rounding boundary: the producer rounds the interpolated quaternion to float before normalizing those rounded components in double, while the independent reference normalizes the interpolated double quaternion before its first float cast. The engine TRS construction then normalizes the float quaternion in both paths. A component ULP can propagate through Fox's long translated bone chains. This is a source-level hypothesis for the observed discrepancies, not proof that every failed component is explained by rounding.

`FoxChannelArithmeticAudit.Run(reference)` records independently decoded channel values, raw float bits, exact-channel-key classification and physical rotation-angle differences at the four previously observed diagnostic times. It does not alter the reference sampler, call producer accessors to obtain expected values, or change any acceptance threshold. Execution is queued through the s&box validation coordinator.

Any proposed rounding allowance must account for operation count, translation scale and cancellation, and distinguish interpolation error from hierarchy and skinning error. The existing failed receipts remain evidence even if a later, justified numerical acceptance policy is agreed.
