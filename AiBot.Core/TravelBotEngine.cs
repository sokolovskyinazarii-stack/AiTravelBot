using System.Globalization;
using System.Text;
using System.Text.Json;

namespace AiBot.Core;

public sealed class TravelBotEngine
{
    private readonly NaiveBayesIntentClassifier _classifier;
    private readonly ILanguageModelClient? _languageModelClient;
    private readonly List<string> _history = [];
    private int _turn;
    private string _currentDestination = "напрям не обрано";
    private string _currentBudget = "бюджет не вказано";
    private IntentType _lastIntent = IntentType.Greeting;
    private bool _languageModelUnavailable;

    public TravelBotEngine()
        : this(new NaiveBayesIntentClassifier(TrainingDataset.Examples), new OpenAiResponsesLanguageModelClient())
    {
    }

    public TravelBotEngine(NaiveBayesIntentClassifier classifier, ILanguageModelClient? languageModelClient = null)
    {
        _classifier = classifier;
        _languageModelClient = languageModelClient;
    }

    public BotResponse Reply(string userMessage)
    {
        var turn = AnalyzeTurn(userMessage);
        Remember(userMessage, turn.FallbackReply);

        return new BotResponse(turn.FallbackReply, turn.Prediction, turn.Entities, turn.Context, turn.Trace);
    }

    public async Task<BotResponse> ReplyAsync(
        string userMessage,
        bool preferLanguageModel = true,
        CancellationToken cancellationToken = default)
    {
        var turn = AnalyzeTurn(userMessage);
        var trace = turn.Trace.ToList();
        var reply = turn.FallbackReply;

        if (preferLanguageModel && _languageModelClient is not null && !_languageModelUnavailable)
        {
            var modelResult = await _languageModelClient.GenerateAsync(BuildLanguageModelPrompt(turn), cancellationToken);
            if (modelResult.Success && TryParseTravelModelAnalysis(modelResult.Text, out var analysis))
            {
                ApplyEntities(analysis.Entities);
                var intent = analysis.IsTravelRelated ? analysis.Intent : IntentType.Fallback;
                var prediction = BuildModelPrediction(intent, analysis.Confidence, turn.Prediction.Scores);
                var context = new BotContextSnapshot(
                    _turn,
                    _currentDestination,
                    _currentBudget,
                    prediction.Intent.ToString(),
                    FormatEntities(analysis.Entities));

                reply = analysis.Reply;
                if (!analysis.IsTravelRelated)
                {
                    trace.Add("Guardrail: запит не належить до тематики подорожей");
                }

                trace.Add($"LLM: {modelResult.Provider} розпізнала intent/entities і сформувала відповідь");
                Remember(userMessage, reply);
                return new BotResponse(reply, prediction, analysis.Entities, context, trace);
            }
            else if (modelResult.Success)
            {
                trace.Add($"LLM: {modelResult.Provider} повернула невалідний JSON; використано локальний fallback");
            }
            else
            {
                _languageModelUnavailable = true;
                trace.Add($"LLM: {modelResult.Provider} недоступна ({modelResult.Error}); використано локальний fallback");
            }
        }
        else if (_languageModelUnavailable)
        {
            trace.Add("LLM: попередньо недоступна; використано локальний fallback");
        }
        else
        {
            trace.Add("LLM: вимкнено; використано локальний fallback");
        }

        Remember(userMessage, reply);
        return new BotResponse(reply, turn.Prediction, turn.Entities, turn.Context, trace);
    }

