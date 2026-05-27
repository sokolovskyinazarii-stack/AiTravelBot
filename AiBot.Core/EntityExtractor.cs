using System.Text.RegularExpressions;

namespace AiBot.Core;

public static partial class EntityExtractor
{
    private static readonly IReadOnlyDictionary<string, string> DestinationAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Париж"] = "Париж",
            ["Парижі"] = "Париж",
            ["Рим"] = "Рим",
            ["Римі"] = "Рим",
            ["Барселона"] = "Барселона",
            ["Барселоні"] = "Барселона",
            ["Стамбул"] = "Стамбул",
            ["Стамбулі"] = "Стамбул",
            ["Прага"] = "Прага",
            ["Прагу"] = "Прага",
            ["Празі"] = "Прага",
            ["Відень"] = "Відень",
            ["Відні"] = "Відень",
            ["Львів"] = "Львів",
            ["Львові"] = "Львів",
            ["Львова"] = "Львів",
            ["Львівська область"] = "Львівська область",
            ["Львівській області"] = "Львівська область",
            ["Львівщині"] = "Львівська область",
            ["Львівщина"] = "Львівська область",
            ["Одеса"] = "Одеса",
            ["Одесі"] = "Одеса",
            ["Карпати"] = "Карпати",
            ["Буковель"] = "Буковель",
            ["Єгипет"] = "Єгипет",
            ["Туреччина"] = "Туреччина",
            ["Туреччину"] = "Туреччина",
            ["Греція"] = "Греція",
            ["Греції"] = "Греція",
            ["Польща"] = "Польща",
            ["Польщу"] = "Польща",
            ["Італія"] = "Італія",
            ["Італії"] = "Італія",
            ["Франція"] = "Франція",
            ["Франції"] = "Франція",
            ["Іспанія"] = "Іспанія",
            ["Іспанії"] = "Іспанія",
            ["Таїланд"] = "Таїланд",
            ["Кіпр"] = "Кіпр",
            ["Дрогобич"] = "Дрогобич",
            ["Дрогобича"] = "Дрогобич",
            ["Дрогобичі"] = "Дрогобич",
            ["Трускавець"] = "Трускавець",
            ["Трускавці"] = "Трускавець",
            ["Трускавця"] = "Трускавець",
            ["Східниця"] = "Східниця",
            ["Східниці"] = "Східниця",
            ["Борислав"] = "Борислав",
            ["Бориславі"] = "Борислав",
            ["Самбір"] = "Самбір",
            ["Самборі"] = "Самбір",
            ["Стрий"] = "Стрий",
            ["Стрию"] = "Стрий",
            ["Нагуєвичі"] = "Нагуєвичі",
            ["Урич"] = "Урич",
            ["Тустань"] = "Тустань",
            ["Моршин"] = "Моршин"
        };

    private static readonly string[] Transports =
    [
        "літак", "авіа", "рейс", "потяг", "автобус", "авто", "машина", "трансфер"
    ];

    private static readonly string[] TravelStyles =
    [
        "море", "пляж", "гори", "екскурсії", "музеї", "романтичну", "сімейний",
        "бюджетну", "активний", "лижі", "weekend", "вихідні"
    ];

    private static readonly string[] NearbyTerms =
    [
        "неподалік", "поруч", "біля", "поблизу", "поряд", "околиці", "околицях"
    ];

    private static readonly string[] Documents =
    [
        "паспорт", "віза", "страховка", "сертифікат", "id карта", "квитки", "бронювання", "документи"
    ];

    public static IReadOnlyList<BotEntity> Extract(string text)
    {
        var entities = new List<BotEntity>();

        foreach (Match match in DateRegex().Matches(text))
        {
            entities.Add(new BotEntity("date", match.Value, match.Value));
        }

        foreach (Match match in BudgetRegex().Matches(text))
        {
            entities.Add(new BotEntity("budget", match.Value, match.Value));
        }

        foreach (Match match in PeopleRegex().Matches(text))
        {
            entities.Add(new BotEntity("people_count", match.Value, match.Value));
        }

        AddDestinationMatches(entities, text);
        AddDictionaryMatches(entities, "transport", Transports, text);
        AddDictionaryMatches(entities, "travel_style", TravelStyles, text);
        AddNearbyStyle(entities, text);
        AddDictionaryMatches(entities, "document", Documents, text);

        return entities
            .GroupBy(entity => $"{entity.Type}:{entity.Value}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static void AddDestinationMatches(List<BotEntity> entities, string text)
    {
        foreach (Match match in PlaceAfterPrepositionRegex().Matches(text))
        {
            var candidate = match.Groups["place"].Value;
            entities.Add(new BotEntity("destination", NormalizeDestination(candidate), candidate));
        }

        foreach (Match match in NamedPlaceAfterKindRegex().Matches(text))
        {
            var candidate = match.Groups["place"].Value;
            entities.Add(new BotEntity("destination", NormalizeDestination(candidate), candidate));
        }

        foreach (var (alias, destination) in DestinationAliases)
        {
            if (text.Contains(alias, StringComparison.OrdinalIgnoreCase))
            {
                entities.Add(new BotEntity("destination", destination, alias));
            }
        }
    }

    private static void AddNearbyStyle(List<BotEntity> entities, string text)
    {
        var nearbyTerm = NearbyTerms.FirstOrDefault(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
        if (nearbyTerm is not null)
        {
            entities.Add(new BotEntity("travel_style", "поруч", nearbyTerm));
        }
    }

    private static string NormalizeDestination(string candidate)
    {
        return DestinationAliases.TryGetValue(candidate, out var destination)
            ? destination
            : candidate;
    }

    private static void AddDictionaryMatches(List<BotEntity> entities, string type, IEnumerable<string> terms, string text)
    {
        foreach (var term in terms)
        {
            if (text.Contains(term, StringComparison.OrdinalIgnoreCase))
            {
                entities.Add(new BotEntity(type, term, term));
            }
        }
    }

    [GeneratedRegex(@"\b(?:сьогодні|завтра|післязавтра|січні|лютому|березні|квітні|травні|червні|липні|серпні|вересні|жовтні|листопаді|грудні|влітку|восени|взимку|навесні|\d{1,2}[./-]\d{1,2}(?:[./-]\d{2,4})?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex DateRegex();

    [GeneratedRegex(@"\b\d{2,6}\s*(?:грн|uah|євро|eur|\$|долар\w*)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex BudgetRegex();

    [GeneratedRegex(@"\b\d+\s*(?:люд(?:ей|ини|ина)?|особ(?:и|а)?|турист(?:и|ів)?|доросл(?:их|ий)?)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex PeopleRegex();

    [GeneratedRegex(@"\b(?:у|в|до|біля|поблизу|неподалік|поруч\s+із|поруч\s+з|поряд\s+із|поряд\s+з)\s+(?<place>[А-ЯІЇЄҐ][а-яіїєґ'’\-]+(?:\s+[А-ЯІЇЄҐ][а-яіїєґ'’\-]+){0,3})\b", RegexOptions.Compiled)]
    private static partial Regex PlaceAfterPrepositionRegex();

    [GeneratedRegex(@"\b(?:у|в|до|біля|поблизу|неподалік|поруч\s+із|поруч\s+з|поряд\s+із|поряд\s+з)\s+(?:міста|місті|містечка|села|селі|селища|курорту|озера|замку|фортеці|гори|водоспаду|парку|заповідника|урочища)\s+(?<place>[А-ЯІЇЄҐ][а-яіїєґ'’\-]+(?:\s+[А-ЯІЇЄҐ][а-яіїєґ'’\-]+){0,3})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NamedPlaceAfterKindRegex();
}
