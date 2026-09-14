using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ.Effects;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.EffectTool
{
    // 전용 이펙트 에디터 툴 — CompositeEffect(조합)를 브라우징/편집/프리뷰한다.
    // 원자는 별도 SO 없이 각 Entry가 프리팹을 직접 참조하고 배치/풀링 설정을 들고 있다.
    // 캐릭터 애니와 함께 타이밍을 맞추려면 AnimationConfigTool의 Effect 탭을 쓴다.
    //
    // 구성: [좌측 조합 목록] | [우측: 프리뷰바 / 타임라인 / 인스펙터]  (+ 풀 개요 토글)
    public partial class EffectTool : EditorWindow
    {
        [SerializeField] private CompositeEffect _selectedComposite;
        private SerializedObject _compositeSO;

        private readonly List<CompositeEffect> _composites = new List<CompositeEffect>();

        private Vector2 _listScroll;
        private Vector2 _inspScroll;

        [SerializeField] private bool _poolOverview;

        private const float ToolbarH    = 22f;
        private const float PreviewBarH  = 30f;
        private const float ListW       = 240f;
        private const float TimelineH   = 180f;

        [MenuItem("ZZZ/Effect Tool")]
        public static void Open()
        {
            var w = GetWindow<EffectTool>("Effect Tool");
            w.minSize = new Vector2(720f, 480f);
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            RefreshList();
            RebuildSelectionSO();
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            StopPreview();
        }

        private void OnPlayModeChanged(PlayModeStateChange s)
        {
            if (s == PlayModeStateChange.ExitingEditMode || s == PlayModeStateChange.ExitingPlayMode)
                StopPreview();
        }

        private void OnEditorUpdate()
        {
            if (_previewPlaying) { TickPreview(); Repaint(); }
        }

        private void RefreshList()
        {
            _composites.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:CompositeEffect"))
            {
                var c = AssetDatabase.LoadAssetAtPath<CompositeEffect>(AssetDatabase.GUIDToAssetPath(guid));
                if (c != null) _composites.Add(c);
            }
            _composites.Sort((x, y) => string.CompareOrdinal(x.name, y.name));
        }

        private void RebuildSelectionSO()
        {
            _compositeSO = _selectedComposite != null ? new SerializedObject(_selectedComposite) : null;
        }

        private void SelectComposite(CompositeEffect composite)
        {
            StopPreview();
            _selectedComposite = composite;
            RebuildSelectionSO();
        }

        private void OnGUI()
        {
            DrawToolbar();

            float bodyY = ToolbarH;
            float bodyH = position.height - bodyY;

            DrawList(new Rect(0, bodyY, ListW, bodyH));
            EditorGUI.DrawRect(new Rect(ListW, bodyY, 1f, bodyH), new Color(0.1f, 0.1f, 0.1f));

            float rightX = ListW + 1f;
            float rightW = position.width - rightX;
            var rightArea = new Rect(rightX, bodyY, rightW, bodyH);

            if (_poolOverview) { DrawPoolOverview(rightArea); return; }

            DrawPreviewBar(new Rect(rightX, bodyY, rightW, PreviewBarH));
            float y = bodyY + PreviewBarH;

            if (_selectedComposite != null)
            {
                DrawTimeline(new Rect(rightX, y, rightW, TimelineH));
                y += TimelineH;
                EditorGUI.DrawRect(new Rect(rightX, y, rightW, 1f), new Color(0.1f, 0.1f, 0.1f));
                y += 1f;
            }

            DrawInspector(new Rect(rightX, y, rightW, position.height - y));
        }

        private void DrawToolbar()
        {
            var r = new Rect(0, 0, position.width, ToolbarH);
            GUI.Box(r, GUIContent.none, EditorStyles.toolbar);
            GUILayout.BeginArea(r);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("New Composite", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                var c = EffectEditorShared.CreateAsset<CompositeEffect>("Cmp_New");
                RefreshList();
                SelectComposite(c);
            }
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(64)))
                RefreshList();

            GUILayout.FlexibleSpace();

            _poolOverview = GUILayout.Toggle(_poolOverview, "Pool Overview",
                EditorStyles.toolbarButton, GUILayout.Width(100));

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
    }
}
