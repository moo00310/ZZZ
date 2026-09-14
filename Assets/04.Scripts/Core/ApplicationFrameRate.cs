using UnityEngine;

namespace ZZZ
{
    internal static class ApplicationFrameRate
    {
        private const int TARGET_FRAME_RATE = 60;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Configure()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Application.targetFrameRate = TARGET_FRAME_RATE;
#endif
        }
    }
}
