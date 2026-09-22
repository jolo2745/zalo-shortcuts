using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ZaloShortcuts.Services;

namespace ZaloShortcuts;

public partial class SuggestionWindow : Window
{
    public SuggestionWindow()
    {
        InitializeComponent();
    }

    public void ApplySnapshot(SuggestionSnapshot snapshot)
    {
        if (!snapshot.IsVisible || snapshot.Matches.Count == 0)
        {
            Hide();
            return;
        }

        QueryText.Text = $"{snapshot.TypedToken}  —  {snapshot.Matches.Count} match{(snapshot.Matches.Count == 1 ? string.Empty : "es")}";
        ResultsPanel.Children.Clear();

        for (var index = 0; index < snapshot.Matches.Count; index++)
        {
            var item = snapshot.Matches[index];
            var textPanel = new StackPanel { Spacing = 2 };
            textPanel.Children.Add(new TextBlock
            {
                Text = item.TriggerDisplay,
                FontWeight = index == 0 ? FontWeight.SemiBold : FontWeight.Normal,
                Foreground = new SolidColorBrush(Color.Parse(index == 0 ? "#075FBF" : "#26364A"))
            });
            textPanel.Children.Add(new TextBlock
            {
                Text = item.MessagePreview,
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.Parse("#68778C")),
                TextTrimming = TextTrimming.CharacterEllipsis
            });

            ResultsPanel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.Parse(index == 0 ? "#EAF4FF" : "#F7F9FC")),
                CornerRadius = new CornerRadius(7),
                Padding = new Thickness(10, 7),
                Child = textPanel
            });
        }

        if (!IsVisible)
        {
            Show();
        }

        PositionAtBottomRight();
    }

    private void PositionAtBottomRight()
    {
        var screen = Screens.Primary;
        if (screen is null)
        {
            return;
        }

        var area = screen.WorkingArea;
        var estimatedHeight = Math.Min(390, 74 + (ResultsPanel.Children.Count * 58));
        Position = new PixelPoint(
            area.Right - (int)Width - 24,
            area.Bottom - estimatedHeight - 24);
    }
}
