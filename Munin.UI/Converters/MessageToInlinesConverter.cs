using Munin.UI.Services;
using Munin.UI.ViewModels;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Munin.UI.Converters;

/// <summary>
/// Converts a MessageViewModel to a Panel with formatted inlines (IRC colors, links, emojis) and inline images.
/// </summary>
public class MessageToInlinesConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not MessageViewModel message)
            return null;

        var textBlock = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = message.MessageColor
        };

        try
        {
            var inlines = IrcTextFormatter.ParseWithLinks(message.FormattedMessage, message.MessageColor);
            foreach (var inline in inlines)
            {
                textBlock.Inlines.Add(inline);
            }
        }
        catch
        {
            // Fallback to plain text
            textBlock.Text = message.FormattedMessage;
        }

        // Check for inline images
        if (IrcTextFormatter.EnableImagePreviews)
        {
            var imageUrls = IrcTextFormatter.ExtractImageUrls(message.FormattedMessage).ToList();
            if (imageUrls.Count > 0)
            {
                var panel = new StackPanel { Orientation = Orientation.Vertical };
                panel.Children.Add(textBlock);
                
                foreach (var imageUrl in imageUrls.Take(3)) // Limit to 3 images per message
                {
                    var imageContainer = CreateImagePreview(imageUrl);
                    if (imageContainer != null)
                    {
                        panel.Children.Add(imageContainer);
                    }
                }
                
                return panel;
            }
        }

        return textBlock;
    }
    
    /// <summary>
    /// Creates a bordered image preview element from an image URL.
    /// </summary>
    /// <param name="imageUrl">The URL of the image to display.</param>
    /// <returns>A <see cref="FrameworkElement"/> containing the image preview, or null if the image fails to load.</returns>
    private static FrameworkElement? CreateImagePreview(string imageUrl)
    {
        try
        {
            var border = new Border
            {
                Margin = new Thickness(0, 4, 0, 4),
                CornerRadius = new CornerRadius(6),
                BorderBrush = new SolidColorBrush(Color.FromRgb(60, 60, 60)),
                BorderThickness = new Thickness(1),
                MaxWidth = IrcTextFormatter.MaxImageWidth,
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Click to open in browser"
            };
            
            var image = new Image
            {
                MaxWidth = IrcTextFormatter.MaxImageWidth,
                MaxHeight = IrcTextFormatter.MaxImageHeight,
                Stretch = Stretch.Uniform
            };
            
            // Load image asynchronously
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(imageUrl);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = (int)IrcTextFormatter.MaxImageWidth;
            bitmap.EndInit();
            
            image.Source = bitmap;
            border.Child = image;
            
            // Click to open in browser
            border.MouseLeftButtonUp += (s, e) =>
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = imageUrl,
                        UseShellExecute = true
                    });
                }
                catch { }
            };
            
            return border;
        }
        catch
        {
            return null; // Failed to load image, skip it
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
