//! Unbound's job system: Unity's job semantics on the job workers' rayon pool ([`workers`]).
//!
//! A scheduled job waits in the batch until it is flushed (`JobHandle.ScheduleBatchedJobs`,
//! `IsCompleted`, `Complete`) and its dependencies are done; then its participants (one, or a
//! parallel-for's job indices, which take and steal batches of its range on the managed side) run
//! on the workers. [`wait`] (`Complete`) runs on the calling thread what is not started yet, the
//! job and its dependencies, as Unity's `JobHandle.Complete` "attempts to execute the job on the
//! thread which calls the Complete method", helps with the participants left, and otherwise blocks
//! until the job is done. A combination (`CombineDependencies`) is done when its dependencies are.
//!
//! The jobs are the managed engine's: each carries a `user` value its callbacks get back
//! (`managed/UnityEngine/Jobs/JobSystem.cs`, through [`ffi`]).

pub mod ffi;
mod workers;

#[cfg(test)]
mod tests;

pub use workers::{max_workers, processor_count, thread_index, worker_count};

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicBool, AtomicU32, Ordering};
use std::sync::{Arc, Condvar, Mutex, MutexGuard, PoisonError};

/// The functions a job's `user` value is passed to. They may unwind (Rust callers' panics are
/// caught and reported: the job still completes); the managed ones catch their exceptions.
#[derive(Clone, Copy)]
pub struct Callbacks {
    /// Called once, right before the first participant runs (the dependencies are done): returns
    /// the job's participant count.
    pub prepare: extern "C-unwind" fn(usize) -> u32,
    /// Runs participant (job index) `.1` on the thread of `JobsUtility.ThreadIndex` `.2`.
    pub execute: extern "C-unwind" fn(usize, i32, i32),
    /// Called once after the last participant, before the job counts as done.
    pub finish: extern "C-unwind" fn(usize),
}

#[derive(Clone, Copy, PartialEq, Eq, Debug)]
enum State {
    /// Scheduled, waiting for a flush.
    Batched,
    /// Flushed, waiting for its dependencies.
    Waiting,
    /// Handed to the workers or to a waiting thread: preparing or running.
    Launched,
    Done,
}

/// A slot and its generation: a handle (`JobHandle.jobGroup`) is `generation << 32 | slot + 1`, so
/// 0 is no job and the handle of a released job never names the slot's next job.
#[derive(Clone, Copy, PartialEq, Eq, Hash, Debug)]
struct Key {
    slot: u32,
    generation: u32,
}

impl Key {
    fn handle(self) -> u64 {
        (u64::from(self.generation) << 32) | u64::from(self.slot + 1)
    }

    fn of(handle: u64) -> Option<Key> {
        let low = handle as u32;
        (low != 0).then(|| Key {
            slot: low - 1,
            generation: (handle >> 32) as u32,
        })
    }
}

/// What the threads running a job's participants share.
struct Run {
    key: Key,
    user: usize,
    callbacks: Callbacks,
    /// Set by the thread that prepares the job.
    preparing: AtomicBool,
    /// Preparing it panicked: its participants do not run its body.
    prepare_failed: AtomicBool,
    /// 0 until the job is prepared.
    participants: AtomicU32,
    /// The next participant to claim.
    next: AtomicU32,
    /// Participants not finished.
    remaining: AtomicU32,
}

struct Node {
    run: Arc<Run>,
    combination: bool,
    state: State,
    /// Kept in the batch by a flush (engine jobs preparing their inputs).
    held: bool,
    /// Dependencies not done.
    pending: u32,
    /// The dependencies that were not done when it was scheduled.
    dependencies: Vec<Key>,
    dependents: Vec<Key>,
    /// Threads blocked in [`wait`] on it.
    waiters: u32,
}

#[derive(Default)]
struct Slot {
    generation: u32,
    node: Option<Node>,
}

struct Graph {
    slots: Vec<Slot>,
    free: Vec<u32>,
    /// The scheduled jobs a flush hands out; jobs started otherwise meanwhile are skipped.
    batch: Vec<Key>,
    callbacks: Option<Callbacks>,
    /// Jobs launched while there were no workers: their participants wait for a thread that waits
    /// for them, or for the next workers ([`set_worker_count`]).
    unhelped: Vec<Arc<Run>>,
    /// The length at which `unhelped` drops the runs nobody needs to help any more (doubling, so
    /// that pruning stays linear in the launches).
    unhelped_prune_at: usize,
}

