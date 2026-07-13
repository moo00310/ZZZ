using UnityEngine;

namespace ZZZ.Effects
{
    /// <summary>
    /// 화염방사 "스트림 카드"(026 텍스처 quad)에 생명감을 주는 컴포넌트.
    /// 셰이더 없이 <b>길이 맥동(분사 리듬)</b> + <b>미세한 흔들림(난류)</b>만으로 살아있어 보이게 한다.
    /// 카드 2장에 각각 붙이고 phase를 다르게 주면, 겹친 무늬가 계속 어긋나며 흐르는 느낌이 난다.
    /// 베이스 텍스처(026)는 <b>스크롤하지 않는다</b> — 통짜 실루엣이라 밀면 그림이 새어나온다.
    /// </summary>
    [DisallowMultipleComponent]
    public class FlameStreamCard : MonoBehaviour
    {
        [Header("길이 맥동 (로컬 X 스케일 = 분사 뻗는 길이)")]
        [Tooltip("기준 길이. 프리팹의 X 스케일과 맞춰 둔다.")]
        public float baseLengthX = 5f;
        [Tooltip("맥동 진폭(월드 유닛). 뿜는 리듬으로 길이가 늘었다 줄었다 한다.")]
        public float pulseAmount = 0.5f;
        [Tooltip("맥동 속도.")]
        public float pulseSpeed = 8f;

        [Header("미세 흔들림 (로컬 Z 회전, 도)")]
        public float wobbleAngle = 2.5f;
        public float wobbleSpeed = 5f;

        [Header("위상 (카드마다 다르게 주면 겹침이 어긋나 자연스럽다)")]
        public float phase = 0f;

        Vector3 _baseScale;
        Quaternion _baseRot;

        void Awake()
        {
            _baseScale = transform.localScale;
            _baseRot = transform.localRotation;
        }

        void Update()
        {
            float t = Time.time + phase;

            // 길이만 맥동시키고 Y/Z 스케일(뒤집힘 부호 포함)은 보존한다.
            float x = baseLengthX + Mathf.Sin(t * pulseSpeed) * pulseAmount;
            transform.localScale = new Vector3(x, _baseScale.y, _baseScale.z);

            // 노즐 기준으로 살짝 좌우로 넘실.
            float z = Mathf.Sin(t * wobbleSpeed) * wobbleAngle;
            transform.localRotation = _baseRot * Quaternion.Euler(0f, 0f, z);
        }
    }
}
