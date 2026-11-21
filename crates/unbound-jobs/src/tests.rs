use super::*;
use std::sync::atomic::{AtomicBool, AtomicI32, AtomicU32};
use std::sync::{Condvar, Mutex, Once, mpsc};
use std::thread;
use std::time::Duration;

/// A job of the tests: its participant count and body; what its callbacks saw.
struct TestJob {
    participants: u32,
    body: Box<dyn Fn(i32, i32) + Send + Sync>,
    prepared: AtomicU32,
    executed: AtomicU32,
    finished: AtomicU32,
    panic_in_prepare: bool,
    panic_in_finish: bool,
}

impl TestJob {
    fn user(&'static self) -> usize {
        self as *const TestJob as usize
    }
}

fn of(user: usize) -> &'static TestJob {
    // SAFETY: every user value of the tests is a leaked TestJob.
    unsafe { &*(user as *const TestJob) }
}

extern "C-unwind" fn prepare(user: usize) -> u32 {
    let job = of(user);
    job.prepared.fetch_add(1, Ordering::SeqCst);
    if job.panic_in_prepare {
        panic!("test panic in prepare");
    }
    job.participants
}

extern "C-unwind" fn execute(user: usize, index: i32, thread: i32) {
    let job = of(user);
    job.executed.fetch_add(1, Ordering::SeqCst);
    (job.body)(index, thread);
}

extern "C-unwind" fn finish(user: usize) {
    let job = of(user);
    job.finished.fetch_add(1, Ordering::SeqCst);
    if job.panic_in_finish {
        panic!("test panic in finish");
    }
}

fn new_job(participants: u32, body: impl Fn(i32, i32) + Send + Sync + 'static) -> &'static TestJob {
    Box::leak(Box::new(TestJob {
        participants,
        body: Box::new(body),
        prepared: AtomicU32::new(0),
        executed: AtomicU32::new(0),
        finished: AtomicU32::new(0),
        panic_in_prepare: false,
        panic_in_finish: false,
    }))
}

fn noop() -> &'static TestJob {
    new_job(1, |_, _| {})
}

/// Runs a test on its own thread (the "main" thread of the job system: not a worker), one test at
/// a time (the worker count is global), failing it when it does not end within 30 s.
fn run(test: impl FnOnce() + Send + 'static) {
    static SERIAL: Mutex<()> = Mutex::new(());
    static CALLBACKS: Once = Once::new();
    let _serial = SERIAL.lock().unwrap_or_else(PoisonError::into_inner);
    CALLBACKS.call_once(|| {
        set_callbacks(Callbacks {
            prepare,
            execute,
            finish,
        })
    });
    set_worker_count(max_workers());
    let (sender, receiver) = mpsc::channel();
    let runner = thread::spawn(move || {
        test();
        let _ = sender.send(());
    });
    match receiver.recv_timeout(Duration::from_secs(30)) {
        Ok(()) => runner.join().unwrap(),
        Err(mpsc::RecvTimeoutError::Disconnected) => {
            std::panic::resume_unwind(runner.join().unwrap_err())
        }
        Err(mpsc::RecvTimeoutError::Timeout) => panic!("the test did not finish within 30 s"),
    }
    set_worker_count(max_workers());
}

/// A gate the tests open from one thread to release bodies blocked on another.
struct Gate(Mutex<bool>, Condvar);

impl Gate {
    fn new() -> &'static Gate {
        Box::leak(Box::new(Gate(Mutex::new(false), Condvar::new())))
    }
    fn open(&self) {
        *self.0.lock().unwrap() = true;
        self.1.notify_all();
    }
    fn pass(&self) {
        let mut open = self.0.lock().unwrap();
        while !*open {
            open = self.1.wait(open).unwrap();
        }
    }
}

