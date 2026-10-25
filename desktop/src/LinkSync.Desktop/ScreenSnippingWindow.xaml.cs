using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;

namespace LinkSync.Desktop
{
    public enum SnipAction
    {
        CopyAndSync,
        Annotate,
        Save
    }

    public partial class ScreenSnippingWindow : Window
    {
        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private readonly Bitmap _fullScreenBitmap;
        private readonly double _scaleX;
        private readonly double _scaleY;

        private bool _isDragging = false;
        private Point _startPoint;
        private Point _currentPoint;
        private Rect _selectedRect;
        private readonly bool _isRecordingMode;

        public event Action<Bitmap, SnipAction>? SnipCompleted;
        public event Action<Rect>? RecordRegionRequested;

        public ScreenSnippingWindow(Bitmap fullScreenBitmap, bool isRecordingMode = false)
        {
            InitializeComponent();
            _fullScreenBitmap = fullScreenBitmap;
            _isRecordingMode = isRecordingMode;

            if (_isRecordingMode)
            {
                BtnRecordRegion.Content = "▶ Start Recording Region";
                BtnRecordRegion.FontSize = 12;
                BtnRecordRegion.Padding = new Thickness(12, 6, 12, 6);
            }

            // Cover full virtual screen across all monitors
            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            SnipCanvas.Width = Width;
            SnipCanvas.Height = Height;

            RectDimmer.Width = Width;
            RectDimmer.Height = Height;

            _scaleX = (double)_fullScreenBitmap.Width / Width;
            _scaleY = (double)_fullScreenBitmap.Height / Height;

            // Convert to WPF BitmapSource and set images
            var source = ToBitmapSource(_fullScreenBitmap);
            ImgBackground.Source = source;
            ImgClipped.Source = source;

            ImgBackground.Width = Width;
            ImgBackground.Height = Height;
            ImgClipped.Width = Width;
            ImgClipped.Height = Height;

            LineGuideH.X1 = 0;
            LineGuideH.X2 = Width;
            LineGuideV.Y1 = 0;
            LineGuideV.Y2 = Height;
        }

        private static BitmapSource ToBitmapSource(Bitmap bitmap)
        {
            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                return Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Right)
            {
                Close();
                return;
            }

            if (e.ChangedButton == MouseButton.Left)
            {
                // Check if user clicked on action bar
                if (BarActions.Visibility == Visibility.Visible)
                {
                    Point pt = e.GetPosition(BarActions);
                    if (pt.X >= 0 && pt.X <= BarActions.ActualWidth && pt.Y >= 0 && pt.Y <= BarActions.ActualHeight)
                    {
                        return; // Let button clicks pass
                    }
                }

                _isDragging = true;
                _startPoint = e.GetPosition(SnipCanvas);
                _currentPoint = _startPoint;

                BarActions.Visibility = Visibility.Collapsed;
                RectSelectionBorder.Visibility = Visibility.Visible;
                PillInfo.Visibility = Visibility.Visible;

                UpdateSelection();
            }
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            Point mousePos = e.GetPosition(SnipCanvas);

            // Update crosshair guides
            LineGuideH.Y1 = mousePos.Y;
            LineGuideH.Y2 = mousePos.Y;
            LineGuideV.X1 = mousePos.X;
            LineGuideV.X2 = mousePos.X;

            if (LineGuideH.Visibility != Visibility.Visible)
            {
                LineGuideH.Visibility = Visibility.Visible;
                LineGuideV.Visibility = Visibility.Visible;
            }

            if (_isDragging)
            {
                _currentPoint = mousePos;
                UpdateSelection();
            }
        }

        private void UpdateSelection()
        {
            double x = Math.Min(_startPoint.X, _currentPoint.X);
            double y = Math.Min(_startPoint.Y, _currentPoint.Y);
            double w = Math.Abs(_startPoint.X - _currentPoint.X);
            double h = Math.Abs(_startPoint.Y - _currentPoint.Y);

            _selectedRect = new Rect(x, y, w, h);

            ClipGeometry.Rect = _selectedRect;

            Canvas.SetLeft(RectSelectionBorder, x);
            Canvas.SetTop(RectSelectionBorder, y);
            RectSelectionBorder.Width = w;
            RectSelectionBorder.Height = h;

            int realW = (int)Math.Round(w * _scaleX);
            int realH = (int)Math.Round(h * _scaleY);
            TxtInfo.Text = $"{realW} × {realH} px";

            double pillY = y - 32;
            if (pillY < 10) pillY = y + h + 10;
            Canvas.SetLeft(PillInfo, Math.Max(10, x));
            Canvas.SetTop(PillInfo, Math.Max(10, pillY));
        }

