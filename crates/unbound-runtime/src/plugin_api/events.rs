//! `IUnityEventQueue` (`IUnityEventQueue.h`): plugins send events of their own types and handle
//! them later on the main thread. Behaviour matches the Unity 6000.0.71f1 Editor
//! (tests/projects/nativegfx `events.load`, `frames.sequence`): a sent event is copied and
//! waits; when a frame begins (the player loop's `DispatchEventQueueEvents`) the waiting
//! events are handed out in the order they were sent, each to the handlers of its ID added by
//! then, the last added first, and then to its type's cleanup handler, until none waits (an
//! event a handler sends is handed out in the same turn). A type's cleanup handler is the one
//! a plugin set for it, or the destructor of a handler added for it. The header's own events
//! add and remove a handler in their turn.
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
    get_my_event_destructor: unsafe extern "C" fn(*mut Handler) -> *mut Handler,
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
/// The header's own events (`AddEventHandler`, `RemoveEventHandler`): their payload is a
/// handler's address, added or removed when the event's turn comes.
const ADD_HANDLER: EventId = EventId {
    high: 0x19D736400584B24B,
    low: 0x98B9EFBE26D3F3C9,
};
const REMOVE_HANDLER: EventId = EventId {
    high: 0x8D4A317C4F577F4A,
    low: 0x851D6E457566A905,
};

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

/// Adds a handler: for the ID it says it handles, with the destructor it names as that
/// type's cleanup handler.
unsafe fn add(handler: *mut Handler) {
    if handler.is_null() {
        return;
    }
    // SAFETY: a plugin's handler (the header's contract): its table is the C++ class's.
    let (id, destructor) = unsafe {
        (
            handled_id(handler),
            ((*(*handler).table).get_my_event_destructor)(handler),
        )
    };
    with(|state| {
        state.handlers.push((id, handler as usize));
        if !destructor.is_null() {
            state.cleanups.retain(|(other, _)| *other != id);
            state.cleanups.push((id, destructor as usize));
        }
    });
}

fn remove(handler: *mut Handler) {
    with(|state| state.handlers.retain(|(_, h)| *h != handler as usize));
}

unsafe extern "C" fn add_handler(_: *mut Queue, handler: *mut Handler) {
    unsafe { add(handler) };
}

