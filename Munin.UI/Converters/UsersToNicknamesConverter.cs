using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Data;
using Munin.UI.ViewModels;

namespace Munin.UI.Converters;

/// <summary>
/// Converts a collection of UserViewModel to a list of nickname strings for tab completion.
/// </summary>
public class UsersToNicknamesConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ObservableCollection<UserViewModel> users)
        {
            return users.Select(u => u.User.Nickname).ToList();
        }
        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
