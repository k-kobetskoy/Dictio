#pragma warning disable OPENAI001
using System.ClientModel;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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

    private static readonly HttpClient _httpClient = new();

    private readonly Func<string> _getApiKey;

    public TranscriptionService(Func<string> getApiKey)
    {
        _getApiKey = getApiKey;
    }

    // Returns null when the transcription should be discarded (low confidence / silence).
    // prefixPaddingMs / silenceDurationMs / temperature: when any is set, sends a raw HTTP request
    // instead of using the SDK (SDK 2.10.0 doesn't expose chunking_strategy or temperature).
    public async Task<string?> TranscribeAsync(MemoryStream audioStream, string? prompt = null,
        int? prefixPaddingMs = null, int? silenceDurationMs = null, float? temperature = null,
        string? language = null)
    {
        if (prefixPaddingMs.HasValue || silenceDurationMs.HasValue || (temperature.HasValue && temperature.Value > 0))
            return await TranscribeRawAsync(audioStream, prompt, prefixPaddingMs, silenceDurationMs, temperature, language);

        var client = new OpenAIClient(_getApiKey());
        var audioClient = client.GetAudioClient(ModelId);
        audioStream.Position = 0;

        var options = new AudioTranscriptionOptions
        {
            Includes = AudioTranscriptionIncludes.Logprobs,
        };
        if (!string.IsNullOrWhiteSpace(prompt))
            options.Prompt = prompt;
        if (!string.IsNullOrWhiteSpace(language))
            options.Language = language;

        var result = await audioClient.TranscribeAudioAsync(audioStream, "audio.wav", options);

        var text = result.Value.Text;
        var json = result.GetRawResponse().Content.ToString();
        LogAndFilterLogprobs(json, text);

        double? avg = GetAverageLogprob(json);
        if (avg.HasValue && avg.Value < LogprobRejectThreshold)
        {
            Logger.Log($"Transcription rejected: avg_logprob={avg.Value:F3} < threshold {LogprobRejectThreshold}. Text was: \"{text}\"");
            return null;
        }

        return text;
    }

    private async Task<string?> TranscribeRawAsync(MemoryStream audioStream, string? prompt,
        int? prefixPaddingMs, int? silenceDurationMs, float? temperature, string? language)
    {
        audioStream.Position = 0;

        using var form = new MultipartFormDataContent();

        var audioContent = new StreamContent(audioStream);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audioContent, "file", "audio.wav");
        form.Add(new StringContent(ModelId), "model");
        form.Add(new StringContent("logprobs"), "include[]");
        if (!string.IsNullOrWhiteSpace(prompt))
            form.Add(new StringContent(prompt), "prompt");
        if (!string.IsNullOrWhiteSpace(language))
            form.Add(new StringContent(language), "language");
        if (temperature.HasValue)
            form.Add(new StringContent(temperature.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture)), "temperature");

        if (prefixPaddingMs.HasValue || silenceDurationMs.HasValue)
        {
            var vad = new Dictionary<string, object?> { ["type"] = "server_vad" };
            if (prefixPaddingMs.HasValue) vad["prefix_padding_ms"] = prefixPaddingMs.Value;
            if (silenceDurationMs.HasValue) vad["silence_duration_ms"] = silenceDurationMs.Value;
            form.Add(new StringContent(JsonSerializer.Serialize(vad)), "chunking_strategy");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/audio/transcriptions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _getApiKey());
        request.Content = form;

        var response = await _httpClient.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Transcription API error {(int)response.StatusCode}: {json}");

        using var doc = JsonDocument.Parse(json);
        var text = doc.RootElement.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";

        LogAndFilterLogprobs(json, text);

        double? avg = GetAverageLogprob(json);
        if (avg.HasValue && avg.Value < LogprobRejectThreshold)
        {
            Logger.Log($"Transcription rejected: avg_logprob={avg.Value:F3} < threshold {LogprobRejectThreshold}. Text was: \"{text}\"");
            return null;
        }

        return text;
    }

    private static void LogAndFilterLogprobs(string json, string text)
    {
        try
        {
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
                var token = entry.TryGetProperty("token", out var tEl) ? tEl.GetString() : "?";
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
