using System;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Net;
using Android.Net.Wifi;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Engineering_Watch.BLE;
using Engineering_Watch.Notifications;

namespace Engineering_Watch.UI;

// ============================================================
// 設定タブ (改善版UI)
// WiFi / 画面 (傾けてON・消灯タイムアウト) / 通知転送 / 情報
// ============================================================

public class SettingsTabView : LinearLayout
{
    private readonly Activity _activity;
    private readonly EditText _ssid;
    private readonly EditText _pass;
    private readonly TextView _status;
    private readonly TextView _notifStatus;
    private readonly CheckBox _tiltWake;
    private readonly Button[] _timeoutChips = new Button[5];
    private static readonly int[] Timeouts = { 15, 30, 60, 300, 0 };

    public SettingsTabView(Activity activity) : base(activity)
    {
        _activity = activity;
        Orientation = Orientation.Vertical;
        SetBackgroundColor(Theme.Bg);
        SetPadding((int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 8),
                   (int)Theme.Dp(activity, 12), (int)Theme.Dp(activity, 4));

        // ================= WiFi =================
        AddView(Theme.SectionHeader(activity, "WiFi (オンデマンド接続)"));

        _ssid = Theme.Edit(activity, "SSID (WiFi名)");
        _ssid.TextSize = 13;
        AddView(_ssid);
        _pass = Theme.Edit(activity, "パスワード (1回入力すれば時計に保存)",
                           Android.Text.InputTypes.TextVariationPassword);
        _pass.TextSize = 13;
        Theme.SetMargins(_pass, activity, 0, 8, 0, 0);
        AddView(_pass);

        var importRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        var getSsidBtn = Theme.Chip(activity, "現在のSSID取得");
        getSsidBtn.Click += (_, _) =>
        {
            var ssid = GetCurrentSsid();
            if (!string.IsNullOrEmpty(ssid))
            {
                _ssid.Text = ssid;
                _status.Text = $"SSIDを入力しました: {ssid} (パスワードは手入力)";
            }
            else
            {
                _status.Text = "SSIDを取得できません (WiFi未接続・機内モードを確認)";
            }
        };
        importRow.AddView(getSsidBtn, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        AddView(importRow);

        var wifiRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        var on = Theme.Button(activity, "WiFi ON", primary: true);
        on.TextSize = 13;
        on.Click += (_, _) =>
        {
            BleManager.Instance.SendControl(
                $"{{\"cmd\":\"wifi_on\",\"ssid\":\"{EscapeJson(_ssid.Text)}\",\"pass\":\"{EscapeJson(_pass.Text)}\"}}");
            SetPref("wifi_ssid", _ssid.Text ?? ""); SetPref("wifi_pass", _pass.Text ?? "");
        };
        var off = Theme.Button(activity, "OFF");
        off.TextSize = 13;
        off.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"wifi_off\"}");
        var st = Theme.Button(activity, "状態");
        st.TextSize = 13;
        st.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"get_status\"}");
        wifiRow.AddView(on, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        wifiRow.AddView(off, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        wifiRow.AddView(st, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        Theme.SetMargins(wifiRow, activity, 0, 8, 0, 0);
        AddView(wifiRow);

        var note = Theme.Label(activity,
            "※ パスワードは時計に保存され次回から自動接続。時計は2.4GHz帯のみ対応 (5GHzルーターは2.4GHz接続かテザリングが必要)。接続後10分無通信で自動OFF",
            dim: true);
        note.TextSize = 11;
        Theme.SetMargins(note, activity, 2, 6, 0, 0);
        AddView(note);

        // ================= 画面 =================
        AddView(Theme.SectionHeader(activity, "画面 (省電力)"));

        _tiltWake = Theme.Check(activity, "傾ける/ダブルタップで画面ON");
        _tiltWake.TextSize = 13;
        _tiltWake.CheckedChange += (_, e) =>
        {
            BleManager.Instance.SendControl(
                $"{{\"cmd\":\"tilt_wake\",\"on\":{(e.IsChecked ? "true" : "false")}}}");
            SetPref("tilt_wake", e.IsChecked);
        };
        AddView(_tiltWake);

        var tRow = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        tRow.SetGravity(GravityFlags.CenterVertical);
        var tv = Theme.Label(activity, "自動消灯", dim: true);
        tv.SetPadding((int)Theme.Dp(activity, 4), 0, (int)Theme.Dp(activity, 4), 0);
        tRow.AddView(tv);
        string[] labels = { "15秒", "30秒", "1分", "5分", "常時" };
        for (int i = 0; i < Timeouts.Length; i++)
        {
            var chip = Theme.Chip(activity, labels[i]);
            chip.TextSize = 12;
            int sec = Timeouts[i];
            int idx = i;
            chip.Click += (_, _) =>
            {
                BleManager.Instance.SendControl($"{{\"cmd\":\"screen_timeout\",\"s\":{sec}}}");
                SetPref("screen_timeout", sec);
                UpdateTimeoutChips(idx);
            };
            _timeoutChips[i] = chip;
            tRow.AddView(chip);
        }
        AddView(tRow);

        var wakeBtn = Theme.Chip(activity, "画面ON (テスト)");
        wakeBtn.Click += (_, _) => BleManager.Instance.SendControl("{\"cmd\":\"wake\"}");
        AddView(wakeBtn);

        // ================= 通知 =================
        AddView(Theme.SectionHeader(activity, "通知転送"));

        _notifStatus = Theme.Label(activity, "", dim: true);
        _notifStatus.TextSize = 13;
        _notifStatus.SetTextColor(Theme.Warn);
        AddView(_notifStatus);

        var nBtn = Theme.Button(activity, "通知アクセスを開く");
        nBtn.TextSize = 13;
        nBtn.Click += (_, _) =>
            _activity.StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        AddView(nBtn);

        var nHelp = Theme.Label(activity,
            "有効にすると、すべてのアプリの通知がBLE経由で時計に届きます (振動+バナー表示)",
            dim: true);
        nHelp.TextSize = 11;
        Theme.SetMargins(nHelp, activity, 2, 6, 0, 0);
        AddView(nHelp);

        // ================= 情報 =================
        AddView(Theme.SectionHeader(activity, "情報"));
        var about = Theme.Label(activity,
            "Engineering-Watch v0.1.0\nT-Watch S3 + Android スマートウォッチシステム", dim: true);
        about.TextSize = 12;
        AddView(about);

        _status = new TextView(activity) { Text = "", TextSize = 12 };
        _status.SetTextColor(Theme.Accent);
        Theme.SetMargins(_status, activity, 2, 8, 0, 0);
        AddView(_status);

        // ---- 保存済み設定の復元 ----
        _ssid.Text = GetPref("wifi_ssid", "");
        _pass.Text = GetPref("wifi_pass", "");
        _tiltWake.Checked = GetPref("tilt_wake", true);
        int to = GetPref("screen_timeout", 15);
        int toIdx = Array.IndexOf(Timeouts, to);
        UpdateTimeoutChips(toIdx < 0 ? 0 : toIdx);
    }

    private void UpdateTimeoutChips(int selected)
    {
        for (int i = 0; i < _timeoutChips.Length; i++)
        {
            var c = _timeoutChips[i];
            c.Background = Theme.Rounded(i == selected ? Theme.Accent : Theme.Surface2, 16,
                i == selected ? null : Theme.Border, 1);
            c.SetTextColor(i == selected ? Color.Black : Theme.TextMain);
        }
    }

    private View Spacer() => new View(_activity) { LayoutParameters = new LayoutParams(1, 24) };

    // ============================================================
    // WiFi情報の引き継ぎ
    // ============================================================

    /// <summary>現在接続中のWiFi SSIDを取得する (パスワードはOSが非公開のため取得不可)</summary>
    public static string? GetCurrentSsid()
    {
        try
        {
            var ctx = Application.Context;
            // 1) API 29+: NetworkCapabilities.TransportInfo (WifiInfo) 経由
            var cm = (ConnectivityManager?)ctx.GetSystemService(Context.ConnectivityService);
            var net = cm?.ActiveNetwork;
            if (net != null && cm?.GetNetworkCapabilities(net) is NetworkCapabilities caps &&
                caps.TransportInfo is WifiInfo wi)
            {
                var s = wi.SSID;
                if (!string.IsNullOrEmpty(s) && !s.Contains("unknown"))
                    return Unquote(s);
            }
            // 2) 旧API (位置情報権限が必要な場合あり)
            var wm = (WifiManager?)ctx.GetSystemService(Context.WifiService);
            var s2 = wm?.ConnectionInfo?.SSID;
            if (!string.IsNullOrEmpty(s2) && !s2.Contains("unknown"))
                return Unquote(s2);
        }
        catch { }
        return null;
    }

    private static string Unquote(string s) =>
        s.Length >= 2 && s[0] == '"' && s[^1] == '"' ? s.Substring(1, s.Length - 2) : s;

    private static string EscapeJson(string? s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");

    public void RefreshNotifStatus()
    {
        var enabled = NotificationForwardService.ListenerConnected;
        _notifStatus.Text = enabled
            ? "通知アクセス: 有効 ✓"
            : "通知アクセス: 未設定 (下のボタンから有効化してください)";
    }

    private static ISharedPreferences? Prefs =>
        Application.Context.GetSharedPreferences("ewatch", FileCreationMode.Private);

    private static void SetPref(string key, string v) => Prefs?.Edit()?.PutString(key, v)?.Apply();
    private static void SetPref(string key, bool v) => Prefs?.Edit()?.PutBoolean(key, v)?.Apply();
    private static void SetPref(string key, int v) => Prefs?.Edit()?.PutInt(key, v)?.Apply();

    private static string GetPref(string key, string def) => Prefs?.GetString(key, def) ?? def;
    private static bool GetPref(string key, bool def) => Prefs?.GetBoolean(key, def) ?? def;
    private static int GetPref(string key, int def) => Prefs?.GetInt(key, def) ?? def;
}
