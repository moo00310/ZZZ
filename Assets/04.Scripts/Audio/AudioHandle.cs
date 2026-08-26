namespace ZZZ.Audio
{
    public sealed class AudioHandle
    {
        private AudioServiceRunner _runner;
        private int _voiceIndex = -1;
        private int _bindingVersion;
        private bool _stopped;

        internal bool IsStopped => _stopped;

        internal void Bind(
            AudioServiceRunner runner, int voiceIndex, int bindingVersion)
        {
            if (_stopped)
            {
                runner.StopVoice(voiceIndex, bindingVersion);
                return;
            }

            _runner = runner;
            _voiceIndex = voiceIndex;
            _bindingVersion = bindingVersion;
        }

        internal void Detach(
            AudioServiceRunner runner, int voiceIndex, int bindingVersion)
        {
            if (_runner != runner || _voiceIndex != voiceIndex
                || _bindingVersion != bindingVersion) return;

            _runner = null;
            _voiceIndex = -1;
            _bindingVersion = 0;
            _stopped = true;
        }

        public void Stop()
        {
            if (_stopped) return;
            _stopped = true;

            AudioServiceRunner runner = _runner;
            int voiceIndex = _voiceIndex;
            int bindingVersion = _bindingVersion;
            _runner = null;
            _voiceIndex = -1;
            _bindingVersion = 0;
            if (runner != null)
                runner.StopVoice(voiceIndex, bindingVersion);
        }
    }
}
