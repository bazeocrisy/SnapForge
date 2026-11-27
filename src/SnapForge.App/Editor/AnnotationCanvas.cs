using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SnapForge.App.Rendering;
using SnapForge.Core.Annotations;
using SnapForge.Core.Editing;
using SnapForge.Core.Geometry;
using SnapForge.Core.Imaging;

namespace SnapForge.App.Editor;

/// <summary>
/// The editing surface. Its size in WPF units equals the image size in pixels (see
/// <see cref="BitmapConversion.ImageDpi"/>), so mouse positions are image coordinates. Zoom is applied by the
/// parent's LayoutTransform. All edits go through <see cref="EditorDocument.Execute"/> and are undoable.
/// </summary>
internal sealed class AnnotationCanvas : Canvas
{
    private static readonly Brush SelectionBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x7A, 0x30)));
    private static readonly Brush RedactPreviewFill = Freeze(new SolidColorBrush(Color.FromArgb(0xB4, 0, 0, 0)));

    private EditorDocument? _document;
    private WriteableBitmap? _bitmap;

    private PointD? _dragStart;
    private Annotation? _preview;
    private PixelRect? _redactPreview;

    private Guid? _selectedId;
    private Annotation? _moveOriginal;
    private Annotation? _moveCurrent;
    private PointD _moveStart;

    private TextBox? _textEditor;
    private PointD _textPosition;
    private TextAnnotation? _textOriginal;
    private ArgbColor _textEditColor;
    private double _textEditFontSize;
    private double _textEditStroke;

    public AnnotationCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Background = Brushes.Transparent;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.HighQuality);
    }

    /// <summary>Raised after any visible state change that affects toolbar state (selection, history).</summary>
    public event EventHandler? StateChanged;

    public EditorDocument? Document => _document;

    public AnnotationTool Tool { get; private set; } = AnnotationTool.Arrow;

    public ArgbColor CurrentColor { get; private set; } = ArgbColor.Parse("#E53935");

    public double StrokeWidth { get; private set; } = 4;

    /// <summary>Current zoom factor, used to keep hit-test tolerance and selection outlines constant on screen.</summary>
    public double Zoom { get; set; } = 1;

    public ArgbColor RedactionColor { get; set; } = ArgbColor.Black;

    /// <summary>Blur tool strength (box-blur radius in pixels).</summary>
    public int BlurRadius { get; set; } = 8;

    /// <summary>Pixelate tool block size in pixels.</summary>
    public int PixelateBlockSize { get; set; } = 12;

    /// <summary>The selected annotation, if any (for the Tool Properties panel).</summary>
    public Annotation? Selected => SelectedAnnotation();

    public bool HasSelection => _selectedId is not null && _document?.FindById(_selectedId.Value) is not null;

    public bool IsEditingText => _textEditor is not null;

    /// <summary>True while a drag (draw, redact or move) is in progress.</summary>
    public bool IsInGesture => _dragStart is not null || _moveOriginal is not null;

    /// <summary>Abandons a drag in progress without committing it (before undo/redo/delete change the document).</summary>
    public void CancelGesture()
    {
        if (!IsInGesture)
        {
            return;
        }

        ResetInteraction();
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    public static double FontSizeForStroke(double stroke) => stroke switch
    {
        <= 2 => 18,
        <= 4 => 24,
        <= 6 => 32,
        _ => 44,
    };

    public void Load(EditorDocument document)
    {
        CancelTextEdit();
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }

        _document = document ?? throw new ArgumentNullException(nameof(document));
        _document.Changed += OnDocumentChanged;
        _bitmap = BitmapConversion.ToWriteableBitmap(document.Image);
        Width = document.Image.Width;
        Height = document.Image.Height;
        ResetInteraction();
        _selectedId = null;
        InvalidateVisual();
        RaiseStateChanged();
    }

    /// <summary>Shows nothing (no capture open).</summary>
    public void Unload()
    {
        CancelTextEdit();
        if (_document is not null)
        {
            _document.Changed -= OnDocumentChanged;
        }

        _document = null;
        _bitmap = null;
        _selectedId = null;
        ResetInteraction();
        InvalidateVisual();
        RaiseStateChanged();
    }

    public void SetTool(AnnotationTool tool)
    {
        CommitTextEdit();
        Tool = tool;
        if (tool != AnnotationTool.Select)
        {
            _selectedId = null;
        }

        ResetInteraction();
        Cursor = tool switch
        {
            AnnotationTool.Select => Cursors.Arrow,
            AnnotationTool.Text => Cursors.IBeam,
            _ => Cursors.Cross,
        };
        InvalidateVisual();
        RaiseStateChanged();
    }

    /// <summary>Sets the color for new annotations and recolors the selected one (as an undoable step).</summary>
    public void SetColor(ArgbColor color)
    {
        CurrentColor = color;
        if (_textEditor is not null)
        {
            _textEditColor = color;
            _textEditor.Foreground = new SolidColorBrush(BitmapConversion.ToMediaColor(color));
        }

        if (SelectedAnnotation() is { } selected && selected.Color != color)
        {
            _document!.Execute(new ReplaceAnnotationCommand(selected, selected with { Color = color }, "Change color"));
        }
    }

    /// <summary>Sets the stroke size for new annotations and resizes the selected one (as an undoable step).</summary>
    public void SetStrokeWidth(double width)
    {
        StrokeWidth = width;
        if (_textEditor is not null)
        {
            _textEditFontSize = FontSizeForStroke(width);
            _textEditStroke = width;
            _textEditor.FontSize = _textEditFontSize;
        }

        if (SelectedAnnotation() is not { } selected)
        {
            return;
        }

        Annotation resized = selected switch
        {
            TextAnnotation t => WithMeasuredSize(t with { FontSize = FontSizeForStroke(width), StrokeWidth = width }),
            StepMarkerAnnotation s => s with { Radius = StepMarkerAnnotation.RadiusForStroke(width), StrokeWidth = width },
            _ => selected with { StrokeWidth = width },
        };
        if (resized != selected)
        {
            _document!.Execute(new ReplaceAnnotationCommand(selected, resized, "Change size"));
        }
    }

    public void DeleteSelection()
    {
        CancelGesture();
        if (SelectedAnnotation() is { } selected)
        {
            _document!.Execute(new RemoveAnnotationCommand(selected));
            _selectedId = null;
            RaiseStateChanged();
        }
    }

    /// <summary>Esc: cancel the current gesture, then the text editor, then the selection.</summary>
    public bool CancelCurrentOperation()
    {
        if (_textEditor is not null)
        {
            CancelTextEdit();
            return true;
        }

        if (_dragStart is not null || _moveOriginal is not null)
        {
            ResetInteraction();
            ReleaseMouseCapture();
            InvalidateVisual();
            return true;
        }

        if (_selectedId is not null)
        {
            _selectedId = null;
            InvalidateVisual();
            RaiseStateChanged();
            return true;
        }

        return false;
    }

    public void CommitTextEdit()
    {
        if (_textEditor is null || _document is null)
        {
            return;
        }

        string text = _textEditor.Text.TrimEnd();
        TextAnnotation? original = _textOriginal;
        RemoveTextEditor();

        if (original is null)
        {
            if (text.Length > 0)
            {
                var annotation = WithMeasuredSize(new TextAnnotation(_textPosition, text, _textEditFontSize)
                {
                    Color = _textEditColor,
                    StrokeWidth = _textEditStroke,
                });
                _document.Execute(new AddAnnotationCommand(annotation));
            }
        }
        else if (text.Length == 0)
        {
            _document.Execute(new RemoveAnnotationCommand(original));
        }
        else
        {
            // Color and size picked while the editor was open apply to the edited text too.
            TextAnnotation edited = WithMeasuredSize(original with
            {
                Text = text,
                Color = _textEditColor,
                FontSize = _textEditFontSize,
                StrokeWidth = _textEditStroke,
            });
            if (edited != original)
            {
                _document.Execute(new ReplaceAnnotationCommand(original, edited, "Edit text"));
            }
        }

        InvalidateVisual();
        RaiseStateChanged();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        if (_document is null || _bitmap is null)
        {
            return;
        }

        dc.DrawImage(_bitmap, new Rect(0, 0, _document.Image.Width, _document.Image.Height));

        Guid? hiddenId = _moveCurrent?.Id ?? _textOriginal?.Id;
        foreach (Annotation annotation in _document.Annotations)
        {
            if (annotation.Id != hiddenId)
            {
                AnnotationRenderer.Draw(dc, annotation);
            }
        }

        if (_moveCurrent is not null)
        {
            AnnotationRenderer.Draw(dc, _moveCurrent);
        }

        if (_preview is not null)
        {
            AnnotationRenderer.Draw(dc, _preview);
        }

        double px = 1 / Math.Max(Zoom, 0.01);
        if (_redactPreview is { } redact)
        {
            var pen = new Pen(Brushes.White, px) { DashStyle = new DashStyle([4, 4], 0) };
            var area = new Rect(redact.X, redact.Y, redact.Width, redact.Height);
            if (Tool == AnnotationTool.Crop)
            {
                // Dim everything that will be cut away.
                var outside = new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, _document.Image.Width, _document.Image.Height)), new RectangleGeometry(area));
                dc.DrawGeometry(RedactPreviewFill, null, outside);
                dc.DrawRectangle(null, pen, area);
            }
            else
            {
                dc.DrawRectangle(RedactPreviewFill, pen, area);
            }
        }

        if ((_moveCurrent ?? SelectedAnnotation()) is { } selected && _textEditor is null)
        {
            RectD b = selected.Bounds.Inflate(3 * px);
            var pen = new Pen(SelectionBrush, 1.5 * px) { DashStyle = new DashStyle([4, 3], 0) };
            dc.DrawRectangle(null, pen, new Rect(b.X, b.Y, b.Width, b.Height));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_document is null)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source && IsInsideTextEditor(source))
        {
            return;
        }

        Focus();
        PointD p = Position(e);
        CommitTextEdit();

        switch (Tool)
        {
            case AnnotationTool.Select:
                Annotation? hit = _document.HitTest(p, Tolerance);
                _selectedId = hit?.Id;
                if (hit is not null)
                {
                    if (e.ClickCount == 2 && hit is TextAnnotation text)
                    {
                        BeginTextEdit(text.Position, text);
                        break;
                    }

                    _moveOriginal = hit;
                    _moveCurrent = hit;
                    _moveStart = p;
                    CaptureMouse();
                }

                break;
            case AnnotationTool.Text:
                if (_document.HitTest(p, Tolerance) is TextAnnotation existing)
                {
                    BeginTextEdit(existing.Position, existing);
                }
                else
                {
                    double fontSize = FontSizeForStroke(StrokeWidth);
                    BeginTextEdit(new PointD(p.X, p.Y - fontSize * 0.65), null);
                }

                break;
            case AnnotationTool.StepMarker:
                double radius = StepMarkerAnnotation.RadiusForStroke(StrokeWidth);
                _document.Execute(new AddAnnotationCommand(new StepMarkerAnnotation(p, StepNumbering.Next(_document.Annotations), radius)
                {
                    Color = CurrentColor,
                    StrokeWidth = StrokeWidth,
                }));
                break;
            default:
                _dragStart = p;
                CaptureMouse();
                break;
        }

        e.Handled = true;
        InvalidateVisual();
        RaiseStateChanged();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_document is null)
        {
            return;
        }

        PointD p = Position(e);
        bool constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (_moveOriginal is not null)
        {
            PointD d = p - _moveStart;
            _moveCurrent = _moveOriginal.Translate(d.X, d.Y);
            InvalidateVisual();
            return;
        }

        if (Tool == AnnotationTool.Select && e.LeftButton == MouseButtonState.Released)
        {
            Cursor = _document.HitTest(p, Tolerance) is not null ? Cursors.SizeAll : Cursors.Arrow;
        }

        if (_dragStart is not { } start)
        {
            return;
        }

        if (Tool is AnnotationTool.Redact or AnnotationTool.Crop or AnnotationTool.Blur or AnnotationTool.Pixelate)
        {
            _redactPreview = RectD.FromPoints(start, p).ToCoveringPixelRect().Intersect(_document.Image.Bounds);
        }
        else
        {
            _preview = BuildShape(Tool, start, p, constrain);
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (_document is null)
        {
            return;
        }

        PointD p = Position(e);
        if (_moveOriginal is not null)
        {
            Annotation original = _moveOriginal;
            Annotation? moved = _moveCurrent;
            ResetInteraction();
            ReleaseMouseCapture();
            if (moved is not null && moved != original && _document.FindById(original.Id) == original)
            {
                _document.Execute(new ReplaceAnnotationCommand(original, moved, "Move"));
            }

            InvalidateVisual();
            RaiseStateChanged();
            return;
        }

        if (_dragStart is not { } start)
        {
            return;
        }

        bool constrain = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        ResetInteraction();
        ReleaseMouseCapture();

        if (Tool == AnnotationTool.Redact)
        {
            PixelRect region = RectD.FromPoints(start, p).ToCoveringPixelRect().Intersect(_document.Image.Bounds);
            if (region.Width >= 2 && region.Height >= 2)
            {
                _document.Execute(new RedactCommand(region, RedactionColor));
            }
        }
        else if (Tool == AnnotationTool.Crop)
        {
            PixelRect region = RectD.FromPoints(start, p).ToCoveringPixelRect().Intersect(_document.Image.Bounds);
            if (region.Width >= 4 && region.Height >= 4)
            {
                _document.Execute(new CropCommand(region));
            }
        }
        else if (Tool is AnnotationTool.Blur or AnnotationTool.Pixelate)
        {
            PixelRect region = RectD.FromPoints(start, p).ToCoveringPixelRect().Intersect(_document.Image.Bounds);
            if (region.Width >= 2 && region.Height >= 2)
            {
                _document.Execute(Tool == AnnotationTool.Blur
                    ? new ApplyRegionEffectCommand(region, RegionEffect.Blur, BlurRadius)
                    : new ApplyRegionEffectCommand(region, RegionEffect.Pixelate, PixelateBlockSize));
            }
        }
        else if (BuildShape(Tool, start, p, constrain) is { } shape && !IsDegenerate(shape))
        {
            _document.Execute(new AddAnnotationCommand(shape));
        }

        InvalidateVisual();
        RaiseStateChanged();
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);

        // Capture can be stolen (Alt+Tab, a dialog); drop any half-finished gesture instead of committing it.
        if (_dragStart is not null || _moveOriginal is not null)
        {
            ResetInteraction();
            InvalidateVisual();
        }
    }

    private double Tolerance => 5 / Math.Max(Zoom, 0.01);

    private static bool IsDegenerate(Annotation a) => a switch
    {
        HighlightAnnotation x => x.IsDegenerate,
        ArrowAnnotation x => x.IsDegenerate,
        LineAnnotation x => x.IsDegenerate,
        RectangleAnnotation x => x.IsDegenerate,
        EllipseAnnotation x => x.IsDegenerate,
        _ => false,
    };

    private Annotation? BuildShape(AnnotationTool tool, PointD start, PointD end, bool constrain)
    {
        switch (tool)
        {
            case AnnotationTool.Arrow:
            case AnnotationTool.Line:
                if (constrain)
                {
                    end = SnapAngle(start, end);
                }

                return tool == AnnotationTool.Arrow
                    ? new ArrowAnnotation(start, end) { Color = CurrentColor, StrokeWidth = StrokeWidth }
                    : new LineAnnotation(start, end) { Color = CurrentColor, StrokeWidth = StrokeWidth };
            case AnnotationTool.Highlight:
                return new HighlightAnnotation(RectD.FromPoints(start, end)) { Color = CurrentColor, StrokeWidth = StrokeWidth };
            case AnnotationTool.Rectangle:
            case AnnotationTool.Ellipse:
                if (constrain)
                {
                    double side = Math.Max(Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
                    end = new PointD(start.X + Math.CopySign(side, end.X - start.X), start.Y + Math.CopySign(side, end.Y - start.Y));
                }

                RectD rect = RectD.FromPoints(start, end);
                return tool == AnnotationTool.Rectangle
                    ? new RectangleAnnotation(rect) { Color = CurrentColor, StrokeWidth = StrokeWidth }
                    : new EllipseAnnotation(rect) { Color = CurrentColor, StrokeWidth = StrokeWidth };
            default:
                return null;
        }
    }

    /// <summary>Snaps a line to the nearest 45° direction (Shift-drag).</summary>
    private static PointD SnapAngle(PointD start, PointD end)
    {
        double dx = end.X - start.X, dy = end.Y - start.Y;
        double length = Math.Sqrt(dx * dx + dy * dy);
        double angle = Math.Round(Math.Atan2(dy, dx) / (Math.PI / 4)) * (Math.PI / 4);
        return new PointD(start.X + length * Math.Cos(angle), start.Y + length * Math.Sin(angle));
    }

    private void BeginTextEdit(PointD position, TextAnnotation? existing)
    {
        _textOriginal = existing;
        _textPosition = position;
        _selectedId = null;
        double fontSize = existing?.FontSize ?? FontSizeForStroke(StrokeWidth);
        ArgbColor color = existing?.Color ?? CurrentColor;
        _textEditColor = color;
        _textEditFontSize = fontSize;
        _textEditStroke = existing?.StrokeWidth ?? StrokeWidth;

        _textEditor = new TextBox
        {
            Text = existing?.Text ?? string.Empty,
            FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = fontSize,
            Foreground = new SolidColorBrush(BitmapConversion.ToMediaColor(color)),
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x1E, 0x1F, 0x22)),
            CaretBrush = Brushes.White,
            BorderBrush = SelectionBrush,
            BorderThickness = new Thickness(1 / Math.Max(Zoom, 0.01)),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            MinWidth = fontSize * 2,
            TextWrapping = TextWrapping.NoWrap,
            Style = null,
        };
        _textEditor.PreviewKeyDown += OnTextEditorKeyDown;
        SetLeft(_textEditor, position.X - 2);
        SetTop(_textEditor, position.Y - 2);
        Children.Add(_textEditor);
        _textEditor.Loaded += (_, _) =>
        {
            _textEditor?.Focus();
            _textEditor?.SelectAll();
        };
        InvalidateVisual();
        RaiseStateChanged();
    }

    private void OnTextEditorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelTextEdit();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            // Enter commits; Shift+Enter inserts a new line.
            CommitTextEdit();
            Focus();
            e.Handled = true;
        }
    }

    private void CancelTextEdit()
    {
        if (_textEditor is null)
        {
            return;
        }

        RemoveTextEditor();
        InvalidateVisual();
        RaiseStateChanged();
    }

    private void RemoveTextEditor()
    {
        if (_textEditor is not null)
        {
            _textEditor.PreviewKeyDown -= OnTextEditorKeyDown;
            Children.Remove(_textEditor);
            _textEditor = null;
        }

        _textOriginal = null;
    }

    private bool IsInsideTextEditor(DependencyObject source)
    {
        for (DependencyObject? d = source; d is not null; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
        {
            if (ReferenceEquals(d, _textEditor))
            {
                return true;
            }

            if (ReferenceEquals(d, this))
            {
                return false;
            }
        }

        return false;
    }

    private static TextAnnotation WithMeasuredSize(TextAnnotation t) =>
        t with { MeasuredSize = AnnotationRenderer.MeasureText(t.Text, t.FontSize) };

    private Annotation? SelectedAnnotation() =>
        _selectedId is { } id && _document is not null ? _document.FindById(id) : null;

    private PointD Position(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);
        return new PointD(p.X, p.Y);
    }

    private void ResetInteraction()
    {
        _dragStart = null;
        _preview = null;
        _redactPreview = null;
        _moveOriginal = null;
        _moveCurrent = null;
    }

    private void OnDocumentChanged(object? sender, DocumentChangedEventArgs e)
    {
        if (_document is not null && (e.SizeChanged || (_bitmap is not null && (_bitmap.PixelWidth != _document.Image.Width || _bitmap.PixelHeight != _document.Image.Height))))
        {
            _bitmap = BitmapConversion.ToWriteableBitmap(_document.Image);
            Width = _document.Image.Width;
            Height = _document.Image.Height;
        }
        else if (e.ImageChanged && _bitmap is not null && _document is not null)
        {
            BitmapConversion.CopyInto(_document.Image, _bitmap);
        }

        if (_selectedId is { } id && _document?.FindById(id) is null)
        {
            _selectedId = null;
        }

        InvalidateVisual();
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static T Freeze<T>(T f)
        where T : Freezable
    {
        f.Freeze();
        return f;
    }
}
