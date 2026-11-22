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
