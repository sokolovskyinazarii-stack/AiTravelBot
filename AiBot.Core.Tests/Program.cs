using AiBot.Core;
using System.Net;
using System.Text;
using System.Text.Json;

var tests = new (string Name, Func<Task> Body)[]
{
    ("dataset has 7 balanced tourism intents with at least 10 examples", Sync(DatasetIsBalanced)),
    ("classifier recognizes destination advice request", Sync(RecognizesDestinationAdvice)),
    ("classifier recognizes budget estimate request", Sync(RecognizesBudgetEstimate)),
    ("extractor finds destination, budget and people count", Sync(ExtractsEntities)),
    ("extractor recognizes nearby Drohobych request", Sync(ExtractsNearbyDrohobych)),
    ("extractor keeps multi-word nearby place names", Sync(ExtractsMultiWordNearbyPlace)),
    ("extractor keeps non-city place names", Sync(ExtractsNonCityPlace)),
    ("bot keeps destination and budget context between turns", Sync(KeepsContext)),
    ("bot does not reuse stale destination for nearby request", Sync(DoesNotReuseStaleDestinationForNearbyRequest)),
    ("bot uses language model intent entities and response when available", UsesLanguageModelAnalysisWhenAvailable),
    ("bot refuses non-tourism topics through language model guardrail", RefusesNonTourismTopicsThroughLanguageModel),
    ("openai client calls v1 responses endpoint", OpenAiClientCallsResponsesEndpoint),
    ("openai client can use code api key config", OpenAiClientCanUseCodeApiKeyConfig),
    ("bot local fallback recognizes regional recommendation request", Sync(LocalFallbackRecognizesRegionalRecommendation)),
    ("bot falls back when language model is unavailable", FallsBackWhenLanguageModelUnavailable)
};

foreach (var test in tests)
{
    await test.Body();
    Console.WriteLine($"PASS {test.Name}");
}

Console.WriteLine("AiBot.Core.Tests passed");

static Func<Task> Sync(Action action)
{
    return () =>
    {
        action();
        return Task.CompletedTask;
    };
}

static void DatasetIsBalanced()
{
    var groups = TrainingDataset.Examples
        .GroupBy(example => example.Intent)
        .ToDictionary(group => group.Key, group => group.Count());

    Assert(groups.Count == 7, $"Expected 7 intents, got {groups.Count}");
    foreach (var (intent, count) in groups)
    {
        Assert(count >= 10, $"{intent} has only {count} examples");
    }
}

static void RecognizesDestinationAdvice()
{
    var classifier = new NaiveBayesIntentClassifier(TrainingDataset.Examples);
    var prediction = classifier.Predict("порадь країну для пляжного відпочинку у вересні");

    Assert(prediction.Intent == IntentType.DestinationAdvice, $"Expected DestinationAdvice, got {prediction.Intent}");
    Assert(prediction.Confidence > 0.30, $"Confidence too low: {prediction.Confidence}");
}

static void RecognizesBudgetEstimate()
{
    var classifier = new NaiveBayesIntentClassifier(TrainingDataset.Examples);
    var prediction = classifier.Predict("розрахуй бюджет на двох у Стамбул до 500 євро");

    Assert(prediction.Intent == IntentType.BudgetEstimate, $"Expected BudgetEstimate, got {prediction.Intent}");
}

static void ExtractsEntities()
{
    var entities = EntityExtractor.Extract("Розрахуй бюджет на 2 людей у Стамбул до 500 євро літаком");

    Assert(entities.Any(entity => entity.Type == "destination" && entity.Value == "Стамбул"), "Destination was not extracted");
    Assert(entities.Any(entity => entity.Type == "budget" && entity.Value.Contains("500", StringComparison.OrdinalIgnoreCase)), "Budget was not extracted");
    Assert(entities.Any(entity => entity.Type == "people_count" && entity.Value.Contains("2", StringComparison.OrdinalIgnoreCase)), "People count was not extracted");
    Assert(entities.Any(entity => entity.Type == "transport" && entity.Value == "літак"), "Transport was not extracted");
}

static void ExtractsNearbyDrohobych()
{
    var entities = EntityExtractor.Extract("Порекомендуй щось неподалік Дрогобича");

    Assert(entities.Any(entity => entity.Type == "destination" && entity.Value == "Дрогобич"), "Drohobych was not extracted as destination");
    Assert(entities.Any(entity => entity.Type == "travel_style" && entity.Value == "поруч"), "Nearby travel style was not extracted");
}

