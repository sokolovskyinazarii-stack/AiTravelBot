using AiBot.Core;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Collections.ObjectModel;
using System.Globalization;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace AiBotApp;

public sealed partial class MainWindow : Window
{
    private readonly TravelBotEngine _bot = new();
    private readonly ObservableCollection<ChatMessageViewModel> _messages = [];
    private readonly ObservableCollection<EntityViewModel> _entities = [];
    private readonly ObservableCollection<IntentStatViewModel> _intentStats = [];
    private readonly CultureInfo _culture = CultureInfo.GetCultureInfo("uk-UA");
    private bool _demoLoaded;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Travel Advisor Bot";
        SetWindowIconAndSize();

        ChatList.ItemsSource = _messages;
        EntitiesList.ItemsSource = _entities;
        IntentStatsList.ItemsSource = _intentStats;
        PopulateIntentStats();

        ContentGrid.Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_demoLoaded)
        {
            return;
        }

        _demoLoaded = true;
        AddBotMessage("Привіт. Я туристичний консультант: допоможу обрати напрям, маршрут, бюджет, бронювання, документи та сезон.");
        await SendUserMessageAsync("Хочу спланувати подорож у Париж", preferLanguageModel: false);
        await SendUserMessageAsync("Склади маршрут на три дні", preferLanguageModel: false);
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        await SendFromInputAsync();
    }

    private async void MessageTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await SendFromInputAsync();
        }
    }

    private async void QuickPrompt_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string prompt })
        {
            await SendUserMessageAsync(prompt);
        }
    }

    private async Task SendFromInputAsync()
    {
        var text = MessageTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MessageTextBox.Text = string.Empty;
        await SendUserMessageAsync(text);
    }

    private async Task SendUserMessageAsync(string text, bool preferLanguageModel = true)
    {
        AddUserMessage(text);

        SetInputState(isBusy: true);
        try
        {
            var response = await _bot.ReplyAsync(text, preferLanguageModel);
            AddBotMessage(response.Reply);
            UpdateAnalysis(response);
        }
        finally
        {
            SetInputState(isBusy: false);
        }
    }

    private void SetInputState(bool isBusy)
    {
        MessageTextBox.IsEnabled = !isBusy;
        SendButton.IsEnabled = !isBusy;
        ModelStatusText.Text = isBusy ? "Відповідь: формується..." : ModelStatusText.Text;
    }

    private void AddUserMessage(string text)
    {
        AddMessage(new ChatMessageViewModel(
            "Користувач",
            text,
            new SolidColorBrush(ColorHelper.FromArgb(255, 37, 99, 235)),
            new SolidColorBrush(Colors.White),
            new SolidColorBrush(Colors.White),
            HorizontalAlignment.Right));
    }

    private void AddBotMessage(string text)
    {
        AddMessage(new ChatMessageViewModel(
            "Бот",
            text,
            new SolidColorBrush(ColorHelper.FromArgb(255, 236, 253, 245)),
            new SolidColorBrush(ColorHelper.FromArgb(255, 5, 150, 105)),
            new SolidColorBrush(ColorHelper.FromArgb(255, 17, 24, 39)),
            HorizontalAlignment.Left));
    }

    private void AddMessage(ChatMessageViewModel message)
    {
        _messages.Add(message);
        ChatList.ScrollIntoView(message);
    }

    private void UpdateAnalysis(BotResponse response)
    {
        var confidence = response.Prediction.Confidence.ToString("P1", _culture);
        IntentText.Text = $"Intent: {response.Prediction.Intent}";
        SidebarConfidenceText.Text = $"Впевненість: {confidence}";
        ContextText.Text = $"Напрям: {response.Context.CurrentDestination}";
        TechnologyText.Text = $"Бюджет: {response.Context.CurrentBudget}";

        MetricIntentText.Text = response.Prediction.Intent.ToString();
        MetricConfidenceText.Text = confidence;
        MetricMemoryText.Text = $"{response.Context.CurrentDestination}, {response.Context.CurrentBudget}";
        ModelStatusText.Text = FormatModelStatus(response.Trace);

        _entities.Clear();
        foreach (var entity in response.Entities)
        {
            _entities.Add(new EntityViewModel(entity.Type, entity.Value));
        }

        TraceTextBox.Text = string.Join(Environment.NewLine, response.Trace);
    }

    private static string FormatModelStatus(IReadOnlyList<string> trace)
    {
        var llmLine = trace.LastOrDefault(line => line.StartsWith("LLM:", StringComparison.OrdinalIgnoreCase));
        if (llmLine is null)
        {
            return "Відповідь: локальний ML";
        }

        if (llmLine.Contains("OPENAI_API_KEY", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI: ключ не задано";
        }

        if (llmLine.Contains("невалідний JSON", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI: невалідна відповідь";
        }

        if (llmLine.Contains("недоступна", StringComparison.OrdinalIgnoreCase))
        {
            return "OpenAI: недоступно";
        }

        if (llmLine.Contains("OpenAI/", StringComparison.OrdinalIgnoreCase)
            && llmLine.Contains("розпізнала", StringComparison.OrdinalIgnoreCase))
        {
            return "Відповідь: OpenAI API";
        }

        if (llmLine.Contains("fallback", StringComparison.OrdinalIgnoreCase))
        {
            return "Відповідь: локальний fallback";
        }

        return "Відповідь: локальна модель";
    }

    private void PopulateIntentStats()
    {
        var index = 1;
        foreach (var group in TrainingDataset.Examples.GroupBy(example => example.Intent).OrderBy(group => group.Key.ToString()))
        {
            _intentStats.Add(new IntentStatViewModel(index.ToString(CultureInfo.InvariantCulture), group.Key.ToString(), $"{group.Count()} фраз"));
            index++;
        }
    }

    private void SetWindowIconAndSize()
    {
        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);

        appWindow.Resize(new SizeInt32(1380, 900));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }
    }

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var narrow = e.NewSize.Width < 1040;

        if (narrow)
        {
            SidebarColumn.Width = new GridLength(1, GridUnitType.Star);
            WorkspaceColumn.Width = new GridLength(0);
            MainContentRow.Height = GridLength.Auto;
            StackedContentRow.Height = new GridLength(1, GridUnitType.Star);

            Grid.SetRow(SidebarPanel, 0);
            Grid.SetColumn(SidebarPanel, 0);
            Grid.SetRow(WorkspacePanel, 1);
            Grid.SetColumn(WorkspacePanel, 0);
            SidebarPanel.Margin = new Thickness(0, 0, 0, 18);
        }
        else
        {
            SidebarColumn.Width = new GridLength(334);
            WorkspaceColumn.Width = new GridLength(1, GridUnitType.Star);
            MainContentRow.Height = new GridLength(1, GridUnitType.Star);
            StackedContentRow.Height = new GridLength(0);

            Grid.SetRow(SidebarPanel, 0);
            Grid.SetColumn(SidebarPanel, 0);
            Grid.SetRow(WorkspacePanel, 0);
            Grid.SetColumn(WorkspacePanel, 1);
            SidebarPanel.Margin = new Thickness(0, 0, 24, 0);
        }

        var stackBottom = e.NewSize.Width < 1180;
        if (stackBottom)
        {
            DatasetColumn.Width = new GridLength(1, GridUnitType.Star);
            TraceColumn.Width = new GridLength(0);
            BottomSecondRow.Height = GridLength.Auto;
            Grid.SetRow(TraceCard, 1);
            Grid.SetColumn(TraceCard, 0);
        }
        else
        {
            DatasetColumn.Width = new GridLength(1.05, GridUnitType.Star);
            TraceColumn.Width = new GridLength(1, GridUnitType.Star);
            BottomSecondRow.Height = new GridLength(0);
            Grid.SetRow(TraceCard, 0);
            Grid.SetColumn(TraceCard, 1);
        }
    }
}

public sealed record ChatMessageViewModel(
    string Sender,
    string Text,
    SolidColorBrush BubbleBrush,
    SolidColorBrush SenderBrush,
    SolidColorBrush TextBrush,
    HorizontalAlignment Alignment);

public sealed record EntityViewModel(string Type, string Value);

public sealed record IntentStatViewModel(string Number, string Intent, string Count);
