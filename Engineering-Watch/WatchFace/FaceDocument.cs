using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Engineering_Watch.WatchFace;

// ============================================================
// 文字盤ドキュメントモデル
// Android側で全パーツを管理し、静的パーツはレンダリングして
// 背景PNGに、動的パーツは dynamic.json として時計に送る。
// ============================================================

public static class PartKinds
{
    // 静的 (背景PNGへレンダリング)
    public const string Rect = "rect";
    public const string Circle = "circle";
    public const string Line = "line";
    public const string Arc = "arc";
    public const string Text = "text";
    public const string Image = "image";
    // 動的 (dynamic.json)
    public const string Clock = "clock_digital";
    public const string Date = "date";
    public const string Analog = "analog";
    public const string Battery = "battery";
    public const string Steps = "steps";
    public const string Wifi = "conn_wifi";
    public const string Ble = "conn_ble";
    public const string Notif = "notif";

    public static bool IsStatic(string kind) =>
        kind is Rect or Circle or Line or Arc or Text or Image;
}

public class FacePart
{
    public string Kind { get; set; } = PartKinds.Text;

    // 位置・サイズ (240x240 論理座標)
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }

    // 色
    public string Color { get; set; } = "#FFFFFF";
    public string Color2 { get; set; } = "";
    public string DimColor { get; set; } = "#333333";

    // 図形
    public float StrokeWidth { get; set; } = 2;
    public float Radius { get; set; }
    public float StartAngle { get; set; }
    public float EndAngle { get; set; }
    public bool Filled { get; set; } = true;

    // テキスト
    public string Text { get; set; } = "TEXT";
    public int FontSize { get; set; } = 16;
    public string Align { get; set; } = "left";
    public bool Bold { get; set; }
    public string FontFamily { get; set; } = "sans";

    // 動的パーツ設定
    public string Format { get; set; } = "";
    public bool ShowSeconds { get; set; }
    public bool ShowPct { get; set; }
    public string Label { get; set; } = "";
    public float Size { get; set; } = 24;

    // 画像 (ローカルパス)
    public string ImagePath { get; set; } = "";

    public FacePart Clone()
    {
        var json = JsonSerializer.Serialize(this, FaceDocument.JsonOpts);
        return JsonSerializer.Deserialize<FacePart>(json, FaceDocument.JsonOpts)!;
    }
}

public class FaceDocument
{
    public int V { get; set; } = 1;
    public string Name { get; set; } = "New Face";
    public string BackgroundColor { get; set; } = "#000000";
    public List<FacePart> Parts { get; set; } = new();

    public static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public string Serialize() => JsonSerializer.Serialize(this, JsonOpts);

    public static FaceDocument Deserialize(string json) =>
        JsonSerializer.Deserialize<FaceDocument>(json, JsonOpts) ?? new FaceDocument();

