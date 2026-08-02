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
// 接続タブ (改善版UI)
// ステータスカード / デバイスリスト / コントロール / 通知
// ============================================================

public class ConnectionTabView : LinearLayout
{
    private readonly Activity _activity;
    private readonly TextView _statusTitle;
    private readonly TextView _statusDetail;
    private readonly TextView _log;
    private readonly ListView _devices;
    private readonly List<string> _deviceNames = new();
    private readonly List<BluetoothDevice> _deviceObjs = new();
    private readonly Button _scanBtn;
    private readonly SeekBar _brightness;
    private readonly CheckBox _notifToggle;
    private readonly StringBuilder _logBuf = new();

    private bool _scanning;
    private bool _deviceCardVisible;
    private string? _lastStatus;
    private string? _lastData;

    public ConnectionTabView(Activity activity) : base(activity)
    {
        _activity = activity;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Theme.Bg);
        SetPadding((int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 8),
                   (int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 4));

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
        AddView(card);

        // ---- スキャン ----
        _scanBtn = Theme.Button(activity, "スキャン (時計を探す)", primary: true);
        _scanBtn.TextSize = 15;
        _scanBtn.Click += (_, _) =>
        {
            if (BleManager.Instance.Connected) { BleManager.Instance.Disconnect(); }
            else if (_scanning) BleManager.Instance.StopScan();
            else BleManager.Instance.StartScan();
        };
        Theme.SetMargins(_scanBtn, activity, 0, 10, 0, 0);
        AddView(_scanBtn);

        // ---- デバイスリスト ----
        AddView(Theme.SectionHeader(activity, "見つかった時計 (タップで接続)"));
        _devices = new ListView(activity);
        var adapter = new ArrayAdapter<string>(activity, Android.Resource.Layout.SimpleListItem1, _deviceNames);
        adapter.SetDropDownViewResource(Android.Resource.Layout.SimpleListItem1);
        _devices.Adapter = adapter;
        _devices.Background = Theme.Rounded(Theme.Surface, 10, Theme.Border, 1);
        _devices.SetPadding(8, 8, 8, 8);
        _devices.ItemClick += (_, e) =>
        {
            if (e.Position >= 0 && e.Position < _deviceObjs.Count)
            {
                BleManager.Instance.StopScan();
                _ = BleManager.Instance.ConnectAsync(_deviceObjs[e.Position]);
            }
        };
        _deviceCardVisible = false;
        _devices.Visibility = ViewStates.Gone;
        AddView(_devices, new LayoutParams(LayoutParams.MatchParent, 0, 1f));

        // ---- コントロール ----
        AddView(Theme.SectionHeader(activity, "コントロール"));
        var ctlRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        var syncBtn = Theme.Chip(activity, "時刻同期");
        syncBtn.Click += (_, _) => SendTimeSync();
        var vibBtn = Theme.Chip(activity, "振動テスト");
        vibBtn.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"vibrate\",\"ms\":300}");
        var wakeBtn = Theme.Chip(activity, "画面ON");
        wakeBtn.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"wake\"}");
        ctlRow.AddView(syncBtn);
        ctlRow.AddView(vibBtn);
        ctlRow.AddView(wakeBtn);
        AddView(ctlRow);

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
        AddView(brRow);

        // ---- 通知 ----
        AddView(Theme.SectionHeader(activity, "通知"));
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
        AddView(nrRow);

        // ---- ログ ----
        var scroll = new ScrollView(activity);
        _log = Theme.Label(activity, "", dim: true);
        _log.TextSize = 11;
        scroll.AddView(_log);
        scroll.Background = Theme.Rounded(Theme.Surface, 10, Theme.Border, 1);
        Theme.SetMargins(scroll, activity, 0, 8, 0, 0);
        AddView(scroll, new LayoutParams(LayoutParams.MatchParent, 150));

        // ---- イベント購読 ----
        var b = BleManager.Instance;
        b.Log += m => Post(() => { AppendLog(m); UpdateButtons(); });
        b.ConnectionChanged += _ => Post(() => { UpdateStatus(); UpdateButtons(); });
        b.ScanResults += list => Post(() =>
        {
            _deviceNames.Clear(); _deviceObjs.Clear();
            foreach (var d in list) { _deviceNames.Add($"{d.Name}  ({d.Address})"); _deviceObjs.Add(d); }
            adapter.NotifyDataSetChanged();
            _deviceCardVisible = list.Count > 0;
            _devices.Visibility = _deviceCardVisible ? ViewStates.Visible : ViewStates.Gone;
        });
        b.StatusJson += json => Post(() => { _lastStatus = json; UpdateStatus(); });
        b.WatchDataJson += json => Post(() => { _lastData = json; UpdateStatus(); });

        _notifToggle.Checked = GetPref("notif_forward", true);
        UpdateButtons();
        UpdateStatus();
    }

    private void Post(Action a) => _activity.RunOnUiThread(a);

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

    private void SendTimeSync() => BleManager.Instance.SendTimeSync();

    private static ISharedPreferences? Prefs =>
        Application.Context.GetSharedPreferences("ewatch", FileCreationMode.Private);

    private static void SetPref(string key, bool v) => Prefs?.Edit()?.PutBoolean(key, v)?.Apply();

    private static bool GetPref(string key, bool def) => Prefs?.GetBoolean(key, def) ?? def;
}
