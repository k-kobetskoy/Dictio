using System.IO;
using NAudio.Wave;

namespace Dictio.Services;

public class AudioRecorderService : IDisposable
{
    private WaveInEvent? _waveIn;
    private MemoryStream? _buffer;
    private WaveFileWriter? _writer;

    public bool IsRecording => _waveIn != null;

    public void Start(int deviceIndex = 0)
    {
        _buffer = new MemoryStream();
        _waveIn = new WaveInEvent { WaveFormat = new WaveFormat(16000, 1), DeviceNumber = deviceIndex };
        _writer = new WaveFileWriter(_buffer, _waveIn.WaveFormat);
        _waveIn.DataAvailable += (_, e) => _writer.Write(e.Buffer, 0, e.BytesRecorded);
        _waveIn.StartRecording();
    }

    public MemoryStream Stop()
    {
        if (_waveIn == null) return new MemoryStream();

        using var done = new System.Threading.ManualResetEventSlim();
        _waveIn.RecordingStopped += (_, _) => done.Set();
        _waveIn.StopRecording();
        done.Wait(TimeSpan.FromSeconds(3));

        _waveIn.Dispose();
        _waveIn = null;
        _writer?.Dispose(); // finalizes WAV header, closes _buffer
        _writer = null;
        var data = _buffer!.ToArray(); // ToArray() works on closed MemoryStream
        _buffer = null;
        return new MemoryStream(data);
    }

    public void Dispose()
    {
        _waveIn?.Dispose();
        _writer?.Dispose();
        _buffer?.Dispose();
    }
}