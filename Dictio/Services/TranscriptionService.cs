#pragma warning disable OPENAI001
using System.ClientModel;
using System.IO;
using System.Text.Json;
using OpenAI;
using OpenAI.Audio;

namespace Dictio.Services;

public class TranscriptionService
{
    private const string ModelId = "gpt-4o-transcribe";

    // Transcriptions where the average token log-probability falls below this
    // value are silently discarded — they indicate silence, noise, or garbled input.
    // logprob = 0 means 100% confidence; -2.5 ≈ 8% average per token.
    private const double LogprobRejectThreshold = -2.5;

    private readonly Func<string> _getApiKey;

    public TranscriptionService(Func<string> getApiKey)
    {
        _getApiKey = getApiKey;
    }

    // Returns null when the transcription should be discarded (low confidence / silence).
    public async Task<string?> TranscribeAsync(MemoryStream audioStream, string? prompt = null)
    {
        var client = new OpenAIClient(_getApiKey());
        var audioClient = client.GetAudioClient(ModelId);
        audioStream.Position = 0;

        var options = new AudioTranscriptionOptions
        {
            Includes = AudioTranscriptionIncludes.Logprobs,
        };
        if (!string.IsNullOrWhiteSpace(prompt))
            options.Prompt = prompt;

        var result = await audioClient.TranscribeAudioAsync(audioStream, "audio.wav", options);

        var text = result.Value.Text;
        LogAndFilterLogprobs(result, text);

        // Re-read average from raw JSON to decide whether to accept
        double? avg = GetAverageLogprob(result.GetRawResponse().Content.ToString());
        if (avg.HasValue && avg.Value < LogprobRejectThreshold)
        {
            Logger.Log($"Transcription rejected: avg_logprob={avg.Value:F3} < threshold {LogprobRejectThreshold}. Text was: \"{text}\"");
            return null;
        }

        return text;
    }

    private static void LogAndFilterLogprobs(ClientResult<AudioTranscription> result, string text)
    {
        try
        {
            var json = result.GetRawResponse().Content.ToString();
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("logprobs", out var logprobsEl) ||
                logprobsEl.ValueKind != JsonValueKind.Array)
            {
                Logger.Log("Logprobs: not present in response");
                return;
            }

            double sum = 0;
            int count = 0;
            var lines = new System.Text.StringBuilder();
            foreach (var entry in logprobsEl.EnumerateArray())
            {
                var token = entry.TryGetProperty("token", out var t) ? t.GetString() : "?";
                var lp = entry.TryGetProperty("logprob", out var l) ? l.GetDouble() : 0;
                sum += lp;
                count++;
                lines.Append($"  [{token}]={lp:F3}");
            }

            double avg = count > 0 ? sum / count : 0;
            Logger.Log($"Logprobs: {count} tokens, avg={avg:F3} | text=\"{text}\"");
            if (count > 0)
                Logger.Log(lines.ToString());
        }
        catch (Exception ex)
        {
            Logger.Log($"Logprobs parse error: {ex.Message}");
        }
    }

    private static double? GetAverageLogprob(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("logprobs", out var arr) ||
                arr.ValueKind != JsonValueKind.Array)
                return null;

            double sum = 0;
            int count = 0;
            foreach (var entry in arr.EnumerateArray())
            {
                if (entry.TryGetProperty("logprob", out var lp))
                {
                    sum += lp.GetDouble();
                    count++;
                }
            }
            return count > 0 ? sum / count : null;
        }
        catch { return null; }
    }
}
