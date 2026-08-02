using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Engineering_Watch.BLE;

namespace Engineering_Watch.UI;

// ============================================================
// 接続タブ (スクロール対応版)
// ステータスカード / デバイスリスト / コントロール / 通知 / ログ
// ============================================================

public class ConnectionTabView : LinearLayout
{
    private readonly Activity _activity;
    private readonly TextView _statusTitle;
    private readonly TextView _statusDetail;
    private readonly TextView _log;
    private readonly LinearLayout _deviceList;
    private readonly Button _scanBtn;
    private readonly SeekBar _brightness;
    private readonly CheckBox _notifToggle;
    private readonly StringBuilder _logBuf = new();

    private bool _scanning;
    private string? _lastStatus;
    private string? _lastData;

    public ConnectionTabView(Activity activity) : base(activity)
    {
        _activity = activity;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Theme.Bg);

        // ---- 全体スクロール ----
        var scroll = new ScrollView(activity) { FillViewport = true };
        var content = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.SetPadding((int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 8),
                           (int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 16));
        scroll.AddView(content);
        AddView(scroll, new LayoutParams(LayoutParams.MatchParent, LayoutParams.MatchParent));

        // ---- ステータスカード ----
        var card = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        card.Background = Theme.Rounded(Theme.Surface, 14, Theme.Border, 1);
        card.SetPadding((int)Theme.Dp(activity, 16), (int)Theme.Dp(activity, 14),
                        (int)Theme.Dp(activity, 16), (int)Theme.Dp(activity, 14));
        _statusTitle = new TextView(activity) { Text = "未接続", TextSize = 18 };
        _statusTitle.SetTypeface(null, TypefaceStyle.Bold);
        _statusTitle.SetTextColor(Theme.TextDim);
        card.AddView(_statusTitle);
        _statusDetail = Theme.Label(activity, "「スキャン」で時計を探してください", dim: true);
        _statusDetail.TextSize = 12;
        Theme.SetMargins(_statusDetail, activity, 0, 4, 0, 0);
        card.AddView(_statusDetail);
        content.AddView(card);

        // ---- スキャン ----
        _scanBtn = Theme.Button(activity, "スキャン (時計を探す)", primary: true);
        _scanBtn.TextSize = 15;
        _scanBtn.Click += (_, _) =>
        {
            if (BleManager.Instance.Connected)
            {
                BleManager.Instance.Disconnect();
            }
            else if (_scanning)
            {
                _scanning = false;
                BleManager.Instance.StopScan();
            }
            else
            {
                _scanning = true;
                BleManager.Instance.StartScan();
            }
            UpdateButtons();
        };
        Theme.SetMargins(_scanBtn, activity, 0, 10, 0, 0);
        content.AddView(_scanBtn);

        // ---- デバイスリスト (手動行。ScrollView内でも問題なし) ----
        content.AddView(Theme.SectionHeader(activity, "見つかった時計 (タップで接続)"));
        _deviceList = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.AddView(_deviceList);

