#include "ble_server.h"
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include "watchface.h"
#include "notifications.h"
#include "wifi_mgr.h"
#include "screen_mgr.h"
#include <BLEDevice.h>
#include <BLEUtils.h>
#include <BLEServer.h>
#include <BLE2902.h>
#include <LilyGoLib.h>
#include <FFat.h>
#include <ArduinoJson.h>
#include <sys/time.h>

// ============================================================
// 静的状態
// ============================================================
static BLEServer* pServer = nullptr;
static BLECharacteristic* chWf = nullptr;
static BLECharacteristic* chCtl = nullptr;
static BLECharacteristic* chSt = nullptr;
static BLECharacteristic* chWd = nullptr;
static BLECharacteristic* chN = nullptr;
static bool connected = false;

// 通知キュー (BLE タスクから main ループへ)
static const int STATUS_Q = 8;
static const int DATA_Q = 4;
static String statusQueue[STATUS_Q];
static String dataQueue[DATA_Q];
static int statusHead = 0, statusCount = 0;
static int dataHead = 0, dataCount = 0;
static SemaphoreHandle_t qMutex = nullptr;

// 通知受信キュー (BLE タスクから main ループへ、LVGL スレッドセーフ化)
struct NotifEntry {
    String app;
    String title;
    String text;
    uint32_t id;
    uint32_t when;
};
static const int NOTIF_Q = 16;
static NotifEntry notifQueue[NOTIF_Q];
static int notifHead = 0, notifCount = 0;

// 受信チャンク状態
struct RecvFile {
    bool active = false;
    uint8_t fileId = 0;
    uint32_t total = 0;
    uint32_t offset = 0;
    uint32_t crc = 0;
    File fp;
};
static RecvFile recvFile;

// 保留コマンド
static String ctlBuf;
static volatile bool ctlPending = false;

// ============================================================
// キュー
// ============================================================
static bool q_push(String* q, int cap, int& head, int& count, const char* json) {
    if (qMutex) xSemaphoreTake(qMutex, portMAX_DELAY);
    bool ok = false;
    if (count < cap) {
        q[(head + count) % cap] = String(json);
        count++;
        ok = true;
    }
    if (qMutex) xSemaphoreGive(qMutex);
    return ok;
}

static String q_pop(String* q, int cap, int& head, int& count) {
    String s;
    if (qMutex) xSemaphoreTake(qMutex, portMAX_DELAY);
    if (count > 0) {
        s = q[head];
        head = (head + 1) % cap;
        count--;
    }
    if (qMutex) xSemaphoreGive(qMutex);
    return s;
}

void ble_notify_status(const char* json) {
    if (connected) q_push(statusQueue, STATUS_Q, statusHead, statusCount, json);
}

void ble_send_watchdata() {
    if (!connected) return;
    char buf[160];
    snprintf(buf, sizeof(buf),
             "{\"steps\":%u,\"battery\":%d,\"charging\":%s,\"ts\":%lu}",
             app.steps, app.battery, app.charging ? "true" : "false",
             (unsigned long)time(nullptr));
    q_push(dataQueue, DATA_Q, dataHead, dataCount, buf);
}

void ble_send_state() {
    if (!connected) return;
    char buf[200];
    const char* ws = app.wifiState == 0 ? "off" : (app.wifiState == 1 ? "connecting" : "connected");
    snprintf(buf, sizeof(buf),
             "{\"type\":\"state\",\"ver\":\"%s\",\"wifi\":\"%s\",\"ble\":true,"
             "\"battery\":%d,\"charging\":%s,\"steps\":%u}",
             FW_VERSION, ws, app.battery, app.charging ? "true" : "false", app.steps);
    ble_notify_status(buf);
}

// ============================================================
// 文字盤パッケージ受信 (チャンク再構築)
// ============================================================
static void wf_status(const char* file, const char* state, uint32_t bytes = 0) {
    char buf[160];
    snprintf(buf, sizeof(buf), "{\"type\":\"wf\",\"file\":\"%s\",\"state\":\"%s\",\"bytes\":%u}",
             file, state, bytes);
    ble_notify_status(buf);
}

