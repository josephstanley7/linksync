using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using LinkSync.Desktop.Services;
using Microsoft.Win32;
using Path = System.IO.Path;

namespace LinkSync.Desktop
{
    public partial class ImageAnnotatorWindow : Window
    {
        private string _filePath = string.Empty;
        private WriteableBitmap _baseBitmap = null!;
        private readonly Stack<WriteableBitmap> _bitmapUndoStack = new();
        private readonly List<IAnnotation> _annotations = new();
        private readonly Stack<List<IAnnotation>> _annotationUndoStack = new();
        private readonly Stack<List<IAnnotation>> _annotationRedoStack = new();

        private AnnotationTool _currentTool = AnnotationTool.Arrow;
        private Color _activeColor = Color.FromRgb(239, 68, 68);
        private double _activeThickness = 4.0;
        private int _stepBadgeCounter = 1;

        private bool _isDrawing = false;
        private Point _startPoint;
        private readonly List<Point> _highlighterPoints = new();
        private Shape? _previewShape = null;

        public ImageAnnotatorWindow(string imagePath)
        {
            InitializeComponent();
            _filePath = imagePath;
            LoadImage(imagePath);
        }

        private void LoadImage(string path)
        {
            if (!File.Exists(path)) return;

            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(path);
                bi.EndInit();
                bi.Freeze();

                var conv = new FormatConvertedBitmap(bi, PixelFormats.Bgra32, null, 0);
                _baseBitmap = new WriteableBitmap(conv);

                ImgBase.Source = _baseBitmap;
                AnnotCanvas.Width = _baseBitmap.PixelWidth;
                AnnotCanvas.Height = _baseBitmap.PixelHeight;

                TxtDimensions.Text = $"{_baseBitmap.PixelWidth} × {_baseBitmap.PixelHeight} px";
                TxtStatus.Text = $"Loaded: {Path.GetFileName(path)}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region Tool & Color Selection

        private void Tool_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;

            _currentTool = rb.Name switch
            {
                "ToolArrow" => AnnotationTool.Arrow,
                "ToolStep" => AnnotationTool.StepBadge,
                "ToolRect" => AnnotationTool.Rectangle,
                "ToolSpotlight" => AnnotationTool.Spotlight,
                "ToolPixelate" => AnnotationTool.Pixelate,
                "ToolBlur" => AnnotationTool.Blur,
                "ToolHighlight" => AnnotationTool.Highlighter,
                "ToolText" => AnnotationTool.Text,
                _ => AnnotationTool.Arrow
            };

            if (TxtStatus != null)
            {
                TxtStatus.Text = _currentTool switch
                {
                    AnnotationTool.Arrow => "Tool: Arrow • Drag from start to end",
                    AnnotationTool.StepBadge => $"Tool: Step Badge • Click to drop badge #{_stepBadgeCounter}",
                    AnnotationTool.Rectangle => "Tool: Box • Drag to highlight region",
                    AnnotationTool.Spotlight => "Tool: Spotlight • Drag to spotlight area (darkens background)",
                    AnnotationTool.Pixelate => "Tool: Pixelate • Drag rectangle to obscure passwords/tokens",
                    AnnotationTool.Blur => "Tool: Blur • Drag rectangle to blur sensitive information",
                    AnnotationTool.Highlighter => "Tool: Highlighter • Drag across text",
                    AnnotationTool.Text => "Tool: Text Callout • Click where you want to add text",
                    _ => "Select a tool"
                };
            }
        }

        private void Color_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            _activeColor = rb.Name switch
            {
                "ColorRed" => Color.FromRgb(239, 68, 68),
                "ColorBlue" => Color.FromRgb(59, 130, 246),
                "ColorGreen" => Color.FromRgb(16, 185, 129),
                "ColorAmber" => Color.FromRgb(245, 158, 11),
                "ColorPurple" => Color.FromRgb(139, 92, 246),
                "ColorWhite" => Color.FromRgb(255, 255, 255),
                _ => Color.FromRgb(239, 68, 68)
            };
        }

