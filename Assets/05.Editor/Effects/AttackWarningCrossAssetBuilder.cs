using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using ZZZ.Effects;

namespace ZZZ.Editor.Effects
{
    public static class AttackWarningCrossAssetBuilder
    {
        private const string TEXTURE_PATH =
            "Assets/02.Effects/Texture/Flare/T_AttackWarningRay.png";
        private const string MATERIAL_FOLDER = "Assets/02.Effects/Materials";
        private const string MATERIAL_PATH =
            MATERIAL_FOLDER + "/M_AttackWarningCross.mat";
        private const string PREFAB_PATH =
            "Assets/02.Effects/00.Prefab/Eff_AttackWarningCross.prefab";
        private const string COMPOSITE_FOLDER = "Assets/02.Effects/Composite";
        private const string COMPOSITE_PATH =
            COMPOSITE_FOLDER + "/Cmp_AttackWarningCross.asset";
        private const string DURAHAN_ATTACK_CONFIG_PATH =
            "Assets/01.Characters/Durahan/SO_Anim/Durahan_Attack_Config.asset";
        private const string SHADER_NAME = "ZZZ/UI/Attack Warning Additive";

        [MenuItem("ZZZ/Effects/Create Attack Warning Cross Assets")]
        public static void CreateAssets()
        {
            GameObject existingPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(PREFAB_PATH);
            Material existingMaterial =
                AssetDatabase.LoadAssetAtPath<Material>(MATERIAL_PATH);
            CompositeEffect existingComposite =
                AssetDatabase.LoadAssetAtPath<CompositeEffect>(COMPOSITE_PATH);
            bool hasAnyAsset = existingPrefab != null
                || existingMaterial != null
                || existingComposite != null;
            bool hasEveryAsset = existingPrefab != null
                && existingMaterial != null
                && existingComposite != null;

            if (hasEveryAsset)
            {
                ConfigureFaceSocket(existingComposite);
                int linkedExistingWarningCount =
                    AttachToDurahanWarnings(existingComposite);
                AssetDatabase.SaveAssets();
                Selection.activeObject = existingComposite;
                Debug.Log(
                    $"Reused existing attack warning assets and linked "
                    + $"{linkedExistingWarningCount} Durahan warning notifies.");
                return;
            }

            if (hasAnyAsset)
            {
                EditorUtility.DisplayDialog(
                    "Attack Warning Cross",
                    "Only part of the attack-warning asset set exists. "
                    + "Existing assets were left unchanged.",
                    "OK");
                return;
            }

            ConfigureTextureImporter();
            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TEXTURE_PATH);
            Shader shader = Shader.Find(SHADER_NAME);
            if (texture == null || shader == null)
            {
                Debug.LogError(
                    $"Attack warning asset creation failed. Texture: {texture}, Shader: {shader}");
                return;
            }

            EnsureFolder(MATERIAL_FOLDER);
            EnsureFolder(COMPOSITE_FOLDER);

            var material = new Material(shader)
            {
                name = "M_AttackWarningCross",
            };
            AssetDatabase.CreateAsset(material, MATERIAL_PATH);

            GameObject prefab = CreatePrefab(texture, material);
            CompositeEffect composite = CreateComposite(prefab);
            int linkedWarningCount = AttachToDurahanWarnings(composite);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeObject = composite;
            Debug.Log(
                $"Created attack warning cross assets at {COMPOSITE_PATH} "
                + $"and linked {linkedWarningCount} Durahan warning notifies.");
        }