static void wf_error(const char* msg) {
    char buf[160];
    snprintf(buf, sizeof(buf), "{\"type\":\"wf\",\"state\":\"error\",\"msg\":\"%s\"}", msg);
    ble_notify_status(buf);
}

static void handle_wf_frame(const uint8_t* p, size_t len) {
    if (!p || len < 6) return;
    uint8_t type = p[0];
    uint8_t fileId = p[1];
    uint32_t v = (uint32_t)p[2] | ((uint32_t)p[3] << 8) |
                 ((uint32_t)p[4] << 16) | ((uint32_t)p[5] << 24);
    const uint8_t* data = p + 6;
    size_t dlen = len - 6;

    switch (type) {
    case 0x01: {  // WF_BEGIN
        // 前回の転送が途中で残っていた場合、安全にクローズして削除
        if (recvFile.active) {
            recvFile.fp.close();
            recvFile.active = false;
            // 部分的に書き込まれたファイルを削除
            char oldPath[64];
            snprintf(oldPath, sizeof(oldPath), "/face/%d", recvFile.fileId);
            FFat.remove(oldPath);
            Serial.printf("[ble] cleaned up partial file: %s\n", oldPath);
        }
        if (dlen == 0 || dlen > 32) { wf_error("bad_name"); return; }
        char name[40];
        memcpy(name, data, dlen);
        name[dlen] = 0;
        if (name[0] == '/' || strstr(name, "..")) { wf_error("bad_name"); return; }
        if (v > 512 * 1024) { wf_error("too_large"); return; }
        char path[64];
        snprintf(path, sizeof(path), "/face/%s", name);
        recvFile.fp = FFat.open(path, FILE_WRITE);
        if (!recvFile.fp) { wf_error("open_failed"); return; }
        recvFile.active = true;
        recvFile.fileId = fileId;
        recvFile.total = v;
        recvFile.offset = 0;
        recvFile.crc = 0;
        Serial.printf("[ble] begin %s (%u)\n", path, v);
        break;
    }
    case 0x02: {  // WF_DATA
        if (!recvFile.active || recvFile.fileId != fileId) return;
        if (dlen == 0) return;   // 空データは無視
        if (recvFile.offset + dlen > recvFile.total) {
            wf_error("overflow");
            recvFile.fp.close();
            recvFile.active = false;
            return;
        }
        recvFile.fp.seek(recvFile.offset);
        if (recvFile.fp.write(data, dlen) != dlen) { wf_error("write_failed"); return; }
        recvFile.offset += dlen;
        recvFile.crc = storage_crc32_update(recvFile.crc, data, dlen);
        // 進捗ログ (10チャンクごと)
        if ((recvFile.offset / dlen) % 10 == 0)
            Serial.printf("[ble] data %u/%u\n", recvFile.offset, recvFile.total);
        break;
    }
    case 0x03: {  // WF_END
        if (!recvFile.active || recvFile.fileId != fileId) return;
        recvFile.fp.close();
        recvFile.active = false;
        if (v != recvFile.crc) {
            Serial.printf("[ble] crc mismatch: got %08x expect %08x\n", recvFile.crc, v);
            wf_error("crc_mismatch");
            return;
        }
        char fname[16];
        snprintf(fname, sizeof(fname), "%d", fileId);
        wf_status(fname, "ok", recvFile.offset);
        break;
    }
    case 0x04: {  // WF_APPLY (dynamic.json)
        if (dlen == 0 || dlen > 4096) { wf_error("bad_json"); return; }
        if (!storage_write_file(FACE_PATH_DYNAMIC, data, dlen)) {
            wf_error("write_failed");
            return;
        }
        watchface_apply_face();
        wf_status("dynamic.json", "applied", dlen);
        break;
    }
    default:
        break;
    }
}

