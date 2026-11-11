//! `IUnityEventQueue` (`IUnityEventQueue.h`): plugins send events of their own types and handle
//! them later on the main thread. Behaviour matches the Unity 6000.0.71f1 Editor
//! (tests/projects/nativegfx `events.*`): a sent event is copied and waits; once a frame,
//! after the profiler's frame callbacks, the waiting events are handed in the order they were
//! sent to the handlers of their ID that are added then (one added after the event was sent
//! too, one removed meanwhile not), and then to the cleanup handler their type registered.
//!
//! The interface is a C++ class with virtual functions, and so are the handlers plugins pass:
//! both are laid out here as the Itanium C++ ABI lays them out (the ABI of the platforms
//! Unbound runs on): an object starts with the address of its table of virtual functions,
//! which take the object first.
use std::ffi::c_void;
use std::sync::Mutex;

/// `UnityEventQueue::EventId`. It has a copy constructor of its own, so it is passed and
/// returned by address.
#[repr(C)]
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
struct EventId {
    high: u64,
    low: u64,
}

/// What a virtual function returning an `EventId` returns: the caller passes the address of
/// the result as a hidden argument, which is how a larger aggregate is returned in C too.
/// The callee writes an `EventId` there.
#[repr(C)]
struct EventIdResult {
    id: EventId,
    _unused: u64,
}

/// `UnityEventQueue::EventHandler`: a plugin's object.
#[repr(C)]
struct Handler {
    table: *const HandlerTable,
    _next: *mut Handler,
}

/// The virtual functions of an `EventHandler`, in declaration order (a virtual destructor
/// takes two places).
#[repr(C)]
struct HandlerTable {
    _destroy: [usize; 2],
    handle_event: unsafe extern "C" fn(*mut Handler, *mut EventId, *mut c_void),
    handler_event_id: unsafe extern "C" fn(*mut Handler) -> EventIdResult,
    _get_my_event_destructor: unsafe extern "C" fn(*mut Handler) -> *mut Handler,
}

/// The virtual functions of `IUnityEventQueue`.
#[repr(C)]
struct QueueTable {
    destroy: [extern "C" fn(*mut Queue); 2],
    add_handler: unsafe extern "C" fn(*mut Queue, *mut Handler),
    remove_handler: unsafe extern "C" fn(*mut Queue, *mut Handler),
    send_event: unsafe extern "C" fn(*mut Queue, *const EventId, *const u8, i32),
    set_cleanup: unsafe extern "C" fn(*mut Queue, *mut Handler),
}

/// `IUnityEventQueue`: the one object plugins get.
#[repr(C)]
pub struct Queue {
    table: &'static QueueTable,
}

// SAFETY: the object is its table's address, never written.
unsafe impl Sync for Queue {}

static TABLE: QueueTable = QueueTable {
    destroy: [destroy, destroy],
    add_handler,
    remove_handler,
    send_event,
    set_cleanup,
};
pub static QUEUE: Queue = Queue { table: &TABLE };

/// `kMaxEventQueueEventSize` without the ID: the largest payload the header lets a plugin send.
const MAX_PAYLOAD: usize = 8196;

#[derive(Default)]
struct State {
    /// The handlers added, each with the ID it handles, in the order they were added.
    handlers: Vec<(EventId, usize)>,
    /// The cleanup handler of each event type that registered one.
    cleanups: Vec<(EventId, usize)>,
    /// The events sent and not handed out yet.
    waiting: Vec<(EventId, Vec<u8>)>,
}

static STATE: Mutex<Option<State>> = Mutex::new(None);

fn with<R>(f: impl FnOnce(&mut State) -> R) -> R {
    let mut state = STATE
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner());
    f(state.get_or_insert_with(State::default))
}

/// The ID a handler handles (its virtual `HandlerEventId`).
unsafe fn handled_id(handler: *mut Handler) -> EventId {
    unsafe { ((*(*handler).table).handler_event_id)(handler).id }
}

/// The queue belongs to the engine: a plugin that destroys it is told so.
extern "C" fn destroy(_: *mut Queue) {
    tracing::error!("a native plugin destroyed the IUnityEventQueue it was given: ignored");
}

unsafe extern "C" fn add_handler(_: *mut Queue, handler: *mut Handler) {
    if handler.is_null() {
        return;
    }
    let id = unsafe { handled_id(handler) };
    with(|state| state.handlers.push((id, handler as usize)));
}

unsafe extern "C" fn remove_handler(_: *mut Queue, handler: *mut Handler) {
    with(|state| state.handlers.retain(|(_, h)| *h != handler as usize));
}

unsafe extern "C" fn send_event(_: *mut Queue, id: *const EventId, data: *const u8, size: i32) {
    if id.is_null() || size < 0 || (data.is_null() && size > 0) {
        return;
    }
    let size = size as usize;
    if size > MAX_PAYLOAD {
        tracing::error!(
            "a native plugin sent an event of {size} bytes through IUnityEventQueue; the largest is {MAX_PAYLOAD}: dropped"
        );
        return;
    }
    let payload = match size {
        0 => Vec::new(),
        _ => unsafe { std::slice::from_raw_parts(data, size) }.to_vec(),
    };
    let id = unsafe { *id };
    with(|state| state.waiting.push((id, payload)));
}

