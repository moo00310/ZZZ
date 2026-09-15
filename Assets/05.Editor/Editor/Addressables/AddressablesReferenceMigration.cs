using System;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AddressableAssets;
using ZZZ.Effects;
using ZZZ.Player;

namespace ZZZ.Editor.Addressables
{
    public static class AddressablesReferenceMigration
    {
        [MenuItem("ZZZ/Addressables/Migrate Runtime References")]
        public static void MigrateRuntimeReferences()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Migrate references outside Play Mode.");
            AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
            if (settings == null) throw new InvalidOperationException("Configure Addressables first.");
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            int effects = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:CompositeEffect", new[] { "Assets" }))
            {
                CompositeEffect composite = AssetDatabase.LoadAssetAtPath<CompositeEffect>(AssetDatabase.GUIDToAssetPath(guid));
                bool changed = false;
                foreach (CompositeEffectEntry entry in composite.Entries)
                {
                    if (entry == null || entry.LegacyPrefab == null) continue;
                    string prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(entry.LegacyPrefab));
                    RequireAddressable(settings, prefabGuid);
                    entry.SetPrefabReference(new AssetReferenceGameObject(prefabGuid));
                    effects++;
                    changed = true;
                }
                if (changed) EditorUtility.SetDirty(composite);
            }
            AssetDatabase.SaveAssets();

            // Batch migration works on the build scenes, preserving other scenes and prefab GUIDs.
            SceneSetup[] previousScenes = EditorSceneManager.GetSceneManagerSetup();
            int characters = 0;
            try
            {
                foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
                {
                    if (!buildScene.enabled) continue;
                    var scene = EditorSceneManager.OpenScene(buildScene.path, OpenSceneMode.Single);
                    bool changed = false;
                    foreach (GameObject root in scene.GetRootGameObjects())
                    {
                        foreach (SquadController squad in root.GetComponentsInChildren<SquadController>(true))
                        {
                            var serialized = new SerializedObject(squad);
                            SerializedProperty legacy = serialized.FindProperty("_agentPrefabs");
                            if (legacy.arraySize == 0) continue;
                            SerializedProperty references = serialized.FindProperty("_agentReferences");
                            if (references.arraySize != 0)
                                throw new InvalidOperationException("Both legacy and Addressable squad lists are populated.");
                            references.arraySize = legacy.arraySize;
                            for (int i = 0; i < legacy.arraySize; i++)
                            {
                                UnityEngine.Object prefab = legacy.GetArrayElementAtIndex(i).objectReferenceValue;
                                string prefabGuid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(prefab));
                                RequireAddressable(settings, prefabGuid);
                                references.GetArrayElementAtIndex(i).FindPropertyRelative("m_AssetGUID").stringValue = prefabGuid;
                                characters++;
                            }
                            legacy.ClearArray();
                            serialized.ApplyModifiedPropertiesWithoutUndo();
                            changed = true;
                        }
                    }
                    if (changed) EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                if (previousScenes.Length > 0) EditorSceneManager.RestoreSceneManagerSetup(previousScenes);
            }
            Debug.Log($"Addressables references migrated: {effects} VFX entries, {characters} squad slots.");
        }

        public static void MigrateAndBuild()
        {
            AddressablesProjectSetup.ConfigureLocalDevelopment();
            MigrateRuntimeReferences();
            AddressablesProjectSetup.BuildActiveContent();
        }

        private static void RequireAddressable(AddressableAssetSettings settings, string guid)
        {
            if (string.IsNullOrEmpty(guid) || settings.FindAssetEntry(guid) == null)
                throw new InvalidOperationException($"Asset must be registered before migration: {AssetDatabase.GUIDToAssetPath(guid)}");
        }
    }
}
