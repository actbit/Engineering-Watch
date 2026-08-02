using System;
using System.Collections.Generic;
using System.Text.Json;
using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Provider;
using Android.Views;
using Android.Widget;
using Engineering_Watch.BLE;
using Engineering_Watch.Notifications;

namespace Engineering_Watch.UI;

// ============================================================
// 通知タブ (スクロール対応版)
// 通知アクセスの状態 / キャプチャした通知の履歴 / テスト送信
// ============================================================

public class NotificationTabView : LinearLayout
{
    private readonly Activity _activity;
    private readonly TextView _status;
    private readonly LinearLayout _history;

    public NotificationTabView(Activity activity) : base(activity)
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
        card.SetPadding((int)Theme.Dp(activity, 16), (int)Theme.Dp(activity, 12),
                        (int)Theme.Dp(activity, 16), (int)Theme.Dp(activity, 12));
        _status = new TextView(activity) { Text = "確認中...", TextSize = 15 };
        _status.SetTextColor(Theme.Warn);
        card.AddView(_status);
        var help = Theme.Label(activity,
            "全アプリの通知がBLE経由で時計に届きます。届くと時計で振動+バナー表示されます。",
            dim: true);
        help.TextSize = 11;
        Theme.SetMargins(help, activity, 0, 4, 0, 0);
        card.AddView(help);
        content.AddView(card);

        // ---- 操作 ----
        content.AddView(Theme.SectionHeader(activity, "操作"));
        var row1 = new LinearLayout(activity) { Orientation = Orientation.Horizontal };
        var testBtn = Theme.Button(activity, "テスト通知を送る", primary: true);
        testBtn.TextSize = 13;
        testBtn.Click += (_, _) => SendTestNotification();
        var accessBtn = Theme.Button(activity, "通知アクセス設定");
        accessBtn.TextSize = 13;
        accessBtn.Click += (_, _) =>
            _activity.StartActivity(new Intent(Settings.ActionNotificationListenerSettings));
        row1.AddView(testBtn, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        row1.AddView(accessBtn, new LayoutParams(0, LayoutParams.WrapContent, 1f));
        content.AddView(row1);

        // ---- 履歴 (手動行。ScrollView内でも問題なし) ----
        content.AddView(Theme.SectionHeader(activity, "キャプチャした通知の履歴 (最新50件)"));
        _history = new LinearLayout(activity) { Orientation = Orientation.Vertical };
        content.AddView(_history);

        // ---- イベント ----
        NotificationForwardService.NotificationsChanged += () =>
            _activity.RunOnUiThread(RefreshHistory);
        BleManager.Instance.ConnectionChanged += _ =>
            _activity.RunOnUiThread(RefreshStatus);
        RefreshHistory();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        var nls = NotificationForwardService.ListenerConnected;
        var ble = BleManager.Instance.Connected;
        if (!nls)
        {
            _status.Text = "通知アクセス: 未設定";
            _status.SetTextColor(Theme.Danger);
        }
        else if (!ble)
        {
            _status.Text = "通知アクセス: 有効 ✓ (時計未接続)";
            _status.SetTextColor(Theme.Warn);
        }
        else
        {
            _status.Text = "通知アクセス: 有効 ✓ / 時計: 接続中 ✓";
            _status.SetTextColor(Theme.Accent);
        }
    }

    private void RefreshHistory()
    {
        _history.RemoveAllViews();
        var list = NotificationForwardService.GetNotifications();
        if (list.Count == 0)
        {
            var empty = Theme.Label(_activity, "まだ通知をキャプチャしていません。\n通知アクセスを有効にして、実アプリの通知を待つか「テスト通知を送る」を押してください。", dim: true);
            empty.TextSize = 12;
            _history.AddView(empty);
            return;
        }
        foreach (var n in list)
        {
            var row = new LinearLayout(_activity) { Orientation = Orientation.Vertical };
            row.Background = Theme.Rounded(Theme.Surface2, 10, Theme.Border, 1);
            row.SetPadding((int)Theme.Dp(_activity, 12), (int)Theme.Dp(_activity, 8),
                           (int)Theme.Dp(_activity, 12), (int)Theme.Dp(_activity, 8));

            var t = DateTimeOffset.FromUnixTimeMilliseconds(n.When).ToLocalTime();
            var head = Theme.Label(_activity, $"{t:HH:mm}  {n.App}");
            head.TextSize = 12;
            head.SetTextColor(Theme.Accent);
            row.AddView(head);
            if (!string.IsNullOrEmpty(n.Title))
            {
                var title = Theme.Label(_activity, n.Title);
                title.TextSize = 14;
                row.AddView(title);
            }
            if (!string.IsNullOrEmpty(n.Text))
            {
                var text = Theme.Label(_activity, n.Text, dim: true);
                text.TextSize = 12;
                row.AddView(text);
            }

            var lp = new LinearLayout.LayoutParams(LayoutParams.MatchParent, LayoutParams.WrapContent);
            lp.SetMargins(0, 0, 0, (int)Theme.Dp(_activity, 6));
            _history.AddView(row, lp);
        }
    }

    private void SendTestNotification()
    {
        var json = JsonSerializer.Serialize(new
        {
            app = "TEST",
            title = "テスト通知",
            text = "時計に届きました ✓",
            id = (int)(DateTime.Now.Ticks & 0x7FFFFFFF),
            when = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        });
        BleManager.Instance.SendNotification(json);
        Toast.MakeText(_activity, "テスト通知を送信しました (時計で確認)", ToastLength.Short)?.Show();
    }
}
