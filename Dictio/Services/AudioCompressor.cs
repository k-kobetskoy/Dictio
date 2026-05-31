using Concentus;
using Concentus.Enums;
using Concentus.Oggfile;
using System.IO;

namespace Dictio.Services;

public static class AudioCompressor
{
    private const int SampleRate = 16_000;
    private const int Channels   = 1;

    // Encodes a 16 kHz / mono / 16-bit signed PCM WAV stream to Ogg/Opus.
    // bitrate: bits per second (24 000 ≈ 10× compression vs raw WAV).
    // The returned stream is positioned at 0.
    public static MemoryStream ToOggOpus(MemoryStream wav, int bitrate = 24_000)
    {
        // Skip the standard 44-byte WAV header to reach raw PCM data.
        const int WavHeaderSize = 44;
        int pcmByteCount = (int)(wav.Length - WavHeaderSize);
        var pcmBytes = new byte[pcmByteCount];
        wav.Position = WavHeaderSize;
        _ = wav.Read(pcmBytes, 0, pcmByteCount);

        int sampleCount = pcmByteCount / 2;
        var samples = new short[sampleCount];
        Buffer.BlockCopy(pcmBytes, 0, samples, 0, pcmByteCount);

        using var encoder = OpusCodecFactory.CreateEncoder(SampleRate, Channels, OpusApplication.OPUS_APPLICATION_VOIP);
        encoder.Bitrate = bitrate;

        var output = new MemoryStream();
        var oggStream = new OpusOggWriteStream(encoder, output, null, SampleRate);
        oggStream.WriteSamples(samples, 0, sampleCount);
        oggStream.Finish();

        output.Position = 0;
        return output;
    }
}