        private void Size_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            _activeThickness = rb.Name switch
            {
                "SizeThin" => 2.0,
                "SizeMed" => 4.0,
                "SizeThick" => 8.0,
                _ => 4.0
            };
        }

        private void ChkBezelMockup_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                bool isBezel = ChkBezelMockup.IsChecked == true;
                if (isBezel)
                {
                    if (CanvasHost.Parent is Panel p)
                    {
                        p.Children.Remove(CanvasHost);
                    }
                    BezelScreenSlot.Content = CanvasHost;
                    BezelBorder.Visibility = Visibility.Visible;
                    TxtStatus.Text = "Samsung Galaxy A Bezel Mockup: ENABLED";
                }
                else
                {
                    BezelScreenSlot.Content = null;
                    BezelBorder.Visibility = Visibility.Collapsed;
                    if (BezelBorder.Parent is Grid parentGrid && !parentGrid.Children.Contains(CanvasHost))
                    {
                        parentGrid.Children.Add(CanvasHost);
                    }
                    TxtStatus.Text = "Standard Screenshot View";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ImageAnnotator] Mockup toggle error: {ex.Message}");
            }
        }

        #endregion

        #region Mouse Canvas Interaction

        private void AnnotCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed) return;

            Point p = e.GetPosition(AnnotCanvas);
            _startPoint = p;
            _isDrawing = true;
            AnnotCanvas.CaptureMouse();

            if (_currentTool == AnnotationTool.StepBadge)
            {
                SaveAnnotationState();
                var badge = new StepBadgeAnnotation
                {
                    Center = p,
                    Number = _stepBadgeCounter++,
                    FillColor = _activeColor,
                    Radius = 18.0
                };
                _annotations.Add(badge);
                _isDrawing = false;
                AnnotCanvas.ReleaseMouseCapture();
                RedrawAnnotations();
                TxtStatus.Text = $"Placed Step Badge #{badge.Number} • Next is #{_stepBadgeCounter}";
                return;
            }

            if (_currentTool == AnnotationTool.Text)
            {
                _isDrawing = false;
                AnnotCanvas.ReleaseMouseCapture();
                PromptForText(p);
                return;
            }

            if (_currentTool == AnnotationTool.Highlighter)
            {
                _highlighterPoints.Clear();
                _highlighterPoints.Add(p);
            }
        }

        private void AnnotCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawing) return;

            Point cur = e.GetPosition(AnnotCanvas);

            // Remove previous preview
            if (_previewShape != null)
            {
                AnnotCanvas.Children.Remove(_previewShape);
                _previewShape = null;
            }

            switch (_currentTool)
            {
                case AnnotationTool.Arrow:
                    var arrowLine = new System.Windows.Shapes.Line
                    {
                        X1 = _startPoint.X,
                        Y1 = _startPoint.Y,
                        X2 = cur.X,
                        Y2 = cur.Y,
                        Stroke = new SolidColorBrush(_activeColor),
                        StrokeThickness = _activeThickness,
                        StrokeDashArray = new DoubleCollection { 3, 2 }
                    };
                    _previewShape = arrowLine;
                    AnnotCanvas.Children.Add(arrowLine);
                    break;

                case AnnotationTool.Rectangle:
                case AnnotationTool.Spotlight:
                case AnnotationTool.Pixelate:
                case AnnotationTool.Blur:
                    double rx = Math.Min(_startPoint.X, cur.X);
                    double ry = Math.Min(_startPoint.Y, cur.Y);
                    double rw = Math.Abs(cur.X - _startPoint.X);
                    double rh = Math.Abs(cur.Y - _startPoint.Y);

                    var rect = new System.Windows.Shapes.Rectangle
                    {
                        Width = rw,
                        Height = rh,
                        Stroke = new SolidColorBrush(_activeColor),
                        StrokeThickness = 2,
                        StrokeDashArray = new DoubleCollection { 4, 3 },
                        Fill = (_currentTool == AnnotationTool.Pixelate || _currentTool == AnnotationTool.Blur)
                            ? new SolidColorBrush(Color.FromArgb(50, 0, 0, 0))
                            : null
                    };
                    Canvas.SetLeft(rect, rx);
                    Canvas.SetTop(rect, ry);
                    _previewShape = rect;
                    AnnotCanvas.Children.Add(rect);
                    break;

                case AnnotationTool.Highlighter:
                    _highlighterPoints.Add(cur);
                    RedrawHighlighterPreview();
                    break;
            }
        }

        private void AnnotCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawing) return;
            _isDrawing = false;
            AnnotCanvas.ReleaseMouseCapture();

            if (_previewShape != null)
            {
                AnnotCanvas.Children.Remove(_previewShape);
                _previewShape = null;
            }

            Point end = e.GetPosition(AnnotCanvas);
            double dx = Math.Abs(end.X - _startPoint.X);
            double dy = Math.Abs(end.Y - _startPoint.Y);

            // Minimum drag threshold
            if (dx < 3 && dy < 3 && _currentTool != AnnotationTool.Highlighter) return;

            SaveAnnotationState();

            switch (_currentTool)
            {
                case AnnotationTool.Arrow:
                    _annotations.Add(new ArrowAnnotation
                    {
                        Start = _startPoint,
                        End = end,
                        Color = _activeColor,
                        Thickness = _activeThickness
                    });
                    break;

                case AnnotationTool.Rectangle:
                    _annotations.Add(new RectAnnotation
                    {
                        Bounds = new Rect(Math.Min(_startPoint.X, end.X), Math.Min(_startPoint.Y, end.Y), dx, dy),
                        Color = _activeColor,
                        Thickness = _activeThickness
                    });
                    break;

                case AnnotationTool.Spotlight:
                    _annotations.Add(new SpotlightAnnotation
                    {
                        SpotlightRect = new Rect(Math.Min(_startPoint.X, end.X), Math.Min(_startPoint.Y, end.Y), dx, dy),
                        CanvasWidth = _baseBitmap.PixelWidth,
                        CanvasHeight = _baseBitmap.PixelHeight
                    });
                    break;

                case AnnotationTool.Pixelate:
                    ApplyPixelateRedaction(new Int32Rect((int)Math.Min(_startPoint.X, end.X), (int)Math.Min(_startPoint.Y, end.Y), (int)dx, (int)dy));
                    break;

                case AnnotationTool.Blur:
                    ApplyBlurRedaction(new Int32Rect((int)Math.Min(_startPoint.X, end.X), (int)Math.Min(_startPoint.Y, end.Y), (int)dx, (int)dy));
                    break;

                case AnnotationTool.Highlighter:
                    if (_highlighterPoints.Count > 1)
                    {
                        _annotations.Add(new HighlighterAnnotation
                        {
                            Points = new List<Point>(_highlighterPoints),
                            Color = Color.FromArgb(120, _activeColor.R, _activeColor.G, _activeColor.B),
                            Thickness = _activeThickness * 3.5
                        });
                    }
                    _highlighterPoints.Clear();
                    break;
            }

            RedrawAnnotations();
        }

        private void PromptForText(Point position)
        {
            var inputWindow = new Window
            {
                Title = "Add Text Callout",
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var txtLabel = new TextBlock { Text = "Enter annotation text:", Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 8) };
            var textBox = new TextBox { Text = "Bug observed here", Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)), Foreground = Brushes.White, Padding = new Thickness(6) };
            var btnOk = new Button
            {
                Content = "Add Callout",
                Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                Padding = new Thickness(12, 6, 12, 6),
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 12, 0, 0),
                Cursor = Cursors.Hand
            };

            btnOk.Click += (s, e) =>
            {
                if (!string.IsNullOrWhiteSpace(textBox.Text))
                {
                    SaveAnnotationState();
                    _annotations.Add(new TextAnnotation
                    {
                        Position = position,
                        Text = textBox.Text.Trim(),
                        TextColor = Colors.White,
                        BackgroundColor = Color.FromRgb(15, 23, 42),
                        FontSize = 18.0
                    });
                    RedrawAnnotations();
                }
                inputWindow.Close();
            };

            textBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) btnOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if (e.Key == Key.Escape) inputWindow.Close();
            };

            Grid.SetRow(txtLabel, 0);
            Grid.SetRow(textBox, 1);
            Grid.SetRow(btnOk, 2);

            grid.Children.Add(txtLabel);
            grid.Children.Add(textBox);
            grid.Children.Add(btnOk);
            inputWindow.Content = grid;
            textBox.Focus();
            textBox.SelectAll();
            inputWindow.ShowDialog();
        }

        #endregion

        #region Pixelate & Blur Redaction

        private void ApplyPixelateRedaction(Int32Rect region)
        {
            _bitmapUndoStack.Push(_baseBitmap.Clone());
            _baseBitmap = PixelateHelper.ApplyPixelate(_baseBitmap, region, blockSize: 14);
            ImgBase.Source = _baseBitmap;
            TxtStatus.Text = "Applied Mosaic Pixelation redaction";
        }

        private void ApplyBlurRedaction(Int32Rect region)
        {
            _bitmapUndoStack.Push(_baseBitmap.Clone());
            _baseBitmap = PixelateHelper.ApplyBlur(_baseBitmap, region, radius: 8);
            ImgBase.Source = _baseBitmap;
            TxtStatus.Text = "Applied Gaussian Blur redaction";
        }

        #endregion

        #region Redrawing & Rendering

        private void RedrawAnnotations()
        {
            AnnotCanvas.Children.Clear();

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                foreach (var ann in _annotations)
                {
                    ann.Render(dc, 1.0);
                }
            }

            var rtb = new RenderTargetBitmap(
                _baseBitmap.PixelWidth,
                _baseBitmap.PixelHeight,
                96, 96,
                PixelFormats.Pbgra32);
            rtb.Render(dv);

            var img = new Image
            {
                Source = rtb,
                Width = _baseBitmap.PixelWidth,
                Height = _baseBitmap.PixelHeight
            };
            AnnotCanvas.Children.Add(img);
        }

        private void RedrawHighlighterPreview()
        {
            if (_previewShape != null) AnnotCanvas.Children.Remove(_previewShape);

            var polyline = new System.Windows.Shapes.Polyline
            {
                Stroke = new SolidColorBrush(Color.FromArgb(120, _activeColor.R, _activeColor.G, _activeColor.B)),
                StrokeThickness = _activeThickness * 3.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round
            };

            foreach (var pt in _highlighterPoints)
            {
                polyline.Points.Add(pt);
            }

            _previewShape = polyline;
            AnnotCanvas.Children.Add(polyline);
        }

        #endregion

        #region Undo / Redo & State

        private void SaveAnnotationState()
        {
            _annotationUndoStack.Push(new List<IAnnotation>(_annotations));
            _annotationRedoStack.Clear();
        }

        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (_annotations.Count > 0 && _annotationUndoStack.Count > 0)
            {
                _annotationRedoStack.Push(new List<IAnnotation>(_annotations));
                var prev = _annotationUndoStack.Pop();
                _annotations.Clear();
                _annotations.AddRange(prev);
                RedrawAnnotations();
                TxtStatus.Text = "Undid last annotation";
                return;
            }

            if (_bitmapUndoStack.Count > 0)
            {
                _baseBitmap = _bitmapUndoStack.Pop();
                ImgBase.Source = _baseBitmap;
                TxtStatus.Text = "Undid redaction";
            }
        }

        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (_annotationRedoStack.Count > 0)
            {
                _annotationUndoStack.Push(new List<IAnnotation>(_annotations));
                var next = _annotationRedoStack.Pop();
                _annotations.Clear();
                _annotations.AddRange(next);
                RedrawAnnotations();
                TxtStatus.Text = "Redid annotation";
            }
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (_annotations.Count == 0) return;
            SaveAnnotationState();
            _annotations.Clear();
            _stepBadgeCounter = 1;
            RedrawAnnotations();
            TxtStatus.Text = "Cleared all annotations (Badge counter reset to 1)";
        }

        #endregion

        #region Export & Save

        private RenderTargetBitmap GenerateFullComposite()
        {
            int w = _baseBitmap.PixelWidth;
            int h = _baseBitmap.PixelHeight;

            bool isBezel = ChkBezelMockup.IsChecked == true;

            int targetW = isBezel ? w + 160 : w;
            int targetH = isBezel ? h + 240 : h;
            double offsetX = isBezel ? 80 : 0;
            double offsetY = isBezel ? 140 : 0;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                if (isBezel)
                {
                    // Draw outer phone chassis
                    var outerRect = new Rect(20, 20, targetW - 40, targetH - 40);
                    dc.DrawRoundedRectangle(
                        new SolidColorBrush(Color.FromRgb(24, 24, 27)), // Sleek Zinc chassis
                        new Pen(new SolidColorBrush(Color.FromRgb(63, 63, 70)), 3.0),
                        outerRect, 56, 56);

                    // Top Speaker Grill
                    dc.DrawRoundedRectangle(
                        new SolidColorBrush(Color.FromRgb(9, 9, 11)),
                        null,
                        new Rect(targetW / 2.0 - 45, 50, 90, 8), 4, 4);

                    // Front Camera Punch-hole
                    dc.DrawEllipse(
                        new SolidColorBrush(Color.FromRgb(9, 9, 11)),
                        new Pen(new SolidColorBrush(Color.FromRgb(39, 39, 42)), 1.5),
                        new Point(targetW / 2.0, 95), 10, 10);
                }

                // Draw base screenshot
                dc.PushTransform(new TranslateTransform(offsetX, offsetY));
                dc.DrawImage(_baseBitmap, new Rect(0, 0, w, h));

                // Draw all annotations
                foreach (var ann in _annotations)
                {
                    ann.Render(dc, 1.0);
                }
                dc.Pop();
            }

            var rtb = new RenderTargetBitmap(targetW, targetH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var composited = GenerateFullComposite();
                Clipboard.SetImage(composited);
                TxtStatus.Text = "✅ Annotated image copied to Windows Clipboard! Paste with Ctrl+V.";
                MessageBox.Show("Annotated screenshot copied to Windows Clipboard!\nYou can now paste it directly into Telegram, GitHub, Slack, or Word.", "LinkSync Annotator", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to copy image: {ex.Message}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(_filePath))
            {
                BtnSaveAs_Click(sender, e);
                return;
            }

            try
            {
                SaveToFile(_filePath);
                TxtStatus.Text = $"✅ Saved: {Path.GetFileName(_filePath)}";
                MessageBox.Show($"Saved successfully to:\n{_filePath}", "LinkSync Annotator", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save: {ex.Message}", "LinkSync", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var sfd = new SaveFileDialog
            {
                Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg",
                FileName = $"Annotated_{Path.GetFileNameWithoutExtension(_filePath)}.png"
            };

            if (sfd.ShowDialog() == true)
            {
                SaveToFile(sfd.FileName);
                _filePath = sfd.FileName;
                TxtStatus.Text = $"✅ Saved as: {Path.GetFileName(_filePath)}";
            }
        }

        private void SaveToFile(string path)
        {
            var composited = GenerateFullComposite();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(composited));

            using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(fs);
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Z)
            {
                BtnUndo_Click(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Y)
            {
                BtnRedo_Click(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.C)
            {
                BtnCopy_Click(sender, e);
                e.Handled = true;
            }
            else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.S)
            {
                BtnSave_Click(sender, e);
                e.Handled = true;
            }
        }

        #endregion
    }
}
