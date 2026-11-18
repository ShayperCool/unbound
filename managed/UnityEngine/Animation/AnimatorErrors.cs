namespace UnityEngine
{
    /// <summary>
    /// The warnings of the Animator's and AnimatorControllerPlayable's parameter, layer and state
    /// lookups, as the Unity 6000.0.71f1 Editor writes them (tests/projects/apierrors `animator.*`,
    /// `playable.*`). A parameter given by id is named "Hash id".
    /// </summary>
    internal static class AnimatorErrors
    {
        internal const string NotPlaying = "Animator is not playing an AnimatorController";
        internal const string NoController = "Animator does not have an AnimatorController";
        internal const string NotInitialized = "Animator has not been initialized.";
        internal static string EmptyOverrideAssigned(string name) =>
            $"Could not set Runtime Animator Controller. The controller {name} is an AnimatorOverrideController with no AnimatorController to override.";
        internal const string Inactive = "Game object with animator is inactive";
        internal const string StateNotFound = "Animator.GotoState: State could not be found";

        internal static string InvalidLayer(int layer) => $"Invalid Layer Index '{layer}'";

        internal static string HashLabel(int id) => "Hash " + id;

        internal static string MissingParameter(string label) => $"Parameter '{label}' does not exist.";

        internal static string TypeMismatch(string label) => $"Parameter type '{label}' does not match.";

        /// <summary>Whether a parameter of one type is used as another: bools and triggers are
        /// both boolean values.</summary>
        internal static bool Mismatched(AnimatorControllerParameterType type, AnimatorControllerParameterType expected) =>
            type != expected
            && !((type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Trigger)
                 && (expected == AnimatorControllerParameterType.Bool || expected == AnimatorControllerParameterType.Trigger));

        /// <summary>A state the lookup did not find; on any layer (-1) the layer is then reported
        /// invalid too.</summary>
        internal static void StateMissing(int layer, Object context)
        {
            Debug.LogWarning(StateNotFound, context);
            if (layer == -1) Debug.LogWarning(InvalidLayer(-1), context);
        }
    }
}
