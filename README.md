# AiBotLab

Лабораторна робота №6 з дисципліни "Системи штучного інтелекту".

Тема: "Розроблення бота з використанням технологій ШІ".

## Склад рішення

- `AiBot.Core` - ML-логіка туристичного консультанта: OpenAI Responses API для LLM-аналізу, локальний Naive Bayes fallback, entities, memory.
- `AiBotApp` - WinUI 3 + XAML застосунок із піктограмою, адаптивним чат-інтерфейсом і полем для власних повідомлень.
- `AiBot.Core.Tests` - консольні тести ядра бота.
- `screenshots` - знімки роботи застосунку для звіту.
- `artifacts/report_render` - рендер сторінок PDF-звіту для перевірки.

Предметна область: консультант з туризму.
Бот розпізнає 7 намірів і виділяє сутності `destination`, `budget`,
`people_count`, `transport`, `travel_style`, `document`, `date`.

## Запуск

```powershell
dotnet build .\AiBotLab.sln
dotnet run --project .\AiBotApp\AiBotApp.csproj
```

## OpenAI API

Застосунок спочатку пробує розпізнати intent/entities і сформувати відповідь через OpenAI Responses API. Якщо `OPENAI_API_KEY` не заданий або API недоступний, бот автоматично повертається до локального Naive Bayes + rule-based fallback.

Найпростіший варіант - вставити ключ у файл `AiBot.Core\OpenAiLocalConfig.cs`:

```csharp
public static string ApiKey { get; set; } = "your-api-key";
public static string Model { get; set; } = "gpt-5.4-mini";
```

Альтернативно ключ можна задати через змінну середовища:

```powershell
$env:OPENAI_API_KEY = "your-api-key"
$env:TRAVEL_BOT_OPENAI_MODEL = "gpt-5.4-mini"
dotnet run --project .\AiBotApp\AiBotApp.csproj
```

Якщо застосунок запускається подвійним кліком, ключ краще зберегти у профіль користувача й перезапустити Windows Terminal або Visual Studio:

```powershell
[Environment]::SetEnvironmentVariable("OPENAI_API_KEY", "your-api-key", "User")
[Environment]::SetEnvironmentVariable("TRAVEL_BOT_OPENAI_MODEL", "gpt-5.4-mini", "User")
```

Guardrail у системному prompt дозволяє відповідати тільки на туристичні теми: напрям, маршрут, бюджет, бронювання, документи, транспорт, сезон, погода та локальні пам'ятки. На сторонні теми бот має відмовлятися і повертати користувача до планування подорожей.

## Перевірка

```powershell
dotnet run --project .\AiBot.Core.Tests\AiBot.Core.Tests.csproj
```

Готовий звіт:

- `СШІ_КН-2427Б_Соколовський_Бурик_ЛР6.docx`
- `СШІ_КН-2427Б_Соколовський_Бурик_ЛР6.pdf`
