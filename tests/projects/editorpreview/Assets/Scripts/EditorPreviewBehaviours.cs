using System;
using UnityEngine;

public class EditorPreviewBehaviour : MonoBehaviour { }

[AddComponentMenu("Probe/Fancy Name")]
public class EditorPreviewMenuBehaviour : MonoBehaviour { }

[AddComponentMenu("")]
public class EditorPreviewHiddenBehaviour : MonoBehaviour { }

[AddComponentMenu("Probe/Trailing/")]
public class EditorPreviewTrailingBehaviour : MonoBehaviour { }

[Obsolete("old")]
public class EditorPreviewOldBehaviour : MonoBehaviour { }

// A class derived from one with a menu path has none of its own.
public class EditorPreviewDerivedMenuBehaviour : EditorPreviewMenuBehaviour { }