static void ExtractsMultiWordNearbyPlace()
{
    var entities = EntityExtractor.Extract("А що порекомендуєш неподалік Нижніх Гаїв");

    Assert(entities.Any(entity => entity.Type == "destination" && entity.Value == "Нижніх Гаїв"), "Multi-word place was not preserved");
}

static void ExtractsNonCityPlace()
{
    var entities = EntityExtractor.Extract("Що подивитися біля озера Синевир");

    Assert(entities.Any(entity => entity.Type == "destination" && entity.Value == "Синевир"), "Non-city place was not extracted");
}

static void KeepsContext()
{
    var bot = new TravelBotEngine();
    bot.Reply("Привіт, хочу поїхати у Париж");
    var response = bot.Reply("порахуй бюджет до 500 євро");

    Assert(response.Context.CurrentDestination == "Париж", $"Expected Париж, got {response.Context.CurrentDestination}");
    Assert(response.Context.CurrentBudget.Contains("500", StringComparison.OrdinalIgnoreCase), "Budget context was not preserved");
    Assert(response.Prediction.Intent == IntentType.BudgetEstimate, $"Expected BudgetEstimate, got {response.Prediction.Intent}");
}

static void DoesNotReuseStaleDestinationForNearbyRequest()
{
    var bot = new TravelBotEngine(new NaiveBayesIntentClassifier(TrainingDataset.Examples));
    bot.Reply("Привіт, хочу поїхати у Париж");
    var response = bot.Reply("Порекомендуй щось неподалік Дрогобича");

    Assert(response.Context.CurrentDestination == "Дрогобич", $"Expected Дрогобич, got {response.Context.CurrentDestination}");
    Assert(response.Reply.Contains("Трускавець", StringComparison.OrdinalIgnoreCase), "Nearby recommendations should mention Truskavets");
    Assert(!response.Reply.Contains("Париж", StringComparison.OrdinalIgnoreCase), "Response should not reuse stale Paris context");
}

static async Task UsesLanguageModelAnalysisWhenAvailable()
{
    var classifier = new NaiveBayesIntentClassifier(TrainingDataset.Examples);
    var bot = new TravelBotEngine(classifier, new FakeLanguageModelClient(new LanguageModelResult(
        true,
        """
        {
          "is_travel_related": true,
          "intent": "DestinationAdvice",
          "confidence": 0.97,
          "entities": [
            { "type": "destination", "value": "Львівська область", "source": "Львівській області" }
          ],
          "reply": "У Львівській області варто розглянути Львів, Дрогобич, Трускавець, Східницю, Жовкву та Тустань."
        }
        """,
        "FakeModel")));

    var response = await bot.ReplyAsync("Порекомендуй щось у Львівській області");

    Assert(response.Prediction.Intent == IntentType.DestinationAdvice, $"Expected LLM DestinationAdvice, got {response.Prediction.Intent}");
    Assert(response.Prediction.Confidence > 0.90, $"Expected high LLM confidence, got {response.Prediction.Confidence}");
    Assert(response.Context.CurrentDestination == "Львівська область", $"Expected Львівська область, got {response.Context.CurrentDestination}");
    Assert(response.Reply.Contains("Тустань", StringComparison.OrdinalIgnoreCase), "Language model travel response was not used");
    Assert(response.Trace.Any(line => line.Contains("LLM: FakeModel", StringComparison.OrdinalIgnoreCase)), "LLM trace line is missing");
}

static async Task RefusesNonTourismTopicsThroughLanguageModel()
{
    var classifier = new NaiveBayesIntentClassifier(TrainingDataset.Examples);
    var bot = new TravelBotEngine(classifier, new FakeLanguageModelClient(new LanguageModelResult(
        true,
        """
        {
          "is_travel_related": false,
          "intent": "Fallback",
          "confidence": 1.0,
          "entities": [],
          "reply": "Я можу допомагати лише з подорожами: напрямами, маршрутами, бюджетом, бронюванням, документами та сезоном."
        }
        """,
        "FakeModel")));

    var response = await bot.ReplyAsync("Напиши код сортування масиву");

    Assert(response.Prediction.Intent == IntentType.Fallback, $"Expected Fallback, got {response.Prediction.Intent}");
    Assert(response.Reply.Contains("лише з подорожами", StringComparison.OrdinalIgnoreCase), "Non-tourism guardrail response was not used");
}

