﻿using System;
using System.Globalization;
using System.Windows.Data;

namespace EQLogParser
{
  public class ZeroConverter : IValueConverter
  {
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value?.GetType() == typeof(uint) || value?.GetType() == typeof(double))
      {
        return System.Convert.ToDouble(value, CultureInfo.CurrentCulture) > 0 ? value.ToString() : "-";
      }
      return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
      if (value is string)
      {
        if (!double.TryParse((string)value, out double decValue))
        {
          decValue = 0;
        }
        return decValue;
      }
      return 0;
    }
  }
}
