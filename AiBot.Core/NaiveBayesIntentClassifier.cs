namespace AiBot.Core;

public sealed class NaiveBayesIntentClassifier
{
    private readonly Dictionary<IntentType, Dictionary<string, int>> _tokenCounts = [];
    private readonly Dictionary<IntentType, int> _intentExampleCounts = [];
    private readonly Dictionary<IntentType, int> _intentTokenTotals = [];
    private readonly HashSet<string> _vocabulary = new(StringComparer.OrdinalIgnoreCase);
    private readonly int _totalExamples;

    public NaiveBayesIntentClassifier(IEnumerable<TrainingExample> examples)
    {
        var materialized = examples.ToArray();
        _totalExamples = materialized.Length;

        foreach (var example in materialized)
        {
            if (!_tokenCounts.ContainsKey(example.Intent))
            {
                _tokenCounts[example.Intent] = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                _intentExampleCounts[example.Intent] = 0;
                _intentTokenTotals[example.Intent] = 0;
            }

            _intentExampleCounts[example.Intent]++;

            foreach (var token in TextTokenizer.Tokenize(example.Text))
            {
                _vocabulary.Add(token);
                _tokenCounts[example.Intent][token] = _tokenCounts[example.Intent].GetValueOrDefault(token) + 1;
                _intentTokenTotals[example.Intent]++;
            }
        }
    }

    public IntentPrediction Predict(string text)
    {
        var tokens = TextTokenizer.Tokenize(text);
        if (tokens.Count == 0)
        {
            return new IntentPrediction(IntentType.Fallback, 0, []);
        }

        var logScores = _intentExampleCounts.Keys
            .Select(intent => new
            {
                Intent = intent,
                LogScore = ScoreIntent(intent, tokens)
            })
            .ToArray();

        var maxLog = logScores.Max(item => item.LogScore);
        var normalized = logScores
            .Select(item => new IntentScore(item.Intent, Math.Exp(item.LogScore - maxLog)))
            .ToArray();
        var sum = normalized.Sum(item => item.Score);
        var scores = normalized
            .Select(item => new IntentScore(item.Intent, item.Score / sum))
            .OrderByDescending(item => item.Score)
            .ToArray();

        var best = scores[0];
        return new IntentPrediction(best.Intent, best.Score, scores);
    }

    private double ScoreIntent(IntentType intent, IReadOnlyList<string> tokens)
    {
        var vocabularySize = Math.Max(_vocabulary.Count, 1);
        var prior = Math.Log((double)_intentExampleCounts[intent] / _totalExamples);
        var denominator = _intentTokenTotals[intent] + vocabularySize;

        return tokens.Aggregate(prior, (score, token) =>
        {
            var count = _tokenCounts[intent].GetValueOrDefault(token);
            var probability = (count + 1.0) / denominator;
            return score + Math.Log(probability);
        });
    }
}