unsafe extern "C" fn set_cleanup(_: *mut Queue, handler: *mut Handler) {
    if handler.is_null() {
        return;
    }
    let id = unsafe { handled_id(handler) };
    with(|state| {
        state.cleanups.retain(|(other, _)| *other != id);
        state.cleanups.push((id, handler as usize));
    });
}

/// Whether an event waits.
pub(super) fn waiting() -> bool {
    with(|state| !state.waiting.is_empty())
}

/// Hands the waiting events out (the main thread, once a frame). Handlers may send events,
/// which wait for the next frame, and add or remove handlers.
pub(super) fn dispatch() {
    let waiting = with(|state| std::mem::take(&mut state.waiting));
    for (mut id, mut payload) in waiting {
        let (handlers, cleanup) = with(|state| {
            let of_id = |list: &[(EventId, usize)]| -> Vec<usize> {
                list.iter()
                    .filter(|(other, _)| *other == id)
                    .map(|(_, handler)| *handler)
                    .collect()
            };
            (of_id(&state.handlers), of_id(&state.cleanups))
        });
        for handler in handlers.into_iter().chain(cleanup) {
            let handler = handler as *mut Handler;
            // SAFETY: a handler a plugin added stays alive until it removes it (the header's
            // contract); its table is the C++ class's.
            unsafe {
                ((*(*handler).table).handle_event)(
                    handler,
                    &mut id,
                    payload.as_mut_ptr() as *mut c_void,
                )
            };
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicI32, Ordering};

    /// A handler as a plugin's compiler lays one out: the table, the `m_Next` link, then its
    /// own fields.
    #[repr(C)]
    struct TestHandler {
        base: Handler,
        id: EventId,
        seen: AtomicI32,
        sum: AtomicI32,
    }

    unsafe extern "C" fn handle(handler: *mut Handler, id: *mut EventId, data: *mut c_void) {
        let handler = unsafe { &*(handler as *mut TestHandler) };
        assert_eq!(unsafe { *id }, handler.id);
        handler.seen.fetch_add(1, Ordering::Relaxed);
        handler
            .sum
            .fetch_add(unsafe { *(data as *const i32) }, Ordering::Relaxed);
    }

    unsafe extern "C" fn id_of(handler: *mut Handler) -> EventIdResult {
        EventIdResult {
            id: unsafe { (*(handler as *mut TestHandler)).id },
            _unused: 0,
        }
    }

    unsafe extern "C" fn no_destructor(_: *mut Handler) -> *mut Handler {
        std::ptr::null_mut()
    }

    static HANDLER_TABLE: HandlerTable = HandlerTable {
        _destroy: [0; 2],
        handle_event: handle,
        handler_event_id: id_of,
        _get_my_event_destructor: no_destructor,
    };

    fn handler(id: EventId) -> &'static mut TestHandler {
        Box::leak(Box::new(TestHandler {
            base: Handler {
                table: &HANDLER_TABLE,
                _next: std::ptr::null_mut(),
            },
            id,
            seen: AtomicI32::new(0),
            sum: AtomicI32::new(0),
        }))
    }

    fn send(id: EventId, value: i32) {
        let queue = &QUEUE as *const Queue as *mut Queue;
        unsafe {
            (QUEUE.table.send_event)(queue, &id, &value as *const i32 as *const u8, 4);
        }
    }

    /// Events wait until the dispatch and go to the handlers of their ID added by then, in
    /// the order sent; a removed handler gets none; the cleanup handler of the type comes
    /// after the handlers.
    #[test]
    fn events_wait_for_the_dispatch_and_go_to_the_handlers_of_their_id() {
        // IDs of this test alone (the queue is the process's).
        let (ours, other) = (EventId { high: 71, low: 1 }, EventId { high: 71, low: 2 });
        let queue = &QUEUE as *const Queue as *mut Queue;
        let (first, second, cleanup) = (handler(ours), handler(ours), handler(ours));
        let raw = |h: &TestHandler| h as *const TestHandler as *mut Handler;
        send(ours, 5);
        // Added after the event was sent: it still gets it.
        unsafe { (QUEUE.table.add_handler)(queue, raw(first)) };
        unsafe { (QUEUE.table.add_handler)(queue, raw(second)) };
        unsafe { (QUEUE.table.set_cleanup)(queue, raw(cleanup)) };
        send(other, 100);
        send(ours, 7);
        assert_eq!(first.seen.load(Ordering::Relaxed), 0);
        dispatch();
        for h in [&*first, &*second, &*cleanup] {
            assert_eq!(
                (
                    h.seen.load(Ordering::Relaxed),
                    h.sum.load(Ordering::Relaxed)
                ),
                (2, 12)
            );
        }
        // Handed out once.
        dispatch();
        assert_eq!(first.seen.load(Ordering::Relaxed), 2);
        unsafe { (QUEUE.table.remove_handler)(queue, raw(second)) };
        send(ours, 1);
        dispatch();
        assert_eq!(first.seen.load(Ordering::Relaxed), 3);
        assert_eq!(second.seen.load(Ordering::Relaxed), 2);
        // An event larger than the header allows is dropped.
        let big = vec![0u8; MAX_PAYLOAD + 1];
        unsafe { (QUEUE.table.send_event)(queue, &ours, big.as_ptr(), big.len() as i32) };
        dispatch();
        assert_eq!(first.seen.load(Ordering::Relaxed), 3);
        unsafe { (QUEUE.table.remove_handler)(queue, raw(first)) };
    }
}
