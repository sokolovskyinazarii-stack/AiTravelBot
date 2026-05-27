namespace AiBot.Core;

public static class OpenAiLocalConfig
{
    // Встав API key сюди, якщо не хочеш налаштовувати змінну середовища.
    // Приклад: public static string ApiKey { get; set; } = "sk-proj-...";
    public static string ApiKey { get; set; } = "";

    // Модель можна змінити тут або залишити порожньою для стандартної gpt-5.4-mini.
    public static string Model { get; set; } = "";
}
