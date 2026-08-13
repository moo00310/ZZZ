using UnityEngine;

namespace ZZZ
{
    public enum CombatTeam
    {
        Neutral,
        Player,
        Enemy
    }

    public enum HitShape
    {
        Sphere,
        Cone,
        Box,
        Capsule,
        ExpandingSphere,
        ExpandingCone
    }

    public enum HitOrigin
    {
        CharacterRoot,
        Socket,
        Effect
    }

    public enum HitOriginTracking
    {
        Follow,
        WorldSnapshot
    }

    public enum HitFrequency
    {
        OncePerActivation,
        RepeatInterval
    }

    public enum HitQueryMode
    {
        Overlap,
        Sweep
    }

    [System.Serializable]
    public sealed class HitData
    {
        [SerializeField, Min(0f)] private float _damage = 10f;
        [SerializeField] private AttackStrength _strength = AttackStrength.Light;
        [SerializeField] private LayerMask _targetMask = ~0;
        [SerializeField] private bool _includeTriggers = true;
        [SerializeField] private HitOrigin _origin = HitOrigin.CharacterRoot;
        [SerializeField] private HitOriginTracking _originTracking =
            HitOriginTracking.Follow;
        [SerializeField] private string _socket = "";
        [SerializeField] private string _effectKey = "";
        [SerializeField] private Vector3 _positionOffset;
        [SerializeField] private Vector3 _eulerOffset;
        [SerializeField] private HitShape _shape = HitShape.Sphere;
        [SerializeField, Min(0f)] private float _radius = 1.5f;
        [SerializeField, Range(0f, 360f)] private float _angle = 120f;
        [SerializeField] private Vector3 _boxSize = new Vector3(2f, 2f, 2f);
        [SerializeField, Min(0f)] private float _length = 4f;
        [SerializeField, Min(0f)] private float _startRadius;
        [SerializeField, Min(0f)] private float _endRadius = 5f;
        [SerializeField, Min(0.01f)] private float _duration = 1f;
        [SerializeField] private AnimationCurve _radiusCurve =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private HitQueryMode _queryMode = HitQueryMode.Overlap;
        [SerializeField] private HitFrequency _frequency = HitFrequency.OncePerActivation;
        [SerializeField, Min(0.01f)] private float _repeatInterval = 0.2f;
        [SerializeField] private bool _showGizmo = true;

        public float Damage { get => _damage; set => _damage = Mathf.Max(0f, value); }
        public AttackStrength Strength { get => _strength; set => _strength = value; }
        public LayerMask TargetMask { get => _targetMask; set => _targetMask = value; }
        public bool IncludeTriggers { get => _includeTriggers; set => _includeTriggers = value; }
        public QueryTriggerInteraction TriggerInteraction => _includeTriggers
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;
        public HitOrigin Origin { get => _origin; set => _origin = value; }
        public HitOriginTracking OriginTracking
        {
            get => _originTracking;
            set => _originTracking = value;
        }
        public string Socket { get => _socket; set => _socket = value ?? ""; }
        public string EffectKey
        {
            get => _effectKey;
            set => _effectKey = value?.Trim() ?? "";
        }
        public Vector3 PositionOffset { get => _positionOffset; set => _positionOffset = value; }
        public Vector3 EulerOffset { get => _eulerOffset; set => _eulerOffset = value; }
        public Quaternion RotationOffset => Quaternion.Euler(_eulerOffset);
        public HitShape Shape { get => _shape; set => _shape = value; }
        public float Radius { get => _radius; set => _radius = Mathf.Max(0f, value); }
        public float Angle { get => _angle; set => _angle = Mathf.Clamp(value, 0f, 360f); }
        public Vector3 BoxSize { get => _boxSize; set => _boxSize = Positive(value); }
        public float Length { get => _length; set => _length = Mathf.Max(0f, value); }
        public float StartRadius { get => _startRadius; set => _startRadius = Mathf.Max(0f, value); }
        public float EndRadius { get => _endRadius; set => _endRadius = Mathf.Max(_startRadius, value); }
        public float Duration { get => _duration; set => _duration = Mathf.Max(0.01f, value); }
        public AnimationCurve RadiusCurve
        {
            get => _radiusCurve;
            set => _radiusCurve = value ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
        }
        public HitQueryMode QueryMode { get => _queryMode; set => _queryMode = value; }
        public HitFrequency Frequency { get => _frequency; set => _frequency = value; }
        public float RepeatInterval
        {
            get => _repeatInterval;
            set => _repeatInterval = Mathf.Max(0.01f, value);
        }
        public bool ShowGizmo { get => _showGizmo; set => _showGizmo = value; }

