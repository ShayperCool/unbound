//! Instrumented lock types.
//!
//! This module defines a set of instrumented wrappers for the lock
//! types used in `wgpu-core` ([`Mutex`], [`RwLock`], and
//! [`SnatchLock`]) that help us understand and validate `wgpu-core`
//! synchronization.
//!
//! - The [`ranked`] module defines lock types that perform run-time
//!   checks to ensure that each thread acquires locks only in a
//!   specific order, to prevent deadlocks.
//!
//! - The [`observing`] module defines lock types that record
//!   `wgpu-core`'s lock acquisition activity to disk, for later
//!   analysis by the `lock-analyzer` binary.
//!
//! - The [`vanilla`] module defines lock types that are
//!   uninstrumented, no-overhead wrappers around the standard lock
//!   types.
//!
//! If the `wgpu_validate_locks` config is set (for example, with
//! `RUSTFLAGS='--cfg wgpu_validate_locks'`), `wgpu-core` uses the
//! [`ranked`] module's locks. We hope to make this the default for
//! debug builds soon.
//!
//! If the `observe_locks` feature is enabled, `wgpu-core` uses the
//! [`observing`] module's locks.
//!
//! Otherwise, `wgpu-core` uses the [`vanilla`] module's locks.
//!
//! [`Mutex`]: parking_lot::Mutex
//! [`RwLock`]: parking_lot::RwLock
//! [`SnatchLock`]: crate::snatch::SnatchLock

pub mod rank;

#[cfg(feature = "std")] // requires thread-locals to work
#[cfg_attr(not(wgpu_validate_locks), allow(dead_code))]
mod ranked;

#[cfg(feature = "observe_locks")]
mod observing;

#[cfg_attr(any(wgpu_validate_locks, feature = "observe_locks"), allow(dead_code))]
mod vanilla;

#[cfg(wgpu_validate_locks)]
use ranked as chosen;

#[cfg(feature = "observe_locks")]
use observing as chosen;

#[cfg(not(any(wgpu_validate_locks, feature = "observe_locks")))]
use vanilla as chosen;

pub use chosen::{Mutex, MutexGuard, RankData, RwLock, RwLockReadGuard, RwLockWriteGuard};

/// Unbound: coordinates scoped GLES EGL context pinning (`wgpu::Device::with_gles_context_pinned`)
/// with `wgpu-core`'s internal locks so the lock order stays strictly acyclic:
/// `SUBMISSION -> GL_PIN_GATE -> wgpu-core locks -> wgpu-hal AdapterContext`.
///
/// While no GLES device exists (e.g. Vulkan / Metal), `GL_PIN_ENABLED` stays `false` and the
/// reader path is a single relaxed atomic load.
#[cfg(all(feature = "std", not(any(windows, target_arch = "wasm32"))))]
pub(crate) mod gles_pin {
    use core::{
        cell::Cell,
        marker::PhantomData,
        sync::atomic::{AtomicBool, Ordering},
    };
    use parking_lot::lock_api::{RawRwLock as _, RawRwLockRecursive as _};

    static GL_PIN_ENABLED: AtomicBool = AtomicBool::new(false);
    static GL_PIN_GATE: parking_lot::RawRwLock = parking_lot::RawRwLock::INIT;

    std::thread_local! {
        // (exclusive_writer_depth, shared_reader_depth)
        static GATE_DEPTH: Cell<(usize, usize)> = const { Cell::new((0, 0)) };
    }

    pub(crate) fn enable() {
        GL_PIN_ENABLED.store(true, Ordering::Release);
    }

    pub(crate) struct CoreGateGuard(bool);

    impl CoreGateGuard {
        #[inline]
        pub(crate) fn enter() -> Self {
            if !GL_PIN_ENABLED.load(Ordering::Relaxed) {
                return Self(false);
            }
            GATE_DEPTH.with(|cell| {
                let (w, r) = cell.get();
                if w > 0 {
                    return Self(false);
                }
                if r == 0 {
                    GL_PIN_GATE.lock_shared();
                }
                cell.set((w, r + 1));
                Self(true)
            })
        }

        pub(crate) fn detach(self) -> bool {
            let active = self.0;
            core::mem::forget(self);
            if !active {
                return false;
            }
            GATE_DEPTH.with(|cell| {
                let (w, r) = cell.get();
                debug_assert!(r > 0);
                let next_r = r - 1;
                cell.set((w, next_r));
                if next_r > 0 {
                    GL_PIN_GATE.lock_shared_recursive();
                }
            });
            true
        }
    }

    impl Drop for CoreGateGuard {
        #[inline]
        fn drop(&mut self) {
            if self.0 {
                GATE_DEPTH.with(|cell| {
                    let (w, r) = cell.get();
                    debug_assert!(r > 0);
                    let next_r = r - 1;
                    cell.set((w, next_r));
                    if next_r == 0 {
                        // SAFETY: matched with the `lock_shared()` acquired on `0 -> 1`.
                        unsafe { GL_PIN_GATE.unlock_shared() };
                    }
                });
            }
        }
    }

    pub(crate) unsafe fn release_detached(detached: bool) {
        if detached {
            // SAFETY: matched with `detach()` transferring/acquiring one shared count.
            unsafe { GL_PIN_GATE.unlock_shared() };
        }
    }

    /// Exclusive gate guard held while a thread pins the GLES EGL context across `wgpu` calls.
    #[expect(missing_debug_implementations)]
    pub struct GlesPinGateGuard {
        active: bool,
        _not_send: PhantomData<*const ()>,
    }

    /// Acquires exclusive access to the `wgpu-core` GLES pin gate (reentrant on the owning thread).
    pub fn enter_gles_pin_gate() -> GlesPinGateGuard {
        GL_PIN_ENABLED.store(true, Ordering::Release);
        GATE_DEPTH.with(|cell| {
            let (w, r) = cell.get();
            debug_assert_eq!(
                r, 0,
                "cannot pin GLES context while holding a wgpu-core lock"
            );
            if w == 0 {
                GL_PIN_GATE.lock_exclusive();
            }
            cell.set((w + 1, r));
        });
        GlesPinGateGuard {
            active: true,
            _not_send: PhantomData,
        }
    }

    impl Drop for GlesPinGateGuard {
        fn drop(&mut self) {
            if self.active {
                GATE_DEPTH.with(|cell| {
                    let (w, r) = cell.get();
                    debug_assert!(w > 0);
                    let next_w = w - 1;
                    cell.set((next_w, r));
                    if next_w == 0 {
                        // SAFETY: matched with `lock_exclusive()` acquired on `0 -> 1`.
                        unsafe { GL_PIN_GATE.unlock_exclusive() };
                    }
                });
            }
        }
    }
}

#[cfg(not(all(feature = "std", not(any(windows, target_arch = "wasm32")))))]
pub(crate) mod gles_pin {
    pub(crate) fn enable() {}
    pub(crate) struct CoreGateGuard;
    impl CoreGateGuard {
        #[inline]
        pub(crate) fn enter() -> Self {
            Self
        }
        #[inline]
        pub(crate) fn detach(self) -> bool {
            false
        }
    }
    pub(crate) unsafe fn release_detached(_detached: bool) {}
    #[expect(missing_debug_implementations)]
    pub struct GlesPinGateGuard;
    pub fn enter_gles_pin_gate() -> GlesPinGateGuard {
        GlesPinGateGuard
    }
}

pub use gles_pin::{enter_gles_pin_gate, GlesPinGateGuard};
