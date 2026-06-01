using System.IO;
using UnityEditor;
using UnityEngine;

namespace ZZZ.Editor.AnimationTool
{
    public static class FbxAnimationExtractor
    {
        [MenuItem("Assets/ZZZ/Extract Animation Clips", true)]
        private static bool ValidateExtract()
        {
            string path = AssetDatabase.GetAssetPath(Selection.activeObject);
            return path.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase);
        }

        [MenuItem("Assets/ZZZ/Extract Animation Clips")]
        private static void Extract()
        {
            string fbxPath = AssetDatabase.GetAssetPath(Selection.activeObject);

            string defaultFolder = Path.Combine(Application.dataPath, "01.Characters");
            string outputAbsolute = EditorUtility.OpenFolderPanel("애니메이션 클립 저장 폴더 선택", defaultFolder, "");

            if (string.IsNullOrEmpty(outputAbsolute))
                return;

            if (!outputAbsolute.StartsWith(Application.dataPath))
            {
                EditorUtility.DisplayDialog("오류", "Assets 폴더 내부를 선택해 주세요.", "확인");
                return;
            }

            string outputDir = "Assets" + outputAbsolute.Substring(Application.dataPath.Length).Replace('\\', '/');

            if (!Directory.Exists(outputAbsolute))
                Directory.CreateDirectory(outputAbsolute);

            Object[] allAssets = AssetDatabase.LoadAllAssetsAtPath(fbxPath);
            int count = 0;

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (Object asset in allAssets)
                {
                    if (asset is AnimationClip clip && !clip.name.Contains("__preview__"))
                    {
                        AnimationClip newClip = Object.Instantiate(clip);
                        newClip.name = clip.name;

                        RemoveFaceBlendShapeTracks(newClip);

                        string savePath = outputDir + "/" + clip.name + ".anim";
                        AssetDatabase.CreateAsset(newClip, savePath);
                        count++;
                    }
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog(
                "추출 완료",
                $"{count}개의 애니메이션 클립을 추출했습니다.\n{outputDir}",
                "확인"
            );

            Debug.Log($"[FbxAnimationExtractor] {count}개 추출 완료 → {outputDir}");
        }

        // Burnice_Face의 SkinnedMeshRenderer BlendShape 트랙만 제거 (SRT는 유지)
        private static void RemoveFaceBlendShapeTracks(AnimationClip clip)
        {
            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            foreach (EditorCurveBinding binding in bindings)
            {
                bool isFacePath = binding.path == "Burnice_Face" || binding.path.EndsWith("/Burnice_Face");
                bool isBlendShape = binding.type == typeof(SkinnedMeshRenderer)
                                    && binding.propertyName.StartsWith("blendShape");

                if (isFacePath && isBlendShape)
                    AnimationUtility.SetEditorCurve(clip, binding, null);
            }
        }
    }
}
