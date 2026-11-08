using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace EndorVPN.UI
{
    /// <summary>Shared brushes, effects and small drawing helpers for the dark theme.</summary>
    public static class Theme
    {
        public static readonly Color Bg = (Color)ColorConverter.ConvertFromString("#070B16");
        public static readonly Color BgElev = (Color)ColorConverter.ConvertFromString("#0D1426");
        public static readonly Color BgElev2 = (Color)ColorConverter.ConvertFromString("#121B31");
        public static readonly Color Line = (Color)ColorConverter.ConvertFromString("#1B2540");
        public static readonly Color Text = (Color)ColorConverter.ConvertFromString("#EAF0FF");
        public static readonly Color TextDim = (Color)ColorConverter.ConvertFromString("#93A0BD");
        public static readonly Color TextMute = (Color)ColorConverter.ConvertFromString("#64718E");
        public static readonly Color Accent = (Color)ColorConverter.ConvertFromString("#3B82F6");
        public static readonly Color Accent2 = (Color)ColorConverter.ConvertFromString("#22D3EE");
        public static readonly Color Good = (Color)ColorConverter.ConvertFromString("#22C55E");
        public static readonly Color Warn = (Color)ColorConverter.ConvertFromString("#F59E0B");
        public static readonly Color Bad = (Color)ColorConverter.ConvertFromString("#EF4444");

        public static readonly Brush BgBrush = Freeze(new SolidColorBrush(Bg));
        public static readonly Brush BgElevBrush = Freeze(new SolidColorBrush(BgElev));
        public static readonly Brush BgElev2Brush = Freeze(new SolidColorBrush(BgElev2));
        public static readonly Brush LineBrush = Freeze(new SolidColorBrush(Line));
        public static readonly Brush TextBrush = Freeze(new SolidColorBrush(Text));
        public static readonly Brush TextDimBrush = Freeze(new SolidColorBrush(TextDim));
        public static readonly Brush TextMuteBrush = Freeze(new SolidColorBrush(TextMute));
        public static readonly Brush AccentBrush = Freeze(new SolidColorBrush(Accent));
        public static readonly Brush AccentSoftBrush = Freeze(new SolidColorBrush(Color.FromArgb(40, Accent.R, Accent.G, Accent.B)));
        public static readonly Brush GoodBrush = Freeze(new SolidColorBrush(Good));
        public static readonly Brush WarnBrush = Freeze(new SolidColorBrush(Warn));
        public static readonly Brush BadBrush = Freeze(new SolidColorBrush(Bad));
        public static readonly Brush HoverBrush = Freeze(new SolidColorBrush(Color.FromArgb(18, 255, 255, 255)));

        public static readonly FontFamily Font = new FontFamily("Segoe UI");

        public static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }

        public static Brush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        public static DropShadowEffect Glow(Color color, double blur = 30, double opacity = 0.55, double depth = 0)
        {
            return new DropShadowEffect
            {
                Color = color,
                BlurRadius = blur,
                ShadowDepth = depth,
                Opacity = opacity,
                Direction = 270,
            };
        }

        public static TextBlock Label(string text, double size, Brush brush, FontWeight? weight = null)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = size,
                Foreground = brush ?? TextBrush,
                FontFamily = Font,
                FontWeight = weight ?? FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
        }

        /// <summary>Vector icon drawn from a geometry path string (24x24 viewbox).</summary>
        public static Path Icon(string pathData, double size, Brush brush)
        {
            var p = new Path
            {
                Data = Geometry.Parse(pathData),
                Fill = brush ?? TextDimBrush,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
                SnapsToDevicePixels = true,
            };
            RenderOptions.SetEdgeMode(p, EdgeMode.Aliased);
            return p;
        }

        public static Path StrokeIcon(string pathData, double size, Brush brush, double thickness = 2)
        {
            var p = new Path
            {
                Data = Geometry.Parse(pathData),
                Stroke = brush ?? TextDimBrush,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Stretch = Stretch.Uniform,
                Width = size,
                Height = size,
            };
            return p;
        }

        public static Border Card(UIElement child, double radius = 12, Brush bg = null, Brush border = null)
        {
            return new Border
            {
                CornerRadius = new CornerRadius(radius),
                Background = bg ?? Solid(Color.FromArgb(14, 255, 255, 255)),
                BorderBrush = border ?? LineBrush,
                BorderThickness = new Thickness(1),
                Child = child,
            };
        }

        public static void FadeIn(UIElement el, int ms = 220, double from = 0)
        {
            el.Opacity = from;
            var anim = new DoubleAnimation(from, 1, TimeSpan.FromMilliseconds(ms))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            el.BeginAnimation(UIElement.OpacityProperty, anim);
        }

    }

    /// <summary>Minimal helper for animating arbitrary numbers (not DPs).</summary>
    internal static class TweenHelper
    {
        public static void Run(Action<double> setter, double from, double to, int ms, IEasingFunction easing)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            timer.Tick += (s, e) =>
            {
                double t = Math.Min(1.0, sw.Elapsed.TotalMilliseconds / ms);
                double k = easing != null ? Ease(easing, t) : t;
                setter(from + (to - from) * k);
                if (t >= 1.0) timer.Stop();
            };
            timer.Start();
        }

        private static double Ease(IEasingFunction fn, double t)
        {
            var ease = fn as EasingFunctionBase;
            if (ease == null) return t;
            // Quadratic in/out approximation is plenty for our scale of motion.
            return t * t * (3 - 2 * t);
        }
    }

}
