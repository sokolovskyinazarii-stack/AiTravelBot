namespace AiBot.Core;

public sealed record TrainingExample(IntentType Intent, string Text);

public sealed record IntentScore(IntentType Intent, double Score);

public sealed record IntentPrediction(
    IntentType Intent,
    double Confidence,
    IReadOnlyList<IntentScore> Scores);

public sealed record BotEntity(string Type, string Value, string Source);

public sealed record BotResponse(
    string Reply,
    IntentPrediction Prediction,
    IReadOnlyList<BotEntity> Entities,
    BotContextSnapshot Context,
    IReadOnlyList<string> Trace);

public sealed record BotContextSnapshot(
    int Turn,
    string CurrentDestination,
    string CurrentBudget,
    string LastIntent,
    string EntitySummary);
