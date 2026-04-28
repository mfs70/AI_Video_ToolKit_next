using System;
using NAudio.Wave;

namespace AI_Video_ToolKit.UI.Services
{
    public class AudioPlayerService
    {
        private WaveOutEvent? _output;
        private AudioFileReader? _reader;

        public void Play(string file, double startSeconds)
        {
            Stop();

            _reader = new AudioFileReader(file);
            _reader.CurrentTime = TimeSpan.FromSeconds(startSeconds);

            _output = new WaveOutEvent();
            _output.Init(_reader);
            _output.Play();
        }

        public void Pause()
        {
            _output?.Pause();
        }

        public void Resume()
        {
            _output?.Play();
        }

        public void Stop()
        {
            _output?.Stop();
            _output?.Dispose();
            _output = null;

            _reader?.Dispose();
            _reader = null;
        }

        public double GetPosition()
        {
            return _reader?.CurrentTime.TotalSeconds ?? 0;
        }
    }
}