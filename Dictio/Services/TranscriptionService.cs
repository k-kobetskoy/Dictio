using System.IO;
using OpenAI;
using OpenAI.Audio;

namespace Dictio.Services;

public class TranscriptionService
{
    private readonly Func<string> _getApiKey;
    private readonly Func<string> _getModelId;

    public TranscriptionService(Func<string> getApiKey, Func<string> getModelId)
    {
        _getApiKey = getApiKey;
        _getModelId = getModelId;
    }

    public async Task<string> TranscribeAsync(MemoryStream audioStream)
    {
        var client = new OpenAIClient(_getApiKey());
        var audioClient = client.GetAudioClient(_getModelId());
        audioStream.Position = 0;
        var result = await audioClient.TranscribeAudioAsync(audioStream, "audio.wav");
        return result.Value.Text;
    }
}