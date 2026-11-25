# editorchannels

What the Editor's messaging channel service (`UnityEditor.MPE.ChannelService`), its interface
language settings (`LocalizationDatabase`), the collaboration service (`Collab.instance`) and the
play mode toolbar's `onPlayModeButtonsCreated` event answer in Unity 6000.0.71f1 Play mode
(`Assets/Scripts/EditorChannelsProbe.cs`; two byte-identical macOS batch Editor captures): the
members packages call while a game runs.

Measured: in batch mode the service is not running until `Start` (asking a stopped service for its
channels ends the Editor, so the probe starts it first); running, it holds the channels `echo`,
`echoAll`, `event` and `status`; a channel's id is the same before it opens, while it is open and
after it closes (its name's 32-bit xxHash with the service's seed, ids of eight more names
recorded); `GetOrCreateChannel` opens a channel and gives an action that unregisters its handler, a
second registration of the handler and one on an unknown channel throw Unity's messages, broadcasts
with no client connected send nothing, the address is `127.0.0.1` and the port positive.
`currentEditorLanguage` is English, `enableEditorLocalization` true, `Collab.instance` a `Collab`,
and a handler can be added to and removed from the toolbar event.

Residual (`residuals.json`): Unbound runs no channel server and reports no port (0, with a warning).

Unmeasured: a windowed Editor's service at startup (running there), channels used while the
service is stopped (Unbound keeps them), messages from clients.

```sh
python3 tests/projects/editorchannels/run.py record --label a
python3 tests/projects/editorchannels/run.py record --label b
python3 tests/projects/editorchannels/run.py publish a b
python3 tests/projects/editorchannels/run.py gate
```
