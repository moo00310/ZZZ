using UnityEditor;
using UnityEngine;
using ZZZ;

namespace ZZZ.Editor.AnimationTool
{
    internal sealed class CameraPathPreviewView : SceneView
    {
    }

    public partial class AnimationConfigTool
    {
        private void OpenCameraPathPreview()
        {
            CameraPathPreviewView previewView =
                GetWindow<CameraPathPreviewView>();
            previewView.titleContent = new GUIContent("Path Camera Preview");
            previewView.drawGizmos = false;
            previewView.Show();
            PreviewSelectedCameraPathAtTime(_trackTime);
        }

        private void PreviewSelectedCameraPathAtTime(float trackTime)
        {
            if (!_followPathCameraPreview || _target == null || _config == null)
                return;

            CameraPathPreviewView previewView = FindCameraPathPreviewView();
            if (previewView == null
                || _notifyClipIdx < 0
                || _notifyClipIdx >= _config.Clips.Count)
                return;

            TrackClip clip = _config.Clips[_notifyClipIdx];
            if (_selectedNotify < 0
                || _selectedNotify >= clip.Notifies.Count)
                return;

            TrackNotify notify = clip.Notifies[_selectedNotify];
            if (notify.Payload is not CameraNotifyPayload payload
                || payload.Mode != CameraNotifyMode.Path
                || payload.PathPoints.Count < 2)
                return;

            float notifyTime = GetCameraNotifyTrackTime(
                clip, _notifyClipIdx, notify);
            float pathElapsed = Mathf.Max(0f, trackTime - notifyTime);
            float moveNormalizedTime = payload.PathMoveDuration > 0f
                ? Mathf.Clamp01(
                    (pathElapsed - payload.PathBlendIn)
                    / payload.PathMoveDuration)
                : 1f;
            float moveTime = payload.PathMoveCurve != null
                ? Mathf.Clamp01(
                    payload.PathMoveCurve.Evaluate(moveNormalizedTime))
                : moveNormalizedTime;

            int pointCount = payload.PathPoints.Count;
            var pointTimes = new float[pointCount];
            var lookAtHeights = new float[pointCount];
            for (int i = 0; i < pointCount; i++)
            {
                pointTimes[i] = payload.GetPathPointTime(i);
                lookAtHeights[i] =
                    payload.GetPathPointLookAtHeight(i);
            }

            float pathParameter = CameraPathUtility.RemapPointTime(
                pointTimes, pointCount, moveTime);
            Transform anchor = _target.transform;
            Vector3 position = anchor.TransformPoint(
                CameraPathUtility.Evaluate(
                    payload.PathPoints, pathParameter));
            float lookAtHeight = CameraPathUtility.EvaluateLinear(
                lookAtHeights, pathParameter, 1f);
            Vector3 lookTarget =
                anchor.position + anchor.up * lookAtHeight;
            Vector3 direction = lookTarget - position;
            Quaternion rotation = direction.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(direction, anchor.up)
                : anchor.rotation;
            float fieldOfView = Mathf.Lerp(
                payload.PathStartFieldOfView,
                payload.PathEndFieldOfView,
                moveTime);

            MoveSceneViewToCameraPose(
                previewView, position, rotation, fieldOfView);
        }

        private static CameraPathPreviewView FindCameraPathPreviewView()
        {
            CameraPathPreviewView[] previewViews =
                Resources.FindObjectsOfTypeAll<CameraPathPreviewView>();
            return previewViews.Length > 0 ? previewViews[0] : null;
        }

        private static SceneView FindEditingSceneView()
        {
            SceneView sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView is not CameraPathPreviewView)
                return sceneView;

            foreach (SceneView candidate in SceneView.sceneViews)
            {
                if (candidate != null
                    && candidate is not CameraPathPreviewView)
                    return candidate;
            }
            return null;
        }
    }
}