        private void Window_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isDragging && e.ChangedButton == MouseButton.Left)
            {
                _isDragging = false;

                if (_selectedRect.Width < 8 || _selectedRect.Height < 8)
                {
                    // Click without drag: reset
                    RectSelectionBorder.Visibility = Visibility.Collapsed;
                    ClipGeometry.Rect = new Rect(0, 0, 0, 0);
                    PillInfo.Visibility = Visibility.Collapsed;
                    BarActions.Visibility = Visibility.Collapsed;
                    return;
                }

                // Double click check: if rapid clicks, trigger instant action
                if (e.ClickCount >= 2)
                {
                    if (_isRecordingMode)
                    {
                        TriggerRecordRegion();
                    }
                    else
                    {
                        ExecuteAction(SnipAction.CopyAndSync);
                    }
                    return;
                }

                // Position Action Toolbar at bottom-right of selection
                BarActions.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
                double barWidth = BarActions.DesiredSize.Width > 0 ? BarActions.DesiredSize.Width : 260;
                double barHeight = BarActions.DesiredSize.Height > 0 ? BarActions.DesiredSize.Height : 40;

                double barX = _selectedRect.Right - barWidth;
                double barY = _selectedRect.Bottom + 10;

                if (barX < 10) barX = 10;
                if (barX + barWidth > Width - 10) barX = Width - barWidth - 10;
                if (barY + barHeight > Height - 10) barY = _selectedRect.Top - barHeight - 10;

                Canvas.SetLeft(BarActions, barX);
                Canvas.SetTop(BarActions, barY);
                BarActions.Visibility = Visibility.Visible;
            }
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
            else if (e.Key == Key.Enter && _selectedRect.Width > 5)
            {
                if (_isRecordingMode)
                {
                    TriggerRecordRegion();
                }
                else
                {
                    ExecuteAction(SnipAction.CopyAndSync);
                }
            }
        }

        private void TriggerRecordRegion()
        {
            if (_selectedRect.Width < 16 || _selectedRect.Height < 16) return;

            int cropX = (int)Math.Max(0, Math.Round(_selectedRect.X * _scaleX));
            int cropY = (int)Math.Max(0, Math.Round(_selectedRect.Y * _scaleY));
            int cropW = (int)Math.Round(_selectedRect.Width * _scaleX);
            int cropH = (int)Math.Round(_selectedRect.Height * _scaleY);

            RecordRegionRequested?.Invoke(new Rect(cropX, cropY, cropW, cropH));
            Close();
        }

        private void BtnRecordRegion_Click(object sender, RoutedEventArgs e)
        {
            TriggerRecordRegion();
        }

        private Bitmap? CropSelectedRegion()
        {
            if (_selectedRect.Width < 2 || _selectedRect.Height < 2) return null;

            int cropX = (int)Math.Max(0, Math.Round(_selectedRect.X * _scaleX));
            int cropY = (int)Math.Max(0, Math.Round(_selectedRect.Y * _scaleY));
            int cropW = (int)Math.Round(_selectedRect.Width * _scaleX);
            int cropH = (int)Math.Round(_selectedRect.Height * _scaleY);

            if (cropX + cropW > _fullScreenBitmap.Width) cropW = _fullScreenBitmap.Width - cropX;
            if (cropY + cropH > _fullScreenBitmap.Height) cropH = _fullScreenBitmap.Height - cropY;

            if (cropW <= 0 || cropH <= 0) return null;

            var cropped = new Bitmap(cropW, cropH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(cropped))
            {
                g.DrawImage(_fullScreenBitmap,
                    new Rectangle(0, 0, cropW, cropH),
                    new Rectangle(cropX, cropY, cropW, cropH),
                    GraphicsUnit.Pixel);
            }
            return cropped;
        }

        private void ExecuteAction(SnipAction action)
        {
            var cropped = CropSelectedRegion();
            if (cropped != null)
            {
                SnipCompleted?.Invoke(cropped, action);
            }
            Close();
        }

        private void BtnCopySync_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAction(SnipAction.CopyAndSync);
        }

        private void BtnAnnotate_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAction(SnipAction.Annotate);
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            ExecuteAction(SnipAction.Save);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
