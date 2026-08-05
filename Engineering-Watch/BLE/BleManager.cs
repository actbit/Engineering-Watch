using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Bluetooth;
using Android.Content;

namespace Engineering_Watch.BLE;

// ============================================================
// BLE マネージャー (中央装置)
// スキャン / 接続 / GATT / 文字盤パッケージ転送 / 通知転送 / 制御
// ============================================================

public class BleManager : BluetoothGattCallback, BluetoothAdapter.ILeScanCallback
{
    public static BleManager Instance { get; } = new();

    // イベント
    public event Action<string>? Log;
    public event Action<bool>? ConnectionChanged;
    public event Action<string>? StatusJson;     // FF03
    public event Action<string>? WatchDataJson;  // FF04
    public event Action<string, int, int>? SendProgress; // 状態, 現在, 総数
    public event Action<string>? SendDone;       // "ok" / エラーメッセージ
    public event Action<List<BluetoothDevice>>? ScanResults;

    private BluetoothManager? _btm;
    private BluetoothAdapter? _adapter;
    private BluetoothGatt? _gatt;
    private BluetoothDevice? _lastDevice;        // 自動再接続用
    private BluetoothGattCharacteristic? _wfCh, _ctlCh, _stCh, _wdCh, _notifCh;
    private readonly List<BluetoothDevice> _found = new();
    private bool _scanning;
    private int _mtu = 23;
    private bool _connected;
    private bool _autoReconnect = true;
    private int _reconnectAttempts;
    private const int MaxReconnectAttempts = 5;

    // 書き込みフロー制御: OnCharacteristicWrite で完了を通知
    // ロックで保護し、コールバックの照合に一意 ID を使用
    private readonly object _writeLock = new();
    private TaskCompletionSource<bool>? _writeTcs;
    private int _writeId;                    // 現在の書き込み ID
    private int _lastCompletedWriteId;       // 最後に完了した書き込み ID
    private static readonly TimeSpan WriteTimeout = TimeSpan.FromSeconds(5);

    public bool Connected => _connected;

    private static ISharedPreferences? Prefs =>
        Application.Context.GetSharedPreferences("ewatch", FileCreationMode.Private);

    private BleManager() { }

    private void LogMsg(string m) => Log?.Invoke(m);

    private BluetoothAdapter? Adapter()
    {
        if (_adapter != null) return _adapter;
        _btm = (BluetoothManager?)Application.Context.GetSystemService(Context.BluetoothService);
        _adapter = _btm?.Adapter;
        return _adapter;
    }

    // ---------------- スキャン ----------------

    public void StartScan()
    {
        var a = Adapter();
        if (a == null || !a.IsEnabled) { LogMsg("Bluetoothが無効です"); return; }
        if (_scanning) return;
        _found.Clear();
        _scanning = true;
        a.StartLeScan(this);
        LogMsg("スキャン開始...");
    }

    public void StopScan()
    {
        if (!_scanning) return;
        Adapter()?.StopLeScan(this);
        _scanning = false;
        ScanResults?.Invoke(_found.ToList());
    }

    public void OnLeScan(BluetoothDevice device, int rssi, byte[] scanRecord)
    {
        var name = device.Name ?? "";
        // アドレスで重複排除 (BluetoothDevice の等価比較に依存しない)
        if (name.StartsWith("EWatch-", StringComparison.OrdinalIgnoreCase) &&
            !_found.Any(d => d.Address == device.Address))
        {
            _found.Add(device);
            LogMsg($"発見: {name} ({device.Address})");
            // 新しい端末が見つかったときだけ一覧を更新する
            // (毎パケット更新すると行が再生成され、タップがキャンセルされるため)
            ScanResults?.Invoke(_found.ToList());
        }
    }

    // ---------------- 接続 ----------------