static async Task OpenAiClientCallsResponsesEndpoint()
{
    var responsePayload = JsonSerializer.Serialize(new
    {
        output_text = """
        {
          "is_travel_related": true,
          "intent": "DestinationAdvice",
          "confidence": 0.98,
          "entities": [],
          "reply": "Можу допомогти підібрати туристичний напрям."
        }
        """
    });

    var handler = new RecordingHttpMessageHandler(responsePayload);
    var httpClient = new HttpClient(handler);
    var client = new OpenAiResponsesLanguageModelClient(
        httpClient,
        baseUrl: "https://api.openai.com/v1",
        model: "test-model",
        apiKey: "test-key");

    var result = await client.GenerateAsync("порадь напрям");

    Assert(result.Success, $"Expected successful fake OpenAI response, got {result.Error}");
    Assert(handler.RequestUri?.AbsoluteUri == "https://api.openai.com/v1/responses",
        $"Expected /v1/responses endpoint, got {handler.RequestUri}");
    Assert(handler.Authorization == "Bearer test-key", "Authorization header was not set");
}

static async Task OpenAiClientCanUseCodeApiKeyConfig()
{
    var previousApiKey = OpenAiLocalConfig.ApiKey;
    var previousModel = OpenAiLocalConfig.Model;
    OpenAiLocalConfig.ApiKey = "code-key";
    OpenAiLocalConfig.Model = "code-model";

    try
    {
        var responsePayload = JsonSerializer.Serialize(new
        {
            output_text = """
            {
              "is_travel_related": true,
              "intent": "DestinationAdvice",
              "confidence": 0.98,
              "entities": [],
              "reply": "Можу допомогти підібрати туристичний напрям."
            }
            """
        });

        var handler = new RecordingHttpMessageHandler(responsePayload);
        var client = new OpenAiResponsesLanguageModelClient(
            new HttpClient(handler),
            baseUrl: "https://api.openai.com/v1");

        var result = await client.GenerateAsync("порадь напрям");

        Assert(result.Success, $"Expected successful fake OpenAI response, got {result.Error}");
        Assert(handler.Authorization == "Bearer code-key", "Code API key was not used");
        Assert(result.Provider == "OpenAI/code-model", $"Code model was not used: {result.Provider}");
    }
    finally
    {
        OpenAiLocalConfig.ApiKey = previousApiKey;
        OpenAiLocalConfig.Model = previousModel;
    }
}

static void LocalFallbackRecognizesRegionalRecommendation()
{
    var bot = new TravelBotEngine(new NaiveBayesIntentClassifier(TrainingDataset.Examples));
    var response = bot.Reply("Порекомендуй щось у Львівській області");

    Assert(response.Prediction.Intent == IntentType.DestinationAdvice, $"Expected DestinationAdvice, got {response.Prediction.Intent}");
    Assert(response.Prediction.Confidence >= 0.80, $"Expected rule-backed confidence, got {response.Prediction.Confidence}");
    Assert(response.Context.CurrentDestination == "Львівська область", $"Expected Львівська область, got {response.Context.CurrentDestination}");
    Assert(!response.Reply.Contains("не повністю впевнений", StringComparison.OrdinalIgnoreCase), "Regional recommendation should not fall back to uncertainty");
}

static async Task FallsBackWhenLanguageModelUnavailable()
{
    var classifier = new NaiveBayesIntentClassifier(TrainingDataset.Examples);
    var bot = new TravelBotEngine(classifier, new FakeLanguageModelClient(LanguageModelResult.Unavailable("FakeModel", "offline")));

    var response = await bot.ReplyAsync("куди поїхати на море у вересні");

    Assert(response.Reply.Contains("Для підбору напряму", StringComparison.OrdinalIgnoreCase), "Fallback response was not used");
    Assert(response.Trace.Any(line => line.Contains("локальний fallback", StringComparison.OrdinalIgnoreCase)), "Fallback trace line is missing");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed class FakeLanguageModelClient(LanguageModelResult result) : ILanguageModelClient
{
    public Task<LanguageModelResult> GenerateAsync(string prompt, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(result);
    }
}

internal sealed class RecordingHttpMessageHandler(string payload) : HttpMessageHandler
{
    public Uri? RequestUri { get; private set; }
    public string? Authorization { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        RequestUri = request.RequestUri;
        Authorization = request.Headers.Authorization?.ToString();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };

        return Task.FromResult(response);
    }
}
