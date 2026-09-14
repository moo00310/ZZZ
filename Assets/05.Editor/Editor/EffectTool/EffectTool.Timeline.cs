using UnityEngine;
using ZZZ.Editor.Effects;

namespace ZZZ.Editor.EffectTool
{
    public partial class EffectTool
    {
        private int   _dragEntry = -1;
        private float _dragGrabDx;

        // 조합 타임라인 — 공용 위젯에 프리뷰 플레이헤드/스크럽을 연결
        private void DrawTimeline(Rect area)
        {
            float playhead = (_previewRoot != null || _previewPlaying) ? _previewTime : -1f;
            EffectEditorShared.DrawStartDelayTimeline(
                area, _selectedComposite, ref _dragEntry, ref _dragGrabDx,
                playhead, t => { _previewTime = t; ScrubPreview(t); });
        }
    }
}