#[test]
fn a_batched_job_starts_only_when_flushed() {
    run(|| {
        let job = noop();
        let handle = schedule(job.user(), 0);
        // Nothing hands a batched job to the workers.
        thread::sleep(Duration::from_millis(50));
        assert_eq!(job.prepared.load(Ordering::SeqCst), 0);
        assert!(!is_done(handle));
        flush();
        assert!(wait(handle));
        assert!(is_done(handle));
        assert!(!wait(handle));
        assert_eq!(
            (
                job.prepared.load(Ordering::SeqCst),
                job.executed.load(Ordering::SeqCst),
                job.finished.load(Ordering::SeqCst)
            ),
            (1, 1, 1)
        );
        release(handle);
        assert!(is_done(handle));
    });
}

#[test]
fn wait_runs_unstarted_jobs_and_their_dependencies_on_the_calling_thread() {
    run(|| {
        let caller = thread::current().id();
        let order = Box::leak(Box::new(Mutex::new(Vec::new())));
        let make = |name: &'static str| {
            let order = &*order;
            new_job(1, move |_, thread_index| {
                assert_eq!(thread::current().id(), caller);
                assert_eq!(thread_index, 0);
                order.lock().unwrap().push(name);
            })
        };
        let a = schedule(make("a").user(), 0);
        let b = schedule(make("b").user(), a);
        let c = schedule(make("c").user(), b);
        let other = noop();
        let unrelated = schedule(other.user(), 0);
        wait(c);
        assert_eq!(*order.lock().unwrap(), ["a", "b", "c"]);
        // Wait runs what it waits for, not the rest of the batch.
        assert_eq!(other.executed.load(Ordering::SeqCst), 0);
        flush();
        wait(unrelated);
        for handle in [a, b, c, unrelated] {
            release(handle);
        }
    });
}

#[test]
fn dependents_run_after_their_dependencies() {
    run(|| {
        let order = Box::leak(Box::new(Mutex::new(Vec::new())));
        let mut previous = 0;
        let mut handles = Vec::new();
        for i in 0..200 {
            let order = &*order;
            previous = schedule(
                new_job(1, move |_, _| order.lock().unwrap().push(i)).user(),
                previous,
            );
            handles.push(previous);
        }
        flush();
        wait(previous);
        assert_eq!(*order.lock().unwrap(), (0..200).collect::<Vec<_>>());
        handles.into_iter().for_each(release);
    });
}

#[test]
fn a_combination_is_done_when_its_dependencies_are() {
    run(|| {
        let gate = Gate::new();
        let blocked = new_job(1, move |_, _| gate.pass());
        let first = schedule(blocked.user(), 0);
        let second = schedule(noop().user(), 0);
        let combined = combine(0, &[first, second, first]);
        flush();
        assert!(!is_done(combined));
        gate.open();
        wait(combined);
        assert!(is_done(first) && is_done(second));
        // Of nothing, or of a released job: done at once.
        let empty = combine(0, &[]);
        assert!(is_done(empty));
        for handle in [first, second, combined, empty] {
            release(handle);
        }
        let stale = combine(0, &[first]);
        assert!(is_done(stale));
        release(stale);
    });
}

#[test]
fn every_participant_of_a_parallel_job_runs_once() {
    run(|| {
        let counts: &'static [AtomicU32; 16] =
            Box::leak(Box::new(std::array::from_fn(|_| AtomicU32::new(0))));
        let job = new_job(16, move |index, thread_index| {
            assert!((0..=max_workers() as i32).contains(&thread_index));
            counts[index as usize].fetch_add(1, Ordering::SeqCst);
        });
        let handle = schedule(job.user(), 0);
        flush();
        wait(handle);
        assert!(counts.iter().all(|c| c.load(Ordering::SeqCst) == 1));
        assert_eq!(job.finished.load(Ordering::SeqCst), 1);
        release(handle);
    });
}

