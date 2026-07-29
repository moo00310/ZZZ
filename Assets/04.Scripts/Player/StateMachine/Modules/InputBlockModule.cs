namespace ZZZ
{
    [System.Serializable]
    public class InputBlockModule : WindowModule
    {
        public ComboInput Input = ComboInput.Enhance;

        public override bool BlocksInput(TrackClip tc, float nt, ComboInput input)
            => (Input == ComboInput.Any || Input == input) && InWindow(tc, nt);

        public override string MenuName => "입력 차단";
        public override string DisplayName => $"{Input} 차단 · {Start:0.##}~{End:0.##}";
    }
}
