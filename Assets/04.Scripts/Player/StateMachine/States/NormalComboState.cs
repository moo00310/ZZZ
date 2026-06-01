using UnityEngine;
using ZZZ;

namespace ZZZ.Player.StateMachine.States
{
    public class NormalComboState : StateBase
    {
        private readonly AnimationConfig _config;

        private int    _comboIndex;
        private float  _comboTimer;
        private bool   _nextQueued;
        private bool[] _notifyFired;  // 현재 클립에서 발화 완료된 Notify

        // Config 없을 때 쓰는 fallback
        private const int   FallbackMax    = 5;
        private const float FallbackWindow = 0.7f;
        private const float FallbackReset  = 1.2f;

        private int   MaxCombo   => _config != null ? _config.Clips.Count    : FallbackMax;
        private float ResetTime  => _config != null ? _config.ComboResetTime : FallbackReset;

        // 현재 클립의 Normal/Any 링크 윈도우 시작점 = 다음 타 입력 수용 시점
        private float WindowEnd
        {
            get
            {
                if (_config != null && _comboIndex - 1 < _config.Clips.Count)
                {
                    var links = _config.Clips[_comboIndex - 1].Links;
                    foreach (var l in links)
                        if (l.Input == ComboInput.Normal || l.Input == ComboInput.Any)
                            return l.WindowStart;
                }
                return FallbackWindow;
            }
        }

        public NormalComboState(PlayerStateContext ctx, PlayerStateMachine machine,
            AnimationConfig config = null)
            : base(ctx, machine)
        {
            _config = config;
        }

        public override void Enter()
        {
            _comboIndex = 1;
            _comboTimer = 0f;
            _nextQueued = false;
            PlayClip();
        }

        public override void Update()
        {
            _comboTimer += Time.deltaTime;
            float t = Ctx.Animator.GetCurrentNormalizedTime();

            FireNotifies(t);

            if (_nextQueued && t >= WindowEnd)
            {
                _nextQueued = false;

                if (_comboIndex >= MaxCombo)
                {
                    Machine.ChangeState<LocomotionState>();
                    return;
                }

                _comboIndex++;
                _comboTimer = 0f;
                PlayClip();
            }

            if (_comboTimer >= ResetTime && !_nextQueued)
                Machine.ChangeState<LocomotionState>();
        }

        private void PlayClip()
        {
            if (_config != null && _comboIndex - 1 < _config.Clips.Count)
            {
                var tc = _config.Clips[_comboIndex - 1];
                if (tc.Clip != null)
                {
                    Ctx.Animator.Play(tc.Clip.name, tc.TransitionIn);
                    _notifyFired = new bool[tc.Notifies.Count];
                    return;
                }
            }
            // Config 없거나 클립 미설정 → 기존 방식
            Ctx.Animator.PlayNormalAttack(_comboIndex);
            _notifyFired = null;
        }

        private void FireNotifies(float normalizedTime)
        {
            if (_config == null || _notifyFired == null) return;
            if (_comboIndex - 1 >= _config.Clips.Count) return;

            var tc = _config.Clips[_comboIndex - 1];
            for (int i = 0; i < tc.Notifies.Count && i < _notifyFired.Length; i++)
            {
                if (_notifyFired[i]) continue;
                if (normalizedTime >= tc.Notifies[i].NormalizedTime)
                {
                    _notifyFired[i] = true;
                    DispatchNotify(tc.Notifies[i]);
                }
            }
        }

        private void DispatchNotify(TrackNotify notify)
        {
            switch (notify.Type)
            {
                case NotifyType.Effect:
                    if (notify.EffectPrefab != null)
                        Object.Instantiate(notify.EffectPrefab,
                            Ctx.Transform.position, Ctx.Transform.rotation);
                    break;

                case NotifyType.Camera:
                case NotifyType.Sound:
                case NotifyType.Custom:
                    if (!string.IsNullOrEmpty(notify.EventName))
                        Ctx.Controller.gameObject.SendMessage(
                            notify.EventName, SendMessageOptions.DontRequireReceiver);
                    break;
            }
        }

        public void QueueNext() => _nextQueued = true;

        public override void Exit()
        {
            _comboIndex  = 0;
            _nextQueued  = false;
            _notifyFired = null;
        }
    }
}