    public async Task ConnectAsync(BluetoothDevice device)
    {
        _autoReconnect = true;
        _reconnectAttempts = 0;
        Disconnect();
        await Task.Delay(100);
        _connected = false;
        _lastDevice = device;

        // ---- ペアリング ----
        // 未ペアリングなら Android のペアリングダイアログが表示される
        // (時計側の既定パスキーは 123456)。
        // 完了は Bond 状態変化のブロードキャストで検知する (device インスタンスの
        // BondState をポーリングすると古い値のままで失敗するため)。
        try
        {
            if (device.BondState != Bond.Bonded)
            {
                LogMsg("ペアリング要求... (スマホのダイアログで許可してください)");
                bool bonded = await EnsureBondedAsync(device);
                if (bonded)
                {
                    LogMsg("ペアリング完了");
                    // ペアリング済み端末を記録 (システムにも保存される)
                    Prefs?.Edit()?.PutString("bonded_addr", device.Address)?.Apply();
                }
                else
                    LogMsg("ペアリングが完了しませんでした (接続は続行します)");
            }
            else
            {
                LogMsg("既にペアリング済み");
            }
        }
        catch (Exception ex)
        {
            LogMsg("ペアリング: " + ex.Message);
        }

        _gatt = device.ConnectGatt(Application.Context, false, this);
        LogMsg($"接続中: {device.Name} ...");
    }

    // ペアリング完了をブロードキャストで待つ (タイムアウト付き)
    private Task<bool> EnsureBondedAsync(BluetoothDevice device)
    {
        if (device.BondState == Bond.Bonded) return Task.FromResult(true);
        var tcs = new TaskCompletionSource<bool>();
        var receiver = new BondReceiver(device.Address, tcs);
        var filter = new IntentFilter(BluetoothDevice.ActionBondStateChanged);
        Application.Context.RegisterReceiver(receiver, filter);
        // タイムアウト (20s) でも確実に抜ける
        _ = Task.Delay(20000).ContinueWith(_ =>
        {
            if (!tcs.Task.IsCompleted)
            {
                try { Application.Context.UnregisterReceiver(receiver); } catch { }
                tcs.TrySetResult(device.BondState == Bond.Bonded);
            }
        });
        device.CreateBond();
        return tcs.Task.ContinueWith(_ =>
        {
            try { Application.Context.UnregisterReceiver(receiver); } catch { }
            return device.BondState == Bond.Bonded;
        });
    }

    public void Disconnect()
    {
        _autoReconnect = false;   // 明示的切断では再接続しない
        _reconnectAttempts = 0;
        try { _gatt?.Disconnect(); } catch { }
        try { _gatt?.Close(); } catch { }
        _gatt = null;
        _connected = false;
        _wfCh = _ctlCh = _stCh = _wdCh = _notifCh = null;
        // 保留中の書き込み完了待ちを解除
        lock (_writeLock)
        {
            _writeTcs?.TrySetResult(false);
            _writeTcs = null;
        }
        ConnectionChanged?.Invoke(false);
    }

    // ペアリング状態変化を受け取るレシーバー
    private class BondReceiver : BroadcastReceiver
    {
        private readonly string _addr;
        private readonly TaskCompletionSource<bool> _tcs;

        public BondReceiver(string addr, TaskCompletionSource<bool> tcs)
        {
            _addr = addr;
            _tcs = tcs;
        }

        public override void OnReceive(Context? context, Intent? intent)
        {
            if (intent?.Action != BluetoothDevice.ActionBondStateChanged) return;
            var dev = intent.GetParcelableExtra(BluetoothDevice.ExtraDevice) as BluetoothDevice;
            if (dev == null || dev.Address != _addr) return;
            var state = (Bond)intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
            if (state == Bond.Bonded) _tcs.TrySetResult(true);
            else if (state == Bond.None) _tcs.TrySetResult(false);
        }
    }

    // ---------------- GATT コールバック ----------------

