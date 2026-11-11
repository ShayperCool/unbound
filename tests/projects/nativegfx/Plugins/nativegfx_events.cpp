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
static Note s_note;
static IUnityEventQueue* s_queue;

class Cleanup : public EventHandler
{
public:
    virtual void HandleEvent(EventId& id, void* data) { s_note("queue.cleanup value=%d id=%d", *(int*)data, id.mGUIDLow == kProbeEvent.mGUIDLow); }
    virtual EventId HandlerEventId() { return kProbeEvent; }
    virtual EventHandler* GetMyEventDestructor() { return NULL; }
};
static Cleanup s_cleanup;

class Handler : public EventHandler
{
public:
    virtual void HandleEvent(EventId& id, void* data) { s_note("queue.handled value=%d id=%d", *(int*)data, id.mGUIDLow == kProbeEvent.mGUIDLow); }
    virtual EventId HandlerEventId() { return kProbeEvent; }
    virtual EventHandler* GetMyEventDestructor() { return &s_cleanup; }
};
static Handler s_handler;

static void send(const EventId& id, int value)
{
    s_queue->SendEventImpl(id, (unsigned char*)&value, sizeof value);
    s_note("queue.sent value=%d", value);
}

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
    return 1;
}

/// The scripts' frames: an event in the second, and in the third one sent after its handler
/// is removed.
extern "C" void nativegfx_events_frame(int frame)
{
    if (!s_queue) return;
    if (frame == 2) send(kProbeEvent, 30);
    if (frame == 3) {
        s_queue->RemoveHandler(&s_handler);
        s_note("queue.removed");
        send(kProbeEvent, 40);
    }
}
