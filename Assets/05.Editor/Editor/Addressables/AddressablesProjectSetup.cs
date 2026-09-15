using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Build.DataBuilders;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

namespace ZZZ.Editor.Addressables
{
    public static class AddressablesProjectSetup
    {
        private const string LOCAL_PROFILE = "Local Development";
        private const string CHARACTER_GROUP = "Characters - Burnice";
        private const string SHARED_VFX_GROUP = "VFX - Shared";
        private const string CHARACTER_VFX_GROUP = "VFX - Burnice";
        private const string VFX_DEPENDENCIES_GROUP = "VFX - Shared Dependencies";
        private const string VFX_FOLDER = "Assets/02.Effects/00.Prefab/";

        [MenuItem("ZZZ/Addressables/Configure Local Development")]
        public static void ConfigureLocalDevelopment()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Configure Addressables outside Play Mode.");

            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.GetSettings(true);
            string profileId = settings.profileSettings.GetProfileId(LOCAL_PROFILE);
            if (string.IsNullOrEmpty(profileId))
            {
                profileId = settings.profileSettings.AddProfile(LOCAL_PROFILE, settings.activeProfileId);
                settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalBuildPath,
                    AddressableAssetSettings.kLocalBuildPathValue);
                settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kLocalLoadPath,
                    AddressableAssetSettings.kLocalLoadPathValue);
            }
            settings.activeProfileId = profileId;
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteBuildPath,
                "ServerData/[BuildTarget]");
            settings.profileSettings.SetValue(profileId, AddressableAssetSettings.kRemoteLoadPath,
                "http://localhost/[BuildTarget]");
            settings.RemoteCatalogBuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
            settings.RemoteCatalogLoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);

            AddressableAssetGroup character = GetOrCreateGroup(settings, CHARACTER_GROUP,
                BundledAssetGroupSchema.BundlePackingMode.PackTogether);
            AddressableAssetGroup sharedVfx = GetOrCreateGroup(settings, SHARED_VFX_GROUP,
                BundledAssetGroupSchema.BundlePackingMode.PackTogether);
            AddressableAssetGroup characterVfx = GetOrCreateGroup(settings, CHARACTER_VFX_GROUP,
                BundledAssetGroupSchema.BundlePackingMode.PackTogether);
            AddressableAssetGroup vfxDependencies = GetOrCreateGroup(settings, VFX_DEPENDENCIES_GROUP,
                BundledAssetGroupSchema.BundlePackingMode.PackTogether);

            AddEntry(settings, character,
                "Assets/01.Characters/Burnice/Prefabs/Avatar_Female_Size02_Burnice.prefab",
                "characters/burnice/prefab", "character", "burnice");
            AddEntry(settings, characterVfx, VFX_FOLDER + "Eff_FireBeam.prefab",
                "vfx/burnice/fire-beam", "vfx", "burnice");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_AttackWarningCross.prefab",
                "vfx/shared/attack-warning-cross", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_BigExplosion.prefab",
                "vfx/shared/big-explosion", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_Flare.prefab",
                "vfx/shared/flare", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_LightBloom.prefab",
                "vfx/shared/light-bloom", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_MeeleString.prefab",
                "vfx/shared/melee-string", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "Eff_PaticleSprite.prefab",
                "vfx/shared/particle-sprite", "vfx", "shared");
            AddEntry(settings, sharedVfx, VFX_FOLDER + "ParticlesLight.prefab",
                "vfx/shared/particles-light", "vfx", "shared");
            AddEntry(settings, vfxDependencies,
                "Assets/02.Effects/Texture/Fire/Eff_Fire_052_LZL_01.png",
                "vfx/dependencies/fire-052", "vfx", "dependency");
            AddEntry(settings, vfxDependencies,
                "Packages/com.unity.render-pipelines.core/Runtime/RenderPipelineResources/FallbackShader.shader",
                "vfx/dependencies/core-fallback-shader", "vfx", "dependency");
            AddEntry(settings, vfxDependencies,
                "Packages/com.unity.render-pipelines.universal/Shaders/Utils/FallbackError.shader",
                "vfx/dependencies/urp-fallback-shader", "vfx", "dependency");
            AddEntry(settings, vfxDependencies,
                "Packages/com.unity.shadergraph/Editor/Resources/Shaders/FallbackError.shader",
                "vfx/dependencies/shader-graph-fallback", "vfx", "dependency");
            AddEntry(settings, vfxDependencies,
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/ParticlesUnlit.mat",
                "vfx/dependencies/particles-unlit-material", "vfx", "dependency");
            AddEntry(settings, vfxDependencies,
                "Packages/com.unity.render-pipelines.universal/Shaders/Particles/ParticlesUnlit.shader",
                "vfx/dependencies/particles-unlit-shader", "vfx", "dependency");

            settings.BuildRemoteCatalog = false;
            settings.BuildAddressablesWithPlayerBuild = AddressableAssetSettings.PlayerBuildOption.BuildWithPlayer;
            settings.buildSettings.LogResourceManagerExceptions = true;
            ProjectConfigData.GenerateBuildLayout = true;
            SelectDataBuilder<BuildScriptFastMode>(settings, true);
            SelectDataBuilder<BuildScriptPackedMode>(settings, false);
            settings.SetDirty(AddressableAssetSettings.ModificationEvent.BatchModification, null, true, true);
            AssetDatabase.SaveAssets();
            Debug.Log("Addressables Local Development configured: 4 content groups, 9 prefab addresses, " +
                "6 shared dependency addresses. " +
                "Runtime direct references still need migration before measuring unloading.");
        }

        [MenuItem("ZZZ/Addressables/Build Active Content")]
        public static void BuildActiveContent()
        {
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null)
                throw new InvalidOperationException("Configure Addressables before building content.");

            ProjectConfigData.GenerateBuildLayout = true;
            SelectDataBuilder<BuildScriptPackedMode>(settings, false);
            AddressableAssetSettings.BuildPlayerContent(out AddressablesPlayerBuildResult result);
            if (result == null || !string.IsNullOrEmpty(result.Error))
                throw new InvalidOperationException(result == null ? "No Addressables build result." : result.Error);

            Debug.Log($"Addressables content build succeeded for {EditorUserBuildSettings.activeBuildTarget}: " +
                result.OutputPath);
        }

        private static AddressableAssetGroup GetOrCreateGroup(
            AddressableAssetSettings settings, string name,
            BundledAssetGroupSchema.BundlePackingMode packingMode)
        {
            AddressableAssetGroup group = settings.FindGroup(name);
            if (group == null)
                group = settings.CreateGroup(name, false, false, true, null,
                    typeof(BundledAssetGroupSchema), typeof(ContentUpdateGroupSchema));
            BundledAssetGroupSchema bundle = group.GetSchema<BundledAssetGroupSchema>();
            bundle.UseDefaultSchemaSettings = false;
            bundle.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kLocalBuildPath);
            bundle.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kLocalLoadPath);
            bundle.Compression = BundledAssetGroupSchema.BundleCompressionMode.LZ4;
            bundle.BundleMode = packingMode;
            group.GetSchema<ContentUpdateGroupSchema>().StaticContent = false;
            return group;
        }

        private static void AddEntry(
            AddressableAssetSettings settings, AddressableAssetGroup group,
            string path, string address, params string[] labels)
        {
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || AssetDatabase.LoadMainAssetAtPath(path) == null)
                throw new InvalidOperationException($"Addressable asset is missing: {path}");

            // Existing entries may have been reorganized after the initial setup.
            if (settings.FindAssetEntry(guid) != null) return;
            foreach (AddressableAssetGroup existingGroup in settings.groups)
            {
                if (existingGroup == null) continue;
                foreach (AddressableAssetEntry existingEntry in existingGroup.entries)
                {
                    if (existingEntry.address == address)
                        throw new InvalidOperationException($"Address is already assigned: {address}");
                }
            }

            AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group);
            entry.SetAddress(address);
            foreach (string label in labels)
                entry.SetLabel(label, true, true);
        }

        private static void SelectDataBuilder<T>(AddressableAssetSettings settings, bool playMode)
            where T : ScriptableObject, IDataBuilder
        {
            for (int i = 0; i < settings.DataBuilders.Count; i++)
            {
                if (!(settings.DataBuilders[i] is T)) continue;
                if (playMode) settings.ActivePlayModeDataBuilderIndex = i;
                else settings.ActivePlayerDataBuilderIndex = i;
                return;
            }
            throw new InvalidOperationException($"Addressables data builder is missing: {typeof(T).Name}");
        }
    }
}
