using System.IO;
using NAudio.Wave;

namespace Dictio.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;
    private int _chunkCount;
    private long _totalBytesFromCallback;

    public bool IsRecording => _waveIn != null;

    // Fires at ~10 Hz (each NAudio buffer callback) with RMS normalized to [0, 1].
    public event Action<float>? LevelChanged;

    public void Start(int deviceIndex = 0)
    {
        _chunkCount = 0;
        _totalBytesFromCallback = 0;
        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1), DeviceNumber = deviceIndex };
        Logger.Log($"[Audio] WaveIn BufferMilliseconds={_waveIn.BufferMilliseconds}, NumberOfBuffers={_waveIn.NumberOfBuffers}");
        _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) =>
        {
            _chunkCount++;
            _totalBytesFromCallback += e.BytesRecorded;
            _writer.Write(e.Buffer, 0, e.BytesRecorded);
            LevelChanged?.Invoke(ComputeRms(e.Buffer, e.BytesRecorded));
        };
        _waveIn.StartRecording();
    }

    // RMS of 16-bit signed PCM, normalized to [0, 1].
    private static float ComputeRms(byte[] buffer, int bytesRecorded)
    {
        int samples = bytesRecorded / 2;
        if (samples == 0) return 0f;
        double sum = 0;
        for (int i = 0; i < bytesRecorded; i += 2)
        {
            short s = (short)(buffer[i] | (buffer[i + 1] << 8));
            double v = s / 32768.0;
            sum += v * v;
        }
        return (float)Math.Sqrt(sum / samples);
    }

    public MemoryStream Stop()
    {
        if (_waveIn == null) return new MemoryStream();

        using var done = new System.Threading.ManualResetEventSlim();
        _waveIn.RecordingStopped += (_, _) => done.Set();
        _waveIn.StopRecording();
        bool signaled = done.Wait(TimeSpan.FromSeconds(3));
        Logger.Log($"[Audio] RecordingStopped signaled={signaled}, chunks={_chunkCount}, bytesFromCallbacks={_totalBytesFromCallback}");

        _waveIn.Dispose();
        _waveIn = null;
        _writer?.Dispose(); // finalizes WAV header
        _writer = null;
        var data = _buffer!.ToArray();
        _buffer = null;
        Logger.Log($"[Audio] Final WAV stream size={data.Length} bytes (header~44, PCM={(data.Length - 44)} bytes)");
        return new MemoryStream(data);
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
    }
}