        // ---- コントロール ----
        content.AddView(Theme.SectionHeader(activity, "コントロール"));
        var ctlRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        var syncBtn = Theme.Chip(activity, "時刻同期");
        syncBtn.Click += (_, _) => BleManager.Instance.SendTimeSync();
        var vibBtn = Theme.Chip(activity, "振動テスト");
        vibBtn.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"vibrate\",\"ms\":300}");
        var wakeBtn = Theme.Chip(activity, "画面ON");
        wakeBtn.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"wake\"}");
        ctlRow.AddView(syncBtn);
        ctlRow.AddView(vibBtn);
        ctlRow.AddView(wakeBtn);
        content.AddView(ctlRow);

        var brRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        brRow.SetGravity(GravityFlags.CenterVertical);
        var brLabel = Theme.Label(activity, "明るさ", dim: true);
        brLabel.SetPadding((int)Theme.Dp(activity, 4), 0, (int)Theme.Dp(activity, 8), 0);
        brRow.AddView(brLabel);
        _brightness = new SeekBar(activity) { Max = 255, Progress = 128 };
        _brightness.ProgressChanged += (_, e) =>
        {
            if (e.FromUser) BleManager.Instance.SendControl($"{{\"cmd\":\"brightness\",\"v\":{e.Progress}}}");
        };
        brRow.AddView(_brightness, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        content.AddView(brRow);

        // ---- 通知 ----
        content.AddView(Theme.SectionHeader(activity, "通知"));
        var nrRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        nrRow.SetGravity(GravityFlags.CenterVertical);
        _notifToggle = Theme.Check(activity, "通知を時計へ転送");
        _notifToggle.TextSize = 13;
        _notifToggle.CheckedChange += (_, e) => SetPref("notif_forward", e.IsChecked);
        nrRow.AddView(_notifToggle, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        var accBtn = Theme.Chip(activity, "通知アクセス設定");
        accBtn.Click += (_, _) =>
            _activity.StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        nrRow.AddView(accBtn);
        content.AddView(nrRow);

        // ---- ログ (外側スクロールに任せる。入れ子ScrollViewは競合するため非スクロール枠にする) ----
        content.AddView(Theme.SectionHeader(activity, "ログ"));
        var logBox = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        logBox.Background = Theme.Rounded(Theme.Surface, 10, Theme.Border, 1);
        logBox.SetPadding(8, 8, 8, 8);
        _log = Theme.Label(activity, "", dim: true);
        _log.TextSize = 11;
        logBox.AddView(_log);
        content.AddView(logBox);

        // ---- イベント購読 ----
        var b = BleManager.Instance;
        b.Log += m => Post(() => { AppendLog(m); UpdateButtons(); });
        b.ConnectionChanged += _ => Post(() => { _scanning = false; UpdateStatus(); UpdateButtons(); });
        b.ScanResults += list => Post(() => UpdateDeviceList(list));
        b.StatusJson += json => Post(() => { _lastStatus = json; UpdateStatus(); });
        b.WatchDataJson += json => Post(() => { _lastData = json; UpdateStatus(); });

        _notifToggle.Checked = GetPref("notif_forward", true);
        UpdateButtons();
        UpdateStatus();
    }

    private void Post(Action a) => _activity.RunOnUiThread(a);

    // ---- デバイスリスト ----

    private void UpdateDeviceList(List<BluetoothDevice> devices)
    {
        _deviceList.RemoveAllViews();
        if (devices.Count == 0)
        {
            var empty = Theme.Label(_activity, "見つかりません。スキャン中...", dim: true);
            empty.TextSize = 12;
            _deviceList.AddView(empty);
            return;
        }
        foreach (var d in devices)
        {
            var row = new LinearLayout(_activity) { Orientation = Orientation.Horizontal };
            row.SetGravity(GravityFlags.CenterVertical);
            row.Background = Theme.Rounded(Theme.Surface2, 10, Theme.Border, 1);
            row.SetPadding((int)Theme.Dp(_activity, 12), (int)Theme.Dp(_activity, 10),
                           (int)Theme.Dp(_activity, 12), (int)Theme.Dp(_activity, 10));
            row.Clickable = true;
            var dev = d;
            row.Click += (_, _) =>
            {
                _scanning = false;
                UpdateButtons();
                BleManager.Instance.StopScan();
                _ = BleManager.Instance.ConnectAsync(dev);
            };
            var name = Theme.Label(_activity, d.Name ?? "Unknown");
            name.TextSize = 14;
            row.AddView(name, new LayoutParams(0, LayoutParams.WrapContent, 1f));
            var addr = Theme.Label(_activity, d.Address ?? "", dim: true);
            addr.TextSize = 11;
            row.AddView(addr);
            var lp = new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent);
            lp.SetMargins(0, 0, 0, (int)Theme.Dp(_activity, 6));
            _deviceList.AddView(row, lp);
        }
    }

    private void AppendLog(string m)
    {
        _logBuf.AppendLine($"[{DateTime.Now:HH:mm:ss}] {m}");
        if (_logBuf.Length > 4000) _logBuf.Remove(0, 2000);
        _log.Text = _logBuf.ToString();
    }

    private void UpdateButtons()
    {
        _scanBtn.Text = BleManager.Instance.Connected ? "切断する" :
                        _scanning ? "スキャン中... (タップで停止)" : "スキャン (時計を探す)";
    }

    private void UpdateStatus()
    {
        var b = BleManager.Instance;
        if (!b.Connected)
        {
            _statusTitle.Text = "未接続";
            _statusTitle.SetTextColor(Theme.TextDim);
            _statusDetail.Text = "「スキャン」を押して EWatch-XXXX を探し、タップで接続します";
            _brightness.Enabled = false;
            return;
        }
        _brightness.Enabled = true;
        _statusTitle.Text = "接続中 ✓";
        _statusTitle.SetTextColor(Theme.Accent);
        RenderLiveData();
    }

    private void RenderLiveData()
    {
        var sb = new StringBuilder();
        void AppendField(JsonDocument doc, string key, string label)
        {
            if (doc.RootElement.TryGetProperty(key, out var el))
            {
                if (sb.Length > 0) sb.Append("  •  ");
                sb.Append($"{label}: {el}");
            }
        }
        if (_lastData != null)
        {
            try
            {
                using var d = JsonDocument.Parse(_lastData);
                AppendField(d, "steps", "歩数");
                AppendField(d, "battery", "電池");
                AppendField(d, "charging", "充電");
            }
            catch { }
        }
        if (_lastStatus != null)
        {
            try
            {
                using var d = JsonDocument.Parse(_lastStatus);
                AppendField(d, "wifi", "WiFi");
            }
            catch { }
        }
        _statusDetail.Text = sb.Length > 0 ? sb.ToString() : "接続しました。データを受信中...";
    }

    private static ISharedPreferences? Prefs =>
        Application.Context.GetSharedPreferences("ewatch", FileCreationMode.Private);

    private static void SetPref(string key, bool v) => Prefs?.Edit()?.PutBoolean(key, v)?.Apply();

    private static bool GetPref(string key, bool def) => Prefs?.GetBoolean(key, def) ?? def;
}
