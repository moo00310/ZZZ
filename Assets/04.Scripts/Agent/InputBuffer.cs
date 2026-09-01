using UnityEngine;
using ZZZ;

namespace ZZZ.Agent
{
    // 입력 버퍼 — 공격/회피 입력을 짧은 윈도우 동안 기억해 콤보 선입력(buffered input)을 받아준다.
    // 윈도우가 지나면 자동으로 무효가 된다. 발동 시 Consume()로 즉시 비운다.
    public class InputBuffer
    {
        private static readonly int Count = System.Enum.GetValues(typeof(ComboInput)).Length;

        private readonly float _window;   // 입력 유효 시간(초)
        private ComboInput     _input;
        private float          _time = -10f;

        public InputBuffer(float window) => _window = window;

        public bool       HasInput => Time.time - _time <= _window;
        public ComboInput Current  => _input;

        public void Buffer(ComboInput input) { _input = input; _time = Time.time; }
        public void Consume() => _time = -10f;

        public void Clear()
        {
            _time = -10f;
            System.Array.Clear(_held, 0, _held.Length);
        }

        // ── 키 홀드 상태 — OnRelease 타이밍(홀드 차지 → 릴리스)이 참조한다.
        // 타임아웃 버퍼가 아니라 실제 눌림 여부를 그대로 들고 있어, "누르고 있으면 대기 / 떼면 발동"이
        // 클립 길이/프레임 타이밍과 무관하게 확정적으로 판정된다. (인덱스 = (int)ComboInput)
        private readonly bool[] _held = new bool[Count];

        public bool IsHeld(ComboInput input) => _held[(int)input];
        public void SetHeld(ComboInput input, bool held) => _held[(int)input] = held;
    }
}