    public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
    {
        if (newState == ProfileState.Connected)
        {
            LogMsg("GATT接続成功");
            _reconnectAttempts = 0;
            gatt.RequestMtu(512);
        }
        else if (newState == ProfileState.Disconnected)
        {
            LogMsg($"切断されました (status={status})");
            _connected = false;
            try { gatt.Close(); } catch { }
            if (ReferenceEquals(_gatt, gatt)) _gatt = null;
            // 保留中の書き込み完了待ちを解除
            lock (_writeLock)
            {
                _writeTcs?.TrySetResult(false);
                _writeTcs = null;
            }
            ConnectionChanged?.Invoke(false);

            // 自動再接続 (意図的な切断でない場合)
            if (_autoReconnect && _lastDevice != null && _reconnectAttempts < MaxReconnectAttempts)
            {
                _reconnectAttempts++;
                int delayMs = Math.Min(1000 * _reconnectAttempts, 5000);
                LogMsg($"再接続を試みます ({_reconnectAttempts}/{MaxReconnectAttempts})... {delayMs}ms 後");
                _ = Task.Run(async () =>
                {
                    await Task.Delay(delayMs);
                    if (_autoReconnect && !_connected && _lastDevice != null)
                    {
                        try
                        {
                            _gatt = _lastDevice.ConnectGatt(Application.Context, false, Instance);
                            LogMsg("再接続中...");
                        }
                        catch (Exception ex)
                        {
                            LogMsg("再接続失敗: " + ex.Message);
                        }
                    }
                });
            }
            else if (_reconnectAttempts >= MaxReconnectAttempts)
            {
                LogMsg("再接続の上限に達しました。手動で再接続してください");
            }
        }
    }

    public override void OnMtuChanged(BluetoothGatt gatt, int mtu, GattStatus status)
    {
        _mtu = mtu > 0 ? mtu : 23;
        int chunk = Math.Max(32, _mtu - 8);
        LogMsg($"MTU = {_mtu} (チャンクサイズ = {chunk} バイト)");
        gatt.DiscoverServices();
    }

    public override void OnServicesDiscovered(BluetoothGatt gatt, GattStatus status)
    {
        if (status != GattStatus.Success) { LogMsg("サービス発見失敗"); return; }
        var svc = gatt.GetService(Uuid(GattUuids.WatchService));
        var nsvc = gatt.GetService(Uuid(GattUuids.NotifService));
        if (svc == null && nsvc == null) { LogMsg("対応サービスが見つかりません"); return; }
        _wfCh = svc?.GetCharacteristic(Uuid(GattUuids.WatchFaceConfig));
        _ctlCh = svc?.GetCharacteristic(Uuid(GattUuids.Control));
        _stCh = svc?.GetCharacteristic(Uuid(GattUuids.Status));
        _wdCh = svc?.GetCharacteristic(Uuid(GattUuids.WatchData));
        _notifCh = nsvc?.GetCharacteristic(Uuid(GattUuids.Notification));

        // CCCD 有効化 (書き込み完了を待つ)
        _ = Task.Run(async () =>
        {
            if (_stCh != null)
            {
                gatt.SetCharacteristicNotification(_stCh, true);
                await EnableCccdAsync(gatt, _stCh, "Status");
            }
            if (_wdCh != null)
            {
                gatt.SetCharacteristicNotification(_wdCh, true);
                await EnableCccdAsync(gatt, _wdCh, "WatchData");
            }

            _connected = true;
            ConnectionChanged?.Invoke(true);
            LogMsg("準備完了 (サービス発見 + CCCD設定)");
            // 接続直後に自動で初期設定: 時刻同期 (タイムゾーン込み) + 状態取得
            AutoSetupOnConnect();
        });
    }

    /// <summary>接続確立後に自動実行する初期設定</summary>
    private void AutoSetupOnConnect()
    {
        SendTimeSync();
        SendControl("{\"cmd\":\"get_status\"}");
    }

    /// <summary>現在の時刻とタイムゾーンを時計へ同期する</summary>
    public void SendTimeSync()
    {
        long utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        int tz = (int)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes;
        bool dst = TimeZoneInfo.Local.IsDaylightSavingTime(DateTime.Now);
        SendControl($"{{\"cmd\":\"time_sync\",\"utc\":{utc},\"tz\":{tz},\"dst\":{(dst ? "true" : "false")}}}");
        LogMsg($"時刻同期: utc={utc} tz={tz}min dst={dst}");
    }

