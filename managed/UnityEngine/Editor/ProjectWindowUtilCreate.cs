using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.Compilation;
using UnityEditor.ProjectWindowCallback;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>
    /// The Project window's asset-creating helpers. Each starts naming the new asset, which ends at
    /// once without a Project window (see StartNameEditingIfProjectWindowExists): the asset is
    /// created on the unique form of the path under Assets (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/projectwindowutil create.*). CreateNewGUISkin stays a stub.
    /// </summary>
    public partial class ProjectWindowUtil
    {
        /// <summary>Saves <paramref name="asset"/> at <paramref name="pathName"/> (made unique) and
        /// selects it; a null asset throws NullReferenceException.</summary>
        public static void CreateAsset(Object asset, string pathName)
        {
            if ((object)asset == null) throw Unbound.Runtime.MonoExceptions.NullReference();
            StartNameEditingIfProjectWindowExists(asset.GetInstanceID(), ScriptableObject.CreateInstance<DoCreateNewAsset>(), pathName, null, null);
        }

        /// <summary>A folder "New Folder" (made unique) in Assets.</summary>
        public static void CreateFolder() =>
            StartNameEditingIfProjectWindowExists(0, ScriptableObject.CreateInstance<DoCreateFolder>(), "New Folder", null, null);

        /// <summary>A scene "New Scene.unity" (made unique) in Assets with the new scene's camera and
        /// light.</summary>
        public static void CreateScene() =>
            StartNameEditingIfProjectWindowExists(0, ScriptableObject.CreateInstance<DoCreateScene>(), "New Scene.unity", null, null);

        /// <summary>A text file with <paramref name="content"/> (its line endings the project's for new
        /// scripts).</summary>
        public static void CreateAssetWithContent(string filename, string content, Texture2D icon = null)
        {
            var action = ScriptableObject.CreateInstance<DoCreateAssetWithContent>();
            action.filecontent = content;
            StartNameEditingIfProjectWindowExists(0, action, filename, icon, null);
        }

        /// <summary>A script or text file made from the template at <paramref name="templatePath"/>
        /// (see <see cref="PreprocessScriptAssetTemplate"/>), named <paramref name="defaultNewFileName"/>
        /// or else as the template.</summary>
        public static void CreateScriptAssetFromTemplateFile(string templatePath, string defaultNewFileName)
        {
            if (templatePath == null) throw new ArgumentNullException(nameof(templatePath));
            if (!File.Exists(templatePath)) throw new FileNotFoundException("The template file \"" + templatePath + "\" could not be found.", templatePath);
            if (string.IsNullOrEmpty(defaultNewFileName)) defaultNewFileName = Path.GetFileName(templatePath);
            StartNameEditingIfProjectWindowExists(0, ScriptableObject.CreateInstance<DoCreateScriptAsset>(), defaultNewFileName, null, templatePath);
        }

        /// <summary>Writes a script or text file at <paramref name="assetPath"/> (its line endings the
        /// project's for new scripts), imports it and returns its main object.</summary>
        public static Object CreateScriptAssetWithContent(string assetPath, string scriptContent)
        {
            AssetModificationProcessorInternal.OnWillCreateAsset(assetPath);
            scriptContent = SetLineEndings(scriptContent, EditorSettings.lineEndingsForNewScripts);
            File.WriteAllText(AssetDatabase.FileOf(assetPath), scriptContent);
            AssetDatabase.ImportAsset(assetPath);
            return AssetDatabase.LoadAssetAtPath(assetPath, typeof(Object));
        }

        /// <summary>Every line break (\r\n, \r or \n) made the mode's: \n but on Windows for
        /// OSNative.</summary>
        internal static string SetLineEndings(string content, LineEndingsMode lineEndingsMode)
        {
            string ending = lineEndingsMode == LineEndingsMode.Windows
                || (lineEndingsMode == LineEndingsMode.OSNative && Application.platform == RuntimePlatform.WindowsEditor) ? "\r\n" : "\n";
            return Regex.Replace(content, "\\r\\n?|\\n", ending);
        }

        const string RootNamespaceBegin = "#ROOTNAMESPACEBEGIN#";
        const string RootNamespaceEnd = "#ROOTNAMESPACEEND#";

        /// <summary>A template's root namespace block: without a namespace its two markers are
        /// removed with the line break before each (the lines between stay as they are); with one,
        /// the begin marker's line becomes the namespace's opening, the lines up to the end marker
        /// are indented as the marker was, and the end marker's line closes it.</summary>
        internal static string RemoveOrInsertNamespace(string content, string rootNamespace)
        {
            if (!content.Contains(RootNamespaceBegin) || !content.Contains(RootNamespaceEnd)) return content;
            if (string.IsNullOrEmpty(rootNamespace))
            {
                content = Regex.Replace(content, "((\\r\\n)|\\n)?[ \\t]*" + RootNamespaceBegin + "[ \\t]*", string.Empty);
                return Regex.Replace(content, "((\\r\\n)|\\n)?[ \\t]*" + RootNamespaceEnd + "[ \\t]*", string.Empty);
            }
            string separator = content.Contains("\r\n") ? "\r\n" : "\n";
            var lines = new List<string>(content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None));
            int i = lines.FindIndex(l => l.Contains(RootNamespaceBegin));
            string indent = lines[i].Substring(0, lines[i].IndexOf('#'));
            lines[i] = "namespace " + rootNamespace;
            lines.Insert(i + 1, "{");
            for (i += 2; i < lines.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(lines[i])) continue;
                if (lines[i].Contains(RootNamespaceEnd))
                {
                    lines[i] = "}";
                    break;
                }
                lines[i] = indent + lines[i];
            }
            return string.Join(separator, lines);
        }

        /// <summary>A template's text for the file at <paramref name="pathName"/>: #NOTRIM# removed,
        /// #NAME# the file name, #SCRIPTNAME# it without spaces, the root namespace block (a C#
        /// file's assembly root namespace), and #SCRIPTNAME_LOWER# the script name with its first
        /// letter lowered (or "my" before it, capitalized, when it starts lower case).</summary>
        internal static string PreprocessScriptAssetTemplate(string pathName, string resourceContent)
        {
            string rootNamespace = Path.GetExtension(pathName) == ".cs" ? CompilationPipeline.GetAssemblyRootNamespaceFromScriptPath(pathName) : null;
            string name = Path.GetFileNameWithoutExtension(pathName);
            string scriptName = name.Replace(" ", "");
            string text = resourceContent.Replace("#NOTRIM#", "").Replace("#NAME#", name).Replace("#SCRIPTNAME#", scriptName);
            text = RemoveOrInsertNamespace(text, rootNamespace);
            string lower = char.IsUpper(scriptName, 0)
                ? char.ToLower(scriptName[0]) + scriptName.Substring(1)
                : "my" + char.ToUpper(scriptName[0]) + scriptName.Substring(1);
            return text.Replace("#SCRIPTNAME_LOWER#", lower);
        }

        internal static Object CreateScriptAssetFromTemplate(string pathName, string resourceFile) =>
            CreateScriptAssetWithContent(pathName, PreprocessScriptAssetTemplate(pathName, File.ReadAllText(resourceFile)));

        /// <summary>The instance IDs of the main asset of the object's file (when it is not the
        /// object itself) and of each folder above it; none for the asset being created.</summary>
        public static int[] GetAncestors(int instanceID)
        {
            var ancestors = new HashSet<int>();
            if (instanceID == int.MaxValue) return ancestors.ToArray();
            int main = AssetDatabase.GetMainAssetOrInProgressProxyInstanceID(AssetDatabase.GetAssetPath(instanceID));
            if (main != instanceID) ancestors.Add(main);
            for (var folder = GetContainingFolder(AssetDatabase.GetAssetPath(main)); !string.IsNullOrEmpty(folder);)
            {
                int id = AssetDatabase.GetMainAssetOrInProgressProxyInstanceID(folder);
                ancestors.Add(id);
                folder = GetContainingFolder(AssetDatabase.GetAssetPath(id));
            }
            return ancestors.ToArray();
        }
    }
}

