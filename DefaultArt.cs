using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MyTypingPet;

public enum Pose { Idle, Left, Right }

/// <summary>画像未設定のときに使う、キーボードを叩くネコの絵 (800x500)。</summary>
public static class DefaultArt
{
    public const int Width = 800, Height = 500;

    static readonly Brush Fur = Solid(0xF4, 0xA2, 0x61);
    static readonly Brush Ink = Solid(0x3D, 0x2C, 0x2E);
    static readonly Brush Cheek = Solid(0xF2, 0x84, 0x82);
    static readonly Brush Board = Solid(0x8D, 0x99, 0xAE);
    static readonly Brush Key = Solid(0xED, 0xF2, 0xF4);
    static readonly Pen Outline = new(Ink, 8) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    public static BitmapSource Render(Pose pose)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            // 耳
            dc.DrawGeometry(Fur, Outline, Triangle(new(255, 175), new(275, 60), new(355, 130)));
            dc.DrawGeometry(Fur, Outline, Triangle(new(545, 175), new(525, 60), new(445, 130)));

            // 体
            dc.DrawEllipse(Fur, Outline, new Point(400, 275), 195, 165);

            // 顔。叩いているときは目を閉じる
            if (pose == Pose.Idle)
            {
                dc.DrawEllipse(Ink, null, new Point(330, 245), 16, 18);
                dc.DrawEllipse(Ink, null, new Point(470, 245), 16, 18);
            }
            else
            {
                var eye = new Pen(Ink, 8) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawGeometry(null, eye, Geometry.Parse("M 312,250 Q 330,232 348,250"));
                dc.DrawGeometry(null, eye, Geometry.Parse("M 452,250 Q 470,232 488,250"));
            }
            dc.DrawEllipse(Cheek, null, new Point(300, 290), 22, 13);
            dc.DrawEllipse(Cheek, null, new Point(500, 290), 22, 13);
            dc.DrawGeometry(null, new Pen(Ink, 6) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round },
                Geometry.Parse("M 375,280 Q 387,298 400,282 Q 413,298 425,280"));

            // キーボード
            dc.DrawRoundedRectangle(Board, Outline, new Rect(140, 385, 520, 90), 18, 18);
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 10; col++)
                    dc.DrawRoundedRectangle(Key, null, new Rect(168 + col * 47, 400 + row * 33, 38, 25), 5, 5);

            // 手 (画面の左右で判定)
            DrawPaw(dc, shoulder: new(285, 340), paw: pose == Pose.Left ? new(215, 235) : new(275, 405));
            DrawPaw(dc, shoulder: new(515, 340), paw: pose == Pose.Right ? new(585, 235) : new(525, 405));
        }

        var bmp = new RenderTargetBitmap(Width, Height, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(visual);
        bmp.Freeze();
        return bmp;
    }

    static void DrawPaw(DrawingContext dc, Point shoulder, Point paw)
    {
        // 輪郭 → 毛色の順に太線を重ねて腕にする
        dc.DrawLine(new Pen(Ink, 62) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, shoulder, paw);
        dc.DrawLine(new Pen(Fur, 46) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, shoulder, paw);
        dc.DrawEllipse(Fur, Outline, paw, 36, 30);
        dc.DrawEllipse(Cheek, null, new Point(paw.X, paw.Y + 4), 11, 9);
    }

    static Geometry Triangle(Point a, Point b, Point c)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(a, isFilled: true, isClosed: true);
            ctx.LineTo(b, true, true);
            ctx.LineTo(c, true, true);
        }
        g.Freeze();
        return g;
    }

    static Brush Solid(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