    /// <summary>CCCD 有効化 (書き込み完了を待つ)</summary>
    private async Task EnableCccdAsync(BluetoothGatt gatt, BluetoothGattCharacteristic ch, string name)
    {
        var cccd = ch.GetDescriptor(Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"));
        if (cccd == null) { LogMsg($"CCCD が見つかりません ({name})"); return; }
        var tcs = new TaskCompletionSource<bool>();
        lock (_writeLock)
        {
            _writeId++;
            _writeTcs = tcs;
        }
        cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
        gatt.WriteDescriptor(cccd);
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(WriteTimeout)) == tcs.Task;
        if (!completed)
        {
            LogMsg($"CCCD 書き込みタイムアウト ({name})");
            lock (_writeLock) { _writeTcs = null; }
        }
        else if (!await tcs.Task)
        {
            LogMsg($"CCCD 書き込み失敗 ({name})");
        }
        else
        {
            LogMsg($"CCCD 有効化完了 ({name})");
        }
    }

    public override void OnCharacteristicChanged(BluetoothGatt gatt, BluetoothGattCharacteristic ch, byte[] value)
    {
        var json = Encoding.UTF8.GetString(value);
        if (ch.Uuid.Equals(Uuid(GattUuids.Status))) StatusJson?.Invoke(json);
        else if (ch.Uuid.Equals(Uuid(GattUuids.WatchData))) WatchDataJson?.Invoke(json);
    }

    public override void OnCharacteristicWrite(BluetoothGatt gatt, BluetoothGattCharacteristic ch, GattStatus status)
    {
        if (status != GattStatus.Success) LogMsg("書き込み失敗: " + ch.Uuid);
        // 書き込み完了を通知 (フロー制御用)
        // コールバックはBLEスタックスレッドから来るため、ロックで保護
        TaskCompletionSource<bool>? tcs;
        lock (_writeLock)
        {
            _lastCompletedWriteId = _writeId;
            tcs = _writeTcs;
            _writeTcs = null;
        }
        tcs?.TrySetResult(status == GattStatus.Success);
    }

    public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
    {
        if (status != GattStatus.Success) LogMsg("CCCD書込失敗");
        // CCCD 書き込み完了を通知 (EnableCccdAsync で使用)
        TaskCompletionSource<bool>? tcs;
        lock (_writeLock)
        {
            _lastCompletedWriteId = _writeId;
            tcs = _writeTcs;
            _writeTcs = null;
        }
        tcs?.TrySetResult(status == GattStatus.Success);
    }

    private static Java.Util.UUID Uuid(Guid g)
    {
        // バイトオーダー変換は環境依存で脆いため、標準文字列経由で確実に変換する。
        return Java.Util.UUID.FromString(g.ToString());
    }

    // ---------------- 送信 ----------------

    private void Write(BluetoothGattCharacteristic? ch, byte[] data)
    {
        if (ch == null || _gatt == null) return;
        ch.SetValue(data);
        _gatt.WriteCharacteristic(ch);
    }

