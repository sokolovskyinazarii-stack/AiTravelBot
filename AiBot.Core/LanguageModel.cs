using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiBot.Core;

public sealed record LanguageModelResult(bool Success, string Text, string Provider, string? Error = null)
{
    public static LanguageModelResult Unavailable(string provider, string error)
    {
        return new LanguageModelResult(false, string.Empty, provider, error);
    }
}

public interface ILanguageModelClient
{
    Task<LanguageModelResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default);
}

public sealed class OpenAiResponsesLanguageModelClient : ILanguageModelClient
{
    private const string DefaultBaseUrl = "https://api.openai.com/v1/";
    private const string DefaultModel = "gpt-5.4-mini";
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;

    public OpenAiResponsesLanguageModelClient(
        HttpClient? httpClient = null,
        string? baseUrl = null,
        string? model = null,
        string? apiKey = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _apiKey = FirstNotEmptyOrNull(
            apiKey,
            OpenAiLocalConfig.ApiKey,
            Environment.GetEnvironmentVariable("OPENAI_API_KEY"));
        _model = FirstNotEmpty(
            model,
            OpenAiLocalConfig.Model,
            Environment.GetEnvironmentVariable("TRAVEL_BOT_OPENAI_MODEL"),
            Environment.GetEnvironmentVariable("OPENAI_MODEL"),
            DefaultModel);

        var resolvedBaseUrl = FirstNotEmpty(
            baseUrl,
            Environment.GetEnvironmentVariable("TRAVEL_BOT_OPENAI_URL"),
            DefaultBaseUrl);

        if (_httpClient.BaseAddress is null)
        {
            _httpClient.BaseAddress = new Uri($"{resolvedBaseUrl.TrimEnd('/')}/");
        }
    }

    public async Task<LanguageModelResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            return LanguageModelResult.Unavailable(ProviderName, "OPENAI_API_KEY is not set");
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "responses")
            {
                Content = JsonContent.Create(BuildRequest(prompt))
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            using var response = await _httpClient.SendAsync(request, timeout.Token);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(timeout.Token);
                return LanguageModelResult.Unavailable(ProviderName, $"HTTP {(int)response.StatusCode}: {TrimError(error)}");
            }

            var payload = await response.Content.ReadAsStringAsync(timeout.Token);
            var text = ExtractOutputText(payload)?.Trim();
            return string.IsNullOrWhiteSpace(text)
                ? LanguageModelResult.Unavailable(ProviderName, "empty response")
                : new LanguageModelResult(true, text, ProviderName);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return LanguageModelResult.Unavailable(ProviderName, "timeout");
        }
        catch (HttpRequestException ex)
        {
            return LanguageModelResult.Unavailable(ProviderName, ex.Message);
        }
        catch (TaskCanceledException)
        {
            return LanguageModelResult.Unavailable(ProviderName, "timeout");
        }
    }

    private string ProviderName => $"OpenAI/{_model}";

    private object BuildRequest(string prompt)
    {
        return new
        {
            model = _model,
            instructions = BuildSystemInstructions(),
            input = prompt,
            text = new
            {
                format = new
                {
                    type = "json_schema",
                    name = "travel_bot_response",
                    strict = true,
                    schema = BuildSchema()
                }
            },
            max_output_tokens = 900,
            store = false
        };
    }

    private static string BuildSystemInstructions()
    {
        return """
        You are Travel Advisor Bot, a Ukrainian-language tourism consultant.
        Work only with travel topics: destinations, routes, local attractions, budgets, booking, documents, transport, season, weather, lodging, safety, and travel planning.
        If the user asks about any unrelated topic, set is_travel_related=false, intent=Fallback, entities=[], and reply in Ukrainian that you only help with travel planning.
        Extract intent and entities yourself. Do not rely on keyword confidence from the app. Keep Ukrainian place names exactly, including multi-word names.
        For a clear travel-related request, set confidence between 0.75 and 1.0. Use lower confidence only when the message is truly ambiguous.
        Reply in Ukrainian, 3-5 concise practical sentences. For visas/documents/rules, advise checking official sources before payment.
        """;
    }

    private static object BuildSchema()
    {
        return new
        {
            type = "object",
            additionalProperties = false,
            properties = new Dictionary<string, object>
            {
                ["is_travel_related"] = new { type = "boolean" },
                ["intent"] = new
                {
                    type = "string",
                    @enum = Enum.GetNames<IntentType>()
                },
                ["confidence"] = new { type = "number", minimum = 0, maximum = 1 },
                ["entities"] = new
                {
                    type = "array",
                    items = new
                    {
                        type = "object",
                        additionalProperties = false,
                        properties = new Dictionary<string, object>
                        {
                            ["type"] = new
                            {
                                type = "string",
                                @enum = new[]
                                {
                                    "destination",
                                    "budget",
                                    "people_count",
                                    "transport",
                                    "travel_style",
                                    "document",
                                    "date"
                                }
                            },
                            ["value"] = new { type = "string" },
                            ["source"] = new { type = "string" }
                        },
                        required = new[] { "type", "value", "source" }
                    }
                },
                ["reply"] = new { type = "string" }
            },
            required = new[] { "is_travel_related", "intent", "confidence", "entities", "reply" }
        };
    }

    private static string? ExtractOutputText(string payload)
    {
        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;

        if (root.TryGetProperty("output_text", out var outputText) && outputText.ValueKind == JsonValueKind.String)
        {
            return outputText.GetString();
        }

        if (!root.TryGetProperty("output", out var output) || output.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var builder = new StringBuilder();
        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var text)
                    && text.ValueKind == JsonValueKind.String)
                {
                    builder.Append(text.GetString());
                }
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string FirstNotEmpty(params string?[] values)
    {
        return values.First(value => !string.IsNullOrWhiteSpace(value))!;
    }

    private static string? FirstNotEmptyOrNull(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }

    private static string TrimError(string error)
    {
        return error.Length <= 220 ? error : string.Concat(error.AsSpan(0, 220), "...");
    }
}