    // 新規パーツをデフォルト位置で追加
    public FacePart AddPart(string kind)
    {
        var p = new FacePart { Kind = kind };
        switch (kind)
        {
            case PartKinds.Rect: p.W = 60; p.H = 40; p.Color = "#1A2026"; p.Filled = true; p.Radius = 8; break;
            case PartKinds.Circle: p.W = 30; p.H = 30; p.X = 105; p.Y = 105; p.Color = "#1A2026"; p.Filled = true; break;
            case PartKinds.Line: p.X = 20; p.Y = 120; p.W = 200; p.H = 120; p.Color = "#2A3642"; p.StrokeWidth = 2; break;
            case PartKinds.Arc: p.X = 120; p.Y = 120; p.W = 80; p.H = 80; p.Color = "#4CD964"; p.StrokeWidth = 6; p.StartAngle = -90; p.EndAngle = 90; break;
            case PartKinds.Text: p.X = 40; p.Y = 100; p.FontSize = 20; p.Color = "#FFFFFF"; p.Text = "HELLO"; p.W = 160; p.Align = "center"; break;
            case PartKinds.Image: p.X = 60; p.Y = 60; p.W = 120; p.H = 120; break;
            case PartKinds.Clock: p.X = 20; p.Y = 80; p.W = 200; p.H = 60; p.FontSize = 44; p.Color = "#FFFFFF"; p.Format = "HH:MM"; p.Align = "center"; break;
            case PartKinds.Date: p.X = 20; p.Y = 150; p.W = 200; p.H = 24; p.FontSize = 20; p.Color = "#8FA3B8"; p.Format = "MM/DD"; p.Align = "center"; break;
            case PartKinds.Analog: p.X = 120; p.Y = 120; p.W = 100; p.H = 100; p.Color = "#FFFFFF"; p.ShowSeconds = true; break;
            case PartKinds.Battery: p.X = 60; p.Y = 20; p.W = 60; p.H = 16; p.Color = "#4CD964"; p.ShowPct = true; p.FontSize = 12; break;
            case PartKinds.Steps: p.X = 20; p.Y = 190; p.FontSize = 16; p.Color = "#FFCC00"; p.Label = "STEPS "; break;
            case PartKinds.Wifi: p.X = 196; p.Y = 14; p.Size = 24; p.Color = "#8FA3B8"; break;
            case PartKinds.Ble: p.X = 164; p.Y = 14; p.Size = 24; p.Color = "#4CD964"; break;
            case PartKinds.Notif: p.X = 12; p.Y = 14; p.Size = 24; p.Color = "#FF9500"; break;
        }
        Parts.Add(p);
        return p;
    }

    public void RemovePart(FacePart p) => Parts.Remove(p);

    public FacePart? HitTest(float x, float y)
    {
        for (int i = Parts.Count - 1; i >= 0; i--)
        {
            var p = Parts[i];
            if (ContainsPoint(p, x, y)) return p;
        }
        return null;
    }

    private static bool ContainsPoint(FacePart p, float x, float y)
    {
        switch (p.Kind)
        {
            case PartKinds.Line:
                return DistToSegment(x, y, p.X, p.Y, p.W, p.H) < Math.Max(8, p.StrokeWidth + 6);
            case PartKinds.Circle:
            {
                float cx = p.X, cy = p.Y, r = Math.Max(p.W, p.H) / 2;
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return p.Filled ? d <= r + 8 : Math.Abs(d - r) <= Math.Max(8, p.StrokeWidth + 6);
            }
            case PartKinds.Arc:
            {
                float cx = p.X, cy = p.Y, r = Math.Max(p.W, p.H) / 2f;
                float d = (float)Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                return Math.Abs(d - r) <= Math.Max(8, p.StrokeWidth + 6);
            }
            case PartKinds.Text:
            case PartKinds.Clock:
            case PartKinds.Date:
            case PartKinds.Steps:
            {
                float tw = p.W > 0 ? p.W : Math.Max(30, p.Text.Length * p.FontSize * 0.6f);
                float th = p.FontSize * 1.3f;
                return x >= p.X - 8 && x <= p.X + tw + 8 &&
                       y >= p.Y - th - 8 && y <= p.Y + 8;
            }
            case PartKinds.Wifi:
            case PartKinds.Ble:
            case PartKinds.Notif:
            {
                float s = p.Size > 0 ? p.Size : 24;
                return x >= p.X - 8 && x <= p.X + s + 8 &&
                       y >= p.Y - 8 && y <= p.Y + s + 8;
            }
            default:
                return x >= p.X - 8 && x <= p.X + p.W + 8 && y >= p.Y - 8 && y <= p.Y + p.H + 8;
        }
    }

    private static float DistToSegment(float px, float py, float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        float len2 = dx * dx + dy * dy;
        float t = len2 > 0 ? ((px - x1) * dx + (py - y1) * dy) / len2 : 0;
        t = Math.Clamp(t, 0f, 1f);
        float cx = x1 + t * dx, cy = y1 + t * dy;
        return (float)Math.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
    }
}
