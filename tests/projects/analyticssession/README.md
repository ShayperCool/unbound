# AnalyticsSessionInfo and RemoteSettings with Unity services disabled

Unity 6000.0.71f1, recorded with the macOS Editor in batch mode in Play mode, in a project whose
`UnityConnectSettings.asset` disables Unity services (the editorservices fixture's settings and
packages); no service is logged in and nothing is sent. The gate compares every value exactly.

`Assets/Scripts/AnalyticsSessionProbe.cs` reads the session values (those that differ between runs
by their shape only: the id's sign, the user id's length and digits, the count against the
first-run flag and the project's preferences), sets and clears the custom ids, subscribes to every
event, calls each RemoteSettings getter with and without defaults and with null keys, asks
GetObject for plain, abstract and UnityEngine.Object types and with a null default, calls
ForceUpdate and waits ten frames, and calls a few members from a worker thread.

Measured:

- Play mode starts a session (kSessionStarted) with a positive id; the session count and the
  first-run flag (count 1) carry over between runs; the count, the session id and the user id (32
  lowercase hex digits, the same each read) are in the project's PlayerPrefs as
  `unity.player_session_count`, `unity.player_sessionid` and `unity.cloud_userid`. The editor
  writes them even in projects without the analytics module (seen in another probe's preferences).
  The identity token is empty; the custom ids start empty, keep what is set, and null clears them.
- RemoteSettings has no settings: getters return 0, "", false or the default given, GetKeys an
  empty array, GetObject null (or the default given, the same object), GetDictionary null; null
  keys do the same. A null type or default, an abstract type and UnityEngine.Object types throw.
- ForceUpdate raises no event, then or in the next ten frames; no session or token event comes.
- Off the main thread the session values and RemoteSettings throw Unity's main-thread exception
  with their binding's name.

Unmeasured: entering Play mode again within one Editor session, services logged in, other releases.