// ============================================================
// 通知受信 (FF11) — キューに積んで main ループで処理 (LVGL スレッドセーフ化)
// ============================================================
static void handle_notif(const uint8_t* p, size_t len) {
    if (!p || len == 0) return;
    // 通知サイズ検証 (PROTOCOL.md: 512B以内)
    if (len > 512) {
        Serial.printf("[notif] too large: %u bytes (max 512)\n", len);
        return;
    }
    JsonDocument doc;
    if (deserializeJson(doc, p, len)) return;
    String appN = doc["app"] | "";
    String title = doc["title"] | "";
    String text = doc["text"] | "";
    uint32_t id = doc["id"] | 0;
    uint32_t when = doc["when"] | 0;
    if (appN.length() == 0 && title.length() == 0 && text.length() == 0) return;
    // キューに積む (BLE スタックスレッドから安全に呼び出し可能)
    if (qMutex) xSemaphoreTake(qMutex, portMAX_DELAY);
    if (notifCount < NOTIF_Q) {
        int idx = (notifHead + notifCount) % NOTIF_Q;
        notifQueue[idx].app = appN;
        notifQueue[idx].title = title;
        notifQueue[idx].text = text;
        notifQueue[idx].id = id;
        notifQueue[idx].when = when;
        notifCount++;
    } else {
        // キュー満杯: 古いエントリを上書き
        int idx = notifHead;
        notifQueue[idx].app = appN;
        notifQueue[idx].title = title;
        notifQueue[idx].text = text;
        notifQueue[idx].id = id;
        notifQueue[idx].when = when;
        notifHead = (notifHead + 1) % NOTIF_Q;
    }
    if (qMutex) xSemaphoreGive(qMutex);
    // 振動 + 画面ON もキューで処理 (LVGL スレッドセーフ化)
    // screen_on() と setWaveform は ble_poll() で実行
    Serial.printf("[notif-queued] %s: %s %s\n", appN.c_str(), title.c_str(), text.c_str());
}

// ============================================================
// 制御コマンド (FF02)
// ============================================================
static void handle_control(const String& json) {
    Serial.printf("[ble] ctl: %s\n", json.c_str());
    JsonDocument doc;
    if (deserializeJson(doc, json)) return;
    const char* cmd = doc["cmd"] | "";
    screen_activity();   // コマンド受信は操作として扱う

    if (!strcmp(cmd, "time_sync")) {
        int64_t utc = doc["utc"] | (int64_t)0;
        int32_t tz = doc["tz"] | app.tzOffsetMin;
        bool dst = doc["dst"] | false;
        if (utc > 0) {
            app.tzOffsetMin = tz;
            nvs_save_i32(NVS_TZ_OFFSET, tz);
            // DST オフセットを加算 (通常 +60 分)
            int32_t totalOffset = tz + (dst ? 60 : 0);
            time_t local = (time_t)(utc + (int64_t)totalOffset * 60);
            struct tm t;
            gmtime_r(&local, &t);
            watch.setDateTime(t.tm_year + 1900, t.tm_mon + 1, t.tm_mday,
                              t.tm_hour, t.tm_min, t.tm_sec);
            timeval tv = { local, 0 };
            settimeofday(&tv, nullptr);
            ble_notify_status("{\"type\":\"time\",\"state\":\"ok\"}");
            Serial.printf("[ble] time synced (tz=%d dst=%d)\n", tz, dst);
        }
    } else if (!strcmp(cmd, "wifi_on")) {
        String ssid = doc["ssid"] | "";
        String pass = doc["pass"] | "";
        wifi_mgr_connect(ssid, pass);
    } else if (!strcmp(cmd, "wifi_off")) {
        wifi_mgr_disconnect();
    } else if (!strcmp(cmd, "wifi_status") || !strcmp(cmd, "get_status")) {
        ble_send_state();
    } else if (!strcmp(cmd, "brightness")) {
        int v = doc["v"] | -1;
        if (v >= 0 && v <= 255) {
            app.brightness = (uint8_t)v;
            watch.setBrightness(app.brightness);
            nvs_save_i32(NVS_BRIGHTNESS, app.brightness);
            ble_notify_status("{\"type\":\"brightness\",\"state\":\"ok\"}");
        }
    } else if (!strcmp(cmd, "vibrate")) {
        int ms = doc["ms"] | 300;
        // DRV2605 の waveform を使用して指定時間振動
        // effect 78 = strong buzz (100%)
        // 複数回再生して長さを調整
        int repeats = ms / 100;  // 1エフェクト約100ms
        if (repeats < 1) repeats = 1;
        if (repeats > 10) repeats = 10;
        for (int i = 0; i < repeats; i++) {
            watch.setWaveform(i, 78);
        }
        watch.setWaveform(repeats, 0);  // 終端
        Serial.printf("[ble] vibrate %dms (repeats=%d)\n", ms, repeats);
    } else if (!strcmp(cmd, "reboot")) {
        Serial.println("[ble] reboot command");
        delay(100);
        ESP.restart();
    } else if (!strcmp(cmd, "clear_notifs")) {
        notifications_clear_all();
    } else if (!strcmp(cmd, "screen_timeout")) {
        int s = doc["s"] | 15;
        if (s < 0) s = 0;
        if (s > 3600) s = 3600;
        app.screenTimeoutS = s;
        nvs_save_i32(NVS_SCREEN_TIMEOUT, s);
        String st = String("{\"type\":\"screen\",\"state\":\"ok\",\"timeout\":") + s + "}";
        ble_notify_status(st.c_str());
    } else if (!strcmp(cmd, "tilt_wake")) {
        bool on = doc["on"] | true;
        app.tiltWake = on;
        nvs_save_i32(NVS_TILT_WAKE, on ? 1 : 0);
        watch.enableFeature(SensorBMA423::FEATURE_TILT, on);
        watch.enableFeature(SensorBMA423::FEATURE_WAKEUP, on);
        // 歩数割り込みも含めてマスクを設定 (configreFeatureInterrupt は
        // マスク全体を上書きするため)
        watch.configreFeatureInterrupt(
            SensorBMA423::INT_STEP_CNTR |
            (on ? (SensorBMA423::INT_TILT | SensorBMA423::INT_WAKEUP) : 0), true);
        ble_notify_status(on ? "{\"type\":\"screen\",\"tilt_wake\":true}"
                             : "{\"type\":\"screen\",\"tilt_wake\":false}");
    } else if (!strcmp(cmd, "wake")) {
        screen_on();
    }
}

