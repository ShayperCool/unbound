# Animation job velocity writes

Two animation jobs run over an empty mixer on a non-humanoid Animator without
a controller. One writes linear and angular velocities. The second samples both
job phases and, in the phase-order control, replaces the root callback's linear
velocity. The probe records the velocities and stream pose seen at each point,
then the Animator's delta and the actor's final transform. The controls include
zero-time evaluation, root motion disabled, and a rotated non-uniformly scaled
parent.

The Unity 6000.0.71f1 Editor uses the root callback's velocities for the output
step and passes the animation callback's writes to the following animation
callback. Translation is in the root frame. Angular displacement uses the three
axis rotations in X, Y, Z order. The stream pose reads independently of the
writes. The documented properties expose writable avatar velocities for the
evaluated frame. The coordinate and callback rules are observations from this
probe. [AnimationStream.velocity](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/animations/animationstream/velocity), [AnimationStream.angularVelocity](https://docs.unity.com/en-us/engine/6000.0/script-reference/unityengine/animations/animationstream/angularvelocity)

Record each repeat in a fresh cache project with `run.py record --release
6000.0.71f1 --label LABEL`. Use `run.py publish --release 6000.0.71f1 LABEL1
LABEL2` after both captures complete. The gate includes every recorded field
with an absolute numeric tolerance of 0.0002.
