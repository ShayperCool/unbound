//! The job system's C table: the managed engine gets it through the engine's native API, the
//! managed unit tests from this crate's shared library (`unbound_jobs_api`). Its managed mirror is
//! `JobsApi` in `managed/UnityEngine/Jobs/JobSystem.Native.cs`.

use crate::Callbacks;

/// The version of [`JobsApi`]; the managed side refuses a table of another version.
pub const JOBS_API_VERSION: u32 = 1;

#[repr(C)]
pub struct JobsApi {
    pub version: u32,
    /// (prepare, execute, finish): see [`Callbacks`].
    pub set_callbacks: extern "C" fn(
        extern "C-unwind" fn(usize) -> u32,
        extern "C-unwind" fn(usize, i32, i32),
        extern "C-unwind" fn(usize),
    ),
    /// (user, dependency handle or 0) -> handle: a job into the batch.
    pub schedule: extern "C" fn(usize, u64) -> u64,
    /// (user, handles, count) -> handle: a combination of dependencies.
    pub combine: extern "C" fn(usize, *const u64, i32) -> u64,
    /// (handle, held) -> whether the job is still batched.
    pub set_held: extern "C" fn(u64, i32) -> i32,
    pub flush: extern "C" fn(),
    pub start: extern "C" fn(u64),
    /// (handle) -> whether the job was not done yet: runs or waits for it.
    pub wait: extern "C" fn(u64) -> i32,
    pub is_done: extern "C" fn(u64) -> i32,
    pub release: extern "C" fn(u64),
    pub processor_count: extern "C" fn() -> i32,
    pub max_workers: extern "C" fn() -> i32,
    pub worker_count: extern "C" fn() -> i32,
    pub set_worker_count: extern "C" fn(i32),
    pub thread_index: extern "C" fn() -> i32,
}

static API: JobsApi = JobsApi {
    version: JOBS_API_VERSION,
    set_callbacks,
    schedule,
    combine,
    set_held,
    flush,
    start,
    wait,
    is_done,
    release,
    processor_count,
    max_workers,
    worker_count,
    set_worker_count,
    thread_index,
};

/// The table.
pub fn api() -> &'static JobsApi {
    &API
}

/// The table, for a process that loads this crate as a shared library.
#[unsafe(no_mangle)]
pub extern "C" fn unbound_jobs_api() -> *const JobsApi {
    &API
}

extern "C" fn set_callbacks(
    prepare: extern "C-unwind" fn(usize) -> u32,
    execute: extern "C-unwind" fn(usize, i32, i32),
    finish: extern "C-unwind" fn(usize),
) {
    crate::set_callbacks(Callbacks {
        prepare,
        execute,
        finish,
    });
}

extern "C" fn combine(user: usize, handles: *const u64, count: i32) -> u64 {
    let handles = if handles.is_null() || count <= 0 {
        &[][..]
    } else {
        // SAFETY: the caller passes `count` handles at `handles`.
        unsafe { std::slice::from_raw_parts(handles, count as usize) }
    };
    crate::combine(user, handles)
}

extern "C" fn set_held(handle: u64, held: i32) -> i32 {
    crate::set_held(handle, held != 0).into()
}

extern "C" fn is_done(handle: u64) -> i32 {
    crate::is_done(handle).into()
}

extern "C" fn processor_count() -> i32 {
    crate::processor_count() as i32
}

extern "C" fn max_workers() -> i32 {
    crate::max_workers() as i32
}

extern "C" fn worker_count() -> i32 {
    crate::worker_count() as i32
}

extern "C" fn set_worker_count(count: i32) {
    crate::set_worker_count(count.max(0) as usize);
}

extern "C" fn schedule(user: usize, dependency: u64) -> u64 {
    crate::schedule(user, dependency)
}

extern "C" fn flush() {
    crate::flush();
}

extern "C" fn start(handle: u64) {
    crate::start(handle);
}

extern "C" fn wait(handle: u64) -> i32 {
    crate::wait(handle).into()
}

extern "C" fn release(handle: u64) {
    crate::release(handle);
}

extern "C" fn thread_index() -> i32 {
    crate::thread_index()
}
