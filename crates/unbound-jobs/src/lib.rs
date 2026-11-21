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
//! (`managed/UnityEngine/Jobs/JobSystem.cs`, through [`ffi`]). Engine code in Rust runs its
//! parallel work on the same workers with [`install`].

pub mod ffi;
mod workers;

#[cfg(test)]
mod tests;

pub use workers::{
    install, max_workers, processor_count, set_worker_count, thread_index, worker_count,
};

use std::panic::{AssertUnwindSafe, catch_unwind};
use std::sync::atomic::{AtomicU32, Ordering};
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
}

static GRAPH: Mutex<Graph> = Mutex::new(Graph {
    slots: Vec::new(),
    free: Vec::new(),
    batch: Vec::new(),
    callbacks: None,
});

/// Signalled when a job some thread waits for is done, prepared or ready to run.
static CHANGED: Condvar = Condvar::new();

fn graph() -> MutexGuard<'static, Graph> {
    GRAPH.lock().unwrap_or_else(PoisonError::into_inner)
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
        let mut done = vec![key];
        while let Some(key) = done.pop() {
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
                    done.push(dependent);
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
    for run in ready {
        launch(run, false);
    }
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
/// it waits for that has not started runs here, dependencies first; a parallel job's participants
/// left run here too.
pub fn wait(handle: u64) {
    let Some(target) = Key::of(handle) else {
        return;
    };
    let mut graph = graph();
    for key in graph.in_dependency_order(target) {
        graph = run_here(graph, key);
    }
}

fn run_here(mut graph: MutexGuard<'static, Graph>, key: Key) -> MutexGuard<'static, Graph> {
    loop {
        let Some(node) = graph.node_mut(key) else {
            return graph;
        };
        let run = node.run.clone();
        match node.state {
            State::Done => return graph,
            State::Batched | State::Waiting if node.pending == 0 && !node.combination => {
                node.state = State::Launched;
                drop(graph);
                launch(run, true);
                graph = self::graph();
                continue;
            }
            State::Launched => {
                let participants = run.participants.load(Ordering::Acquire);
                if participants != 0 && run.next.load(Ordering::Relaxed) < participants {
                    drop(graph);
                    run_participants(&run);
                    graph = self::graph();
                    continue;
                }
            }
            _ => {}
        }
        // Running elsewhere (or, for a combination, its dependencies): sleep until it changes.
        node.waiters += 1;
        graph = CHANGED.wait(graph).unwrap_or_else(PoisonError::into_inner);
        if let Some(node) = graph.node_mut(key) {
            node.waiters -= 1;
        }
    }
}

/// Prepares a launched job and runs its participants: on this thread too when `here`.
fn launch(run: Arc<Run>, here: bool) {
    let participants = catch_unwind(AssertUnwindSafe(|| (run.callbacks.prepare)(run.user)))
        .unwrap_or_else(|panic| {
            report_panic("preparing", &run, &panic);
            1
        })
        .max(1);
    run.remaining.store(participants, Ordering::Relaxed);
    run.participants.store(participants, Ordering::Release);
    let helpers = participants - u32::from(here);
    if helpers != 0 {
        match workers::pool() {
            Some(pool) => {
                let tasks = (helpers as usize).min(pool.current_num_threads());
                for _ in 0..tasks {
                    let run = run.clone();
                    pool.spawn(move || run_participants(&run));
                }
            }
            // No workers (JobWorkerCount 0): a thread waiting for the job runs its participants.
            None => {
                let graph = graph();
                if graph.node(run.key).is_some_and(|node| node.waiters != 0) {
                    CHANGED.notify_all();
                }
            }
        }
    }
    if here {
        run_participants(&run);
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
        if let Err(panic) = catch_unwind(AssertUnwindSafe(|| {
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
    for run in ready {
        launch(run, false);
    }
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
