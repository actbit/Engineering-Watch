using Android.App;
using Android.Content;
using Android.Service.Notification;
using Engineering_Watch.BLE;
using System.Text.Json;

namespace Engineering_Watch.Notifications;

// ============================================================
// 通知リスナーサービス
// Android の全通知を取得し、BLE で時計へ転送する。
// システムがバインドするためアプリが閉じていても動作する。
// (設定アプリ → 通知アクセス で有効化が必要)
// ============================================================

[Service(Label = "EWatch 通知転送",
         Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
         Exported = false)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public class NotificationForwardService : NotificationListenerService
{
    private static bool _connected;
    public static bool ListenerConnected => _connected;

    public override void OnListenerConnected() => _connected = true;

    public override void OnListenerDisconnected() => _connected = false;

    public override void OnNotificationPosted(StatusBarNotification? sbn, RankingMap? rankingMap)
    {
        try
        {
            if (sbn?.Notification?.Extras == null) return;
            var extras = sbn.Notification.Extras;
            string title = extras.GetString("android.title") ?? "";
            string text = extras.GetString("android.text") ?? "";
            if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(text)) return;

            // 自アプリの通知は無視
            if (sbn.PackageName == Application.Context.PackageName) return;

            // パッケージ名 → アプリ表示名
            string app = sbn.PackageName;
            try
            {
                var pm = Application.Context.PackageManager!;
                var info = pm.GetApplicationInfo(app, 0);
                app = pm.GetApplicationLabel(info)?.ToString() ?? app;
            }
            catch { /* アプリ名取得失敗時はパッケージ名のまま */ }

            var json = JsonSerializer.Serialize(new
            {
                app = Truncate(app, 64),
                title = Truncate(title, 128),
                text = Truncate(text, 400),
                id = sbn.Id,
                when = sbn.PostTime / 1000,
            });
            BleManager.Instance.SendNotification(json);
        }
        catch { /* 通知転送エラーは無視 (ログを汚さない) */ }
    }

    public override void OnNotificationRemoved(StatusBarNotification? sbn)
    {
        // 将来: 時計側から消去を同期する場合に利用
    }

    private static string Truncate(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return "";
        return s.Length <= max ? s : s.Substring(0, max);
    }
}