unsafe extern "C" fn remove_handler(_: *mut Queue, handler: *mut Handler) {
    remove(handler);
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

/// Hands the waiting events out (the main thread, when a frame begins), until none waits:
/// handlers may send events, and add or remove handlers.
pub(super) fn dispatch() {
    loop {
        let waiting = with(|state| std::mem::take(&mut state.waiting));
        if waiting.is_empty() {
            return;
        }
        for (mut id, mut payload) in waiting {
            if id == ADD_HANDLER || id == REMOVE_HANDLER {
                // The payload is the handler's address.
                if let Ok(address) = <[u8; size_of::<usize>()]>::try_from(payload.as_slice()) {
                    let handler = usize::from_ne_bytes(address) as *mut Handler;
                    match id == ADD_HANDLER {
                        // SAFETY: the plugin sent a handler of its own.
                        true => unsafe { add(handler) },
                        false => remove(handler),
                    }
                }
                continue;
            }
            let (handlers, cleanup) = with(|state| {
                let of_id = |list: &[(EventId, usize)]| -> Vec<usize> {
                    list.iter()
                        .filter(|(other, _)| *other == id)
                        .map(|(_, handler)| *handler)
                        .collect()
                };
                (of_id(&state.handlers), of_id(&state.cleanups))
            });
            // The handler added last first, the cleanup handler after them.
            for handler in handlers.into_iter().rev().chain(cleanup) {
                let handler = handler as *mut Handler;
                // SAFETY: a handler a plugin added stays alive until it removes it (the
                // header's contract); its table is the C++ class's.
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
}

#[cfg(test)]
mod tests {
    use super::*;
    use std::sync::atomic::{AtomicI32, Ordering};

    /// What the handlers of this test were handed, in order: (handler, value).
    static HANDED: Mutex<Vec<(i32, i32)>> = Mutex::new(Vec::new());

    /// A handler as a plugin's compiler lays one out: the table, the `m_Next` link, then its
    /// own fields.
    #[repr(C)]
    struct TestHandler {
        base: Handler,
        id: EventId,
        name: i32,
        destructor: *mut Handler,
        /// An event to send from inside the handler, once (0: none).
        resend: AtomicI32,
    }

    unsafe extern "C" fn handle(handler: *mut Handler, id: *mut EventId, data: *mut c_void) {
        let handler = unsafe { &*(handler as *mut TestHandler) };
        assert_eq!(unsafe { *id }, handler.id);
        HANDED
            .lock()
            .unwrap()
            .push((handler.name, unsafe { *(data as *const i32) }));
        let resend = handler.resend.swap(0, Ordering::Relaxed);
        if resend != 0 {
            send(handler.id, resend);
        }
    }

    unsafe extern "C" fn id_of(handler: *mut Handler) -> EventIdResult {
        EventIdResult {
            id: unsafe { (*(handler as *mut TestHandler)).id },
            _unused: 0,
        }
    }

    unsafe extern "C" fn destructor_of(handler: *mut Handler) -> *mut Handler {
        unsafe { (*(handler as *mut TestHandler)).destructor }
    }

    static HANDLER_TABLE: HandlerTable = HandlerTable {
        _destroy: [0; 2],
        handle_event: handle,
        handler_event_id: id_of,
        get_my_event_destructor: destructor_of,
    };

    fn handler(id: EventId, name: i32, destructor: *mut Handler) -> *mut Handler {
        Box::leak(Box::new(TestHandler {
            base: Handler {
                table: &HANDLER_TABLE,
                _next: std::ptr::null_mut(),
            },
            id,
            name,
            destructor,
            resend: AtomicI32::new(0),
        })) as *mut TestHandler as *mut Handler
    }

    fn queue() -> *mut Queue {
        &QUEUE as *const Queue as *mut Queue
    }

    fn send(id: EventId, value: i32) {
        unsafe {
            (QUEUE.table.send_event)(queue(), &id, &value as *const i32 as *const u8, 4);
        }
    }

    fn send_handler(id: EventId, handler: *mut Handler) {
        let address = handler as usize;
        unsafe {
            (QUEUE.table.send_event)(
                queue(),
                &id,
                &address as *const usize as *const u8,
                size_of::<usize>() as i32,
            );
        }
    }

    fn handed() -> Vec<(i32, i32)> {
        std::mem::take(&mut *HANDED.lock().unwrap())
    }

    /// The queue as tests/projects/nativegfx records the Editor's: events wait for the
    /// dispatch and go to the handlers of their ID added by then, the last added first, then
    /// to the type's cleanup handler; an event sent by a handler is handed out in the same
    /// dispatch; the header's own events add and remove a handler in their turn.
    #[test]
    fn events_wait_for_the_dispatch_and_go_to_the_handlers_of_their_id() {
        // IDs of this test alone (the queue is the process's).
        let (ours, other, late) = (
            EventId { high: 71, low: 1 },
            EventId { high: 71, low: 2 },
            EventId { high: 71, low: 3 },
        );
        // A cleanup handler the type's handlers name as their destructor: never set by hand.
        let cleanup = handler(ours, 9, std::ptr::null_mut());
        let (first, second) = (handler(ours, 1, cleanup), handler(ours, 2, cleanup));
        let late_handler = handler(late, 5, std::ptr::null_mut());
        send(ours, 5);
        // Added after the event was sent: they still get it.
        unsafe { (QUEUE.table.add_handler)(queue(), first) };
        unsafe { (QUEUE.table.add_handler)(queue(), second) };
        send(other, 100);
        send_handler(ADD_HANDLER, late_handler);
        send(late, 6);
        // The first handler sends an event when it is handed its first.
        unsafe { &*(first as *mut TestHandler) }
            .resend
            .store(7, Ordering::Relaxed);
        assert!(handed().is_empty());
        dispatch();
        assert_eq!(
            handed(),
            [(2, 5), (1, 5), (9, 5), (5, 6), (2, 7), (1, 7), (9, 7)]
        );
        // Handed out once.
        dispatch();
        assert!(handed().is_empty());
        // A removed handler gets none; the cleanup handler still does. The handler removed by
        // the header's event gets what was sent before the removal's turn only.
        unsafe { (QUEUE.table.remove_handler)(queue(), second) };
        send(late, 8);
        send_handler(REMOVE_HANDLER, late_handler);
        send(late, 9);
        send(ours, 1);
        dispatch();
        assert_eq!(handed(), [(5, 8), (1, 1), (9, 1)]);
        // An event larger than the header allows is dropped.
        let big = vec![0u8; MAX_PAYLOAD + 1];
        unsafe { (QUEUE.table.send_event)(queue(), &ours, big.as_ptr(), big.len() as i32) };
        dispatch();
        assert!(handed().is_empty());
        unsafe { (QUEUE.table.remove_handler)(queue(), first) };
        // A cleanup handler set by hand replaces the type's.
        let by_hand = handler(ours, 8, std::ptr::null_mut());
        unsafe { (QUEUE.table.set_cleanup)(queue(), by_hand) };
        send(ours, 3);
        dispatch();
        assert_eq!(handed(), [(8, 3)]);
    }
}
