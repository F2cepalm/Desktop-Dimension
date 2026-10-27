using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InfiniteSpace.Rendering;
using InfiniteSpace.Shell;
using InfiniteSpace.Spaces;

namespace InfiniteSpace.Tests;

/// <summary>Paint and text drawn on the GPU (see <see cref="InkLayer"/>), read back pixel by pixel.</summary>
public class InkLayerTests
{
    private static Geometry Square(double x, double y, double size) =>
        SpaceGeometry.FromStroke([new(x, y), new(x + size, y), new(x + size, y + size), new(x, y + size)], new Vector(0, 0));

    [Fact]
    public void Strokes_AreDrawnWhereTheyWereDrawn_AtDeviceResolution()
    {
        Gpu.Run(gpu =>
        {
            // 2x scale: a 100x60 DIP area at screen (500,300) is 200x120 device pixels.
            using var ink = new InkLayer(gpu, 200, 120, 2, new Point(500, 300), Square(500, 300, 100), Colors.White);
            var record = new SpaceRecord { Strokes = [new InkStroke { Points = [510, 320, 590, 320] }] };

            var changed = ink.Redraw(record);
            var pixels = ink.ReadPixels();

            Assert.Equal(ink.Bounds, changed);
            Assert.NotEqual(0, Pixel(ink, pixels, 100, 40));  // on the line: (550,320) → device (100,40)
            Assert.Equal(0, Pixel(ink, pixels, 100, 100));    // well below it
        });
    }

    [Fact]
    public void ADrag_DrawnOverTheStillInk_LooksLikeEverythingDrawnAgain()
    {
        Gpu.Run(gpu =>
        {
            using var dragging = new InkLayer(gpu, 300, 200, 1, new Point(0, 0), Square(0, 0, 300), Colors.White);
            using var whole = new InkLayer(gpu, 300, 200, 1, new Point(0, 0), Square(0, 0, 300), Colors.White);
            var still = new InkStroke { Points = [20, 20, 280, 180], Thickness = 6 };
            var note = new TextNote { X = 40, Y = 150, Text = "stays" };
            var dragged = new InkStroke { Points = [30, 100, 150, 100], Thickness = 8, Color = "#FF0000" };
            var record = new SpaceRecord { Strokes = [still, dragged], Notes = [note] };
            var moving = new HashSet<IInk> { dragged };
            Func<IInk, bool> isMoving = moving.Contains;

            // The first frame of the drag paints the still ink; the others only copy it.
            foreach (var offset in new[] { new Vector(0, 1), new Vector(10, 5), new Vector(40, -30), new Vector(55, 12) })
            {
                Matrix Place(InkStroke stroke)
                {
                    var m = Matrix.Identity;
                    if (stroke == dragged)
                        m.Translate(offset.X, offset.Y);
                    return m;
                }

                dragging.Redraw(record, Matrix.Identity, placeStroke: Place, moving: isMoving);
                whole.Redraw(record, Matrix.Identity, placeStroke: Place);

                Assert.Equal(whole.ReadPixels(), dragging.ReadPixels()); // the dragged stroke is last, and meets no note
            }
        });
    }

