# Rejected runtime registration experiment

This experiment flushed structural physics before and after runtime
AddComponent<Rigidbody>, applied isKinematic immediately, and retained activation
activity when rebuilding newly created bodies. It fixed all eight introduced
island-clone observations and the fifteen setter observations. The focused
island contact-order probe matched fresh and clone points, retaining activation
differences.

The complete 21-profile gate rejected it: 4,909/5,055 match, with 96 introduced
failures against the frozen shim6 baseline. Most new failures involve fresh and
activated kd pairs without island history. The unit run also exposes an existing
deferred-mass assumption once Rigidbody allocation happens earlier: the impulse
test yields velocity6 instead of3. Tests and all raw profile outputs are retained.

The runtime AddComponent change was removed. These data are an unsuccessful
experiment, not accepted parity evidence. Original Native recordings and the
comparison tolerances were unchanged.