// ============================================================
// BLE コールバック
// ============================================================
class ServerCallbacks : public BLEServerCallbacks {
    void onConnect(BLEServer*) override {
        connected = true;
        app.bleConnected = true;
        Serial.println("[ble] connected");
        ble_send_state();
    }
    void onDisconnect(BLEServer*) override {
        connected = false;
        app.bleConnected = false;
        Serial.println("[ble] disconnected");
        // 転送中のファイルがあれば安全にクローズ
        if (recvFile.active) {
            recvFile.fp.close();
            recvFile.active = false;
            Serial.println("[ble] recvFile cleaned up on disconnect");
        }
        // 保留コマンドをクリア
        ctlPending = false;
        ctlBuf = "";
        pServer->getAdvertising()->start();
    }
};

// ペアリング受け入れ (Androidのペアリング要求に応答)
class EWatchSecurityCallbacks : public BLESecurityCallbacks {
    uint32_t onPassKeyRequest() override { return 123456; }
    void onPassKeyNotify(uint32_t pass_key) override {
        Serial.printf("[ble] passkey: %06u\n", pass_key);
    }
    bool onConfirmPIN(uint32_t pass_key) override { return true; }
    bool onSecurityRequest() override { return true; }
    void onAuthenticationComplete(esp_ble_auth_cmpl_t auth_cmpl) override {
        if (auth_cmpl.success) {
            Serial.println("[ble] pairing complete");
        } else {
            Serial.println("[ble] pairing failed");
        }
    }
};

class ChCallbacks : public BLECharacteristicCallbacks {
    void onWrite(BLECharacteristic* c) override {
        std::string val = c->getValue();
        const uint8_t* p = (const uint8_t*)val.data();
        size_t len = val.size();
        if (c == chWf) {
            handle_wf_frame(p, len);
        } else if (c == chCtl) {
            if (len > 0 && len < 1024) {
                ctlBuf = String((const char*)p, len);
                ctlPending = true;
            }
        } else if (c == chN) {
            handle_notif(p, len);
        }
    }
};

