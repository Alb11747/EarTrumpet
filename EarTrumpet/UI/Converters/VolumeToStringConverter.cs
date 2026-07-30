using System;
using System.Windows.Data;

namespace EarTrumpet.UI.Converters;

public class VolumeToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is float vol)
        {
            if (!float.IsFinite(vol))
            {
                vol = App.Settings.UseLogarithmicVolume
                    ? App.Settings.LogarithmicVolumeMinDb
                    : 0f;
            }

            if (App.Settings.UseLogarithmicVolume)
            {
                // Special case for -0.0 display
                var formattedVolume = vol >= -0.05
                    ? "-0.0"
                    : vol.ToString("0.0", culture);
                return $"{formattedVolume} {Properties.Resources.VolumeUnit_Decibel}";
            }
            return vol.ToString(culture);
        }
        return "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
