// The probe plugin's use of IUnityEventQueue, a C++ interface: compiled as C++ so that the
// compiler lays the classes out and calls their virtual functions as a plugin's compiler does.
// Only the public plugin header's declarations are used, repeated here (IUnityEventQueue.h).
//
// The plugin sends events of two types of its own, one with a handler and a cleanup handler,
// one with neither, and records when each is handled.
#include <cstddef>

namespace UnityEventQueue
{
struct EventId
{
    EventId(unsigned long long high, unsigned long long low) : mGUIDHigh(high), mGUIDLow(low) {}
    EventId(const EventId& other) { mGUIDHigh = other.mGUIDHigh; mGUIDLow = other.mGUIDLow; }
    EventId& operator=(const EventId& other) { mGUIDHigh = other.mGUIDHigh; mGUIDLow = other.mGUIDLow; return *this; }
    unsigned long long mGUIDHigh;
    unsigned long long mGUIDLow;
};

class EventHandler
{
public:
    EventHandler() : m_Next(0) {}
    virtual ~EventHandler() {}
    virtual void HandleEvent(EventId& id, void* data) = 0;
    virtual EventId HandlerEventId() = 0;
    virtual EventHandler* GetMyEventDestructor() = 0;

private:
    EventHandler* m_Next;
};

class IUnityEventQueue
{
public:
    virtual ~IUnityEventQueue() {}
    virtual void AddHandler(EventHandler* handler) = 0;
    virtual void RemoveHandler(EventHandler* handler) = 0;
    // The header's SendEvent<T> and RegisterCleanup<T> call these two.
    virtual void SendEventImpl(EventId id, unsigned char* data, int size) = 0;
    virtual void SetCleanupImpl(EventHandler* handler) = 0;
};
}

using namespace UnityEventQueue;

struct Guid { unsigned long long high, low; };
struct Interfaces
{
    void* (*GetInterface)(const Guid* guid);
};
typedef void (*Note)(const char* format, ...);

static const EventId kProbeEvent(0x4E61746976654766ULL, 0x7850726F62654576ULL);
static const EventId kUnhandledEvent(0x4E61746976654766ULL, 0x78556E68616E646CULL);
static const EventId kOwnedEvent(0x4E61746976654766ULL, 0x784F776E65644576ULL);
static const EventId kLateEvent(0x4E61746976654766ULL, 0x784C617465457674ULL);
// The header's own events: a handler added or removed by an event (REGISTER_EVENT_ID of
// AddEventHandler and RemoveEventHandler), whose payload is the handler's address.
static const EventId kAddHandler(0x19D736400584B24BULL, 0x98B9EFBE26D3F3C9ULL);
static const EventId kRemoveHandler(0x8D4A317C4F577F4AULL, 0x851D6E457566A905ULL);
static Note s_note;
static IUnityEventQueue* s_queue;

extern "C" int nativegfx_clock(void);

static void send(const EventId& id, int value)
{
    s_queue->SendEventImpl(id, (unsigned char*)&value, sizeof value);
    s_note("queue.sent value=%d", value);
}

/// A handler of one event type that records what it is handed, under a name.
class Recorder : public EventHandler
{
public:
    Recorder(const char* name, const EventId& id, EventHandler* destructor) : m_Name(name), m_Id(id), m_Destructor(destructor) {}
    virtual void HandleEvent(EventId& id, void* data)
    {
        int value = *(int*)data;
        s_note("queue.%s value=%d id=%d clock=%d", m_Name, value, id.mGUIDLow == m_Id.mGUIDLow, nativegfx_clock());
        // An event sent from inside a handler.
        if (value == 2 && m_Destructor == NULL && m_Id.mGUIDLow == kProbeEvent.mGUIDLow && m_Name[0] == 'h') send(kProbeEvent, 3);
    }
    virtual EventId HandlerEventId() { return m_Id; }
    virtual EventHandler* GetMyEventDestructor() { return m_Destructor; }

private:
    const char* m_Name;
    EventId m_Id;
    EventHandler* m_Destructor;
};

// The probe event: a handler without a destructor of its own, and a cleanup handler the plugin
// sets for the type.
static Recorder s_cleanup("cleanup", kProbeEvent, NULL);
static Recorder s_handler("handled", kProbeEvent, NULL);
// The owned event: two handlers, whose destructor is known through the handlers alone.
static Recorder s_owned_cleanup("owned.cleanup", kOwnedEvent, NULL);
static Recorder s_owned_first("owned.first", kOwnedEvent, &s_owned_cleanup);
static Recorder s_owned_second("owned.second", kOwnedEvent, &s_owned_cleanup);
// The late event: its handler is added and removed by events.
static Recorder s_late("late", kLateEvent, NULL);

extern "C" int nativegfx_events_load(Interfaces* interfaces, Note note)
{
    Guid guid = {0x9959C347F5AE374DULL, 0x9BADE6FC8EF49E7FULL};
    s_note = note;
    s_queue = (IUnityEventQueue*)interfaces->GetInterface(&guid);
    if (!s_queue) return 0;
    // Sent before its handler is added, and one of a type nothing handles.
    send(kProbeEvent, 1);
    s_queue->AddHandler(&s_handler);
    s_queue->SetCleanupImpl(&s_cleanup);
    note("queue.added");
    send(kProbeEvent, 2);
    send(kUnhandledEvent, 9);
    s_queue->AddHandler(&s_owned_first);
    s_queue->AddHandler(&s_owned_second);
    send(kOwnedEvent, 5);
    // A handler added by the header's event, then an event for it.
    EventHandler* late = &s_late;
    s_queue->SendEventImpl(kAddHandler, (unsigned char*)&late, sizeof late);
    send(kLateEvent, 6);
    return 1;
}

/// The scripts' frames: an event in the second, and in the third one sent after its handler
/// is removed (the late event's handler by the header's event).
extern "C" void nativegfx_events_frame(int frame)
{
    if (!s_queue) return;
    if (frame == 2) send(kProbeEvent, 30);
    if (frame == 3) {
        s_queue->RemoveHandler(&s_handler);
        s_note("queue.removed");
        send(kProbeEvent, 40);
        EventHandler* late = &s_late;
        s_queue->SendEventImpl(kRemoveHandler, (unsigned char*)&late, sizeof late);
        send(kLateEvent, 7);
    }
}