    [Fact]
    public void InkOutsideTheSpace_IsClippedAway()
    {
        Gpu.Run(gpu =>
        {
            // The space is only the left half of the layer.
            using var ink = new InkLayer(gpu, 200, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 190, 50] }] });
            var pixels = ink.ReadPixels();

            Assert.NotEqual(0, Pixel(ink, pixels, 50, 50));
            Assert.Equal(0, Pixel(ink, pixels, 150, 50));
        });
    }

    [Fact]
    public void Notes_AreDrawnFromTheirTopLeft()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 400, 200, 2, new Point(0, 0), Square(0, 0, 200), Colors.White);
            ink.Redraw(new SpaceRecord { Notes = [new TextNote { X = 20, Y = 30, Text = "Hello" }] });
            var pixels = ink.ReadPixels();

            int inked = pixels.Count(p => p != 0);
            Assert.True(inked > 50, $"expected text pixels, found {inked}");
            // Nothing above or left of the note's origin.
            for (int y = 0; y < 55; y++)
                Assert.Equal(0, Pixel(ink, pixels, 200, y));
            for (int x = 0; x < 38; x++)
                Assert.Equal(0, Pixel(ink, pixels, x, 75));
        });
    }

    [Fact]
    public void PaintingSegments_AccumulateAndReportWhatChanged()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 200, 200, 1, new Point(0, 0), Square(0, 0, 200), Colors.White);
            ink.Redraw(new SpaceRecord());

            ink.AddSegment(new Point(20, 20), new Point(20, 20), 3);
            ink.AddSegment(new Point(20, 20), new Point(180, 20), 3);
            var changed = ink.AddSegment(new Point(180, 20), new Point(180, 150), 3);
            var pixels = ink.ReadPixels();

            Assert.NotEqual(0, Pixel(ink, pixels, 20, 20));
            Assert.NotEqual(0, Pixel(ink, pixels, 100, 20));
            Assert.NotEqual(0, Pixel(ink, pixels, 180, 100));
            Assert.Equal(0, Pixel(ink, pixels, 100, 100));
            Assert.True(changed.X <= 178 && changed.X + changed.Width >= 182 && changed.Y <= 18 && changed.Y + changed.Height >= 152);
        });
    }

    [Fact]
    public void PaintingAfterAMove_LandsWhereTheSpaceIsNow()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 200, 200, 1, new Point(0, 0), Square(0, 0, 200), Colors.White);
            ink.Redraw(new SpaceRecord());
            ink.MoveOrigin(new Vector(1000, 500)); // the space was carried off with everything on it

            ink.AddSegment(new Point(1050, 600), new Point(1150, 600), 4);
            var pixels = ink.ReadPixels();

            Assert.NotEqual(0, Pixel(ink, pixels, 100, 100));
            Assert.Equal(0, Pixel(ink, pixels, 100, 50));
        });
    }

    [Fact]
    public void Ink_IsDrawnInTheThemesColour()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), ClaudishStyle.Ink);
            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 90, 50], Thickness = 6 }] });

            // Premultiplied BGRA of an opaque #151515 pixel in the middle of the stroke.
            Assert.Equal(unchecked((int)0xFF151515), Pixel(ink, ink.ReadPixels(), 50, 50));
        });
    }

    [Fact]
    public void ANewThemesInk_ShowsOnTheNextRedraw()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            var record = new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 90, 50], Thickness = 6 }] };
            ink.Redraw(record);

            ink.ThemeInk = ClaudishStyle.Ink;
            ink.Redraw(record);

            Assert.Equal(unchecked((int)0xFF151515), Pixel(ink, ink.ReadPixels(), 50, 50));
        });
    }

    [Fact]
    public void StrokesAndNotes_WithAColourOfTheirOwn_KeepIt_OthersTakeTheThemes()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 200, 100, 1, new Point(0, 0), Square(0, 0, 200), Colors.White);
            ink.Redraw(new SpaceRecord
            {
                Strokes =
                [
                    new InkStroke { Points = [10, 20, 190, 20], Thickness = 6, Color = "#FF0000" },
                    new InkStroke { Points = [10, 60, 190, 60], Thickness = 6 },
                ],
            });
            var pixels = ink.ReadPixels();

            Assert.Equal(unchecked((int)0xFFFF0000), Pixel(ink, pixels, 100, 20));
            Assert.Equal(unchecked((int)0xFFFFFFFF), Pixel(ink, pixels, 100, 60));
        });
    }

    [Fact]
    public void AStroke_IsAsWideAsItsPen()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 200, 100, 1, new Point(0, 0), Square(0, 0, 200), Colors.White);
            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 190, 50], Thickness = 20 }] });
            var pixels = ink.ReadPixels();

            Assert.NotEqual(0, Pixel(ink, pixels, 100, 42));  // 8 above the middle: inside a 20-wide stroke
            Assert.Equal(0, Pixel(ink, pixels, 100, 35));     // 15 above: outside it
        });
    }

    [Fact]
    public void AStrokeOfOnePoint_IsADot()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [50, 50], Thickness = 10 }] });
            var pixels = ink.ReadPixels();

            Assert.Equal(unchecked((int)0xFFFFFFFF), Pixel(ink, pixels, 50, 50));
            Assert.Equal(unchecked((int)0xFFFFFFFF), Pixel(ink, pixels, 52, 50));
            Assert.Equal(0, Pixel(ink, pixels, 58, 50));
        });
    }

    [Fact]
    public void PaintingASegment_InAColour_DrawsInIt()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            ink.Redraw(new SpaceRecord());

            ink.AddSegment(new Point(10, 50), new Point(90, 50), 6, Color.FromRgb(0, 0, 255));

            Assert.Equal(unchecked((int)0xFF0000FF), Pixel(ink, ink.ReadPixels(), 50, 50));
        });
    }

    [Fact]
    public void PaintIsLaidOverWhatIsThere()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            ink.Redraw(new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 90, 50], Thickness = 10, Color = "#0000FF" }] });

            ink.AddSegment(new Point(50, 10), new Point(50, 90), 10, Color.FromArgb(0x80, 0xFF, 0, 0)); // half red over blue

            int crossing = Pixel(ink, ink.ReadPixels(), 50, 50);
            Assert.Equal(0xFF, (crossing >> 24) & 0xFF);
            Assert.InRange((crossing >> 16) & 0xFF, 0x7F, 0x81); // red
            Assert.InRange(crossing & 0xFF, 0x7E, 0x80);         // blue
        });
    }

    [Fact]
    public void ANoteWithAWidth_WrapsItsLines()
    {
        var free = new TextNote { Text = "one two three four five six seven" };
        var wrapped = new TextNote { Text = free.Text, Width = 60 };

        var line = NoteText.Measure(free);
        var lines = NoteText.Measure(wrapped);

        Assert.True(line.Width > 60);
        Assert.True(lines.Height > line.Height * 2, $"{lines.Height} vs one line of {line.Height}");
        Assert.Equal(60, lines.Width); // it takes up its wrap width
    }

    [Fact]
    public void BiggerType_TakesMoreRoom()
    {
        var small = NoteText.Measure(new TextNote { Text = "Hello", FontSize = 12 });
        var big = NoteText.Measure(new TextNote { Text = "Hello", FontSize = 48 });

        Assert.True(big.Width > small.Width * 3 && big.Height > small.Height * 3);
    }

    [Fact]
    public void EveryLine_IsAsTallAsInTheTextBox_WhateverFontFillsIn()
    {
        var one = NoteText.Measure(new TextNote { Text = "Hello", FontSize = 20 });
        var three = NoteText.Measure(new TextNote { Text = "Hello\nthere\n\U0001F600 and 漢字", FontSize = 20 });

        Assert.Equal(20 * NoteText.LineSpacing, one.Height, 3);
        Assert.Equal(3 * 20 * NoteText.LineSpacing, three.Height, 3);
    }

    [Fact]
    public void ANoteFollowsItsSpacesPlace_ButKeepsItsSize()
    {
        var note = new TextNote { X = 10, Y = 20, Text = "Hello" };
        var size = NoteText.Measure(note);

        var bounds = NoteText.Bounds(note, new Matrix(2, 0, 0, 2, 100, 50));

        Assert.Equal(new Rect(new Point(120, 90), size), bounds);
    }

    [Fact]
    public void ANoteBeingEdited_IsLeftOut_AndSoIsOneEditedAway()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 400, 200, 1, new Point(0, 0), Square(0, 0, 400), Colors.White);
            var editing = new TextNote { X = 20, Y = 20, Text = "Hello" };
            var record = new SpaceRecord { Notes = [editing, new TextNote { X = 20, Y = 100, Text = "" }] };

            ink.Redraw(record, skip: editing);
            Assert.All(ink.ReadPixels(), p => Assert.Equal(0, p));

            ink.Redraw(record);
            Assert.Contains(ink.ReadPixels(), p => p != 0);
        });
    }

    [Fact]
    public void AStrokeRewrittenInPlace_IsDrawnWhereItIsNow()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 200, 200, 1, new Point(0, 0), Square(0, 0, 200), Colors.White);
            var record = new SpaceRecord { Strokes = [new InkStroke { Points = [10, 20, 190, 20], Thickness = 4 }] };
            ink.Redraw(record);

            record.Transform(new Matrix(1, 0, 0, 1, 0, 100)); // rewritten in place, as when a space is baked
            ink.Redraw(record);
            var pixels = ink.ReadPixels();

            Assert.Equal(0, Pixel(ink, pixels, 100, 20));
            Assert.NotEqual(0, Pixel(ink, pixels, 100, 120));
        });
    }

    [Fact]
    public void AReshapedLayer_DrawsAtItsNewSize()
    {
        Gpu.Run(gpu =>
        {
            using var ink = new InkLayer(gpu, 100, 100, 1, new Point(0, 0), Square(0, 0, 100), Colors.White);
            var record = new SpaceRecord { Strokes = [new InkStroke { Points = [10, 50, 590, 50], Thickness = 6 }] };
            ink.Redraw(record);

            ink.SetShape(600, 100, 1, new Point(0, 0), SpaceGeometry.FromStroke([new(0, 0), new(600, 0), new(600, 100), new(0, 100)], new Vector()));
            ink.Redraw(record);
            var pixels = ink.ReadPixels();

            Assert.Equal(600, ink.Width);
            Assert.NotEqual(0, Pixel(ink, pixels, 50, 50));
            Assert.NotEqual(0, Pixel(ink, pixels, 550, 50));
        });
    }

    [Theory]
    [InlineData(12, false, false)]
    [InlineData(16, false, false)]
    [InlineData(27.5, false, false)]
    [InlineData(40, false, false)]
    [InlineData(16, true, false)]
    [InlineData(16, false, true)]
    [InlineData(27.5, true, true)]
    public void TheTextBox_BreaksLinesWhereTheNoteWill(double fontSize, bool bold, bool italic)
    {
        Gpu.RunOnSta(() =>
        {
            const string text = "The quick brown fox jumps over the lazy dog while seven wizards quietly hex a jumbo box of mixed liquor jugs";
            var box = new System.Windows.Controls.TextBox
            {
                FontFamily = NoteText.Typeface.FontFamily,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0),
                AcceptsReturn = true,
                Text = text,
                FontWeight = bold ? FontWeights.Bold : NoteText.Typeface.Weight,
                FontStyle = italic ? FontStyles.Italic : NoteText.Typeface.Style,
            };
            var window = new Window { Content = box, SizeToContent = SizeToContent.WidthAndHeight, WindowStyle = WindowStyle.None, ShowInTaskbar = false, Left = -10000, Top = -10000 };
            window.Show();
            try
            {
                double inset = box.GetRectFromCharacterIndex(0).X;
                double lineHeight = NoteText.Measure(new TextNote { Text = "X", FontSize = fontSize }).Height;
                for (double width = 60; width <= 480; width += 9.7)
                {
                    TextEntry.Shape(box, fontSize, width, inset);
                    window.UpdateLayout();
                    var note = new TextNote { Text = text, FontSize = fontSize, Width = width, Bold = bold, Italic = italic };

                    var inBox = Enumerable.Range(0, box.LineCount).Select(box.GetLineLength).ToArray();
                    Assert.Equal(inBox, NoteText.LineLengths(note));
                    Assert.Equal(box.LineCount, (int)Math.Round(NoteText.Measure(note).Height / lineHeight));
                }
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(2.0)]
    public void TheGpuInk_LooksLikeWpfDrawingIt(double scale)
    {
        Gpu.Run(gpu =>
        {
            // A triangle, so the clip cuts across the stroke and the text; glyphs that overhang (f, j, W).
            var origin = new Point(10, 20);
            var outline = SpaceGeometry.FromStroke([new(10, 20), new(330, 20), new(10, 240)], new Vector(0, 0));
            int width = (int)Math.Ceiling(320 * scale), height = (int)Math.Ceiling(220 * scale);
            var stroke = new InkStroke { Points = [20, 60, 200, 90, 320, 200], Thickness = 7, Color = "#FF5A5F" };
            var note = new TextNote { X = 30.3, Y = 30.7, Text = "fjord WAVE ƒ\nJumpy", FontSize = 24 };
            var wrapped = new TextNote { X = 25, Y = 110, Text = "wrapped text runs on", FontSize = 14, Width = 70, Color = "#3FA7FF" };
            using var ink = new InkLayer(gpu, width, height, scale, origin, outline, Colors.White);

            ink.Redraw(new SpaceRecord { Strokes = [stroke], Notes = [note, wrapped] });
            var actual = ink.ReadPixels();

            var reference = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.PushClip(new GeometryGroup { Children = { outline }, Transform = new TranslateTransform(-origin.X, -origin.Y) });
                var line = new StreamGeometry();
                using (var ctx = line.Open())
                {
                    ctx.BeginFigure(new Point(10, 40), isFilled: false, isClosed: false);
                    ctx.LineTo(new Point(190, 70), isStroked: true, isSmoothJoin: true);
                    ctx.LineTo(new Point(310, 180), isStroked: true, isSmoothJoin: true);
                }
                var red = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5F));
                dc.DrawGeometry(null, new Pen(red, 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round }, line);
                dc.DrawText(WpfText(note, Brushes.White, scale), new Point(note.X - origin.X, note.Y - origin.Y));
                dc.DrawText(WpfText(wrapped, new SolidColorBrush(Color.FromRgb(0x3F, 0xA7, 0xFF)), scale), new Point(wrapped.X - origin.X, wrapped.Y - origin.Y));
                dc.Pop();
            }
            reference.Render(visual);
            var expected = new int[width * height];
            reference.CopyPixels(expected, width * 4, 0);

            // Glyphs are antialiased a little differently; anything cut off, shifted or missing would
            // show as far more: compare how much is inked and where.
            int bothInked = 0, onlyOne = 0, faint = 0;
            for (int i = 0; i < expected.Length; i++)
            {
                int a = (expected[i] >> 24) & 0xFF, b = (actual[i] >> 24) & 0xFF;
                if (a > 128 && b > 128)
                    bothInked++;
                else if ((a > 128) != (b > 128) && Math.Abs(a - b) > 96)
                    onlyOne++;
                if (a == 0 && b > 32)
                    faint++;
            }
            Assert.True(bothInked > 1000, $"{bothInked} inked pixels in common");
            Assert.True(onlyOne < bothInked / 50, $"{onlyOne} pixels inked in one and not the other, of {bothInked}");
            Assert.True(faint < bothInked / 50, $"{faint} pixels inked where WPF has nothing, of {bothInked}");
        });
    }

    [Fact]
    public void ReshapingWithRoom_KeepsTheInk_AndMatchesUncroppedPixels()
    {
        Gpu.Run(gpu =>
        {
            var record = new SpaceRecord
            {
                Strokes = [new InkStroke { Points = [120, 120, 220, 180, 180, 240], Thickness = 5 }],
                Notes = [new TextNote { X = 140, Y = 130, Text = "Kept across reshapes" }],
            };
            using var reshaped = new InkLayer(gpu, 300, 300, 1, new Point(100, 100), Square(100, 100, 300), Colors.White) { ReshapeRoom = 1.75 };
            using var fresh = new InkLayer(gpu, 300, 300, 1, new Point(100, 100), Square(100, 100, 300), Colors.White);

            reshaped.Redraw(record);
            int revision = reshaped.Revision;
            Assert.True(reshaped.IsCropped);

            foreach (var (origin, size) in new[] { (new Point(60, 60), 500), (new Point(20, 20), 900), (new Point(110, 110), 90) })
            {
                var outline = Square(origin.X, origin.Y, size);
                reshaped.SetShape(size, size, 1, origin, outline);
                reshaped.Redraw(record);
                fresh.SetShape(size, size, 1, origin, outline);
                fresh.Redraw(record);

                Assert.Equal(revision, reshaped.Revision);
                Assert.Equal(fresh.ReadPixels(), reshaped.ReadPixels());
            }
        });
    }

    [Fact]
    public void ReshapingWithPushedNotesOverDrawnShapes_MatchesFreshPixels()
    {
        Gpu.Run(gpu =>
        {
            var record = new SpaceRecord
            {
                Strokes =
                [
                    new InkStroke { Points = [120, 120, 260, 180, 180, 260, 120, 120], Thickness = 5 },
                    new InkStroke { Points = [150, 140, 240, 140, 240, 220, 150, 220, 150, 140], Thickness = 3, Color = "#4ECDC4" },
                ],
                Notes =
                [
                    new TextNote { X = 130, Y = 125, Text = "Heading note", Bold = true },
                    new TextNote { X = 230, Y = 160, Width = 120, Text = "Edge note pushed inward" },
                ],
            };
            using var reshaped = new InkLayer(gpu, 300, 300, 1, new Point(100, 100), Square(100, 100, 300), Colors.White) { ReshapeRoom = 1.75 };
            using var fresh = new InkLayer(gpu, 300, 300, 1, new Point(100, 100), Square(100, 100, 300), Colors.White);

            reshaped.Redraw(record);
            foreach (double pushedX in new[] { 200.0, 175.0, 215.0 })
            {
                Point PlaceNote(TextNote n) => n.X > 200 ? new Point(pushedX, n.Y) : new Point(n.X, n.Y);
                var outline = Square(100, 100, 260);
                reshaped.SetShape(260, 260, 1, new Point(100, 100), outline);
                reshaped.Redraw(record, placeNote: PlaceNote);
                fresh.SetShape(260, 260, 1, new Point(100, 100), outline);
                fresh.Redraw(record, placeNote: PlaceNote);

                Assert.Equal(fresh.ReadPixels(), reshaped.ReadPixels());
            }
        });
    }

    [Fact]
    public void Union_CoversBothRectangles_AndIgnoresEmpty()
    {
        var a = new Int32Rect(10, 10, 20, 20);
        var b = new Int32Rect(50, 5, 10, 10);

        Assert.Equal(new Int32Rect(10, 5, 50, 25), InkLayer.Union(a, b));
        Assert.Equal(a, InkLayer.Union(Int32Rect.Empty, a));
        Assert.Equal(a, InkLayer.Union(a, Int32Rect.Empty));
    }

    private static FormattedText WpfText(TextNote note, Brush brush, double pixelsPerDip)
    {
        var text = new FormattedText(note.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, NoteText.Typeface, note.FontSize, brush, pixelsPerDip);
        if (note.Width is { } width)
            text.MaxTextWidth = width;
        return text;
    }

    private static int Pixel(InkLayer ink, int[] pixels, int x, int y) => pixels[y * ink.Width + x];
}