    private AnalyzedTurn AnalyzeTurn(string userMessage)
    {
        _turn++;
        var entities = EntityExtractor.Extract(userMessage);
        ApplyEntities(entities);

        var prediction = _classifier.Predict(userMessage);
        if (prediction.Confidence < 0.24)
        {
            prediction = prediction with { Intent = IntentType.Fallback };
        }

        if (IsNearbyRequest(userMessage, entities) || IsRecommendationRequest(userMessage, entities))
        {
            prediction = ForceIntent(prediction, IntentType.DestinationAdvice, 0.82);
        }

        _lastIntent = prediction.Intent;
        var reply = BuildReply(prediction.Intent, entities, userMessage);

        var context = new BotContextSnapshot(
            _turn,
            _currentDestination,
            _currentBudget,
            prediction.Intent.ToString(),
            FormatEntities(entities));

        var trace = BuildTrace(userMessage, prediction, entities, context);
        return new AnalyzedTurn(userMessage, prediction, entities, context, trace, reply);
    }

    private void Remember(string userMessage, string reply)
    {
        _history.Add($"U: {userMessage}");
        _history.Add($"B: {reply}");

        if (_history.Count > 12)
        {
            _history.RemoveRange(0, _history.Count - 12);
        }
    }

    private void ApplyEntities(IReadOnlyList<BotEntity> entities)
    {
        var destination = entities.LastOrDefault(entity => entity.Type == "destination");
        if (destination is not null)
        {
            _currentDestination = destination.Value;
        }

        var budget = entities.LastOrDefault(entity => entity.Type == "budget");
        if (budget is not null)
        {
            _currentBudget = budget.Value;
        }
    }

    private string BuildReply(IntentType intent, IReadOnlyList<BotEntity> entities, string userMessage)
    {
        var destination = _currentDestination;
        var budget = _currentBudget;

        return intent switch
        {
            IntentType.Greeting => "Привіт. Я туристичний консультант: підбираю напрям, складаю маршрут, оцінюю бюджет, підказую щодо бронювання, документів і сезонності.",
            IntentType.DestinationAdvice when IsNearbyRequest(userMessage, entities) => BuildNearbyReply(destination),
            IntentType.DestinationAdvice when destination.Equals("Львівська область", StringComparison.OrdinalIgnoreCase) => "У Львівській області можна поєднати міські прогулянки, курорти й історичні локації: Львів для архітектури та кав'ярень, Дрогобич для старої міської атмосфери, Трускавець і Східницю для спокійного відпочинку, Жовкву для короткої екскурсії та Тустань для природи й фортеці. Якщо маєш один день, краще обрати один напрям, а не намагатися охопити все одразу.",
            IntentType.DestinationAdvice => $"Для підбору напряму орієнтуйся на стиль поїздки. Для моря підійдуть Туреччина, Греція або Єгипет; для міського weekend - Прага, Відень, Львів; для активного відпочинку - Карпати. Поточний напрям у пам'яті: {destination}.",
            IntentType.ItineraryPlanning => $"Для {destination} рекомендую план без перевантаження: день 1 - центр і головні пам'ятки, день 2 - музеї або екскурсія, день 3 - локальна кухня, оглядові точки та вільний час. Уточни кількість днів, і я зроблю маршрут точніше.",
            IntentType.BudgetEstimate => $"Оцінюй бюджет окремо: дорога, житло, харчування, місцевий транспорт, екскурсії та резерв 10-15%. Поточний бюджет: {budget}. Якщо бюджет обмежений, краще бронювати житло раніше й обирати напрям із прямим транспортом.",
            IntentType.BookingHelp => "Перед бронюванням перевір рейтинг житла, розташування на карті, умови скасування, фінальну ціну з податками, багаж у квитках і час прибуття. Для популярних дат квитки й готель краще бронювати окремо заздалегідь.",
            IntentType.TravelDocuments => "Базовий список: паспорт або ID за правилами країни, квитки, бронювання житла, туристична страховка, банківська картка, контакти посольства та копії документів. Для окремих країн додатково перевіряй візу й правила в'їзду.",
            IntentType.WeatherSeason => $"Сезон залежить від напряму: море комфортне у травні-червні та вересні, Єгипет краще навесні або восени, Карпати для лиж - узимку, міські тури зручні навесні й восени. Поточний напрям: {destination}.",
            _ => "Я не повністю впевнений у запиті. Сформулюй його як підбір напряму, маршрут, бюджет, бронювання, документи або сезонність подорожі."
        };
    }