static GRAPH: Mutex<Graph> = Mutex::new(Graph {
    slots: Vec::new(),
    free: Vec::new(),
    batch: Vec::new(),
    callbacks: None,
    unhelped: Vec::new(),
    unhelped_prune_at: UNHELPED_PRUNE_MIN,
});

/// The smallest length at which the list of runs launched without workers is pruned.
const UNHELPED_PRUNE_MIN: usize = 64;

/// Signalled when a job some thread waits for is done or ready to run, and, while there are no
/// workers, when it is prepared.
static CHANGED: Condvar = Condvar::new();

fn graph() -> MutexGuard<'static, Graph> {
    GRAPH.lock().unwrap_or_else(PoisonError::into_inner)
}

impl Node {
    /// Whether a thread waiting for it may still have to run part of it or wait for it: not
    /// done, and not prepared with every participant taken.
    fn open_here(&self) -> bool {
        if self.state != State::Launched {
            return self.state != State::Done;
        }
        let run = &self.run;
        let participants = run.participants.load(Ordering::Acquire);
        !run.preparing.load(Ordering::Acquire)
            || participants == 0
            || run.next.load(Ordering::Relaxed) < participants
    }
}

impl Graph {
    fn node(&self, key: Key) -> Option<&Node> {
        let slot = self.slots.get(key.slot as usize)?;
        if slot.generation != key.generation {
            return None;
        }
        slot.node.as_ref()
    }

    fn node_mut(&mut self, key: Key) -> Option<&mut Node> {
        let slot = self.slots.get_mut(key.slot as usize)?;
        if slot.generation != key.generation {
            return None;
        }
        slot.node.as_mut()
    }

    fn insert(
        &mut self,
        user: usize,
        combination: bool,
        state: State,
        dependencies: &[u64],
    ) -> Key {
        let callbacks = self
            .callbacks
            .expect("Unbound: a job was scheduled before the job system's callbacks were set");
        let slot = match self.free.pop() {
            Some(slot) => slot,
            None => {
                self.slots.push(Slot {
                    generation: 1,
                    node: None,
                });
                (self.slots.len() - 1) as u32
            }
        };
        let key = Key {
            slot,
            generation: self.slots[slot as usize].generation,
        };
        let mut node = Node {
            run: Arc::new(Run {
                key,
                user,
                callbacks,
                preparing: AtomicBool::new(false),
                prepare_failed: AtomicBool::new(false),
                participants: AtomicU32::new(0),
                next: AtomicU32::new(0),
                remaining: AtomicU32::new(0),
            }),
            combination,
            state,
            held: false,
            pending: 0,
            dependencies: Vec::new(),
            dependents: Vec::new(),
            waiters: 0,
        };
        for &dependency in dependencies {
            let Some(dependency) = Key::of(dependency) else {
                continue;
            };
            if node.dependencies.contains(&dependency) {
                continue;
            }
            // A released or done dependency is no dependency.
            let Some(other) = self.node_mut(dependency) else {
                continue;
            };
            if other.state == State::Done {
                continue;
            }
            other.dependents.push(key);
            node.pending += 1;
            node.dependencies.push(dependency);
        }
        if combination && node.pending == 0 {
            node.state = State::Done;
        }
        self.slots[slot as usize].node = Some(node);
        key
    }

    fn free(&mut self, key: Key) {
        let slot = &mut self.slots[key.slot as usize];
        slot.node = None;
        slot.generation = slot.generation.wrapping_add(1).max(1);
        self.free.push(key.slot);
    }