        public HitData()
        {
        }

        public HitData(HitData source)
        {
            if (source == null) return;
            _damage = source._damage;
            _strength = source._strength;
            _targetMask = source._targetMask;
            _includeTriggers = source._includeTriggers;
            _origin = source._origin;
            _originTracking = source._originTracking;
            _socket = source._socket;
            _effectKey = source._effectKey;
            _positionOffset = source._positionOffset;
            _eulerOffset = source._eulerOffset;
            _shape = source._shape;
            _radius = source._radius;
            _angle = source._angle;
            _boxSize = source._boxSize;
            _length = source._length;
            _startRadius = source._startRadius;
            _endRadius = source._endRadius;
            _duration = source._duration;
            _radiusCurve = CloneCurve(source._radiusCurve);
            _queryMode = source._queryMode;
            _frequency = source._frequency;
            _repeatInterval = source._repeatInterval;
            _showGizmo = source._showGizmo;
        }

        internal HitData(HitDefinition source)
        {
            if (source == null) return;
            _damage = source.Damage;
            _strength = source.Strength;
            _targetMask = source.TargetMask;
            _includeTriggers = source.IncludeTriggers;
            _origin = source.Origin;
            _originTracking = source.OriginTracking;
            _socket = source.Socket;
            _effectKey = source.EffectKey;
            _positionOffset = source.PositionOffset;
            _eulerOffset = source.EulerOffset;
            _shape = source.Shape;
            _radius = source.Radius;
            _angle = source.Angle;
            _boxSize = source.BoxSize;
            _length = source.Length;
            _startRadius = source.StartRadius;
            _endRadius = source.EndRadius;
            _duration = source.Duration;
            _radiusCurve = CloneCurve(source.RadiusCurve);
            _queryMode = source.QueryMode;
            _frequency = source.Frequency;
            _repeatInterval = source.RepeatInterval;
            _showGizmo = source.ShowGizmo;
        }

        public float EvaluateRadius(float normalizedProgress)
        {
            float t = _radiusCurve != null
                ? _radiusCurve.Evaluate(Mathf.Clamp01(normalizedProgress))
                : normalizedProgress;
            return Mathf.LerpUnclamped(_startRadius, _endRadius, t);
        }

        public void Validate()
        {
            _damage = Mathf.Max(0f, _damage);
            _radius = Mathf.Max(0f, _radius);
            _angle = Mathf.Clamp(_angle, 0f, 360f);
            _boxSize = Positive(_boxSize);
            _length = Mathf.Max(0f, _length);
            _startRadius = Mathf.Max(0f, _startRadius);
            _endRadius = Mathf.Max(_startRadius, _endRadius);
            _duration = Mathf.Max(0.01f, _duration);
            _repeatInterval = Mathf.Max(0.01f, _repeatInterval);
        }

        private static Vector3 Positive(Vector3 value) => new Vector3(
            Mathf.Max(0f, value.x), Mathf.Max(0f, value.y), Mathf.Max(0f, value.z));

        private static AnimationCurve CloneCurve(AnimationCurve source)
        {
            if (source == null) return AnimationCurve.Linear(0f, 0f, 1f, 1f);
            var clone = new AnimationCurve(source.keys)
            {
                preWrapMode = source.preWrapMode,
                postWrapMode = source.postWrapMode,
            };
            return clone;
        }
    }

