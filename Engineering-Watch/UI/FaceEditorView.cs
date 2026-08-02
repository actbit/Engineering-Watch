using System;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Engineering_Watch.WatchFace;

namespace Engineering_Watch.UI;

// ============================================================
// 文字盤エディタビュー (ドラッグ&ドロップ配置)
// 240x240 論理座標をビューにフィットさせて表示する。
// ============================================================

public class FaceEditorView : View
{
    public FaceDocument Document { get; private set; } = new();
    public FacePart? Selected { get; private set; }

    public event Action? SelectionChanged;

    private Bitmap? _preview;
    private float _scale = 1f;
    private float _offX, _offY;   // 描画オフセット
    private FacePart? _dragPart;
    private bool _resizing;
    private float _lastX, _lastY;

    private readonly DateTime _previewTime = DateTime.Now;
    private readonly int _previewSteps = 1234;
    private readonly int _previewBattery = 87;

    public FaceEditorView(Context ctx) : base(ctx)
    {
        SetBackgroundColor(Color.Rgb(0x0D, 0x11, 0x15));
        SetLayerType(LayerType.Software, null);
    }

    protected override void OnDraw(Canvas canvas)
    {
        base.OnDraw(canvas);
        if (_preview == null) return;
        canvas.DrawBitmap(_preview, null,
            new RectF(_offX, _offY, _offX + FaceRenderer.WatchW * _scale, _offY + FaceRenderer.WatchH * _scale), null);

        // 空状態ヒント
        if (Document.Parts.Count == 0)
        {
            var hint = new Paint(PaintFlags.AntiAlias) { Color = Color.Rgb(0x55, 0x66, 0x77) };
            hint.TextSize = 15f * _scale;
            hint.TextAlign = Paint.Align.Center;
            canvas.DrawText("下のパレットからパーツを追加", _offX + FaceRenderer.WatchW * _scale / 2,
                _offY + FaceRenderer.WatchH * _scale / 2, hint);
        }

        // 選択ハイライト
        if (Selected != null)
        {
            var paint = new Paint { Color = Color.Rgb(0x4C, 0xD9, 0x64) };
            paint.SetStyle(Paint.Style.Stroke);
            paint.StrokeWidth = 2f / _scale;
            var r = BoundsOf(Selected);
            var rect = new RectF(
                _offX + r.Left * _scale, _offY + r.Top * _scale,
                _offX + r.Right * _scale, _offY + r.Bottom * _scale);
            canvas.DrawRect(rect, paint);
            canvas.DrawCircle(rect.Right, rect.Bottom, 5f, new Paint { Color = Color.Rgb(0x4C, 0xD9, 0x64) });
        }
    }

    public void SetDocument(FaceDocument doc)
    {
        Document = doc;
        Selected = null;
        RefreshPreview();
        SelectionChanged?.Invoke();
    }

    public void Select(FacePart? p)
    {
        Selected = p;
        SelectionChanged?.Invoke();
        Invalidate();
    }

    public void RefreshPreview()
    {
        _preview?.Recycle();
        _preview = FaceRenderer.RenderPreview(
            Document, _previewTime, _previewSteps, _previewBattery,
            charging: false, wifiOn: true, bleOn: true, unread: 2);
        Invalidate();
    }

    protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
    {
        base.OnSizeChanged(w, h, oldw, oldh);
        _scale = Math.Min((w - 16f) / FaceRenderer.WatchW, (h - 16f) / FaceRenderer.WatchH);
        if (_scale <= 0) _scale = 1;
        _offX = (w - FaceRenderer.WatchW * _scale) / 2f;
        _offY = (h - FaceRenderer.WatchH * _scale) / 2f;
    }

    private (float x, float y) ToLogical(float px, float py)
        => ((px - _offX) / _scale, (py - _offY) / _scale);

    private (float x, float y) ToScreen(float lx, float ly)
        => (lx * _scale + _offX, ly * _scale + _offY);