    /// Marks a job done, and the combinations that completes; collects the dependents now ready to
    /// run. Returns whether a thread waits for one of them.
    fn complete(&mut self, key: Key, ready: &mut Vec<Arc<Run>>) -> bool {
        let mut wake = false;
        // Combinations completed on the way (allocated only for them).
        let mut combinations = Vec::new();
        let mut next = Some(key);
        while let Some(key) = next.take().or_else(|| combinations.pop()) {
            let Some(node) = self.node_mut(key) else {
                continue;
            };
            node.state = State::Done;
            wake |= node.waiters != 0;
            for dependent in std::mem::take(&mut node.dependents) {
                let Some(node) = self.node_mut(dependent) else {
                    continue;
                };
                node.pending -= 1;
                if node.pending != 0 {
                    continue;
                }
                // A waiting thread may run a batched job now.
                wake |= node.waiters != 0;
                if node.combination {
                    combinations.push(dependent);
                } else if node.state == State::Waiting {
                    node.state = State::Launched;
                    ready.push(node.run.clone());
                }
            }
        }
        wake
    }

    /// The job and the unfinished dependencies it waits for, each after its dependencies.
    fn in_dependency_order(&self, key: Key) -> Vec<Key> {
        let Some(node) = self.node(key) else {
            return Vec::new();
        };
        if node.pending == 0 {
            return vec![key];
        }
        let mut order = Vec::new();
        let mut seen = std::collections::HashSet::new();
        let mut stack = vec![(key, false)];
        while let Some((key, expanded)) = stack.pop() {
            if expanded {
                order.push(key);
                continue;
            }
            let Some(node) = self.node(key) else { continue };
            if node.state == State::Done || !seen.insert(key) {
                continue;
            }
            stack.push((key, true));
            stack.extend(node.dependencies.iter().map(|&d| (d, false)));
        }
        order
    }
}

/// Sets the managed engine's callbacks; the jobs scheduled from then on use them.
pub fn set_callbacks(callbacks: Callbacks) {
    graph().callbacks = Some(callbacks);
}

/// Schedules a job into the batch, after `dependency` (a handle, or 0).
pub fn schedule(user: usize, dependency: u64) -> u64 {
    let mut graph = graph();
    let key = graph.insert(user, false, State::Batched, &[dependency]);
    graph.batch.push(key);
    key.handle()
}

/// A job done when `dependencies` are (`JobHandle.CombineDependencies`), with nothing to run.
pub fn combine(user: usize, dependencies: &[u64]) -> u64 {
    graph()
        .insert(user, true, State::Waiting, dependencies)
        .handle()
}

/// Keeps a batched job in the batch over flushes (or lets it go); returns whether it is batched.
pub fn set_held(handle: u64, held: bool) -> bool {
    let Some(key) = Key::of(handle) else {
        return false;
    };
    let mut graph = graph();
    match graph.node_mut(key) {
        Some(node) => {
            node.held = held;
            node.state == State::Batched
        }
        None => false,
    }
}

/// Hands the batch to the workers (`ScheduleBatchedJobs`): the jobs whose dependencies are done
/// start, the others when they are. Held jobs stay batched.
pub fn flush() {
    let mut ready = Vec::new();
    {
        let mut graph = graph();
        let mut batch = std::mem::take(&mut graph.batch);
        batch.retain(|&key| {
            let Some(node) = graph.node_mut(key) else {
                return false;
            };
            if node.state != State::Batched {
                return false;
            }
            if node.held {
                return true;
            }
            if node.pending == 0 {
                node.state = State::Launched;
                ready.push(node.run.clone());
            } else {
                node.state = State::Waiting;
            }
            false
        });
        graph.batch = batch;
    }
    launch_all(ready);
}

/// Hands one batched job to the workers, held or not, leaving the rest of the batch.
pub fn start(handle: u64) {
    let Some(key) = Key::of(handle) else { return };
    let run = {
        let mut graph = graph();
        let Some(node) = graph.node_mut(key) else {
            return;
        };
        if node.state != State::Batched {
            return;
        }
        if node.pending != 0 {
            node.state = State::Waiting;
            return;
        }
        node.state = State::Launched;
        node.run.clone()
    };
    launch(run, false);
}

/// Whether the job is done (a released or unknown handle is).
pub fn is_done(handle: u64) -> bool {
    let Some(key) = Key::of(handle) else {
        return true;
    };
    graph()
        .node(key)
        .is_none_or(|node| node.state == State::Done)
}

/// Forgets a done job: its handle names no job from now on. A job not done is kept.
pub fn release(handle: u64) {
    let Some(key) = Key::of(handle) else { return };
    let mut graph = graph();
    match graph.node(key) {
        Some(node) if node.state == State::Done => graph.free(key),
        Some(node) => tracing::error!("Unbound: job {handle:#x} released while {:?}", node.state),
        None => {}
    }
}

