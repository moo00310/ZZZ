using UnityEngine;
using ZZZ;

namespace ZZZ.Player.StateMachine
{
    // 입력 버퍼 — 공격/회피 입력을 짧은 윈도우 동안 기억해 콤보 선입력(buffered input)을 받아준다.
    // 윈도우가 지나면 자동으로 무효가 된다. 발동 시 Consume()로 즉시 비운다.
    public class InputBuffer
    {
        private readonly float _window;   // 입력 유효 시간(초)
        private ComboInput     _input;
        private float          _time = -10f;

        public InputBuffer(float window) => _window = window;

        public bool       HasInput => Time.time - _time <= _window;
        public ComboInput Current  => _input;

        public void Buffer(ComboInput input) { _input = input; _time = Time.time; }
        public void Consume() => _time = -10f;
    }
}
