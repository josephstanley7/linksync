using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace LinkSync.Desktop.Services
{
    public enum AnnotationTool
    {
        Select,
        Arrow,
        StepBadge,
        Rectangle,
        Spotlight,
        Pixelate,
        Blur,
        Highlighter,
        Text
    }

    public interface IAnnotation
    {
        void Render(DrawingContext dc, double scale);
    }

    public class ArrowAnnotation : IAnnotation
    {
        public Point Start { get; set; }
        public Point End { get; set; }
        public Color Color { get; set; } = Color.FromRgb(239, 68, 68); // Red
        public double Thickness { get; set; } = 4.0;

        public void Render(DrawingContext dc, double scale)
        {
            double lineThick = Thickness * scale;
            var pen = new Pen(new SolidColorBrush(Color), lineThick)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };

            Point s = new(Start.X * scale, Start.Y * scale);
            Point e = new(End.X * scale, End.Y * scale);

            dc.DrawLine(pen, s, e);

            // Vector Arrow Head
            double dx = e.X - s.X;
            double dy = e.Y - s.Y;
            double len = Math.Sqrt(dx * dx + dy * dy);
            if (len < 5) return;

            double headLen = Math.Max(14 * scale, lineThick * 3.5);
            double angle = Math.Atan2(dy, dx);
            double arrowAngle = Math.PI / 6.0; // 30 degrees

            Point p1 = new(e.X - headLen * Math.Cos(angle - arrowAngle), e.Y - headLen * Math.Sin(angle - arrowAngle));
            Point p2 = new(e.X - headLen * Math.Cos(angle + arrowAngle), e.Y - headLen * Math.Sin(angle + arrowAngle));

            var headGeo = new PathGeometry();
            var figure = new PathFigure { StartPoint = e, IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(p1, true));
            figure.Segments.Add(new LineSegment(p2, true));
            headGeo.Figures.Add(figure);

            dc.DrawGeometry(new SolidColorBrush(Color), null, headGeo);
        }
    }

    public class StepBadgeAnnotation : IAnnotation
    {
        public Point Center { get; set; }
        public int Number { get; set; } = 1;
        public Color FillColor { get; set; } = Color.FromRgb(37, 99, 235); // Blue
        public double Radius { get; set; } = 18.0;

        public void Render(DrawingContext dc, double scale)
        {
            double r = Radius * scale;
            Point c = new(Center.X * scale, Center.Y * scale);

            // Shadow
            dc.DrawEllipse(new SolidColorBrush(Color.FromArgb(80, 0, 0, 0)), null, new Point(c.X + 2, c.Y + 2), r, r);

            // Badge Circle
            dc.DrawEllipse(new SolidColorBrush(FillColor), new Pen(Brushes.White, 2.0 * scale), c, r, r);

            // Centered Number
            string text = Number.ToString();
            var formattedText = new FormattedText(
                text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal),
                r * 1.15,
                Brushes.White,
                VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window()).PixelsPerDip);

            Point textPos = new(c.X - formattedText.Width / 2.0, c.Y - formattedText.Height / 2.0);
            dc.DrawText(formattedText, textPos);
        }
    }

    public class RectAnnotation : IAnnotation
    {
        public Rect Bounds { get; set; }
        public Color Color { get; set; } = Color.FromRgb(239, 68, 68);
        public double Thickness { get; set; } = 3.5;
        public bool HasFill { get; set; } = false;

        public void Render(DrawingContext dc, double scale)
        {
            Rect r = new(Bounds.X * scale, Bounds.Y * scale, Bounds.Width * scale, Bounds.Height * scale);
            var pen = new Pen(new SolidColorBrush(Color), Thickness * scale);
            Brush? fill = HasFill ? new SolidColorBrush(Color.FromArgb(40, Color.R, Color.G, Color.B)) : null;

            dc.DrawRoundedRectangle(fill, pen, r, 6 * scale, 6 * scale);
        }
    }

    public class SpotlightAnnotation : IAnnotation
    {
        public Rect SpotlightRect { get; set; }
        public double CanvasWidth { get; set; }
        public double CanvasHeight { get; set; }

        public void Render(DrawingContext dc, double scale)
        {
            Rect full = new(0, 0, CanvasWidth * scale, CanvasHeight * scale);
            Rect spot = new(SpotlightRect.X * scale, SpotlightRect.Y * scale, SpotlightRect.Width * scale, SpotlightRect.Height * scale);

            var outer = new RectangleGeometry(full);
            var inner = new RectangleGeometry(spot, 8 * scale, 8 * scale);

            var combined = new CombinedGeometry(GeometryCombineMode.Exclude, outer, inner);
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(160, 0, 0, 0)), null, combined);

            // Highlight border around spotlight area
            dc.DrawRoundedRectangle(null, new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 2.5 * scale), spot, 8 * scale, 8 * scale);
        }
    }

    public class HighlighterAnnotation : IAnnotation
    {
        public List<Point> Points { get; set; } = new();
        public Color Color { get; set; } = Color.FromArgb(120, 250, 204, 21); // Translucent Yellow
        public double Thickness { get; set; } = 16.0;

        public void Render(DrawingContext dc, double scale)
        {
            if (Points.Count < 2) return;

            var pen = new Pen(new SolidColorBrush(Color), Thickness * scale)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round,
                LineJoin = PenLineJoin.Round
            };

            var geo = new StreamGeometry();
            using (var ctx = geo.Open())
            {
                ctx.BeginFigure(new Point(Points[0].X * scale, Points[0].Y * scale), false, false);
                for (int i = 1; i < Points.Count; i++)
                {
                    ctx.LineTo(new Point(Points[i].X * scale, Points[i].Y * scale), true, true);
                }
            }

            dc.DrawGeometry(null, pen, geo);
        }
    }

    public class TextAnnotation : IAnnotation
    {
        public Point Position { get; set; }
        public string Text { get; set; } = string.Empty;
        public Color BackgroundColor { get; set; } = Color.FromRgb(15, 23, 42); // Slate 900
        public Color TextColor { get; set; } = Color.FromRgb(248, 250, 252);
        public double FontSize { get; set; } = 18.0;

        public void Render(DrawingContext dc, double scale)
        {
            if (string.IsNullOrWhiteSpace(Text)) return;

            double fs = FontSize * scale;
            var formatted = new FormattedText(
                Text,
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
                fs,
                new SolidColorBrush(TextColor),
                VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? new Window()).PixelsPerDip);

            double pad = 8.0 * scale;
            Point p = new(Position.X * scale, Position.Y * scale);
            Rect bgRect = new(p.X, p.Y, formatted.Width + (pad * 2), formatted.Height + (pad * 2));

            // Background pill with subtle border
            dc.DrawRoundedRectangle(
                new SolidColorBrush(BackgroundColor),
                new Pen(new SolidColorBrush(Color.FromRgb(51, 65, 85)), 1.5 * scale),
                bgRect, 6 * scale, 6 * scale);

            dc.DrawText(formatted, new Point(p.X + pad, p.Y + pad));
        }
    }
}