namespace UnityEditor.ProjectWindowCallback
{
    internal partial class DoCreateNewAsset : EndNameEditAction
    {
        public DoCreateNewAsset() { }

        public override void Action(int instanceId, string pathName, string resourceFile) =>
            AssetDatabase.CreateAsset(EditorUtility.InstanceIDToObject(instanceId), AssetDatabase.GenerateUniqueAssetPath(pathName));

        public override void Cancelled(int instanceId, string pathName, string resourceFile) => Selection.activeObject = null;
    }

    internal partial class DoCreateFolder : EndNameEditAction
    {
        public DoCreateFolder() { }

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            string guid = AssetDatabase.CreateFolder(Path.GetDirectoryName(pathName), Path.GetFileName(pathName));
            ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath(AssetDatabase.GUIDToAssetPath(guid), typeof(Object)));
        }
    }

    internal partial class DoCreateScene : EndNameEditAction
    {
        public DoCreateScene() { }

        public override void Action(int instanceId, string pathName, string resourceFile)
        {
            if (EditorSceneManager.CreateSceneAsset(pathName, true))
                ProjectWindowUtil.ShowCreatedAsset(AssetDatabase.LoadAssetAtPath(pathName, typeof(SceneAsset)));
        }
    }

    internal partial class DoCreateAssetWithContent : EndNameEditAction
    {
        public string filecontent;

        public DoCreateAssetWithContent() { }

        public override void Action(int instanceId, string pathName, string resourceFile) =>
            ProjectWindowUtil.ShowCreatedAsset(ProjectWindowUtil.CreateScriptAssetWithContent(pathName, filecontent));
    }

    internal partial class DoCreateScriptAsset : EndNameEditAction
    {
        public DoCreateScriptAsset() { }

        public override void Action(int instanceId, string pathName, string resourceFile) =>
            ProjectWindowUtil.ShowCreatedAsset(ProjectWindowUtil.CreateScriptAssetFromTemplate(pathName, resourceFile));
    }
}