/// Returns once the job is done (`JobHandle.Complete` without the flush and the safety sync). What
/// it waits for that has not started runs here, dependencies first, and so do a parallel job's
/// participants left; the thread sleeps only while everything it waits for runs elsewhere.
/// Returns whether the job was not done yet.
pub fn wait(handle: u64) -> bool {
    let Some(target) = Key::of(handle) else {
        return false;
    };
    let mut graph = graph();
    let mut order = graph.in_dependency_order(target);
    let open =
        |graph: &Graph, key: Key| key == target || graph.node(key).is_some_and(Node::open_here);
    // Jobs before `first` are done or taken by other threads.
    let mut first = 0;
    let mut pending = false;
    loop {
        if graph
            .node(target)
            .is_none_or(|node| node.state == State::Done)
        {
            return pending;
        }
        pending = true;
        while !open(&graph, order[first]) {
            first += 1;
        }
        if let Some((run, prepared)) = runnable_here(&mut graph, &order[first..]) {
            drop(graph);
            if prepared {
                run_participants(&run);
            } else {
                launch(run, true);
            }
            graph = self::graph();
            continue;
        }
        // Everything left runs elsewhere (or waits for what does): sleep until the first of it
        // is done or ready. What this thread no longer waits for or can take part in drops out.
        order.drain(..first);
        first = 0;
        order.retain(|&key| open(&graph, key));
        let key = order[0];
        if let Some(node) = graph.node_mut(key) {
            node.waiters += 1;
        }
        graph = CHANGED.wait(graph).unwrap_or_else(PoisonError::into_inner);
        if let Some(node) = graph.node_mut(key) {
            node.waiters -= 1;
        }
    }
}

/// The first of `keys` this thread can work on: a job whose dependencies are done that nobody
/// prepared yet (claimed for launching here: false), or a prepared one with participants left
/// (true).
fn runnable_here(graph: &mut Graph, keys: &[Key]) -> Option<(Arc<Run>, bool)> {
    for &key in keys {
        let Some(node) = graph.node_mut(key) else {
            continue;
        };
        match node.state {
            State::Batched | State::Waiting if node.pending == 0 && !node.combination => {
                node.state = State::Launched;
                return Some((node.run.clone(), false));
            }
            State::Launched => {
                let run = &node.run;
                if !run.preparing.load(Ordering::Acquire) {
                    return Some((run.clone(), false));
                }
                let participants = run.participants.load(Ordering::Acquire);
                if participants != 0 && run.next.load(Ordering::Relaxed) < participants {
                    return Some((run.clone(), true));
                }
            }
            _ => {}
        }
    }
    None
}

/// Prepares a launched job, unless another thread already does, and runs its participants: on
/// this thread too when `here`.
fn launch(run: Arc<Run>, here: bool) {
    if run.preparing.swap(true, Ordering::AcqRel) {
        return;
    }
    let participants = catch_unwind(AssertUnwindSafe(|| (run.callbacks.prepare)(run.user)))
        .unwrap_or_else(|panic| {
            report_panic("preparing", &run, &panic);
            run.prepare_failed.store(true, Ordering::Relaxed);
            1
        })
        .max(1);
    run.remaining.store(participants, Ordering::Relaxed);
    run.participants.store(participants, Ordering::Release);
    let helpers = (participants - u32::from(here)) as usize;
    if helpers != 0 {
        if workers::on_current_worker() {
            // On a worker of the current pool: its own queue, without the workers' lock.
            for _ in 0..helpers.min(rayon::current_num_threads()) {
                let run = run.clone();
                rayon::spawn(move || run_participants(&run));
            }
        } else if let Some(pool) = workers::pool() {
            spawn_helpers(&pool, &run, helpers);
        } else {
            // No workers (JobWorkerCount 0): a thread waiting for the job runs its participants,
            // or the next workers do.
            {
                let mut graph = graph();
                if graph.unhelped.len() >= graph.unhelped_prune_at {
                    graph.unhelped.retain(|run| run.unclaimed() != 0);
                    graph.unhelped_prune_at = UNHELPED_PRUNE_MIN.max(2 * graph.unhelped.len());
                }
                graph.unhelped.push(run.clone());
                if graph.node(run.key).is_some_and(|node| node.waiters != 0) {
                    CHANGED.notify_all();
                }
            }
            // The count may have been raised meanwhile.
            if worker_count() != 0 {
                help_unhelped();
            }
        }
    }
    if here {
        run_participants(&run);
    }
}

