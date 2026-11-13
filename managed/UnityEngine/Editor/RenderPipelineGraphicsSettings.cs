using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace UnityEditor.Rendering
{
    public sealed partial class EditorGraphicsSettings
    {
        /// <summary>
        /// Brings a pipeline's global settings asset to the settings its pipeline supports, as the
        /// Editor does when the asset is registered (SetRenderPipelineGlobalSettingsAsset, which
        /// URP calls when it makes its pipeline): every concrete [Serializable] settings class
        /// supported on the asset's pipeline that the asset lacks is created (with its non-public
        /// constructor) and reset after all are added; a stored one the pipeline no longer supports
        /// (another pipeline's, or obsolete as an error) is removed; a resource container's null
        /// resource fields are loaded from their paths. Matches the Unity 6000.0.71f1 Editor,
        /// tests/projects/rpglobalsettings. Nothing is written to the project: the changes are the
        /// loaded asset's.
        /// </summary>
        public static void PopulateRenderPipelineGraphicsSettings(RenderPipelineGlobalSettings settings)
        {
            if (settings == null) return;
            var supportedOn = settings.GetType().GetCustomAttribute<SupportedOnRenderPipelineAttribute>();
            if (supportedOn == null || supportedOn.renderPipelineTypes == null || supportedOn.renderPipelineTypes.Length == 0)
                throw new InvalidOperationException($"{settings.GetType()} must declare the render pipeline it is used with ({nameof(SupportedOnRenderPipelineAttribute)})");
            var pipelineAsset = supportedOn.renderPipelineTypes[0];
            var created = new List<IRenderPipelineGraphicsSettings>();
            foreach (var type in TypeCache.GetTypesDerivedFrom(typeof(IRenderPipelineGraphicsSettings)))
            {
                if (!IsSettingsClass(type)) continue;
                bool supported = !(type.GetCustomAttribute<ObsoleteAttribute>()?.IsError ?? false)
                    && SupportedOnRenderPipelineAttribute.IsTypeSupportedOnRenderPipeline(type, pipelineAsset);
                bool has = settings.TryGet(type, out var stored);
                if (!supported)
                {
                    if (has) settings.Remove(stored);
                    continue;
                }
                if (!has && TryCreate(type, out stored))
                {
                    settings.Add(stored);
                    created.Add(stored);
                }
                if (stored is IRenderPipelineResources resources)
                    RenderPipelineResourcesEditorUtils.TryReloadContainedNullFields(resources);
            }
            foreach (var setting in created) setting.Reset();
        }

        /// <summary>A class the settings of a global settings asset can be made of: concrete,
        /// not generic, [Serializable] and declaring the pipelines it is supported on (the Editor
        /// warns of a class without one of the attributes and skips it).</summary>
        static bool IsSettingsClass(Type type)
        {
            if (type.IsAbstract || type.IsGenericType || type.IsInterface) return false;
            if (type.GetCustomAttribute<SerializableAttribute>() == null)
            {
                Debug.LogWarning($"{nameof(SerializableAttribute)} must be added to {type}, the setting will be skipped");
                return false;
            }
            if (type.GetCustomAttribute<SupportedOnRenderPipelineAttribute>() == null)
            {
                Debug.LogWarning($"{nameof(SupportedOnRenderPipelineAttribute)} must be added to {type}, the setting will be skipped");
                return false;
            }
            return true;
        }

        static bool TryCreate(Type type, out IRenderPipelineGraphicsSettings instance)
        {
            try
            {
                instance = (IRenderPipelineGraphicsSettings)Activator.CreateInstance(type, nonPublic: true);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                instance = null;
                return false;
            }
        }
    }

    /// <summary>A render pipeline resource container's resources loaded from the paths its fields
    /// declare (<see cref="ResourcePathsBaseAttribute"/>): a project path is relative to the package
    /// of the container's assembly (else `Assets/`), a built-in path is a built-in resource's, a
    /// shader name a shader's. Only null fields and null array elements are loaded.</summary>
    internal static partial class RenderPipelineResourcesEditorUtils
    {
        public enum ResultStatus
        {
            NothingToUpdate = 0,
            InvalidPathOrNameFound = 1,
            ResourceReloaded = 2,
            SkipedDueToNotMainThread = 3,
        }

        /// <summary>A resource path that names nothing.</summary>
        sealed class MissingResourceException : Exception
        {
            internal MissingResourceException(string message) : base(message) { }
        }

        internal static bool TryReloadContainedNullFields(IRenderPipelineResources resource, out ResultStatus result, out string message)
        {
            message = string.Empty;
            result = ResultStatus.NothingToUpdate;
            try
            {
                if (ReloadNullFields(resource, RootOf(resource.GetType()))) result = ResultStatus.ResourceReloaded;
            }
            catch (MissingResourceException e)
            {
                message = e.Message;
                result = ResultStatus.InvalidPathOrNameFound;
                return false;
            }
            return true;
        }

        public static ResultStatus TryReloadContainedNullFields(IRenderPipelineResources resource)
        {
            if (!TryReloadContainedNullFields(resource, out var result, out var message)) Debug.LogError(message);
            return result;
        }

        static string RootOf(Type type)
        {
            var package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(type.Assembly);
            return package == null ? "Assets/" : $"Packages/{package.name}/";
        }

        static bool IsNull(object value) => value == null || value.Equals(null);

        static bool ReloadNullFields(object container, string root)
        {
            bool changed = false;
            foreach (var field in container.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var attribute = field.GetCustomAttribute<ResourcePathsBaseAttribute>(inherit: false);
                if (attribute?.paths == null) continue;
                string Full(string path) => attribute.location == SearchType.ProjectPath ? root + path : path;
                if (attribute.isField)
                {
                    if (!IsNull(field.GetValue(container))) continue;
                    field.SetValue(container, Load(Full(attribute.paths[0]), field.FieldType, attribute.location));
                    changed = true;
                    continue;
                }
                if (!(field.GetValue(container) is Array array) || array.Length != attribute.paths.Length)
                {
                    array = (Array)Activator.CreateInstance(field.FieldType, attribute.paths.Length);
                    field.SetValue(container, array);
                    changed = true;
                }
                var element = field.FieldType.GetElementType();
                for (int i = 0; i < attribute.paths.Length; i++)
                {
                    if (!IsNull(array.GetValue(i))) continue;
                    array.SetValue(Load(Full(attribute.paths[i]), element, attribute.location), i);
                    changed = true;
                }
            }
            return changed;
        }

        static Object Load(string path, Type type, SearchType location)
        {
            if (location == SearchType.ProjectPath && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
                throw new MissingResourceException($"Failed to find {path} in {location}.");
            Object result;
            if (type == typeof(Shader))
                result = location == SearchType.ProjectPath ? AssetDatabase.LoadAssetAtPath(path, typeof(Shader)) : Shader.Find(path);
            else
                result = location switch
                {
                    SearchType.BuiltinPath => Resources.GetBuiltinResource(type, path),
                    SearchType.BuiltinExtraPath => AssetDatabase.GetBuiltinExtraResource(type, path),
                    SearchType.ProjectPath => AssetDatabase.LoadAssetAtPath(path, type),
                    SearchType.ShaderName => throw new ArgumentException($"{nameof(SearchType.ShaderName)} is only available for Shaders."),
                    _ => throw new NotImplementedException($"Unknown {location}"),
                };
            if (IsNull(result))
                throw new MissingResourceException(location == SearchType.ProjectPath
                    ? $"Cannot load. Path {path} is correct but AssetDatabase cannot load now."
                    : $"Failed to find {path} in {location}.");
            return result;
        }
    }
}
