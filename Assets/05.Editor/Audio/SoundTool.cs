using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using ZZZ.Audio;

namespace ZZZ.Editor.Audio
{
    public sealed class SoundTool : EditorWindow
    {
        [SerializeField] private CompositeSound _selectedSound;
        [SerializeField] private string _search = "";

        private readonly List<CompositeSound> _sounds =
            new List<CompositeSound>();

        private SerializedObject _soundObject;
        private Vector2 _listScroll;
        private Vector2 _inspectorScroll;

        private const float TOOLBAR_HEIGHT = 22f;
        private const float LIST_WIDTH = 240f;

        private static readonly Color RowSelected =
            new Color(0.24f, 0.40f, 0.62f);
        private static readonly Color RowHover =
            new Color(1f, 1f, 1f, 0.05f);

        [MenuItem("ZZZ/Sound Tool")]
        public static void Open()
        {
            var window = GetWindow<SoundTool>("Sound Tool");
            window.minSize = new Vector2(640f, 400f);
        }

        private void OnEnable()
        {
            CompositeSound selected =
                Selection.activeObject as CompositeSound;
            if (selected != null) _selectedSound = selected;

            RefreshList();
            RebuildSelectionObject();
        }

        private void OnGUI()
        {
            DrawToolbar();

            float bodyY = TOOLBAR_HEIGHT;
            float bodyHeight = position.height - bodyY;
            DrawList(new Rect(0f, bodyY, LIST_WIDTH, bodyHeight));
            EditorGUI.DrawRect(
                new Rect(LIST_WIDTH, bodyY, 1f, bodyHeight),
                new Color(0.1f, 0.1f, 0.1f));
            DrawInspector(new Rect(
                LIST_WIDTH + 1f, bodyY,
                position.width - LIST_WIDTH - 1f, bodyHeight));
        }

        private void DrawToolbar()
        {
            var area = new Rect(
                0f, 0f, position.width, TOOLBAR_HEIGHT);
            GUI.Box(area, GUIContent.none, EditorStyles.toolbar);
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button(
                    "New Composite Sound",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(145f)))
                CreateSound();
            if (GUILayout.Button(
                    "Refresh", EditorStyles.toolbarButton,
                    GUILayout.Width(64f)))
                RefreshList();

            GUILayout.FlexibleSpace();
            if (_selectedSound != null
                && GUILayout.Button(
                    "Ping Asset", EditorStyles.toolbarButton,
                    GUILayout.Width(72f)))
            {
                Selection.activeObject = _selectedSound;
                EditorGUIUtility.PingObject(_selectedSound);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawList(Rect area)
        {
            EditorGUI.DrawRect(
                area, new Color(0.19f, 0.19f, 0.19f));
            GUILayout.BeginArea(area);

            _search = EditorGUILayout.TextField(
                _search, EditorStyles.toolbarSearchField);
            _listScroll = EditorGUILayout.BeginScrollView(
                _listScroll);

            Rect header = GUILayoutUtility.GetRect(
                0f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(
                header, new Color(0.14f, 0.14f, 0.14f));
            GUI.Label(
                new Rect(
                    header.x + 4f, header.y + 2f,
                    header.width - 4f, 16f),
                $"Composite Sounds ({_sounds.Count})",
                EditorStyles.boldLabel);

            for (int i = 0; i < _sounds.Count; i++)
            {
                CompositeSound sound = _sounds[i];
                if (sound == null || !MatchesSearch(sound)) continue;

                Rect row = GUILayoutUtility.GetRect(
                    area.width, 20f);
                bool selected = _selectedSound == sound;
                if (selected) EditorGUI.DrawRect(row, RowSelected);
                else if (row.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(row, RowHover);

                GUI.Label(
                    new Rect(
                        row.x + 8f, row.y + 1f,
                        row.width - 12f, 18f),
                    sound.name, EditorStyles.label);

                Event currentEvent = Event.current;
                if (currentEvent.type != EventType.MouseDown
                    || currentEvent.button != 0
                    || !row.Contains(currentEvent.mousePosition))
                    continue;

                SelectSound(sound);
                if (currentEvent.clickCount == 2)
                {
                    Selection.activeObject = sound;
                    EditorGUIUtility.PingObject(sound);
                }
                currentEvent.Use();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawInspector(Rect area)
        {
            EditorGUI.DrawRect(
                area, new Color(0.22f, 0.22f, 0.22f));
            GUILayout.BeginArea(new Rect(
                area.x + 8f, area.y + 6f,
                area.width - 16f, area.height - 12f));
            _inspectorScroll = EditorGUILayout.BeginScrollView(
                _inspectorScroll);

            if (_selectedSound == null)
            {
                EditorGUILayout.HelpBox(
                    "좌측에서 CompositeSound를 선택하거나 새로 만드세요.",
                    MessageType.Info);
            }
            else
            {
                if (_soundObject == null) RebuildSelectionObject();
                _soundObject.Update();

                EditorGUILayout.LabelField(
                    _selectedSound.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    AssetDatabase.GetAssetPath(_selectedSound),
                    EditorStyles.miniLabel);
                EditorGUILayout.Space(4f);
                EditorGUILayout.HelpBox(
                    "하나의 의미 단위로 재사용하며 Clips 중 하나를 무작위로 재생합니다.",
                    MessageType.Info);

                CompositeSoundEditorShared.DrawSound(_soundObject);
                _soundObject.ApplyModifiedProperties();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void RefreshList()
        {
            _sounds.Clear();
            string[] guids =
                AssetDatabase.FindAssets("t:CompositeSound");
            for (int i = 0; i < guids.Length; i++)
            {
                string path =
                    AssetDatabase.GUIDToAssetPath(guids[i]);
                CompositeSound sound =
                    AssetDatabase.LoadAssetAtPath<CompositeSound>(
                        path);
                if (sound != null) _sounds.Add(sound);
            }

            _sounds.Sort((left, right) =>
                string.CompareOrdinal(left.name, right.name));
            Repaint();
        }

        private void SelectSound(CompositeSound sound)
        {
            _selectedSound = sound;
            RebuildSelectionObject();
        }

        private void RebuildSelectionObject()
        {
            _soundObject = _selectedSound != null
                ? new SerializedObject(_selectedSound)
                : null;
        }

        private bool MatchesSearch(CompositeSound sound)
        {
            return string.IsNullOrWhiteSpace(_search)
                || sound.name.IndexOf(
                    _search.Trim(),
                    System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void CreateSound()
        {
            CompositeSound sound =
                CompositeSoundEditorShared.CreateAsset("Snd_New");

            RefreshList();
            SelectSound(sound);
            Selection.activeObject = sound;
            EditorGUIUtility.PingObject(sound);
        }
    }
}