        private static void ConfigureTextureImporter()
        {
            AssetDatabase.ImportAsset(TEXTURE_PATH, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(TEXTURE_PATH) as TextureImporter;
            if (importer == null) return;

            importer.textureType = TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.npotScale = TextureImporterNPOTScale.ToNearest;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
        }

        private static GameObject CreatePrefab(Texture texture, Material material)
        {
            var root = new GameObject("Eff_AttackWarningCross");
            try
            {
                var poolConfig = root.AddComponent<EffectPoolConfig>();
                poolConfig.PrewarmCount = 1;
                poolConfig.MaxSize = 4;
                var effect = root.AddComponent<AttackWarningCrossEffect>();

                var canvasObject = new GameObject(
                    "OverlayCanvas",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(CanvasGroup));
                canvasObject.transform.SetParent(root.transform, false);

                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = 250;

                CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920f, 1080f);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;

                CanvasGroup canvasGroup = canvasObject.GetComponent<CanvasGroup>();
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;

                RectTransform right = CreateRay(
                    "Ray_Right", canvasObject.transform, texture, material, 0f);
                RectTransform down = CreateRay(
                    "Ray_Down", canvasObject.transform, texture, material, 270f);
                RectTransform left = CreateRay(
                    "Ray_Left", canvasObject.transform, texture, material, 180f);
                RectTransform up = CreateRay(
                    "Ray_Up", canvasObject.transform, texture, material, 90f);

                var serializedEffect = new SerializedObject(effect);
                serializedEffect.FindProperty("_canvas").objectReferenceValue = canvas;
                serializedEffect.FindProperty("_canvasGroup").objectReferenceValue = canvasGroup;
                serializedEffect.FindProperty("_rightRay").objectReferenceValue = right;
                serializedEffect.FindProperty("_downRay").objectReferenceValue = down;
                serializedEffect.FindProperty("_leftRay").objectReferenceValue = left;
                serializedEffect.FindProperty("_upRay").objectReferenceValue = up;
                serializedEffect.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PREFAB_PATH);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static RectTransform CreateRay(
            string name,
            Transform parent,
            Texture texture,
            Material material,
            float rotation)
        {
            var rayObject = new GameObject(
                name,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(RawImage));
            rayObject.transform.SetParent(parent, false);

            var rect = rayObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(960f, 84f);
            rect.localEulerAngles = new Vector3(0f, 0f, rotation);

            RawImage image = rayObject.GetComponent<RawImage>();
            image.texture = texture;
            image.material = material;
            image.color = new Color(1f, 0.24f, 0.015f, 1f);
            image.raycastTarget = false;
            return rect;
        }

        private static CompositeEffect CreateComposite(GameObject prefab)
        {
            var composite = ScriptableObject.CreateInstance<CompositeEffect>();
            composite.name = "Cmp_AttackWarningCross";
            composite.Entries = new List<CompositeEffectEntry>
            {
                new CompositeEffectEntry
                {
                    Prefab = prefab,
                    Socket = "Bip001 Head",
                    PositionOffset = Vector3.zero,
                    FollowSpawner = true,
                    Despawn = DespawnMode.Fixed,
                    Lifetime = AttackWarningCrossEffect.DEFAULT_DURATION,
                },
            };
            AssetDatabase.CreateAsset(composite, COMPOSITE_PATH);
            return composite;
        }

        private static void ConfigureFaceSocket(CompositeEffect composite)
        {
            if (composite == null
                || composite.Entries == null
                || composite.Entries.Count == 0
                || composite.Entries[0] == null)
                return;

            CompositeEffectEntry entry = composite.Entries[0];
            entry.Socket = "Bip001 Head";
            entry.PositionOffset = Vector3.zero;
            entry.FollowSpawner = true;
            EditorUtility.SetDirty(composite);
        }

        private static int AttachToDurahanWarnings(CompositeEffect composite)
        {
            var config = AssetDatabase.LoadAssetAtPath<AnimationConfig>(
                DURAHAN_ATTACK_CONFIG_PATH);
            if (config == null)
            {
                Debug.LogWarning(
                    $"Durahan attack config was not found at {DURAHAN_ATTACK_CONFIG_PATH}");
                return 0;
            }

            int linkedCount = 0;
            for (int clipIndex = 0; clipIndex < config.Clips.Count; clipIndex++)
            {
                TrackClip clip = config.Clips[clipIndex];
                var warningTimes = new List<float>();
                for (int notifyIndex = 0;
                    notifyIndex < clip.Notifies.Count;
                    notifyIndex++)
                {
                    TrackNotify notify = clip.Notifies[notifyIndex];
                    if (notify.Payload is HitNotifyPayload hitPayload
                        && hitPayload.Action == HitNotifyAction.ParryWarning)
                        warningTimes.Add(notify.NormalizedTime);
                }

                for (int warningIndex = 0;
                    warningIndex < warningTimes.Count;
                    warningIndex++)
                {
                    float warningTime = warningTimes[warningIndex];
                    if (HasWarningEffect(clip, composite, warningTime)) continue;

                    var effectNotify = new TrackNotify
                    {
                        NormalizedTime = warningTime,
                        EndNormalizedTime = 0f,
                        Locked = true,
                    };
                    effectNotify.Type = NotifyType.Effect;
                    effectNotify.Effect = composite;
                    clip.Notifies.Add(effectNotify);
                    linkedCount++;
                }

                if (warningTimes.Count > 0)
                    clip.Notifies.Sort(
                        (left, right) => left.NormalizedTime.CompareTo(
                            right.NormalizedTime));
            }

            if (linkedCount > 0) EditorUtility.SetDirty(config);
            return linkedCount;
        }

        private static bool HasWarningEffect(
            TrackClip clip,
            CompositeEffect composite,
            float normalizedTime)
        {
            for (int i = 0; i < clip.Notifies.Count; i++)
            {
                TrackNotify notify = clip.Notifies[i];
                if (Mathf.Approximately(notify.NormalizedTime, normalizedTime)
                    && notify.Payload is EffectNotifyPayload effectPayload
                    && effectPayload.Effect == composite)
                    return true;
            }
            return false;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folder = System.IO.Path.GetFileName(path);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folder)) return;
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folder);
        }
    }
}
