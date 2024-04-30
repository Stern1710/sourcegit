﻿using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SourceGit.Converters
{
    public static class BoolConverters
    {
        public static readonly FuncValueConverter<bool, double> HalfIfFalse =
            new FuncValueConverter<bool, double>(x => x ? 1 : 0.5);

        public static readonly FuncValueConverter<bool, FontWeight> BoldIfTrue =
            new FuncValueConverter<bool, FontWeight>(x => x ? FontWeight.Bold : FontWeight.Regular);

        public static readonly FuncValueConverter<bool, GridLength> BoolToStarOrAutoGridLength =
            new(value => value ? new GridLength(1, GridUnitType.Star) : new GridLength(1, GridUnitType.Auto));
    }
}
