/* macOS dyld interposer for execve (crates/unbound/src/editor_launch.rs).
 *
 * Linked as a dynamic library (@rpath/libunbound_launch_guard.dylib) so dyld applies
 * its __DATA,__interpose table across the process, including dlopen'd .NET native
 * libraries (libSystem.Native.dylib's Process.Start). Calling execve() from inside
 * this dylib reaches libSystem's real execve directly.
 */
#include <stdatomic.h>
#include <stddef.h>
#include <unistd.h>

typedef int (*unbound_execve_fn)(const char *, char *const[], char *const[]);
typedef int (*unbound_execve_hook_fn)(
    const char *,
    char *const[],
    char *const[],
    unbound_execve_fn);

static _Atomic(unbound_execve_hook_fn) g_execve_hook = NULL;

void unbound_launch_guard_set_hook(unbound_execve_hook_fn hook) {
    atomic_store_explicit(&g_execve_hook, hook, memory_order_release);
}

static int unbound_interposed_execve(const char *path, char *const argv[], char *const envp[]) {
    unbound_execve_hook_fn hook = atomic_load_explicit(&g_execve_hook, memory_order_acquire);
    if (hook != NULL) {
        return hook(path, argv, envp, execve);
    }
    return execve(path, argv, envp);
}

__attribute__((used, section("__DATA,__interpose")))
static const struct {
    const void *replacement;
    const void *replacee;
} unbound_interpose_execve = {
    (const void *)unbound_interposed_execve,
    (const void *)execve,
};
