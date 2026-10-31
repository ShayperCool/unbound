# Inactive 3D Rigidbody continuation

The starting source is d5d42fb25dca89d9d8541f553cb3697417d030e9. The existing
1,050 inactive records and 20,700 active records each have two byte-identical
independent Unity 6000.0.71f1 Linux captures. Their hashes were verified before
runtime changes. The inactive baseline passes 318 records and retains 732.

InactiveCommandSequences.cs adds 4,668 observations per independent Native
process. Both captures are byte-identical. Cases use nonidentity initial rotation
and translated poses, Transform changes before and after adding Rigidbody,
awake and sleeping deactivation, initially inactive and deactivated clones,
ordered Move and pose setter sequences, inactive sync and PublishTransform,
Sleep/WakeUp, velocity writes, activation and twenty simulation steps.

Native retains the pose from component attachment or deactivation while inactive.
Transform edits and inactive sync leave that pose unchanged. Rigidbody setters
replace their physics pose component without publishing Transform. Move commands,
PublishTransform and Sleep/WakeUp leave the inactive pose and sleep unchanged.
Activation takes the current Transform pose. Dynamic velocity survives
deactivation and inactive velocity writes; kinematic velocity writes are ignored.
Clones begin with zero velocities. These statements describe the measured cases;
older releases remain unmeasured and this is not full engine parity.

native-manifest.json hashes the exact sources, settings and independent captures.
Raw projects and execution logs are in
/mnt/storage-hdd/node-b-caches-20261023/inactive3d-t3-3a3af6f7-20261031/.
