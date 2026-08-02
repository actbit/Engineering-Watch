using System;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using Engineering_Watch.BLE;
using Engineering_Watch.UI;
using UiTheme = Engineering_Watch.UI.Theme;

namespace Engineering_Watch
{
    [Activity(Label = "@string/app_name", MainLauncher = true,
              WindowSoftInputMode = SoftInput.AdjustResize)]
    public class MainActivity : Activity
    {
        private const int RequestBlePerms = 200;
        private const int RequestNotifPerms = 201;

        private EditorTabView? _editorTab;
        private ConnectionTabView? _connectTab;
        private NotificationTabView? _notifTab;
        private SettingsTabView? _settingsTab;
        private FrameLayout? _content;
        private LinearLayout[]? _tabItems;
        private View[]? _tabIndicators;
        private TimeChangeReceiver? _timeReceiver;

        // タイムゾーン/時刻変更を検知して時計へ再同期 (海外移動時など)
        private class TimeChangeReceiver : BroadcastReceiver
        {
            public override void OnReceive(Context? context, Intent? intent)
            {
                BleManager.Instance.SendTimeSync();
            }
        }

        // ステータスバー/ナビゲーションバーの高さ分だけルートを内側に寄せる
        private class SystemBarInsetListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
        {
            public WindowInsets OnApplyWindowInsets(View v, WindowInsets insets)
            {
                v.SetPadding(0, insets.SystemWindowInsetTop, 0, insets.SystemWindowInsetBottom);
                return insets.ConsumeSystemWindowInsets();
            }
        }

        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);
            try
            {
                BuildUi();
            }
            catch (Exception ex)
            {
                // クラッシュさせずにエラー内容を画面表示 (診断用)
                var err = new TextView(this)
                {
                    Text = "起動エラー:\n" + ex,
                    TextSize = 13,
                };
                err.SetTextColor(Android.Graphics.Color.Rgb(0xFF, 0x77, 0x66));
                err.SetPadding(24, 24, 24, 24);
                SetContentView(err);
                Android.Util.Log.Error("EWatch", ex.ToString());
            }
        }

        private void BuildUi()
        {
            Window!.SetStatusBarColor(UiTheme.Bg);
            Window.AddFlags(WindowManagerFlags.KeepScreenOn);

            var root = new LinearLayout(this) { Orientation = Orientation.Vertical };
            root.SetBackgroundColor(UiTheme.Bg);

            // システムバー(ステータスバー/ナビゲーションバー)にコンテンツが重ならないよう、
            // 実際のインセットをルートのパディングとして適用する
            root.SetOnApplyWindowInsetsListener(new SystemBarInsetListener());

            // ---- タブバー (下線インジケータ付き) ----
            var tabBar = new LinearLayout(this) { Orientation = Orientation.Horizontal };
            tabBar.SetBackgroundColor(UiTheme.Surface);
            _tabItems = new LinearLayout[4];
            _tabIndicators = new View[4];
            var names = new[] { "文字盤", "接続", "通知", "設定" };
            for (int i = 0; i < 4; i++)
            {
                int idx = i;
                var item = new LinearLayout(this) { Orientation = Orientation.Vertical };
                item.SetGravity(GravityFlags.Center);
                item.Clickable = true;
                item.Click += (_, _) => SelectTab(idx);
                // ラベルは最低高さを確保して必ず表示されるようにする
                var label = new TextView(this)
                {
                    Text = names[i],
                    TextSize = 15,
                    Gravity = GravityFlags.Center,
                };
                label.SetMinimumHeight((int)UiTheme.Dp(this, 44));
                label.SetTextColor(UiTheme.TextDim);
                label.SetTypeface(null, TypefaceStyle.Bold);
                var indicator = new View(this);
                indicator.SetBackgroundColor(UiTheme.Accent);
                item.AddView(label, new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MatchParent, LinearLayout.LayoutParams.WrapContent));
                item.AddView(indicator, new LinearLayout.LayoutParams(
                    LinearLayout.LayoutParams.MatchParent, (int)UiTheme.Dp(this, 3)));
                _tabItems[i] = item;
                _tabIndicators[i] = indicator;
                tabBar.AddView(item, new LinearLayout.LayoutParams(0, LinearLayout.LayoutParams.WrapContent, 1f));
            }
            root.AddView(tabBar, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MatchParent, LinearLayout.LayoutParams.WrapContent));

            // ---- コンテンツ ----
            _content = new FrameLayout(this);
            root.AddView(_content, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MatchParent, 0, 1f));

            // ---- バージョン表示 (起動確認用。タブバーを邪魔しないよう最下部へ) ----
            var ver = new TextView(this)
            {
                Text = "EWatch v0.2.0",
                TextSize = 10,
                Gravity = GravityFlags.Center,
            };
            ver.SetTextColor(UiTheme.TextDim);
            root.AddView(ver, new LinearLayout.LayoutParams(
                LinearLayout.LayoutParams.MatchParent, LinearLayout.LayoutParams.WrapContent));

            SetContentView(root);

            _editorTab = new EditorTabView(this);
            _connectTab = new ConnectionTabView(this);
            _notifTab = new NotificationTabView(this);
            _settingsTab = new SettingsTabView(this);
            _content.AddView(_editorTab);
            _content.AddView(_connectTab);
            _content.AddView(_notifTab);
            _content.AddView(_settingsTab);

            SelectTab(0);
            RequestPermissions();
        }

        private void SelectTab(int index)
        {
            if (_editorTab != null) _editorTab.Visibility = index == 0 ? ViewStates.Visible : ViewStates.Gone;
            if (_connectTab != null) _connectTab.Visibility = index == 1 ? ViewStates.Visible : ViewStates.Gone;
            if (_notifTab != null) _notifTab.Visibility = index == 2 ? ViewStates.Visible : ViewStates.Gone;
            if (_settingsTab != null)
            {
                _settingsTab.Visibility = index == 3 ? ViewStates.Visible : ViewStates.Gone;
                if (index == 3) _settingsTab.RefreshNotifStatus();
            }
            if (_tabItems != null && _tabIndicators != null)
            {
                for (int i = 0; i < 4; i++)
                {
                     bool sel = i == index;
                    var label = (TextView)_tabItems[i].GetChildAt(0);
                    label.SetTextColor(sel ? UiTheme.Accent : UiTheme.TextDim);
                    label.SetTypeface(null, sel ? TypefaceStyle.Bold : TypefaceStyle.Normal);
                    _tabIndicators[i].Visibility = sel ? ViewStates.Visible : ViewStates.Invisible;
                }
            }
        }

        protected override void OnResume()
        {
            base.OnResume();
            // タイムゾーン/時刻の変更を検知して時計へ自動再同期
            if (_timeReceiver == null)
            {
                _timeReceiver = new TimeChangeReceiver();
                var filter = new IntentFilter();
                filter.AddAction(Intent.ActionTimezoneChanged);
                filter.AddAction(Intent.ActionTimeChanged);
                RegisterReceiver(_timeReceiver, filter);
            }
        }

        protected override void OnPause()
        {
            base.OnPause();
            if (_timeReceiver != null)
            {
                UnregisterReceiver(_timeReceiver);
                _timeReceiver = null;
            }
        }

        // ---- 権限 ----

        private void RequestPermissions()
        {
            if ((int)Build.VERSION.SdkInt < 31) return;
            var missing = new System.Collections.Generic.List<string>();
            foreach (var p in new[]
            {
                "android.permission.BLUETOOTH_SCAN",
                "android.permission.BLUETOOTH_CONNECT",
            })
            {
                if (CheckSelfPermission(p) != Permission.Granted) missing.Add(p);
            }
            if (missing.Count > 0)
                RequestPermissions(missing.ToArray(), RequestBlePerms);

            if ((int)Build.VERSION.SdkInt >= 33 &&
                CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
                RequestPermissions(new[] { "android.permission.POST_NOTIFICATIONS" }, RequestNotifPerms);
        }

        public override void OnRequestPermissionsResult(int requestCode, string[] permissions, Permission[] grantResults)
        {
            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        // ---- 画像ピッカー結果 ----

        protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
        {
            base.OnActivityResult(requestCode, resultCode, data);
            if (requestCode == EditorTabView.ImageRequestCode && resultCode == Result.Ok)
                _editorTab?.OnImagePicked(data);
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }
    }
}