// ============================================================
// 初期化
// ============================================================
void ble_init() {
    qMutex = xSemaphoreCreateMutex();

    uint8_t mac[6];
    esp_read_mac(mac, ESP_MAC_BT);
    char name[20];
    snprintf(name, sizeof(name), "EWatch-%02X%02X", mac[4], mac[5]);

    BLEDevice::init(name);
    BLEDevice::setMTU(517);
    BLEDevice::setSecurityCallbacks(new EWatchSecurityCallbacks());
    pServer = BLEDevice::createServer();
    pServer->setCallbacks(new ServerCallbacks());

    BLEService* svc = pServer->createService(BLE_SERVICE_WATCH);
    chWf = svc->createCharacteristic(BLE_CHAR_WATCHFACE, ESP_GATT_CHAR_PROP_BIT_WRITE);
    chCtl = svc->createCharacteristic(BLE_CHAR_CONTROL,
                                      ESP_GATT_CHAR_PROP_BIT_WRITE | ESP_GATT_CHAR_PROP_BIT_READ);
    chSt = svc->createCharacteristic(BLE_CHAR_STATUS, ESP_GATT_CHAR_PROP_BIT_NOTIFY);
    chSt->addDescriptor(new BLE2902());
    chWd = svc->createCharacteristic(BLE_CHAR_WATCHDATA, ESP_GATT_CHAR_PROP_BIT_NOTIFY);
    chWd->addDescriptor(new BLE2902());

    BLEService* nsvc = pServer->createService(BLE_SERVICE_NOTIF);
    chN = nsvc->createCharacteristic(BLE_CHAR_NOTIF, ESP_GATT_CHAR_PROP_BIT_WRITE);

    ChCallbacks* cb = new ChCallbacks();
    chWf->setCallbacks(cb);
    chCtl->setCallbacks(cb);
    chN->setCallbacks(cb);

    svc->start();
    nsvc->start();

    BLEAdvertising* ad = pServer->getAdvertising();
    ad->addServiceUUID(BLE_SERVICE_WATCH);
    ad->addServiceUUID(BLE_SERVICE_NOTIF);
    ad->setScanResponse(true);
    ad->setMinPreferred(0x06);
    ad->setMaxPreferred(0x12);
    BLEDevice::startAdvertising();
    Serial.printf("[ble] advertising as %s\n", name);
}

bool ble_connected() { return connected; }

void ble_poll() {
    // キュー送出
    while (statusCount > 0) {
        String s = q_pop(statusQueue, STATUS_Q, statusHead, statusCount);
        if (connected && chSt) {
            chSt->setValue((uint8_t*)s.c_str(), s.length());
            chSt->notify();
        }
    }
    while (dataCount > 0) {
        String s = q_pop(dataQueue, DATA_Q, dataHead, dataCount);
        if (connected && chWd) {
            chWd->setValue((uint8_t*)s.c_str(), s.length());
            chWd->notify();
        }
    }
    // 通知キュー処理 (main ループで LVGL を安全に操作)
    while (notifCount > 0) {
        NotifEntry ne;
        if (qMutex) xSemaphoreTake(qMutex, portMAX_DELAY);
        ne = notifQueue[notifHead];
        notifHead = (notifHead + 1) % NOTIF_Q;
        notifCount--;
        if (qMutex) xSemaphoreGive(qMutex);
        // main ループから LVGL を呼び出し (スレッドセーフ)
        notifications_add(ne.app, ne.title, ne.text, ne.id, ne.when);
        // 振動 + 画面ON (main ループから安全に呼び出し)
        screen_on();
        watch.setWaveform(0, 78);
        char buf[96];
        snprintf(buf, sizeof(buf), "{\"type\":\"notif\",\"state\":\"shown\",\"id\":%u}", ne.id);
        ble_notify_status(buf);
        Serial.printf("[notif] %s: %s %s\n", ne.app.c_str(), ne.title.c_str(), ne.text.c_str());
    }
    // コマンド処理
    if (ctlPending) {
        ctlPending = false;
        handle_control(ctlBuf);
        ctlBuf = "";
    }
}
