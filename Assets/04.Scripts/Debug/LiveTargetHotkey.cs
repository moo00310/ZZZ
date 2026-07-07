#if UNITY_EDITOR
using UnityEngine;
using UnityEngine.InputSystem;

namespace ZZZ.Debugging
{
    // 에디터 플레이 중 라이브 디버그 대상(캐릭터)을 게임 창에서 숫자키로 전환하기 위한 브리지.
    //
    // 왜 필요한가: 에디터 루프(EditorApplication.update)에서 읽는 입력은 Game View 입력을 반영하지 않는다
    // (Input System이 에디터/플레이 업데이트를 분리). 그래서 '플레이 루프에서 도는' 이 폴러가 입력을 잡아
    // 정적 필드에 기록하고, AnimationConfigTool(에디터)이 그 값을 읽어 대상을 바꾼다.
    //
    // 에디터 전용(#if UNITY_EDITOR) — 빌드엔 포함되지 않는다. 플레이 시작 시 자동 생성(씬 세팅 불필요).
    public static class LiveTargetHotkey
    {
        public static int Pressed = -1;   // 마지막으로 눌린 인덱스(0~3). 에디터가 읽고 -1로 소비.

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Install()
        {
            Pressed = -1;
            var go = new GameObject("~LiveTargetHotkeyPoller") { hideFlags = HideFlags.HideAndDontSave };
            Object.DontDestroyOnLoad(go);
            go.AddComponent<LiveTargetHotkeyPoller>();
        }
    }

    public class LiveTargetHotkeyPoller : MonoBehaviour
    {
        private void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if      (kb.digit1Key.wasPressedThisFrame) LiveTargetHotkey.Pressed = 0;
            else if (kb.digit2Key.wasPressedThisFrame) LiveTargetHotkey.Pressed = 1;
            else if (kb.digit3Key.wasPressedThisFrame) LiveTargetHotkey.Pressed = 2;
            else if (kb.digit4Key.wasPressedThisFrame) LiveTargetHotkey.Pressed = 3;
        }
    }
}
#endif
