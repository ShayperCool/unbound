//! The CPU time of the calling thread, for tests that judge how a workload's cost grows: unlike
//! the wall clock, a loaded machine does not stretch it, and the ratio of two such times does not
//! depend on the machine or the build profile.

use std::time::Duration;

/// The CPU time the calling thread has used so far.
pub fn thread_cpu_time() -> Duration {
    let mut t = libc::timespec {
        tv_sec: 0,
        tv_nsec: 0,
    };
    // SAFETY: `t` is a valid timespec for the call to write.
    let status = unsafe { libc::clock_gettime(libc::CLOCK_THREAD_CPUTIME_ID, &mut t) };
    assert_eq!(status, 0, "{}", std::io::Error::last_os_error());
    Duration::new(t.tv_sec as u64, t.tv_nsec as u32)
}

/// The least CPU times of `a` and of `b` on the calling thread over `runs` runs of each, taken in
/// turn (a, b, a, b, ...) so that a change in the machine's speed during the measurement reaches
/// both alike. Work that a closure hands to other threads is not counted.
pub fn least_cpu_times(
    runs: usize,
    mut a: impl FnMut(),
    mut b: impl FnMut(),
) -> (Duration, Duration) {
    assert!(runs > 0);
    let timed = |f: &mut dyn FnMut()| {
        let start = thread_cpu_time();
        f();
        thread_cpu_time() - start
    };
    let (mut least_a, mut least_b) = (Duration::MAX, Duration::MAX);
    for _ in 0..runs {
        least_a = least_a.min(timed(&mut a));
        least_b = least_b.min(timed(&mut b));
    }
    (least_a, least_b)
}

#[cfg(test)]
mod tests {
    use super::*;

    /// Spins until the calling thread has used `cpu` of CPU time.
    fn spin(cpu: Duration) {
        let start = thread_cpu_time();
        while thread_cpu_time() - start < cpu {
            std::hint::spin_loop();
        }
    }

    #[test]
    fn counts_the_threads_own_work_only() {
        let (busy, idle) = least_cpu_times(
            3,
            || spin(Duration::from_millis(20)),
            // A sleep, and work on another thread, take no CPU time of this one.
            || {
                std::thread::sleep(Duration::from_millis(20));
                std::thread::spawn(|| spin(Duration::from_millis(20)))
                    .join()
                    .unwrap();
            },
        );
        assert!(busy >= Duration::from_millis(20), "{busy:?}");
        assert!(idle < Duration::from_millis(10), "{idle:?}");
    }

    #[test]
    fn takes_the_least_of_the_runs() {
        let mut calls = 0;
        let (a, _) = least_cpu_times(
            3,
            || {
                // The first run is the long one.
                calls += 1;
                spin(Duration::from_millis(if calls == 1 { 40 } else { 5 }));
            },
            || {},
        );
        assert_eq!(calls, 3);
        assert!(a < Duration::from_millis(30), "{a:?}");
    }
}
