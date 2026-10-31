# Rejected eager kinematic setter experiment

Creating new bodies dynamically and starting a zero-displacement kinematic target
for activation matches 5,027/5,055 scene-settings observations. None of its 28
remaining failures is introduced relative to the frozen shim6 baseline. Extended
trigger callbacks and all15 immediate setter observations match, and41 unit
tests pass. The153 extended sleeping-state differences remain.

The broader physics fixture nevertheless rejects the candidate. Calling Flush
from isKinematic even when the native body does not exist changes allocation
order in 3d.kinematic, causing a new failing scenario. Its281 observations matched
the baseline before this experiment. Physics goes from8 to9 failing scenarios.
The complete failing output and comparison are retained here.

The next candidate applies the type change immediately to an existing native
body, while preserving the original lazy creation path for an unrealized body.
Reading any native-backed property still realizes the body's pending type.
No reference, expectation or tolerance was changed.