/// A panic in a job's callbacks is reported, and the job still completes: its waiters return and
/// its dependents run (before, a panicking participant left its waiters blocked for ever).
#[test]
fn a_panicking_job_still_completes() {
    run(|| {
        for (in_prepare, in_body, in_finish) in [
            (true, false, false),
            (false, true, false),
            (false, false, true),
        ] {
            let job = Box::leak(Box::new(TestJob {
                participants: 4,
                body: Box::new(move |index, _| {
                    if in_body && index == 1 {
                        panic!("test panic in a participant");
                    }
                }),
                prepared: AtomicU32::new(0),
                executed: AtomicU32::new(0),
                finished: AtomicU32::new(0),
                panic_in_prepare: in_prepare,
                panic_in_finish: in_finish,
            }));
            let dependent = noop();
            let handle = schedule(job.user(), 0);
            let after = schedule(dependent.user(), handle);
            flush();
            wait(after);
            assert!(is_done(handle));
            assert_eq!(dependent.executed.load(Ordering::SeqCst), 1);
            release(handle);
            release(after);
        }
    });
}

/// With JobWorkerCount 0 nothing runs the jobs but the thread that waits for them.
#[test]
fn without_workers_the_waiting_thread_runs_every_participant() {
    run(|| {
        set_worker_count(0);
        assert_eq!(worker_count(), 0);
        let caller = thread::current().id();
        let job = new_job(8, move |_, thread_index| {
            assert_eq!(thread::current().id(), caller);
            assert_eq!(thread_index, 0);
        });
        let first = schedule(noop().user(), 0);
        let handle = schedule(job.user(), first);
        flush();
        wait(handle);
        assert_eq!(job.executed.load(Ordering::SeqCst), 8);
        release(first);
        release(handle);
    });
}

/// Jobs handed to the workers before JobWorkerCount drops to 0 still run (before, they could stay
/// in a parked worker's queue for ever).
#[test]
fn lowering_the_worker_count_strands_no_job() {
    run(|| {
        let gate = Gate::new();
        let started = Box::leak(Box::new(AtomicU32::new(0)));
        let handles: Vec<u64> = (0..64)
            .map(|_| {
                let started = &*started;
                schedule(
                    new_job(4, move |_, thread_index| {
                        if thread_index != 0 {
                            started.fetch_add(1, Ordering::SeqCst);
                            gate.pass();
                        }
                    })
                    .user(),
                    0,
                )
            })
            .collect();
        flush();
        set_worker_count(0);
        gate.open();
        for &handle in &handles {
            wait(handle);
        }
        handles.into_iter().for_each(release);
    });
}

#[test]
fn worker_thread_indices_follow_the_worker_count() {
    run(|| {
        assert!(max_workers() >= 1);
        assert_eq!(thread_index(), 0);
        let count = max_workers().min(2);
        set_worker_count(count);
        let seen = Box::leak(Box::new(Mutex::new(Vec::new())));
        let gate = Gate::new();
        let job = {
            let seen = &*seen;
            new_job(64, move |_, thread_index| {
                if thread_index != 0 {
                    let name = thread::current().name().unwrap_or_default().to_string();
                    assert_eq!(name, format!("Job.Worker {}", thread_index - 1));
                }
                seen.lock().unwrap().push(thread_index);
                gate.pass();
            })
        };
        let handle = schedule(job.user(), 0);
        flush();
        gate.open();
        wait(handle);
        let seen = seen.lock().unwrap();
        assert_eq!(seen.len(), 64);
        assert!(
            seen.iter().all(|&i| (0..=count as i32).contains(&i)),
            "{seen:?}"
        );
        release(handle);
    });
}

/// Wait claims the participants nobody took yet while a parallel job runs on the workers (here
/// the only worker blocks until a participant ran on the waiting thread).
#[test]
fn wait_helps_with_a_running_parallel_job() {
    run(|| {
        set_worker_count(1);
        let gate = Gate::new();
        let job = new_job(4, move |_, thread_index| {
            if thread_index == 0 {
                gate.open();
            } else {
                gate.pass();
            }
        });
        let handle = schedule(job.user(), 0);
        flush();
        wait(handle);
        assert_eq!(job.executed.load(Ordering::SeqCst), 4);
        release(handle);
    });
}

