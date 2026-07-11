using System;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Avalonia.VisualTree;
using LlamaSwapManager.Desktop;
using LlamaSwapManager.ViewModels;
using LlamaSwapManager.Services;

namespace LlamaSwapManager.Views;

public partial class MainWindow : Window
{
    private static readonly HttpClient HuggingFaceHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };
    private static readonly HuggingFaceModelCatalog HuggingFaceCatalog =
        new(HuggingFaceHttpClient);
    private async void OnChooseModelClick(object? sender, RoutedEventArgs e)
        {
            if (DataContext is not MainViewModel vm || vm.SelectedModel is null)
                return;
    
            var bg = Brush("#0F0F18");
            var surface = Brush("#161622");
            var border = Brush("#252536");
            var text = Brush("#CDD6F4");
            var muted = Brush("#6C7086");
            var accent = Brush("#89B4FA");
    
            var dialog = new Window
            {
                Title = "Choose model",
                Width = 860,
                Height = 680,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                CanResize = true,
                Background = bg
            };
    
            // --- Header ---
            var title = new TextBlock
            {
                Text = "Choose model source",
                FontSize = 18,
                FontWeight = FontWeight.SemiBold,
                Foreground = text
            };
            var closeX = new Button
            {
                Content = new TextBlock { Text = "✕", FontSize = 16, FontWeight = FontWeight.SemiBold, Foreground = text, HorizontalAlignment = HorizontalAlignment.Center },
                Width = 36,
                Height = 36,
                Padding = new Thickness(0),
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            closeX.Click += (_, __) => dialog.Close();
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 16) };
            DockPanel.SetDock(closeX, Dock.Right);
            header.Children.Add(closeX);
            header.Children.Add(title);
    
            // --- Local card ---
            var browseButton = new Button
            {
                Content = "📁  Browse local .gguf…",
                Padding = new Thickness(14, 9),
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Foreground = text
            };
            browseButton.Click += async (_, _) =>
            {
                var path = await PickGgufPathAsync();
                if (!string.IsNullOrWhiteSpace(path))
                {
                    vm.SetLocalModelPath(path);
                    dialog.Close();
                }
            };
            var localCard = new Border
            {
                Background = surface,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16),
                Margin = new Thickness(0, 0, 0, 14),
                Child = new StackPanel
                {
                    Spacing = 10,
                    Children =
                    {
                        new TextBlock { Text = "Local GGUF", FontWeight = FontWeight.SemiBold, Foreground = text },
                        new TextBlock { Text = "Pick a file already on disk.", FontSize = 12, Foreground = muted },
                        browseButton
                    }
                }
            };
    
            // --- Search ---
            var queryBox = new TextBox
            {
                PlaceholderText = "Search Hugging Face GGUF repos…",
                Text = vm.HfSearchQuery,
                MinHeight = 36,
                Background = surface,
                Foreground = text,
                BorderBrush = border,
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 8)
            };
            var searchButton = new Button
            {
                Content = "🔍  Search",
                Padding = new Thickness(14, 9),
                MinHeight = 36,
                Background = Brush("#1B3A2A"),
                Foreground = Brush("#A6E3A1"),
                BorderBrush = Brush("#2A4F38"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            var searchRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
            searchRow.Children.Add(queryBox);
            Grid.SetColumn(searchButton, 1);
            searchRow.Children.Add(searchButton);
    
            var contentScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 12, 0, 12)
            };
            var contentPanel = new StackPanel { Spacing = 8 };
            contentScroll.Content = contentPanel;
    
            var status = new TextBlock
            {
                Text = "Search a repo, then pick a GGUF file (quant optional).",
                Foreground = muted,
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            var cancelButton = new Button
            {
                Content = "Cancel",
                Padding = new Thickness(14, 8),
                IsCancel = true,
                Background = surface,
                Foreground = text,
                BorderBrush = border,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
            cancelButton.Click += (_, __) => dialog.Close();
    
            async Task ShowRepositoryFilesAsync(string modelId)
            {
                contentPanel.Children.Clear();
                contentPanel.Children.Add(new TextBlock
                {
                    Text = $"Loading GGUF files in {modelId}…",
                    Foreground = muted,
                    Margin = new Thickness(0, 8)
                });
                status.Text = "Listing GGUF files…";

                try
                {
                    var files = await HuggingFaceCatalog.GetGgufFilesAsync(modelId);
                    contentPanel.Children.Clear();
                    if (files.Count == 0)
                    {
                        contentPanel.Children.Add(new TextBlock
                        {
                            Text = "No .gguf files found in this repository.",
                            Foreground = muted
                        });
                        status.Text = "No GGUF files found.";
                        return;
                    }

                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = modelId,
                        FontWeight = FontWeight.SemiBold,
                        Foreground = text,
                        FontSize = 14,
                        Margin = new Thickness(0, 0, 0, 8)
                    });
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = $"{files.Count} GGUF file(s) — pick one (quant tag used when present).",
                        Foreground = muted,
                        FontSize = 12,
                        Margin = new Thickness(0, 0, 0, 8)
                    });

                    foreach (var file in files)
                    {
                        var button = MakeListButton(file.DisplayText, text, surface, border);
                        button.Click += (_, _) =>
                        {
                            vm.SetHfModelWithQuantization(modelId, file.SelectionToken);
                            dialog.Close();
                        };
                        contentPanel.Children.Add(button);
                    }

                    status.Text = "Select a GGUF file.";
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    status.Text = $"Failed to list files: {ex.Message}";
                    contentPanel.Children.Clear();
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = status.Text,
                        Foreground = Brush("#F38BA8")
                    });
                }
            }

            async Task SearchAsync()
            {
                contentPanel.Children.Clear();
                var query = queryBox.Text;
                if (string.IsNullOrWhiteSpace(query))
                {
                    status.Text = "Type a search query first.";
                    return;
                }

                status.Text = "Searching Hugging Face…";
                searchButton.IsEnabled = false;
                try
                {
                    var repositories = await HuggingFaceCatalog.SearchRepositoriesAsync(query);
                    foreach (var modelId in repositories)
                    {
                        var button = MakeListButton(modelId, text, surface, border);
                        button.Click += async (_, _) => await ShowRepositoryFilesAsync(modelId);
                        contentPanel.Children.Add(button);
                    }

                    status.Text = repositories.Count == 0
                        ? "No GGUF repositories found."
                        : $"{repositories.Count} repo(s). Click one to list GGUF files.";
                    if (repositories.Count == 0)
                    {
                        contentPanel.Children.Add(new TextBlock
                        {
                            Text = "No GGUF repositories found.",
                            Foreground = muted
                        });
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    status.Text = $"Search failed: {ex.Message}";
                    contentPanel.Children.Add(new TextBlock
                    {
                        Text = status.Text,
                        Foreground = Brush("#F38BA8")
                    });
                }
                finally
                {
                    searchButton.IsEnabled = true;
                }
            }

            searchButton.Click += async (_, _) => await SearchAsync();
            queryBox.KeyDown += async (_, ev) =>
            {
                if (ev.Key == Key.Enter)
                {
                    ev.Handled = true;
                    await SearchAsync();
                }
            };
    
            dialog.KeyDown += (_, ev) =>
            {
                if (ev.Key == Key.Escape)
                {
                    ev.Handled = true;
                    dialog.Close();
                }
            };
    
            var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(0, 8, 0, 0) };
            footer.Children.Add(status);
            Grid.SetColumn(cancelButton, 1);
            footer.Children.Add(cancelButton);
    
            var root = new Grid
            {
                Margin = new Thickness(22),
                RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*,Auto")
            };
            root.Children.Add(header);
            Grid.SetRow(localCard, 1);
            root.Children.Add(localCard);
            Grid.SetRow(searchRow, 2);
            root.Children.Add(searchRow);
            Grid.SetRow(contentScroll, 3);
            root.Children.Add(contentScroll);
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);
    
            dialog.Content = root;
            await dialog.ShowDialog(this);
        }
    
        private static IBrush Brush(string hex) =>
            SolidColorBrush.Parse(hex);
    
        private static Button MakeListButton(string content, IBrush foreground, IBrush background, IBrush borderBrush) =>
            new Button
            {
                Content = content,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(12, 10),
                Margin = new Thickness(0, 0, 0, 4),
                Background = background,
                Foreground = foreground,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8)
            };
    
    
}
