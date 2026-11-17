/* The probe's native functions, built into each of its macOS bundles (run.py). */
int pluginbundle_answer(void) { return PLUGINBUNDLE_ANSWER; }

/* Calls a managed callback through the pointer the script made of it. */
typedef void (*pluginbundle_callback)(const char *, int, const char *);
void pluginbundle_call(pluginbundle_callback callback) { callback("native", 17, "call"); }