#[test]
fn a_released_handle_names_no_job() {
    run(|| {
        let first = schedule(noop().user(), 0);
        flush();
        wait(first);
        release(first);
        // The slot's next job has another handle; the old one is done and no dependency.
        let next = noop();
        let second = schedule(next.user(), 0);
        assert_ne!(first, second);
        assert!(is_done(first));
        wait(first);
        assert_eq!(next.executed.load(Ordering::SeqCst), 0);
        let third = schedule(noop().user(), first);
        flush();
        wait(third);
        wait(second);
        release(second);
        release(third);
        // Not done: kept.
        let pending = schedule(noop().user(), 0);
        release(pending);
        assert!(!is_done(pending));
        wait(pending);
        release(pending);
    });
}

#[test]
fn held_jobs_stay_in_the_batch() {
    run(|| {
        let job = noop();
        let handle = schedule(job.user(), 0);
        assert!(set_held(handle, true));
        flush();
        thread::sleep(Duration::from_millis(20));
        assert_eq!(job.prepared.load(Ordering::SeqCst), 0);
        assert!(set_held(handle, false));
        flush();
        wait(handle);
        assert!(!set_held(handle, true));
        release(handle);
        // Start hands out one batched job, held or not, and leaves the others.
        let held = noop();
        let other = noop();
        let held_handle = schedule(held.user(), 0);
        let other_handle = schedule(other.user(), 0);
        set_held(held_handle, true);
        start(held_handle);
        wait(held_handle);
        assert_eq!(other.prepared.load(Ordering::SeqCst), 0);
        flush();
        wait(other_handle);
        release(held_handle);
        release(other_handle);
    });
}

/// A long chain waited for before any flush runs on the caller without deep recursion.
#[test]
fn waiting_for_a_long_chain_does_not_recurse() {
    run(|| {
        let ran = Box::leak(Box::new(AtomicI32::new(0)));
        let mut handles = Vec::with_capacity(100_000);
        let mut previous = 0;
        let body = {
            let ran = &*ran;
            new_job(1, move |_, _| {
                ran.fetch_add(1, Ordering::Relaxed);
            })
        };
        for _ in 0..100_000 {
            previous = schedule(body.user(), previous);
            handles.push(previous);
        }
        wait(previous);
        assert_eq!(ran.load(Ordering::Relaxed), 100_000);
        flush();
        handles.into_iter().for_each(release);
    });
}

#[test]
fn install_runs_on_the_job_workers() {
    run(|| {
        use rayon::prelude::*;
        let indices: Vec<i32> =
            install(|| (0..1000).into_par_iter().map(|_| thread_index()).collect());
        assert!(
            indices
                .iter()
                .all(|&i| (1..=worker_count() as i32).contains(&i))
        );
        set_worker_count(0);
        let ran = AtomicBool::new(false);
        install(|| ran.store(true, Ordering::SeqCst));
        assert!(ran.load(Ordering::SeqCst));
    });
}

/// Wait runs what it can of the jobs it waits for while another of them runs on a worker (here
/// the worker's job blocks until the waiting thread ran the other).
#[test]
fn wait_runs_later_jobs_while_an_earlier_one_runs_elsewhere() {
    run(|| {
        let gate = Gate::new();
        let started = Gate::new();
        let blocked = new_job(1, move |_, _| {
            started.open();
            gate.pass();
        });
        let opener = new_job(1, move |_, thread_index| {
            assert_eq!(thread_index, 0);
            gate.open();
        });
        let first = schedule(blocked.user(), 0);
        let second = schedule(opener.user(), 0);
        let both = combine(0, &[first, second]);
        start(first);
        started.pass();
        wait(both);
        for handle in [first, second, both] {
            release(handle);
        }
    });
}