    /// <summary>書き込み完了を待つフロー制御付き送信</summary>
    private async Task<bool> WriteAsync(BluetoothGattCharacteristic? ch, byte[] data)
    {
        if (ch == null || _gatt == null) return false;
        int myId;
        TaskCompletionSource<bool> tcs;
        lock (_writeLock)
        {
            _writeId++;
            myId = _writeId;
            tcs = new TaskCompletionSource<bool>();
            _writeTcs = tcs;
        }
        ch.SetValue(data);
        if (!_gatt.WriteCharacteristic(ch))
        {
            lock (_writeLock)
            {
                if (_writeId == myId) { _writeTcs = null; }
            }
            LogMsg("WriteCharacteristic 呼び出し失敗");
            return false;
        }
        // 書き込み完了コールバックを待つ (タイムアウト付き)
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(WriteTimeout)) == tcs.Task;
        if (!completed)
        {
            LogMsg("書き込みタイムアウト");
            lock (_writeLock)
            {
                if (_writeId == myId) { _writeTcs = null; }
            }
            return false;
        }
        return await tcs.Task;
    }

    public void SendControl(string json) => Write(_ctlCh, Encoding.UTF8.GetBytes(json));

    public void SendNotification(string json)
    {
        if (_notifCh == null) { LogMsg("未接続のため通知を送信できません"); return; }
        Write(_notifCh, Encoding.UTF8.GetBytes(json));
    }

    // 文字盤パッケージ送信 (非同期・フロー制御付き)
    public async Task SendFacePackageAsync(byte[] bgPng, byte[]? hourPng, byte[]? minPng, byte[]? secPng, string dynamicJson)
    {
        if (!Connected || _wfCh == null) { SendDone?.Invoke("未接続"); return; }
        try
        {
            int total = 1 + (hourPng != null ? 1 : 0) + (minPng != null ? 1 : 0) + (secPng != null ? 1 : 0) + 1;
            _filesDone = 0;
            if (!await SendFileAsync(FaceFiles.Bg, FaceFiles.BgName, bgPng, total)) return;
            if (hourPng != null && !await SendFileAsync(FaceFiles.Hour, FaceFiles.HourName, hourPng, total)) return;
            if (minPng != null && !await SendFileAsync(FaceFiles.Min, FaceFiles.MinName, minPng, total)) return;
            if (secPng != null && !await SendFileAsync(FaceFiles.Sec, FaceFiles.SecName, secPng, total)) return;
            SendProgress?.Invoke("apply", _filesDone, total);
            if (!await WriteAsync(_wfCh, Wire.ApplyFrame(dynamicJson)))
            {
                SendDone?.Invoke("送信タイムアウト (apply)");
                return;
            }
            await Task.Delay(200);   // 時計側の反映待ち
            SendDone?.Invoke("ok");
        }
        catch (Exception ex)
        {
            SendDone?.Invoke("エラー: " + ex.Message);
        }
    }

    private int _filesDone;

    private async Task<bool> SendFileAsync(byte fileId, string name, byte[] data, int total)
    {
        SendProgress?.Invoke(name, _filesDone, total);
        // MTU に基づくチャンクサイズ (最低 32 バイト保証)
        int chunk = Math.Max(32, _mtu - 8);
        uint crc = 0;

        // BEGIN フレーム送信
        if (!await WriteAsync(_wfCh!, Wire.BeginFrame(fileId, data.Length, name)))
        {
            SendDone?.Invoke($"送信タイムアウト (begin:{name})");
            return false;
        }
        await Task.Delay(30);   // 時計側のファイル準備待ち

        // DATA フレーム送信 (書き込み完了ごとに待機)
        int off = 0;
        while (off < data.Length)
        {
            if (!Connected || _gatt == null)
            {
                SendDone?.Invoke("接続が切断されました");
                return false;
            }
            int n = Math.Min(chunk, data.Length - off);
            if (!await WriteAsync(_wfCh!, Wire.DataFrame(fileId, off, data, n)))
            {
                SendDone?.Invoke($"送信タイムアウト (data:{name} offset={off})");
                return false;
            }
            crc = Crc32.Update(crc, data, off, n);
            off += n;
            // チャンク間の短い待機 (BLE スタックの安定化)
            if (off < data.Length) await Task.Delay(5);
        }

        // END フレーム送信
        if (!await WriteAsync(_wfCh!, Wire.EndFrame(fileId, crc)))
        {
            SendDone?.Invoke($"送信タイムアウト (end:{name})");
            return false;
        }
        _filesDone++;
        SendProgress?.Invoke(name, _filesDone, total);
        await Task.Delay(200);      // 時計側の保存/CRC確認待ち
        return true;
    }
}
