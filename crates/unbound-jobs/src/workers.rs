//! The job workers: one rayon thread pool of `JobsUtility.JobWorkerCount` threads named
//! `Job.Worker N`.

use rayon::{ThreadPool, ThreadPoolBuilder};
use std::cell::Cell;
use std::sync::atomic::{AtomicU64, Ordering};
use std::sync::{Arc, Mutex, MutexGuard, OnceLock, PoisonError};

thread_local! {
    /// `JobsUtility.ThreadIndex` of a worker thread: its index in its pool plus one (the main
    /// thread, and every thread that is not a worker, is 0).
    static WORKER_INDEX: Cell<i32> = const { Cell::new(0) };
    /// The generation of the pool a worker thread belongs to (0 on other threads).
    static WORKER_GENERATION: Cell<u64> = const { Cell::new(0) };
}

/// The generation of the current pool: it changes with the worker count (generations start at 1).
static GENERATION: AtomicU64 = AtomicU64::new(1);

/// The logical processors the process may run on (`SystemInfo.processorCount`): the affinity mask
/// and the cgroup CPU quota included (a fractional quota rounds down, at least 1).
pub fn processor_count() -> usize {
    static COUNT: OnceLock<usize> = OnceLock::new();
    *COUNT.get_or_init(|| std::thread::available_parallelism().map_or(1, |n| n.get()))
}

/// `JobsUtility.JobWorkerMaximumCount`: a worker per processor but the main thread's, at least one.
pub fn max_workers() -> usize {
    processor_count().saturating_sub(1).max(1)
}

struct Workers {
    count: usize,
    /// Built on first use, dropped when the count changes.
    pool: Option<Arc<ThreadPool>>,
}

fn workers() -> MutexGuard<'static, Workers> {
    static WORKERS: OnceLock<Mutex<Workers>> = OnceLock::new();
    WORKERS
        .get_or_init(|| {
            Mutex::new(Workers {
                count: max_workers(),
                pool: None,
            })
        })
        .lock()
        .unwrap_or_else(PoisonError::into_inner)
}

/// `JobsUtility.JobWorkerCount`.
pub fn worker_count() -> usize {
    workers().count
}

/// Sets `JobsUtility.JobWorkerCount` (at most [`max_workers`]; the managed side checks the range).
/// A new count gets a new pool, so that the workers' thread indices stay below it; the previous
/// pool's threads finish the work already handed to them, then exit, and hand the jobs that work
/// makes ready to the new pool.
pub fn set_worker_count(count: usize) {
    let count = count.min(max_workers());
    let mut workers = workers();
    if workers.count != count {
        workers.count = count;
        workers.pool = None;
        GENERATION.fetch_add(1, Ordering::AcqRel);
    }
}

/// `JobsUtility.ThreadIndex` of the calling thread.
pub fn thread_index() -> i32 {
    WORKER_INDEX.get()
}

/// Whether the calling thread is a worker of the current pool, which may queue work on its own
/// pool without the workers' lock.
pub(crate) fn on_current_worker() -> bool {
    WORKER_GENERATION.get() == GENERATION.load(Ordering::Acquire)
}

/// The pool of the current worker count, none while it is 0.
pub(crate) fn pool() -> Option<Arc<ThreadPool>> {
    let mut workers = workers();
    if workers.count == 0 {
        return None;
    }
    if workers.pool.is_none() {
        let generation = GENERATION.load(Ordering::Acquire);
        let pool = ThreadPoolBuilder::new()
            .num_threads(workers.count)
            .thread_name(|i| format!("Job.Worker {i}"))
            .start_handler(move |i| {
                WORKER_INDEX.set(i as i32 + 1);
                WORKER_GENERATION.set(generation);
            })
            .panic_handler(|panic| {
                tracing::error!(
                    "Unbound: a job worker task panicked: {}",
                    crate::panic_message(&panic)
                )
            })
            .build()
            .unwrap_or_else(|e| panic!("Unbound: cannot start the job worker threads: {e}"));
        workers.pool = Some(Arc::new(pool));
    }
    workers.pool.clone()
}
