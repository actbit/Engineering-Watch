#pragma once
#include <Arduino.h>

// ============================================================
// ディスプレイ (T-Watch S3: ST7789 240x240)
// ============================================================
#define WATCH_W 240
#define WATCH_H 240

// ============================================================
// BLE GATT (docs/PROTOCOL.md と一致させること)
// ============================================================
#define BLE_SERVICE_WATCH  "0000ff00-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_WATCHFACE "0000ff01-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_CONTROL   "0000ff02-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_STATUS    "0000ff03-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_WATCHDATA "0000ff04-0000-1000-8000-00805f9b34fb"

#define BLE_SERVICE_NOTIF  "0000ff10-0000-1000-8000-00805f9b34fb"
#define BLE_CHAR_NOTIF     "0000ff11-0000-1000-8000-00805f9b34fb"

// ============================================================
// 文字盤パッケージ (docs/PROTOCOL.md)
// ============================================================
enum FaceFileId : uint8_t {
    FACE_FILE_BG   = 1,   // bg.png   (240x240 背景)
    FACE_FILE_HOUR = 2,   // hour.png (アナログ時針)
    FACE_FILE_MIN  = 3,   // min.png
    FACE_FILE_SEC  = 4,   // sec.png
};

// FFat上の保存先 (storage.cpp は FFat API で "/face/xxx" を操作)
#define FACE_PATH_BG       "/face/bg.png"
#define FACE_PATH_HOUR     "/face/hour.png"
#define FACE_PATH_MIN      "/face/min.png"
#define FACE_PATH_SEC      "/face/sec.png"
#define FACE_PATH_DYNAMIC  "/face/dynamic.json"

#define MAX_DYNAMIC_PARTS  20

// ============================================================
// WiFi オンデマンド
// ============================================================
#define WIFI_AUTO_OFF_MS       (10UL * 60 * 1000)   // 無通信10分で自動OFF
#define WIFI_CONNECT_TIMEOUT   (20UL * 1000)

// ============================================================
// NVS (Preferences) キー
// ============================================================
#define NVS_WIFI_SSID    "wifi_ssid"
#define NVS_WIFI_PASS    "wifi_pass"
#define NVS_TZ_OFFSET    "tz_offset"
#define NVS_BRIGHTNESS   "brightness"
#define NVS_STEPS        "steps"
#define NVS_SCREEN_TIMEOUT "screen_to"
#define NVS_TILT_WAKE    "tilt_wake"

// ============================================================
// ファームウェアバージョン
// ============================================================
#define FW_VERSION "0.1.0"