    // 이전 HitDefinition 에셋을 읽어 Payload로 변환하기 위한 호환 타입이다.
    public sealed class HitDefinition : ScriptableObject
    {
        [Header("Payload")]
        [SerializeField, Min(0f)] private float _damage = 10f;
        [SerializeField] private AttackStrength _strength = AttackStrength.Light;

        [Header("Target")]
        [SerializeField] private LayerMask _targetMask = ~0;
        [SerializeField] private bool _includeTriggers = true;

        [Header("Origin")]
        [SerializeField] private HitOrigin _origin = HitOrigin.CharacterRoot;
        [SerializeField] private HitOriginTracking _originTracking =
            HitOriginTracking.Follow;
        [SerializeField] private string _socket = "";
        [SerializeField] private string _effectKey = "";
        [SerializeField] private Vector3 _positionOffset;
        [SerializeField] private Vector3 _eulerOffset;

        [Header("Shape")]
        [SerializeField] private HitShape _shape = HitShape.Sphere;
        [SerializeField, Min(0f)] private float _radius = 1.5f;
        [SerializeField, Range(0f, 360f)] private float _angle = 120f;
        [SerializeField] private Vector3 _boxSize = new Vector3(2f, 2f, 2f);
        [SerializeField, Min(0f)] private float _length = 4f;
        [SerializeField, Min(0f)] private float _startRadius;
        [SerializeField, Min(0f)] private float _endRadius = 5f;
        [SerializeField, Min(0.01f)] private float _duration = 1f;
        [SerializeField] private AnimationCurve _radiusCurve =
            AnimationCurve.Linear(0f, 0f, 1f, 1f);

        [Header("Frequency")]
        [SerializeField] private HitQueryMode _queryMode = HitQueryMode.Overlap;
        [SerializeField] private HitFrequency _frequency = HitFrequency.OncePerActivation;
        [SerializeField, Min(0.01f)] private float _repeatInterval = 0.2f;
        [SerializeField] private bool _showGizmo = true;

        public float Damage => _damage;
        public AttackStrength Strength => _strength;
        public LayerMask TargetMask => _targetMask;
        public bool IncludeTriggers => _includeTriggers;
        public QueryTriggerInteraction TriggerInteraction => _includeTriggers
            ? QueryTriggerInteraction.Collide
            : QueryTriggerInteraction.Ignore;
        public HitOrigin Origin => _origin;
        public HitOriginTracking OriginTracking => _originTracking;
        public string Socket => _socket;
        public string EffectKey => _effectKey;
        public Vector3 PositionOffset => _positionOffset;
        public Vector3 EulerOffset => _eulerOffset;
        public Quaternion RotationOffset => Quaternion.Euler(_eulerOffset);
        public HitShape Shape => _shape;
        public float Radius => _radius;
        public float Angle => _angle;
        public Vector3 BoxSize => _boxSize;
        public float Length => _length;
        public float StartRadius => _startRadius;
        public float EndRadius => _endRadius;
        public float Duration => _duration;
        public AnimationCurve RadiusCurve => _radiusCurve;
        public HitQueryMode QueryMode => _queryMode;
        public HitFrequency Frequency => _frequency;
        public float RepeatInterval => _repeatInterval;
        public bool ShowGizmo => _showGizmo;

        public HitData CreateDataCopy() => new HitData(this);

        public float EvaluateRadius(float normalizedProgress)
        {
            float t = _radiusCurve != null
                ? _radiusCurve.Evaluate(Mathf.Clamp01(normalizedProgress))
                : normalizedProgress;
            return Mathf.LerpUnclamped(_startRadius, _endRadius, t);
        }

        private void OnValidate()
        {
            _boxSize.x = Mathf.Max(0f, _boxSize.x);
            _boxSize.y = Mathf.Max(0f, _boxSize.y);
            _boxSize.z = Mathf.Max(0f, _boxSize.z);
            _endRadius = Mathf.Max(_startRadius, _endRadius);
        }
    }
}
