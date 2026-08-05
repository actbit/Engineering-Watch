using System;
using System.Collections.Generic;
using System.Linq;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Views;
using Android.Widget;
using Engineering_Watch.BLE;
using Engineering_Watch.WatchFace;
using IO = System.IO;

namespace Engineering_Watch.UI;

// ============================================================
// 文字盤エディタタブ (改善版UI)
// ヘッダー / 送信 / パーツパレット(チップ) / キャンバス /
// プロパティパネル (カラースウォッチ・チップ・トグル)
// ============================================================

public class EditorTabView : LinearLayout
{
    private readonly Activity _activity;
    private readonly FaceEditorView _editor;
    private readonly TextView _status;
    private readonly LinearLayout _propPanel;
    private readonly ScrollView _propScroll;
    private readonly TextView _propHint;
    private readonly EditText _faceName;

    // プロパティフィールド
    private readonly Dictionary<string, EditText> _fields = new();
    private readonly Dictionary<string, CheckBox> _checks = new();
    private readonly Dictionary<string, LinearLayout> _rows = new();
    private LinearLayout? _colorRow, _color2Row, _dimRow;
    private RadioGroup? _alignGroup;
    private LinearLayout? _fontChips, _sizeChips, _alignRow;

    private static readonly string[] Swatches =
    {
        "#FFFFFF", "#000000", "#4CD964", "#FF3B30",
        "#4D9FFF", "#FFCC00", "#FF9500", "#8FA3B8",
        "#8B5CF6", "#FF69B4",
    };

    private const int PickImage = 1001;
    private bool _sending;   // 送信中フラグ (二重タップ防止)

    public EditorTabView(Activity activity) : base(activity)
    {
        _activity = activity;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Theme.Bg);
        SetPadding((int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 8),
                   (int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 4));

        // ---- ヘッダー: 名前 + 保存/読込 ----
        var headRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        headRow.SetGravity(GravityFlags.CenterVertical);
        _faceName = Theme.Edit(activity, "文字盤名");
        headRow.AddView(_faceName, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        var saveBtn = Theme.Chip(activity, "保存");
        saveBtn.Click += (_, _) => SaveFace();
        var loadBtn = Theme.Chip(activity, "読込");
        loadBtn.Click += (_, _) => LoadFace();
        headRow.AddView(saveBtn);
        headRow.AddView(loadBtn);
        AddView(headRow);
        _faceName.TextChanged += (_, _) => _editor.Document.Name = _faceName.Text ?? "New Face";

        // ---- 送信ボタン ----
        var sendBtn = Theme.Button(activity, "▶ 時計に送信", primary: true);
        sendBtn.TextSize = 16;
        sendBtn.Click += (_, _) => SendToWatch();
        Theme.SetMargins(sendBtn, activity, 0, 8, 0, 4);
        AddView(sendBtn);

        // ---- パレット ----
        AddView(Theme.SectionHeader(activity, "パーツを追加"));
        var pal = new HorizontalScrollView(activity) { HorizontalScrollBarEnabled = false };
        var palRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        AddPalette(palRow, "時計", PartKinds.Clock);
        AddPalette(palRow, "日付", PartKinds.Date);
        AddPalette(palRow, "アナログ", PartKinds.Analog);
        AddPalette(palRow, "テキスト", PartKinds.Text);
        AddPalette(palRow, "矩形", PartKinds.Rect);
        AddPalette(palRow, "円", PartKinds.Circle);
        AddPalette(palRow, "線", PartKinds.Line);
        AddPalette(palRow, "円弧", PartKinds.Arc);
        AddPalette(palRow, "電池", PartKinds.Battery);
        AddPalette(palRow, "歩数", PartKinds.Steps);
        AddPalette(palRow, "WiFi", PartKinds.Wifi);
        AddPalette(palRow, "BT", PartKinds.Ble);
        AddPalette(palRow, "通知", PartKinds.Notif);
        AddPalette(palRow, "画像", PartKinds.Image);
        pal.AddView(palRow);
        AddView(pal);

        // ---- キャンバス ----
        _editor = new FaceEditorView(activity);
        _editor.SelectionChanged += OnSelectionChanged;
        _editor.Background = Theme.Rounded(Theme.Surface, 12, Theme.Border, 1);
        var canvasWrap = new FrameLayout(activity);
        canvasWrap.AddView(_editor);
        Theme.SetMargins(canvasWrap, activity, 0, 8, 0, 4);
        AddView(canvasWrap, new LayoutParams(LayoutParams.MatchParent, 0, 1f));

        // ---- プロパティ ----
        AddView(Theme.SectionHeader(activity, "プロパティ"));
        _propHint = Theme.Label(activity, "パーツをタップすると設定が開きます", dim: true);
        AddView(_propHint);

        _propScroll = new ScrollView(activity);
        _propPanel = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        _propScroll.AddView(_propPanel);
        Theme.SetMargins(_propScroll, activity, 0, 0, 0, 0);
        AddView(_propScroll, new LayoutParams(LayoutParams.MatchParent, 280));

        // ---- ステータス ----
        _status = Theme.Label(activity, "パレットからパーツを追加して文字盤を作成", dim: true);
        _status.TextSize = 12;
        Theme.SetMargins(_status, activity, 2, 4, 0, 0);
        AddView(_status);

        BuildPropertyPanel();
        OnSelectionChanged();
    }

