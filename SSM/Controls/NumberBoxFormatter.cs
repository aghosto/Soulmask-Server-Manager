using ModernWpf.Controls;
using System;

namespace SoulmaskServerManager.Controls
{
    class NumberBoxFormatter : INumberBoxNumberFormatter
    {
        public string FormatDouble(double value)
        {
            return value.ToString("0.###");
        }

        public double? ParseDouble(string text)
        {
            if (double.TryParse(text, out double result))
            {
                return result;
            }
            return null;
        }

    }
}
