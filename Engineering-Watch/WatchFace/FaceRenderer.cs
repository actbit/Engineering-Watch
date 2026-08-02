using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Android.Graphics;

namespace Engineering_Watch.WatchFace;

// ============================================================
// 文字盤レンダラー
// 静的パーツ → 背景PNG (240x240)、アナログ針 → 透過PNG、
// 動的パーツ → dynamic.json、プレビュー → Bitmap
// ============================================================

public static class FaceRenderer
{
    public const int WatchW = 240;
    public const int WatchH = 240;

    private static readonly int[] LvglFonts = { 12, 14, 16, 18, 20, 22, 24, 28, 32, 36, 40, 44, 48 };

    public static int SnapFont(int size)
    {
        int best = 16;
        foreach (var s in LvglFonts)
        {
            best = s;
            if (size <= s) break;
        }
        return best;
    }

    public static Color ParseColor(string hex, Color def)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#') return def;
        try
        {
            var v = Convert.ToInt32(hex.Substring(1), 16);
            return Color.Rgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }
        catch { return def; }
    }

    private static Typeface TypefaceFor(string family, bool bold)
    {
        var style = bold ? TypefaceStyle.Bold : TypefaceStyle.Normal;
        switch (family)
        {
            case "serif": return Typeface.Create(Typeface.Serif, style);
            case "mono": return Typeface.Create(Typeface.Monospace, style);
            case "condensed": return Typeface.Create("sans-serif-condensed", style);
            case "thin": return Typeface.Create("sans-serif-thin", style);
            case "light": return Typeface.Create("sans-serif-light", style);
            default: return Typeface.Create(Typeface.Default, style);
        }
    }

    private static Paint MakePaint(Color color)
    {
        return new Paint(PaintFlags.AntiAlias)
        {
            Color = color,
            StrokeCap = Paint.Cap.Round,
            StrokeJoin = Paint.Join.Round,
        };
    }

    private static Paint MakeStrokePaint(Color color, float strokeWidth, Paint.Style style)
    {
        var paint = MakePaint(color);
        paint.StrokeWidth = strokeWidth;
        paint.SetStyle(style);
        return paint;
    }

    // ============================================================
    // 静的パーツ描画 (背景へ)
    // ============================================================
    private static void DrawStaticPart(Canvas c, FacePart p)
    {
        var color = ParseColor(p.Color, Color.White);
        switch (p.Kind)
        {
            case PartKinds.Rect:
            {
                var r = new RectF(p.X, p.Y, p.X + p.W, p.Y + p.H);
                var paint = MakePaint(color);
                if (p.Filled)
                {
                    paint.SetStyle(Paint.Style.Fill);
                    if (p.Radius > 0) c.DrawRoundRect(r, p.Radius, p.Radius, paint);
                    else c.DrawRect(r, paint);
                }
                else
                {
                    paint.SetStyle(Paint.Style.Stroke);
                    paint.StrokeWidth = p.StrokeWidth;
                    if (p.Radius > 0) c.DrawRoundRect(r, p.Radius, p.Radius, paint);
                    else c.DrawRect(r, paint);
                }
                break;
            }
            case PartKinds.Circle:
            {
                float r = Math.Max(p.W, p.H) / 2f;
                var paint = MakePaint(color);
                if (p.Filled) { paint.SetStyle(Paint.Style.Fill); c.DrawCircle(p.X, p.Y, r, paint); }
                else { paint.SetStyle(Paint.Style.Stroke); paint.StrokeWidth = p.StrokeWidth; c.DrawCircle(p.X, p.Y, r, paint); }
                break;
            }
            case PartKinds.Line:
            {
                var paint = MakeStrokePaint(color, p.StrokeWidth, Paint.Style.Stroke);
                paint.StrokeWidth = p.StrokeWidth;
                c.DrawLine(p.X, p.Y, p.W, p.H, paint);
                break;
            }
            case PartKinds.Arc:
            {
                float r = Math.Max(p.W, p.H) / 2f;
                var rect = new RectF(p.X - r, p.Y - r, p.X + r, p.Y + r);
                var paint = MakeStrokePaint(color, p.StrokeWidth, Paint.Style.Stroke);
                c.DrawArc(rect, p.StartAngle, p.EndAngle - p.StartAngle, false, paint);
                break;
            }
            case PartKinds.Text:
            {
                var paint = MakePaint(ParseColor(p.Color, Color.White));
                paint.TextSize = p.FontSize;
                paint.SetTypeface(TypefaceFor(p.FontFamily, p.Bold));
                switch (p.Align)
                {
                    case "center": paint.TextAlign = Paint.Align.Center; c.DrawText(p.Text, p.X + (p.W > 0 ? p.W / 2 : 0), p.Y, paint); break;
                    case "right": paint.TextAlign = Paint.Align.Right; c.DrawText(p.Text, p.X + p.W, p.Y, paint); break;
                    default: paint.TextAlign = Paint.Align.Left; c.DrawText(p.Text, p.X, p.Y, paint); break;
                }
                break;
            }
            case PartKinds.Image:
            {
                if (string.IsNullOrEmpty(p.ImagePath) || !File.Exists(p.ImagePath)) break;
                using var opts = new BitmapFactory.Options { InSampleSize = 1 };
                var src = BitmapFactory.DecodeFile(p.ImagePath, opts);
                if (src == null) break;
                // FitCenter で WxH に収める
                float scale = Math.Min(p.W / (float)src.Width, p.H / (float)src.Height);
                if (scale <= 0) scale = 1;
                int dw = Math.Max(1, (int)(src.Width * scale));
                int dh = Math.Max(1, (int)(src.Height * scale));
                var dst = Bitmap.CreateScaledBitmap(src, dw, dh, true);
                c.DrawBitmap(dst, p.X + (p.W - dw) / 2f, p.Y + (p.H - dh) / 2f, null);
                dst.Recycle();
                src.Recycle();
                break;
            }
        }
    }

    // ============================================================
    // 背景 PNG
    // ============================================================
    public static byte[] RenderBackgroundPng(FaceDocument doc)
    {
        using var bmp = Bitmap.CreateBitmap(WatchW, WatchH, Bitmap.Config.Argb8888);
        using var c = new Canvas(bmp);
        c.DrawColor(ParseColor(doc.BackgroundColor, Color.Black));
        foreach (var p in doc.Parts)
        {
            if (PartKinds.IsStatic(p.Kind)) DrawStaticPart(c, p);
        }
        using var ms = new MemoryStream();
        bmp.Compress(Bitmap.CompressFormat.Png, 100, ms);
        return ms.ToArray();
    }

    // ============================================================
    // アナログ針 PNG (回転中心 = 画像中央)
    // ============================================================
    public static byte[] RenderHandPng(int length, int width, Color color)
    {
        int imgW = Math.Max(4, width * 2 + 6);
        int imgH = Math.Max(8, length * 2);
        using var bmp = Bitmap.CreateBitmap(imgW, imgH, Bitmap.Config.Argb8888);
        using var c = new Canvas(bmp);
        var paint = MakePaint(color);
        paint.SetStyle(Paint.Style.Fill);

        // 針の輪郭: 先端(上) → 基端(中央) → テール(下)
        float cx = imgW / 2f;
        float tipY = 2f;
        float baseY = length;
        float tailY = imgH - 2f;
        var path = new Android.Graphics.Path();
        path.MoveTo(cx, tipY);
        path.LineTo(cx + width / 2f, baseY);
        path.LineTo(cx + width / 4f, tailY);
        path.LineTo(cx - width / 4f, tailY);
        path.LineTo(cx - width / 2f, baseY);
        path.Close();
        c.DrawPath(path, paint);
        c.DrawCircle(cx, tipY, Math.Max(1.5f, width / 4f), paint);

        using var ms = new MemoryStream();
        bmp.Compress(Bitmap.CompressFormat.Png, 100, ms);
        return ms.ToArray();
    }

    // ============================================================
    // dynamic.json
    // ============================================================
    public static string BuildDynamicJson(FaceDocument doc)
    {
        var parts = new List<object>();
        foreach (var p in doc.Parts)
        {
            if (PartKinds.IsStatic(p.Kind)) continue;
            var d = new Dictionary<string, object> { ["t"] = p.Kind };
            switch (p.Kind)
            {
                case PartKinds.Clock:
                    d["x"] = (int)p.X; d["y"] = (int)p.Y; d["w"] = (int)p.W; d["h"] = (int)p.H;
                    d["font"] = SnapFont(p.FontSize);
                    d["color"] = p.Color;
                    d["format"] = string.IsNullOrEmpty(p.Format) ? "HH:MM" : p.Format;
                    d["align"] = p.Align;
                    break;
                case PartKinds.Date:
                    d["x"] = (int)p.X; d["y"] = (int)p.Y;
                    d["font"] = SnapFont(p.FontSize);
                    d["color"] = p.Color;
                    d["format"] = string.IsNullOrEmpty(p.Format) ? "MM/DD" : p.Format;
                    d["align"] = p.Align;
                    break;
                case PartKinds.Analog:
                    d["cx"] = (int)p.X; d["cy"] = (int)p.Y;
                    d["r"] = (int)(Math.Max(p.W, p.H) / 2f);
                    d["show_seconds"] = p.ShowSeconds;
                    d["axis_color"] = p.Color;
                    break;
                case PartKinds.Battery:
                    d["x"] = (int)p.X; d["y"] = (int)p.Y;
                    d["w"] = (int)p.W; d["h"] = (int)p.H;
                    d["color"] = p.Color;
                    d["show_pct"] = p.ShowPct;
                    d["font"] = SnapFont(p.FontSize);
                    break;
                case PartKinds.Steps:
                    d["x"] = (int)p.X; d["y"] = (int)p.Y;
                    d["font"] = SnapFont(p.FontSize);
                    d["color"] = p.Color;
                    d["label"] = p.Label;
                    d["align"] = p.Align;
                    break;
                case PartKinds.Wifi:
                case PartKinds.Ble:
                case PartKinds.Notif:
                    d["x"] = (int)p.X; d["y"] = (int)p.Y;
                    d["size"] = (int)p.Size;
                    d["color"] = p.Color;
                    d["dim_color"] = p.DimColor;
                    break;
            }
            parts.Add(d);
        }
        var root = new Dictionary<string, object>
        {
            ["v"] = 1,
            ["name"] = doc.Name,
            ["parts"] = parts,
        };
        return JsonSerializer.Serialize(root);
    }

    // ============================================================
    // プレビュー (エディタ表示用)
    // ============================================================
    public static Bitmap RenderPreview(FaceDocument doc, DateTime now, int steps, int battery,
        bool charging, bool wifiOn, bool bleOn, int unread)
    {
        var bmp = Bitmap.CreateBitmap(WatchW, WatchH, Bitmap.Config.Argb8888);
        var c = new Canvas(bmp);
        c.DrawColor(ParseColor(doc.BackgroundColor, Color.Black));
        foreach (var p in doc.Parts)
        {
            if (PartKinds.IsStatic(p.Kind)) DrawStaticPart(c, p);
        }
        foreach (var p in doc.Parts)
        {
            if (PartKinds.IsStatic(p.Kind)) continue;
            DrawDynamicPart(c, p, now, steps, battery, charging, wifiOn, bleOn, unread);
        }
        c.Dispose();
        return bmp;
    }

    private static void DrawDynamicPart(Canvas c, FacePart p, DateTime now, int steps, int battery,
        bool charging, bool wifiOn, bool bleOn, int unread)
    {
        switch (p.Kind)
        {
            case PartKinds.Clock:
            {
                var paint = MakePaint(ParseColor(p.Color, Color.White));
                paint.TextSize = p.FontSize;
                string s = FormatTime(p.Format, now);
                DrawAligned(c, paint, s, p.X, p.Y, p.W, p.Align);
                break;
            }
            case PartKinds.Date:
            {
                var paint = MakePaint(ParseColor(p.Color, Color.White));
                paint.TextSize = p.FontSize;
                string s = FormatDate(p.Format, now);
                DrawAligned(c, paint, s, p.X, p.Y, p.W, p.Align);
                break;
            }
            case PartKinds.Analog:
            {
                float cx = p.X, cy = p.Y;
                float r = Math.Max(p.W, p.H) / 2f;
                var col = ParseColor(p.Color, Color.White);
                float h = (now.Hour % 12) + now.Minute / 60f;
                float m = now.Minute + now.Second / 60f;
                DrawHand(c, cx, cy, h * 30f, r * 0.5f, 7, col);
                DrawHand(c, cx, cy, m * 6f, r * 0.72f, 5, col);
                if (p.ShowSeconds)
                {
                    var sc = ParseColor(string.IsNullOrEmpty(p.Color2) ? "#FF3B30" : p.Color2, Color.Rgb(0xFF, 0x3B, 0x30));
                    DrawHand(c, cx, cy, now.Second * 6f, r * 0.82f, 2, sc);
                }
                var axisPaint = MakePaint(col);
                c.DrawCircle(cx, cy, 4, axisPaint);
                break;
            }
            case PartKinds.Battery:
            {
                var col = ParseColor(p.Color, Color.Lime);
                var frame = new RectF(p.X, p.Y, p.X + p.W, p.Y + p.H);
                var stroke = MakeStrokePaint(col, 1.5f, Paint.Style.Stroke);
                c.DrawRoundRect(frame, 3, 3, stroke);
                int pct = Math.Clamp(battery, 0, 100);
                float fw = (p.W - 4) * pct / 100f;
                if (fw > 0)
                {
                    var fill = MakePaint(charging ? Color.Rgb(0x4C, 0xD9, 0x64) : col);
                    c.DrawRoundRect(new RectF(p.X + 2, p.Y + 2, p.X + 2 + fw, p.Y + p.H - 2), 2, 2, fill);
                }
                if (p.ShowPct)
                {
                    var tp = MakePaint(col);
                    tp.TextSize = p.FontSize;
                    c.DrawText($"{pct}%", p.X + p.W + 4, p.Y + p.H - 2, tp);
                }
                break;
            }
            case PartKinds.Steps:
            {
                var paint = MakePaint(ParseColor(p.Color, Color.White));
                paint.TextSize = p.FontSize;
                DrawAligned(c, paint, $"{p.Label}{steps}", p.X, p.Y, p.W, p.Align);
                break;
            }
            case PartKinds.Wifi:
                DrawWifiIcon(c, p.X, p.Y, p.Size, wifiOn ? p.Color : p.DimColor);
                break;
            case PartKinds.Ble:
                DrawBleIcon(c, p.X, p.Y, p.Size, bleOn ? p.Color : p.DimColor);
                break;
            case PartKinds.Notif:
            {
                DrawBellIcon(c, p.X, p.Y, p.Size, unread > 0 ? p.Color : p.DimColor);
                if (unread > 0)
                {
                    var badgePaint = MakePaint(Color.Rgb(0xFF, 0x3B, 0x30));
                    c.DrawCircle(p.X + p.Size - 4, p.Y - 2, 8, badgePaint);
                    var tp = MakePaint(Color.White);
                    tp.TextSize = 12;
                    tp.TextAlign = Paint.Align.Center;
                    c.DrawText(unread > 99 ? "99" : unread.ToString(), p.X + p.Size - 4, p.Y + 3, tp);
                }
                break;
            }
        }
    }

    private static void DrawAligned(Canvas c, Paint paint, string s, float x, float y, float w, string align)
    {
        switch (align)
        {
            case "center": paint.TextAlign = Paint.Align.Center; c.DrawText(s, x + w / 2f, y, paint); break;
            case "right": paint.TextAlign = Paint.Align.Right; c.DrawText(s, x + w, y, paint); break;
            default: paint.TextAlign = Paint.Align.Left; c.DrawText(s, x, y, paint); break;
        }
    }

    public static string FormatTime(string format, DateTime now)
    {
        if (string.IsNullOrEmpty(format)) format = "HH:MM";
        var s = format;
        var h12 = now.Hour % 12 == 0 ? 12 : now.Hour % 12;
        s = s.Replace("HH", now.Hour.ToString("00"))
             .Replace("hh", h12.ToString("00"))
             .Replace("MM", now.Minute.ToString("00"))
             .Replace("SS", now.Second.ToString("00"))
             .Replace("A", now.Hour < 12 ? "AM" : "PM");
        return s;
    }

    public static string FormatDate(string format, DateTime now)
    {
        if (string.IsNullOrEmpty(format)) format = "MM/DD";
        int wd = ((int)now.DayOfWeek + 6) % 7 + 1; // 1=月
        var s = format;
        s = s.Replace("YYYY", now.Year.ToString("0000"))
             .Replace("YY", (now.Year % 100).ToString("00"))
             .Replace("MM", now.Month.ToString("00"))
             .Replace("M", now.Month.ToString())
             .Replace("DD", now.Day.ToString("00"))
             .Replace("D", now.Day.ToString())
             .Replace("W", wd.ToString());
        return s;
    }

    private static void DrawHand(Canvas c, float cx, float cy, float deg, float len, float width, Color color)
    {
        c.Save();
        c.Rotate(deg, cx, cy);
        var paint = MakePaint(color);
        paint.SetStyle(Paint.Style.Fill);
        var path = new Android.Graphics.Path();
        path.MoveTo(cx - width / 2f, cy);
        path.LineTo(cx + width / 2f, cy);
        path.LineTo(cx + width / 4f, cy - len);
        path.LineTo(cx - width / 4f, cy - len);
        path.Close();
        c.DrawPath(path, paint);
        c.Restore();
    }

    // ---- 状態アイコン (プレビュー用の簡易描画) ----
    private static void DrawWifiIcon(Canvas c, float x, float y, float size, string colorHex)
    {
        var col = ParseColor(colorHex, Color.White);
        var paint = MakeStrokePaint(col, Math.Max(1.5f, size / 12f), Paint.Style.Stroke);
        float cx = x + size / 2f, cy = y + size;
        float[] rs = { size * 0.22f, size * 0.45f, size * 0.68f };
        foreach (var r in rs)
        {
            c.DrawArc(new RectF(cx - r, cy - r, cx + r, cy + r), 200, 140, false, paint);
        }
        c.DrawCircle(cx, cy - 1, Math.Max(1.2f, size / 16f), MakePaint(col));
    }

    private static void DrawBleIcon(Canvas c, float x, float y, float size, string colorHex)
    {
        var col = ParseColor(colorHex, Color.White);
        var paint = MakeStrokePaint(col, Math.Max(1.5f, size / 9f), Paint.Style.Stroke);
        float s = size / 12f;
        c.DrawLine(x + 3 * s, y + 1.5f * s, x + 7 * s, y + 4.5f * s, paint);
        c.DrawLine(x + 7 * s, y + 4.5f * s, x + 3 * s, y + 7.5f * s, paint);
        c.DrawLine(x + 7 * s, y + 1.5f * s, x + 3 * s, y + 4.5f * s, paint);
        c.DrawLine(x + 3 * s, y + 4.5f * s, x + 7 * s, y + 7.5f * s, paint);
    }

    private static void DrawBellIcon(Canvas c, float x, float y, float size, string colorHex)
    {
        var col = ParseColor(colorHex, Color.White);
        var paint = MakeStrokePaint(col, Math.Max(1.5f, size / 10f), Paint.Style.Stroke);
        float cx = x + size / 2f, cy = y + size / 2f;
        float r = size * 0.32f;
        c.DrawArc(new RectF(cx - r, cy - r, cx + r, cy + r), 200, 140, false, paint);
        c.DrawLine(x + 2, y + size * 0.72f, x + size - 2, y + size * 0.72f, paint);
        c.DrawCircle(cx, y + size * 0.85f, size * 0.1f, MakePaint(col));
    }
}