    // ============================================================
    // パレット
    // ============================================================

    private void AddPalette(LinearLayout row, string label, string kind)
    {
        var chip = Theme.Chip(_activity, "+ " + label);
        Theme.SetMargins(chip, _activity, 2, 0, 2, 0);
        chip.Click += (_, _) =>
        {
            var p = _editor.Document.AddPart(kind);
            _editor.Select(p);
            _editor.RefreshPreview();
            _status.Text = $"{label}を追加 ({_editor.Document.Parts.Count}パーツ)";
        };
        row.AddView(chip);
    }

    private void OnSelectionChanged()
    {
        var p = _editor.Selected;
        bool sel = p != null;
        _propScroll.Visibility = sel ? ViewStates.Visible : ViewStates.Gone;
        _propHint.Visibility = sel ? ViewStates.Gone : ViewStates.Visible;
        if (p == null) return;
        UpdatePanelFromPart(p);
    }

    // ============================================================
    // プロパティパネル構築
    // ============================================================

    private LinearLayout Row(string key)
    {
        var row = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
        row.SetGravity(GravityFlags.CenterVertical);
        _rows[key] = row;
        return row;
    }

    private EditText AddFieldTo(LinearLayout row, string key, string label, int weight = 1)
    {
        var tv = Theme.Label(_activity, label, dim: true);
        tv.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        var edit = Theme.Edit(_activity, "");
        edit.TextSize = 13;
        edit.SetMinimumHeight((int)Theme.Dp(_activity, 36));
        edit.TextChanged += (_, _) => OnFieldEdited(key, edit.Text);
        _fields[key] = edit;
        row.AddView(tv, new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent));
        row.AddView(edit, new LayoutParams(0, LayoutParams.WrapContent, weight));
        return edit;
    }

    private void AddColorRow(LinearLayout row, string key, string label)
    {
        var tv = Theme.Label(_activity, label, dim: true);
        tv.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        row.AddView(tv, new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent));
        // スウォッチは横スクロール可能に (狭い画面で切れないように)
        var swatchScroll = new HorizontalScrollView(_activity) { HorizontalScrollBarEnabled = false };
        var swatchRow = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
        foreach (var hex in Swatches)
        {
            var sw = new View(_activity);
            int size = (int)Theme.Dp(_activity, 22);
            sw.Background = Theme.Rounded(ParseColor(hex, Color.White), 6, Theme.Border, 1);
            sw.Clickable = true;
            var h = hex; // クロージャ用コピー
            sw.Click += (_, _) =>
            {
                var p = _editor.Selected;
                if (p == null) return;
                SetPartColor(p, key, h);
                _editor.RefreshPreview();
            };
            var lp = new LinearLayout.LayoutParams(size, size);
            lp.SetMargins((int)Theme.Dp(_activity, 2), 0, (int)Theme.Dp(_activity, 2), 0);
            swatchRow.AddView(sw, lp);
        }
        swatchScroll.AddView(swatchRow);
        var hexEdit = Theme.Edit(_activity, "#RRGGBB");
        hexEdit.TextSize = 12;
        hexEdit.SetMinimumHeight((int)Theme.Dp(_activity, 36));
        hexEdit.TextChanged += (_, _) =>
        {
            var p = _editor.Selected;
            if (p == null) return;
            var s = hexEdit.Text?.Trim() ?? "";
            if (s.Length == 7 && s[0] == '#') { SetPartColor(p, key, s); _editor.RefreshPreview(); }
        };
        _fields[key] = hexEdit;
        row.AddView(swatchScroll, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        row.AddView(hexEdit, new LayoutParams(0, LayoutParams.WrapContent, 1f));
    }

    private static void SetPartColor(FacePart p, string key, string hex)
    {
        switch (key)
        {
            case "Color": p.Color = hex; break;
            case "Color2": p.Color2 = hex; break;
            case "Dim": p.DimColor = hex; break;
        }
    }

    private void AddChipsRow(string key, string label, int[] values, Action<FacePart, int> apply, string? suffix = null)
    {
        var row = Row(key);
        var tv = Theme.Label(_activity, label, dim: true);
        tv.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        row.AddView(tv, new LayoutParams(LayoutParams.WrapContent, LayoutParams.WrapContent));
        var chips = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
        foreach (var v in values)
        {
            var chip = Theme.Chip(_activity, v + (suffix ?? ""));
            chip.TextSize = 12;
            var val = v;
            chip.Click += (_, _) =>
            {
                var p = _editor.Selected;
                if (p == null) return;
                apply(p, val);
                _editor.RefreshPreview();
            };
            chips.AddView(chip);
        }
        row.AddView(chips);
        _propPanel.AddView(row);
    }

    private void AddCheck(string key, string label)
    {
        var c = Theme.Check(_activity, label);
        c.TextSize = 13;
        c.CheckedChange += (_, e) =>
        {
            var p = _editor.Selected;
            if (p == null) return;
            switch (key)
            {
                case "Bold": p.Bold = e.IsChecked; break;
                case "Filled": p.Filled = e.IsChecked; break;
                case "ShowSeconds": p.ShowSeconds = e.IsChecked; break;
                case "ShowPct": p.ShowPct = e.IsChecked; break;
            }
            _editor.RefreshPreview();
        };
        _checks[key] = c;
        _propPanel.AddView(c);
    }

    private void BuildPropertyPanel()
    {
        // 位置
        var pos = Row("pos");
        AddFieldTo(pos, "X", "X");
        AddFieldTo(pos, "Y", "Y");
        _propPanel.AddView(pos);

        // サイズ
        var size = Row("wh");
        AddFieldTo(size, "W", "W");
        AddFieldTo(size, "H", "H");
        _propPanel.AddView(size);

        // 色
        _colorRow = Row("Color");
        AddColorRow(_colorRow, "Color", "色");
        _propPanel.AddView(_colorRow);
        _color2Row = Row("Color2");
        AddColorRow(_color2Row, "Color2", "秒針色");
        _propPanel.AddView(_color2Row);
        _dimRow = Row("Dim");
        AddColorRow(_dimRow, "Dim", "OFF色");
        _propPanel.AddView(_dimRow);

        // 文字
        var textRow = Row("text");
        var tv = Theme.Label(_activity, "文字", dim: true);
        tv.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        var textEdit = Theme.Edit(_activity, "");
        textEdit.TextSize = 13;
        textEdit.SetMinimumHeight((int)Theme.Dp(_activity, 36));
        textEdit.TextChanged += (_, _) =>
        {
            if (_editor.Selected != null) { _editor.Selected.Text = textEdit.Text ?? ""; _editor.RefreshPreview(); }
        };
        _fields["Text"] = textEdit;
        textRow.AddView(tv);
        textRow.AddView(textEdit, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        _propPanel.AddView(textRow);

        // フォントサイズチップ
        _fontChips = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
        var fRow = Row("font");
        var fv = Theme.Label(_activity, "文字サイズ", dim: true);
        fv.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        fRow.AddView(fv);
        foreach (var v in new[] { 14, 16, 20, 24, 32, 44, 48 })
        {
            var chip = Theme.Chip(_activity, v.ToString());
            chip.TextSize = 12;
            var val = v;
            chip.Click += (_, _) =>
            {
                if (_editor.Selected != null) { _editor.Selected.FontSize = val; _editor.RefreshPreview(); }
            };
            _fontChips.AddView(chip);
        }
        fRow.AddView(_fontChips);
        _propPanel.AddView(fRow);

        // 書式 / ラベル
        var fmtRow = Row("format");
        AddFieldTo(fmtRow, "Format", "書式", 2);
        var fmtHint = Theme.Label(_activity, "HH:MM / MM/DD", dim: true);
        fmtHint.TextSize = 11;
        fmtRow.AddView(fmtHint);
        _propPanel.AddView(fmtRow);

        var labelRow = Row("label");
        AddFieldTo(labelRow, "Label", "ラベル", 2);
        _propPanel.AddView(labelRow);

        // アイコンサイズ
        AddChipsRow("iconsize", "サイズ", new[] { 16, 20, 24, 32 }, (p, v) => p.Size = v, "");

        // 図形
        var strokeRow = Row("stroke");
        AddFieldTo(strokeRow, "Stroke", "線幅");
        AddFieldTo(strokeRow, "Radius", "角丸");
        _propPanel.AddView(strokeRow);

        var angleRow = Row("angle");
        AddFieldTo(angleRow, "Angle0", "開始角");
        AddFieldTo(angleRow, "Angle1", "終了角");
        _propPanel.AddView(angleRow);

        // 配置 (セグメント)
        _alignRow = Row("align");
        var av = Theme.Label(_activity, "配置", dim: true);
        av.SetPadding((int)Theme.Dp(_activity, 4), 0, (int)Theme.Dp(_activity, 4), 0);
        _alignRow.AddView(av);
        _alignGroup = new RadioGroup(_activity) { Orientation = Orientation.Horizontal };
        foreach (var (id, text) in new[] { ("left", "左"), ("center", "中央"), ("right", "右") })
        {
            var rb = new RadioButton(_activity) { Text = text, TextSize = 13 };
            rb.Id = View.GenerateViewId();
            rb.Tag = id;
            rb.SetTextColor(Theme.TextMain);
            rb.CheckedChange += (_, e) =>
            {
                if (!e.IsChecked) return;
                if (_editor.Selected != null)
                {
                    _editor.Selected.Align = (string?)rb.Tag ?? "left";
                    _editor.RefreshPreview();
                }
            };
            _alignGroup.AddView(rb);
        }
        _alignRow.AddView(_alignGroup);
        _propPanel.AddView(_alignRow);

        // トグル
        AddCheck("Bold", "太字");
        AddCheck("Filled", "塗りつぶし");
        AddCheck("ShowSeconds", "秒針を表示");
        AddCheck("ShowPct", "パーセント表示");
    }

    private void OnFieldEdited(string key, string? text)
    {
        var p = _editor.Selected;
        if (p == null) return;
        var s = text ?? "";
        try
        {
            switch (key)
            {
                case "X": p.X = ParseF(s); break;
                case "Y": p.Y = ParseF(s); break;
                case "W": p.W = ParseF(s); break;
                case "H": p.H = ParseF(s); break;
                case "Stroke": p.StrokeWidth = ParseF(s); break;
                case "Radius": p.Radius = ParseF(s); break;
                case "Angle0": p.StartAngle = ParseF(s); break;
                case "Angle1": p.EndAngle = ParseF(s); break;
                case "Format": p.Format = s; break;
                case "Label": p.Label = s; break;
            }
            _editor.RefreshPreview();
        }
        catch { }
    }

    private static float ParseF(string s)
    {
        float.TryParse(s, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var v);
        return v;
    }

    // ============================================================
    // パネル更新
    // ============================================================

    private void UpdatePanelFromPart(FacePart p)
    {
        SetField("X", p.X); SetField("Y", p.Y);
        SetField("W", p.W); SetField("H", p.H);
        SetField("Color", p.Color);
        SetField("Color2", p.Color2);
        SetField("Dim", p.DimColor);
        SetField("Text", p.Text);
        SetField("Format", p.Format);
        SetField("Label", p.Label);
        SetField("Stroke", p.StrokeWidth);
        SetField("Radius", p.Radius);
        SetField("Angle0", p.StartAngle); SetField("Angle1", p.EndAngle);
        SetCheck("Bold", p.Bold); SetCheck("Filled", p.Filled);
        SetCheck("ShowSeconds", p.ShowSeconds); SetCheck("ShowPct", p.ShowPct);
        if (_alignGroup != null)
        {
            for (int i = 0; i < _alignGroup.ChildCount; i++)
            {
                if (_alignGroup.GetChildAt(i) is RadioButton rb && (string?)rb.Tag == p.Align)
                { rb.Checked = true; break; }
            }
        }

        // 種別ごとの表示制御
        SetRowVisible("pos", true);
        SetRowVisible("wh", p.Kind is not (PartKinds.Wifi or PartKinds.Ble or PartKinds.Notif or PartKinds.Steps or PartKinds.Date));
        SetRowVisible("Color", true);
        SetRowVisible("Color2", p.Kind == PartKinds.Analog);
        SetRowVisible("Dim", p.Kind is PartKinds.Wifi or PartKinds.Ble or PartKinds.Notif);
        SetRowVisible("text", p.Kind == PartKinds.Text);
        SetRowVisible("font", p.Kind is PartKinds.Text or PartKinds.Clock or PartKinds.Date or PartKinds.Battery or PartKinds.Steps);
        SetRowVisible("format", p.Kind is PartKinds.Clock or PartKinds.Date);
        SetRowVisible("label", p.Kind == PartKinds.Steps);
        SetRowVisible("iconsize", p.Kind is PartKinds.Wifi or PartKinds.Ble or PartKinds.Notif);
        SetRowVisible("stroke", p.Kind is PartKinds.Line or PartKinds.Arc or PartKinds.Rect or PartKinds.Circle);
        SetRowVisible("angle", p.Kind == PartKinds.Arc);
        SetRowVisible("align", p.Kind is PartKinds.Text or PartKinds.Clock or PartKinds.Date or PartKinds.Steps);
        SetCheckVisible("Bold", p.Kind == PartKinds.Text);
        SetCheckVisible("Filled", p.Kind is PartKinds.Rect or PartKinds.Circle);
        SetCheckVisible("ShowSeconds", p.Kind == PartKinds.Analog);
        SetCheckVisible("ShowPct", p.Kind == PartKinds.Battery);
    }

    private void SetField(string key, object v) { if (_fields.TryGetValue(key, out var e)) e.Text = v.ToString(); }
    private void SetCheck(string key, bool v) { if (_checks.TryGetValue(key, out var c)) c.Checked = v; }
    private void SetRowVisible(string key, bool v) { if (_rows.TryGetValue(key, out var r)) r.Visibility = v ? ViewStates.Visible : ViewStates.Gone; }
    private void SetCheckVisible(string key, bool v) { if (_checks.TryGetValue(key, out var c)) c.Visibility = v ? ViewStates.Visible : ViewStates.Gone; }

    // ============================================================
    // 保存 / 読込
    // ============================================================

    private string FacesDir() => IO.Path.Combine(_activity.FilesDir!.AbsolutePath, "faces");

    private void SaveFace()
    {
        var name = _faceName.Text?.Trim();
        if (string.IsNullOrEmpty(name)) name = "My Face";
        _editor.Document.Name = name;
        Directory.CreateDirectory(FacesDir());
        File.WriteAllText(IO.Path.Combine(FacesDir(), SafeName(name) + ".json"), _editor.Document.Serialize());
        _status.Text = $"保存しました: {name}";
    }

    private void LoadFace()
    {
        Directory.CreateDirectory(FacesDir());
        var files = Directory.GetFiles(FacesDir(), "*.json")
            .Select(IO.Path.GetFileNameWithoutExtension).ToArray();
        if (files.Length == 0) { _status.Text = "保存済み文字盤がありません"; return; }
        new AlertDialog.Builder(_activity)
            .SetTitle("文字盤を読込")
            .SetItems(files, (_, e) =>
            {
                int which = e.Which;
                var doc = FaceDocument.Deserialize(File.ReadAllText(IO.Path.Combine(FacesDir(), files[which] + ".json")));
                _editor.SetDocument(doc);
                _faceName.Text = doc.Name;
                _status.Text = $"読込: {files[which]}";
            })
            .SetNegativeButton("キャンセル", (_, _) => { })
            .Show();
    }

    private static string SafeName(string s)
    {
        foreach (var c in IO.Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
        return string.IsNullOrWhiteSpace(s) ? "face" : s;
    }

    // ============================================================
    // 送信
    // ============================================================

    private async void SendToWatch()
    {
        if (_sending) { _status.Text = "送信中です。お待ちください"; return; }
        if (!BleManager.Instance.Connected) { _status.Text = "未接続です。先に接続してください"; return; }
        _sending = true;
        try
        {
            var doc = _editor.Document;
            byte[]? hour = null, min = null, sec = null;
            var analog = doc.Parts.FirstOrDefault(p => p.Kind == PartKinds.Analog);
            if (analog != null)
            {
                float r = Math.Max(analog.W, analog.H) / 2f;
                var col = ParseColor(analog.Color, Color.White);
                var secCol = ParseColor(
                    string.IsNullOrEmpty(analog.Color2) ? "#FF3B30" : analog.Color2,
                    Color.Rgb(0xFF, 0x3B, 0x30));
                hour = FaceRenderer.RenderHandPng((int)(r * 0.5f), 7, col);
                min = FaceRenderer.RenderHandPng((int)(r * 0.72f), 5, col);
                sec = FaceRenderer.RenderHandPng((int)(r * 0.82f), 2, secCol);
            }
            var bg = FaceRenderer.RenderBackgroundPng(doc);
            var dyn = FaceRenderer.BuildDynamicJson(doc);
            _status.Text = "送信中...";
            BleManager.Instance.SendProgress -= OnSendProgress;
            BleManager.Instance.SendDone -= OnSendDone;
            BleManager.Instance.SendProgress += OnSendProgress;
            BleManager.Instance.SendDone += OnSendDone;
            await BleManager.Instance.SendFacePackageAsync(bg, hour, min, sec, dyn);
        }
        catch (Exception ex)
        {
            _status.Text = $"送信エラー: {ex.Message}";
        }
        finally
        {
            _sending = false;
        }
    }

    private void OnSendProgress(string name, int cur, int total)
    {
        _activity.RunOnUiThread(() => _status.Text = $"送信中: {name} ({cur}/{total})");
    }

    private void OnSendDone(string msg)
    {
        _activity.RunOnUiThread(() =>
            _status.Text = msg == "ok" ? "✓ 時計に反映されました" : $"送信失敗: {msg}");
    }

    // ============================================================
    // 画像選択
    // ============================================================

    public void OnImagePicked(Intent? data)
    {
        var p = _editor.Selected;
        if (p == null || p.Kind != PartKinds.Image || data?.Data == null) return;
        try
        {
            using var src = _activity.ContentResolver!.OpenInputStream(data.Data);
            var dir = IO.Path.Combine(_activity.CacheDir!.AbsolutePath, "parts");
            Directory.CreateDirectory(dir);
            var dst = IO.Path.Combine(dir, $"img_{DateTime.Now.Ticks}.png");
            using var fs = File.Create(dst);
            src?.CopyTo(fs);
            p.ImagePath = dst;
            _editor.RefreshPreview();
            _status.Text = "画像を設定しました";
        }
        catch (Exception ex)
        {
            _status.Text = "画像読込失敗: " + ex.Message;
        }
    }

    public void PickImageForSelected()
    {
        var p = _editor.Selected;
        if (p == null) { _status.Text = "先に画像パーツを選択してください"; return; }
        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("image/*");
        _activity.StartActivityForResult(intent, PickImage);
    }

    public static int ImageRequestCode => PickImage;

    private static Color ParseColor(string hex, Color def)
    {
        if (string.IsNullOrEmpty(hex) || hex[0] != '#') return def;
        try
        {
            var v = Convert.ToInt32(hex.Substring(1), 16);
            return Color.Rgb((v >> 16) & 0xFF, (v >> 8) & 0xFF, v & 0xFF);
        }
        catch { return def; }
    }
}
