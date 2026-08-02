using Android.Content;
using Android.Graphics;
using Android.Graphics.Drawables;
using Android.Views;
using Android.Widget;

namespace Engineering_Watch.UI;

// ============================================================
// 共通テーマ (ダーク基調 + アクセントグリーン)
// AndroidX なしの素のViewでも統一感のある見た目にするためのヘルパー
// ============================================================

public static class Theme
{
    // カラーパレット
    public static readonly Color Bg = Color.Rgb(0x0D, 0x11, 0x15);        // 画面背景
    public static readonly Color Surface = Color.Rgb(0x16, 0x1B, 0x22);   // カード
    public static readonly Color Surface2 = Color.Rgb(0x1E, 0x26, 0x32);  // ボタン/入力
    public static readonly Color Border = Color.Rgb(0x2A, 0x36, 0x42);
    public static readonly Color Accent = Color.Rgb(0x4C, 0xD9, 0x64);    // アクセント
    public static readonly Color AccentDark = Color.Rgb(0x2F, 0x8F, 0x48);
    public static readonly Color Danger = Color.Rgb(0xFF, 0x3B, 0x30);
    public static readonly Color Warn = Color.Rgb(0xFF, 0xCC, 0x00);
    public static readonly Color TextMain = Color.Rgb(0xE6, 0xED, 0xF3);
    public static readonly Color TextDim = Color.Rgb(0x8B, 0x94, 0x9E);
    public static readonly Color Ok = Color.Rgb(0x4C, 0xD9, 0x64);

    public static float Dp(Context ctx, float dp) =>
        dp * (ctx.Resources?.DisplayMetrics?.Density ?? 1f);

    public static GradientDrawable Rounded(Color color, float radiusDp,
        Color? stroke = null, float strokeDp = 0)
    {
        var g = new GradientDrawable();
        g.SetColor(color.ToArgb());
        g.SetCornerRadius(Dp(Application.Context, radiusDp));
        if (stroke.HasValue)
            g.SetStroke((int)Dp(Application.Context, strokeDp), stroke.Value);
        return g;
    }

    // ---- コントロールファクトリ ----

    public static Button Button(Context ctx, string label, bool primary = false)
    {
        var b = new Button(ctx) { Text = label, TextSize = 13 };
        b.SetTextColor(primary ? Color.Black : TextMain);
        b.Background = primary
            ? Rounded(Accent, 10)
            : Rounded(Surface2, 10, Border, 1);
        b.SetPadding((int)Dp(ctx, 16), (int)Dp(ctx, 8),
                     (int)Dp(ctx, 16), (int)Dp(ctx, 8));
        b.SetMinimumHeight((int)Dp(ctx, 42));
        b.SetMinimumWidth(0);
        return b;
    }

    /// <summary>チップ (パレット用の小型ボタン)</summary>
    public static Button Chip(Context ctx, string label, Color? bg = null)
    {
        var b = new Button(ctx) { Text = label, TextSize = 12 };
        b.SetTextColor(TextMain);
        b.Background = Rounded(bg ?? Surface2, 16, Border, 1);
        b.SetPadding((int)Dp(ctx, 12), (int)Dp(ctx, 5),
                     (int)Dp(ctx, 12), (int)Dp(ctx, 5));
        b.SetMinimumHeight((int)Dp(ctx, 34));
        b.SetMinimumWidth(0);
        return b;
    }

    public static TextView SectionHeader(Context ctx, string text)
    {
        var tv = new TextView(ctx)
        {
            Text = text,
            TextSize = 12,
        };
        tv.SetTextColor(Accent);
        tv.SetTypeface(null, TypefaceStyle.Bold);
        tv.SetPadding((int)Dp(ctx, 4), (int)Dp(ctx, 10),
                      (int)Dp(ctx, 4), (int)Dp(ctx, 2));
        return tv;
    }

    public static TextView Label(Context ctx, string text, bool dim = false)
    {
        var tv = new TextView(ctx) { Text = text, TextSize = 13 };
        tv.SetTextColor(dim ? TextDim : TextMain);
        return tv;
    }

    public static EditText Edit(Context ctx, string hint, Android.Text.InputTypes? inputType = null)
    {
        var e = new EditText(ctx)
        {
            Hint = hint,
            TextSize = 14,
            InputType = inputType ?? Android.Text.InputTypes.ClassText,
        };
        e.SetTextColor(TextMain);
        e.SetHintTextColor(TextDim);
        e.Background = Rounded(Surface2, 8, Border, 1);
        e.SetPadding((int)Dp(ctx, 10), (int)Dp(ctx, 8),
                     (int)Dp(ctx, 10), (int)Dp(ctx, 8));
        e.SetMinimumHeight((int)Dp(ctx, 42));
        return e;
    }

    public static CheckBox Check(Context ctx, string label)
    {
        var c = new CheckBox(ctx) { Text = label, TextSize = 14 };
        c.SetTextColor(TextMain);
        return c;
    }

    /// <summary>余白ヘルパー</summary>
    public static View Spacer(Context ctx, int heightDp) =>
        new View(ctx) { LayoutParameters = new ViewGroup.LayoutParams(1, (int)Dp(ctx, heightDp)) };

    public static void SetMargins(View v, Context ctx, int l, int t, int r, int b)
    {
        if (v.LayoutParameters is ViewGroup.MarginLayoutParams mlp)
        {
            mlp.SetMargins((int)Dp(ctx, l), (int)Dp(ctx, t),
                           (int)Dp(ctx, r), (int)Dp(ctx, b));
            v.LayoutParameters = mlp;
        }
    }
}
