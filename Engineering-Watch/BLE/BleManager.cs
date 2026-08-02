using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
    private BluetoothGattCharacteristic? _wfCh, _ctlCh, _stCh, _wdCh, _notifCh;
    private readonly List<BluetoothDevice> _found = new();
    private bool _scanning;
    private int _mtu = 23;
    private bool _connected;

    public bool Connected => _connected;

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
        if (name.StartsWith("EWatch-", StringComparison.OrdinalIgnoreCase) && !_found.Contains(device))
        {
            _found.Add(device);
            LogMsg($"発見: {name} ({device.Address})");
            ScanResults?.Invoke(_found.ToList());
        }
    }

    // ---------------- 接続 ----------------

    public async Task ConnectAsync(BluetoothDevice device)
    {
        Disconnect();
        await Task.Delay(100);
        _connected = false;
        _gatt = device.ConnectGatt(Application.Context, false, this);
        LogMsg($"接続中: {device.Name} ...");
    }

    public void Disconnect()
    {
        try { _gatt?.Disconnect(); } catch { }
        try { _gatt?.Close(); } catch { }
        _gatt = null;
        _connected = false;
        _wfCh = _ctlCh = _stCh = _wdCh = _notifCh = null;
        ConnectionChanged?.Invoke(false);
    }

    // ---------------- GATT コールバック ----------------

    public override void OnConnectionStateChange(BluetoothGatt gatt, GattStatus status, ProfileState newState)
    {
        if (newState == ProfileState.Connected)
        {
            LogMsg("GATT接続成功");
            gatt.RequestMtu(512);
        }
        else if (newState == ProfileState.Disconnected)
        {
            LogMsg("切断されました");
            _connected = false;
            try { gatt.Close(); } catch { }
            if (ReferenceEquals(_gatt, gatt)) _gatt = null;
            ConnectionChanged?.Invoke(false);
        }
    }

    public override void OnMtuChanged(BluetoothGatt gatt, int mtu, GattStatus status)
    {
        _mtu = mtu > 0 ? mtu : 23;
        LogMsg($"MTU = {_mtu}");
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

        if (_stCh != null)
        {
            gatt.SetCharacteristicNotification(_stCh, true);
            EnableCccd(gatt, _stCh);
        }
        if (_wdCh != null)
        {
            gatt.SetCharacteristicNotification(_wdCh, true);
            EnableCccd(gatt, _wdCh);
        }

        _connected = true;
        ConnectionChanged?.Invoke(true);
        LogMsg("準備完了 (サービス発見)");
        // 接続直後に自動で初期設定: 時刻同期 (タイムゾーン込み) + 状態取得
        AutoSetupOnConnect();
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
        SendControl($"{{\"cmd\":\"time_sync\",\"utc\":{utc},\"tz\":{tz}}}");
        LogMsg($"時刻同期: utc={utc} tz={tz}min");
    }

    private static void EnableCccd(BluetoothGatt gatt, BluetoothGattCharacteristic ch)
    {
        var cccd = ch.GetDescriptor(Java.Util.UUID.FromString("00002902-0000-1000-8000-00805f9b34fb"));
        if (cccd != null)
        {
            cccd.SetValue(BluetoothGattDescriptor.EnableNotificationValue.ToArray());
            gatt.WriteDescriptor(cccd);
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
    }

    public override void OnDescriptorWrite(BluetoothGatt gatt, BluetoothGattDescriptor descriptor, GattStatus status)
    {
        if (status != GattStatus.Success) LogMsg("CCCD書込失敗");
    }

    private static Java.Util.UUID Uuid(Guid g)
    {
        var b = g.ToByteArray();
        Array.Reverse(b, 0, 4);
        Array.Reverse(b, 4, 2);
        Array.Reverse(b, 6, 2);
        long msb = unchecked((long)BitConverter.ToUInt64(b, 0));
        long lsb = unchecked((long)BitConverter.ToUInt64(b, 8));
        return new Java.Util.UUID(msb, lsb);
    }

    // ---------------- 送信 ----------------

    private void Write(BluetoothGattCharacteristic? ch, byte[] data)
    {
        if (ch == null || _gatt == null) return;
        ch.SetValue(data);
        _gatt.WriteCharacteristic(ch);
    }

    public void SendControl(string json) => Write(_ctlCh, Encoding.UTF8.GetBytes(json));

    public void SendNotification(string json)
    {
        if (_notifCh == null) { LogMsg("未接続のため通知を送信できません"); return; }
        Write(_notifCh, Encoding.UTF8.GetBytes(json));
    }

    // 文字盤パッケージ送信 (非同期)
    public async Task SendFacePackageAsync(byte[] bgPng, byte[]? hourPng, byte[]? minPng, byte[]? secPng, string dynamicJson)
    {
        if (!Connected || _wfCh == null) { SendDone?.Invoke("未接続"); return; }
        try
        {
            int total = 1 + (hourPng != null ? 1 : 0) + (minPng != null ? 1 : 0) + (secPng != null ? 1 : 0) + 1;
            _filesDone = 0;
            await SendFileAsync(FaceFiles.Bg, FaceFiles.BgName, bgPng, total);
            if (hourPng != null) await SendFileAsync(FaceFiles.Hour, FaceFiles.HourName, hourPng, total);
            if (minPng != null) await SendFileAsync(FaceFiles.Min, FaceFiles.MinName, minPng, total);
            if (secPng != null) await SendFileAsync(FaceFiles.Sec, FaceFiles.SecName, secPng, total);
            SendProgress?.Invoke("apply", _filesDone, total);
            Write(_wfCh, Wire.ApplyFrame(dynamicJson));
            await Task.Delay(100);
            SendDone?.Invoke("ok");
        }
        catch (Exception ex)
        {
            SendDone?.Invoke("エラー: " + ex.Message);
        }
    }

    private int _filesDone;

    private async Task SendFileAsync(byte fileId, string name, byte[] data, int total)
    {
        SendProgress?.Invoke(name, _filesDone, total);
        int chunk = _mtu - 8;
        if (chunk < 32) chunk = 32;
        uint crc = 0;
        Write(_wfCh!, Wire.BeginFrame(fileId, data.Length, name));
        await Task.Delay(20);
        int off = 0;
        while (off < data.Length)
        {
            int n = Math.Min(chunk, data.Length - off);
            Write(_wfCh!, Wire.DataFrame(fileId, off, data, n));
            crc = Crc32.Update(crc, data, off, n);
            off += n;
            await Task.Delay(12);   // 時計側のFFat書き込み時間を確保
        }
        Write(_wfCh!, Wire.EndFrame(fileId, crc));
        _filesDone++;
        SendProgress?.Invoke(name, _filesDone, total);
        await Task.Delay(120);      // 時計側の保存/CRC確認待ち
    }
}
