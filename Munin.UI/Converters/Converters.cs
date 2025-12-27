using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Munin.UI.Converters;

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/>.
/// Returns <see cref="Visibility.Visible"/> for true, <see cref="Visibility.Collapsed"/> for false.
/// </summary>
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Visible;
    }
}

/// <summary>
/// Converts a boolean value to <see cref="Visibility"/> with inverse logic.
/// Returns <see cref="Visibility.Collapsed"/> for true, <see cref="Visibility.Visible"/> for false.
/// </summary>
public class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is Visibility v && v == Visibility.Collapsed;
    }
}

/// <summary>
/// Converts a nullable value to <see cref="Visibility"/>.
/// Returns <see cref="Visibility.Visible"/> if the value is not null, <see cref="Visibility.Collapsed"/> if null.
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a nullable value to <see cref="Visibility"/> (inverse).
/// Returns <see cref="Visibility.Collapsed"/> if the value is not null, <see cref="Visibility.Visible"/> if null.
/// </summary>
public class NullToVisibilityConverterInverse : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a nullable value to a boolean.
/// Returns true if the value is not null, false if null.
/// </summary>
public class NullToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value != null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts an integer value to <see cref="Visibility"/>.
/// Returns <see cref="Visibility.Visible"/> if the value is greater than 0, <see cref="Visibility.Collapsed"/> otherwise.
/// </summary>
public class IntToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is int i && i > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Inverts a boolean value.
/// Returns false for true input, and true for false input.
/// </summary>
public class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && !b;
    }
}

/// <summary>
/// Converts a nullable value to <see cref="Visibility"/> (inverse).
/// Returns <see cref="Visibility.Collapsed"/> if the value is not null, <see cref="Visibility.Visible"/> if null.
/// </summary>
public class NullToVisibilityInverseConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value == null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a boolean value to opacity.
/// Returns 1.0 for true, 0.5 for false.
/// </summary>
public class BoolToOpacityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is bool b && b ? 1.0 : 0.5;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a row index (0 or 1) to a background brush for zebra striping.
/// Returns transparent for even rows, subtle highlight for odd rows.
/// </summary>
public class RowIndexToBackgroundConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int rowIndex && rowIndex % 2 == 1)
        {
            // Odd row - subtle highlight
            return Application.Current.TryFindResource("ZebraStripeBrush") ?? Brushes.Transparent;
        }
        return Brushes.Transparent;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a zero length to Visible, non-zero to Collapsed.
/// Used for showing placeholder text in empty text boxes.
/// </summary>
public class ZeroToVisibleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts away status boolean to tooltip text.
/// </summary>
public class AwayStatusTooltipConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var isAway = value is bool b && b;
        // Using English as fallback - localization handled through resources
        return isAway ? "Click to return from away" : "Click to set away";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts width and height to a Rect for clipping rounded borders.
/// </summary>
public class SizeToRectConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is double width && values[1] is double height)
        {
            return new Rect(0, 0, width, height);
        }
        return new Rect(0, 0, 0, 0);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts an integer to a display string with a maximum limit.
/// If the value exceeds 99, returns "99+" instead of the actual number.
/// </summary>
public class UnreadCountConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int count)
        {
            return count > 99 ? "99+" : count.ToString();
        }
        return "0";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