    private static bool IsNearbyRequest(string userMessage, IReadOnlyList<BotEntity> entities)
    {
        return entities.Any(entity => entity.Type == "travel_style" && entity.Value == "поруч")
            || userMessage.Contains("неподалік", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("поруч", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("біля", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("поблизу", StringComparison.OrdinalIgnoreCase)
            || userMessage.Contains("поряд", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRecommendationRequest(string userMessage, IReadOnlyList<BotEntity> entities)
    {
        return entities.Any(entity => entity.Type == "destination")
            && (userMessage.Contains("порекомендуй", StringComparison.OrdinalIgnoreCase)
                || userMessage.Contains("порадь", StringComparison.OrdinalIgnoreCase)
                || userMessage.Contains("що подивитися", StringComparison.OrdinalIgnoreCase)
                || userMessage.Contains("куди поїхати", StringComparison.OrdinalIgnoreCase)
                || userMessage.Contains("що відвідати", StringComparison.OrdinalIgnoreCase)
                || userMessage.Contains("що обрати", StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildNearbyReply(string destination)
    {
        if (destination.Equals("Дрогобич", StringComparison.OrdinalIgnoreCase))
        {
            return "Поруч із Дрогобичем варто розглянути Трускавець для прогулянки курортним центром і бювету, Східницю для спокійного відпочинку біля гір, Нагуєвичі з музеєм Івана Франка, Борислав із нафтовою історією та Урич/Тустань для виїзду на пів дня. Якщо потрібен короткий маршрут, найзручніше зробити Дрогобич - Трускавець - Нагуєвичі за один день.";
        }

        return $"Поруч із напрямом {destination} краще шукати короткі поїздки до сусідніх міст, природних локацій, музеїв і оглядових точок. Для точнішої поради напиши, що саме цікавить: природа, архітектура, спа, історія або маршрут на один день.";
    }

    private string BuildLanguageModelPrompt(AnalyzedTurn turn)
    {
        var recentHistory = _history.Count == 0
            ? "попереднього діалогу ще немає"
            : string.Join(Environment.NewLine, _history.TakeLast(8));

        var builder = new StringBuilder();
        builder.AppendLine("Ти Travel Advisor Bot - україномовний консультант з туризму.");
        builder.AppendLine("Відповідай як корисний чат-бот, а не як навчальна демонстрація.");
        builder.AppendLine("Правила відповіді:");
        builder.AppendLine("- поверни лише JSON за схемою, без markdown і пояснень;");
        builder.AppendLine("- самостійно визнач intent, confidence, entities і reply;");
        builder.AppendLine("- для чіткого туристичного запиту став confidence 0.75-1.0, навіть якщо місце має кілька слів;");
        builder.AppendLine("- якщо запит не про подорожі, is_travel_related=false, intent=Fallback, entities=[];");
        builder.AppendLine("- 3-5 речень у reply, конкретні поради без зайвої теорії;");
        builder.AppendLine("- не згадуй лабораторну роботу, датасет або внутрішній код.");
        builder.AppendLine();
        builder.AppendLine($"Intent: {turn.Prediction.Intent}");
        builder.AppendLine($"Confidence: {turn.Prediction.Confidence.ToString("P1", CultureInfo.GetCultureInfo("uk-UA"))}");
        builder.AppendLine($"Entities: {FormatEntities(turn.Entities)}");
        builder.AppendLine($"Memory: {turn.Context.CurrentDestination}, {turn.Context.CurrentBudget}");
        builder.AppendLine($"Локальна відповідь для опори: {turn.FallbackReply}");
        builder.AppendLine("Попередній діалог:");
        builder.AppendLine(recentHistory);
        builder.AppendLine();
        builder.AppendLine($"Повідомлення користувача: {turn.UserMessage}");

        return builder.ToString();
    }

    private static bool TryParseTravelModelAnalysis(string text, out TravelModelAnalysis analysis)
    {
        analysis = new TravelModelAnalysis(false, IntentType.Fallback, 0, [], string.Empty);

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;

            var isTravelRelated = root.TryGetProperty("is_travel_related", out var travel)
                && travel.ValueKind == JsonValueKind.True;

            var intentText = root.TryGetProperty("intent", out var intentElement)
                ? intentElement.GetString()
                : nameof(IntentType.Fallback);
            if (!Enum.TryParse(intentText, ignoreCase: true, out IntentType intent))
            {
                intent = IntentType.Fallback;
            }

            var confidence = root.TryGetProperty("confidence", out var confidenceElement)
                && confidenceElement.TryGetDouble(out var parsedConfidence)
                ? Math.Clamp(parsedConfidence, 0, 1)
                : 0.85;

            var reply = root.TryGetProperty("reply", out var replyElement)
                ? replyElement.GetString() ?? string.Empty
                : string.Empty;

            var entities = new List<BotEntity>();
            if (root.TryGetProperty("entities", out var entitiesElement) && entitiesElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var entityElement in entitiesElement.EnumerateArray())
                {
                    var type = entityElement.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
                    var value = entityElement.TryGetProperty("value", out var valueElement) ? valueElement.GetString() : null;
                    var source = entityElement.TryGetProperty("source", out var sourceElement) ? sourceElement.GetString() : value;

                    if (!string.IsNullOrWhiteSpace(type) && !string.IsNullOrWhiteSpace(value))
                    {
                        entities.Add(new BotEntity(type, value, source ?? value));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(reply))
            {
                return false;
            }

            analysis = new TravelModelAnalysis(isTravelRelated, intent, confidence, entities, reply);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static IntentPrediction BuildModelPrediction(
        IntentType intent,
        double confidence,
        IReadOnlyList<IntentScore> fallbackScores)
    {
        var scores = fallbackScores
            .Where(score => score.Intent != intent)
            .Prepend(new IntentScore(intent, confidence))
            .OrderByDescending(score => score.Score)
            .Take(3)
            .ToArray();

        return new IntentPrediction(intent, confidence, scores);
    }

    private static IntentPrediction ForceIntent(IntentPrediction prediction, IntentType intent, double confidence)
    {
        var normalizedConfidence = Math.Max(prediction.Confidence, confidence);
        var scores = prediction.Scores
            .Where(score => score.Intent != intent)
            .Prepend(new IntentScore(intent, normalizedConfidence))
            .OrderByDescending(score => score.Score)
            .Take(3)
            .ToArray();

        return new IntentPrediction(intent, normalizedConfidence, scores);
    }

    private static IReadOnlyList<string> BuildTrace(
        string userMessage,
        IntentPrediction prediction,
        IReadOnlyList<BotEntity> entities,
        BotContextSnapshot context)
    {
        var top = prediction.Scores.Take(3)
            .Select(score => $"{score.Intent}: {score.Score.ToString("P1", CultureInfo.GetCultureInfo("uk-UA"))}");

        return
        [
            $"Вхід: {userMessage}",
            $"Tokens: {string.Join(", ", TextTokenizer.Tokenize(userMessage))}",
            $"Top intents: {string.Join(" | ", top)}",
            $"Entities: {FormatEntities(entities)}",
            $"Memory: {context.CurrentDestination}, {context.CurrentBudget}"
        ];
    }

    private static string FormatEntities(IReadOnlyList<BotEntity> entities)
    {
        return entities.Count == 0
            ? "не знайдено"
            : string.Join(", ", entities.Select(entity => $"{entity.Type}={entity.Value}"));
    }

    private sealed record AnalyzedTurn(
        string UserMessage,
        IntentPrediction Prediction,
        IReadOnlyList<BotEntity> Entities,
        BotContextSnapshot Context,
        IReadOnlyList<string> Trace,
        string FallbackReply);

    private sealed record TravelModelAnalysis(
        bool IsTravelRelated,
        IntentType Intent,
        double Confidence,
        IReadOnlyList<BotEntity> Entities,
        string Reply);
}