    private RectF BoundsOf(FacePart p)
    {
        switch (p.Kind)
        {
            case PartKinds.Line:
                return new RectF(
                    Math.Min(p.X, p.W) - 6, Math.Min(p.Y, p.H) - 6,
                    Math.Max(p.X, p.W) + 6, Math.Max(p.Y, p.H) + 6);
            case PartKinds.Circle:
            case PartKinds.Arc:
            {
                float r = Math.Max(p.W, p.H) / 2f;
                return new RectF(p.X - r - 6, p.Y - r - 6, p.X + r + 6, p.Y + r + 6);
            }
            case PartKinds.Analog:
            {
                float r = Math.Max(p.W, p.H) / 2f;
                return new RectF(p.X - r - 6, p.Y - r - 6, p.X + r + 6, p.Y + r + 6);
            }
            case PartKinds.Text:
            case PartKinds.Clock:
            case PartKinds.Date:
            case PartKinds.Steps:
            {
                // テキストの実描画領域 (ベースライン起点) に合わせる
                float tw = p.W > 0 ? p.W : Math.Max(30, p.Text.Length * p.FontSize * 0.6f);
                float th = p.FontSize * 1.3f;
                return new RectF(p.X - 4, p.Y - th - 2, p.X + tw + 4, p.Y + 4);
            }
            case PartKinds.Wifi:
            case PartKinds.Ble:
            case PartKinds.Notif:
            {
                float s = p.Size > 0 ? p.Size : 24;
                return new RectF(p.X - 4, p.Y - 4, p.X + s + 4, p.Y + s + 4);
            }
            default:
                return new RectF(p.X - 6, p.Y - 6, p.X + Math.Max(p.W, 1) + 6, p.Y + Math.Max(p.H, 1) + 6);
        }
    }

    private bool NearHandle(FacePart p, float lx, float ly)
    {
        var r = BoundsOf(p);
        float dx = lx - r.Right, dy = ly - r.Bottom;
        return dx * dx + dy * dy <= 100f;
    }

    public override bool OnTouchEvent(MotionEvent? e)
    {
        if (e == null) return base.OnTouchEvent(e);
        var (lx, ly) = ToLogical(e.GetX(), e.GetY());
        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                if (Selected != null && NearHandle(Selected, lx, ly))
                {
                    _resizing = true;
                }
                else
                {
                    _dragPart = Document.HitTest(lx, ly);
                    _resizing = false;
                    if (_dragPart != null) Select(_dragPart);
                }
                _lastX = lx; _lastY = ly;
                return true;
            case MotionEventActions.Move:
            {
                float dx = lx - _lastX, dy = ly - _lastY;
                var p = _resizing ? Selected : _dragPart;
                if (p != null)
                {
                    if (_resizing)
                    {
                        // 右下ハンドルでリサイズ
                        switch (p.Kind)
                        {
                            case PartKinds.Line:
                                p.W += dx; p.H += dy; break;
                            case PartKinds.Circle:
                            case PartKinds.Arc:
                            case PartKinds.Analog:
                            {
                                float r = Math.Max(p.W, p.H) / 2f;
                                r = Math.Max(6, r + Math.Max(dx, dy));
                                p.W = r * 2; p.H = r * 2;
                                break;
                            }
                            default:
                                p.W = Math.Max(4, p.W + dx);
                                p.H = Math.Max(4, p.H + dy);
                                break;
                        }
                        RefreshPreview();
                    }
                    else
                    {
                        MovePart(p, dx, dy);
                    }
                    _lastX = lx; _lastY = ly;
                }
                return true;
            }
            case MotionEventActions.Up:
            case MotionEventActions.Cancel:
                _dragPart = null;
                _resizing = false;
                return true;
        }
        return base.OnTouchEvent(e);
    }

    private void MovePart(FacePart p, float dx, float dy)
    {
        switch (p.Kind)
        {
            case PartKinds.Line:
                p.X += dx; p.Y += dy; p.W += dx; p.H += dy; break;
            default:
                p.X += dx; p.Y += dy; break;
        }
        RefreshPreview();
    }
}