fn spawn_helpers(pool: &rayon::ThreadPool, run: &Arc<Run>, helpers: usize) {
    for _ in 0..helpers.min(pool.current_num_threads()) {
        let run = run.clone();
        pool.spawn(move || run_participants(&run));
    }
}

/// Sets `JobsUtility.JobWorkerCount` ([`workers::set_worker_count`]); the participants left of
/// jobs launched while there were no workers go to the new workers.
pub fn set_worker_count(count: usize) {
    workers::set_worker_count(count);
    help_unhelped();
}

/// Hands the participants left of the jobs launched without workers to the workers, if there are
/// any now. (A launch adds to the list, then reads the count; a count change writes the count,
/// then takes the list: one of them sees the other.)
fn help_unhelped() {
    loop {
        let runs = std::mem::take(&mut graph().unhelped);
        if runs.is_empty() {
            return;
        }
        if let Some(pool) = workers::pool() {
            for run in runs {
                spawn_helpers(&pool, &run, run.unclaimed() as usize);
            }
            return;
        }
        graph().unhelped.extend(runs);
        if worker_count() == 0 {
            return;
        }
    }
}

/// Launches jobs that became ready. Several ready on a thread that is not a current worker (a
/// flush, or a worker of a pool the worker count replaced) go
/// to the workers in one task, which launches them there: one wake-up of the pool instead of one
/// per job.
fn launch_all(mut ready: Vec<Arc<Run>>) {
    if ready.len() > 1 && !workers::on_current_worker() {
        if let Some(pool) = workers::pool() {
            pool.spawn(move || ready.into_iter().for_each(|run| launch(run, false)));
            return;
        }
    }
    if ready.len() == 1 {
        launch(ready.pop().unwrap(), false);
        return;
    }
    for run in ready {
        launch(run, false);
    }
}

impl Run {
    /// Participants nobody claimed yet (0 before the job is prepared).
    fn unclaimed(&self) -> u32 {
        let participants = self.participants.load(Ordering::Acquire);
        participants.saturating_sub(self.next.load(Ordering::Relaxed))
    }
}

/// Claims and runs the job's participants until none is left.
fn run_participants(run: &Run) {
    let participants = run.participants.load(Ordering::Acquire);
    loop {
        let index = run.next.fetch_add(1, Ordering::Relaxed);
        if index >= participants {
            return;
        }
        let thread = thread_index();
        if run.prepare_failed.load(Ordering::Relaxed) {
            // Its body would run unprepared; it only completes.
        } else if let Err(panic) = catch_unwind(AssertUnwindSafe(|| {
            (run.callbacks.execute)(run.user, index as i32, thread)
        })) {
            report_panic("running", run, &panic);
        }
        if run.remaining.fetch_sub(1, Ordering::AcqRel) == 1 {
            finish(run);
        }
    }
}

fn finish(run: &Run) {
    if let Err(panic) = catch_unwind(AssertUnwindSafe(|| (run.callbacks.finish)(run.user))) {
        report_panic("finishing", run, &panic);
    }
    let mut ready = Vec::new();
    let wake = graph().complete(run.key, &mut ready);
    if wake {
        CHANGED.notify_all();
    }
    launch_all(ready);
}

fn report_panic(what: &str, run: &Run, panic: &Box<dyn std::any::Any + Send>) {
    tracing::error!(
        "Unbound: a job panicked while {what} (job {:#x}): {}",
        run.key.handle(),
        panic_message(panic)
    );
}

pub(crate) fn panic_message(panic: &Box<dyn std::any::Any + Send>) -> &str {
    if let Some(message) = panic.downcast_ref::<&str>() {
        message
    } else if let Some(message) = panic.downcast_ref::<String>() {
        message
    } else {
        "(no message)"
    }
}
