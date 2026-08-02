#pragma once
#include <Arduino.h>

// グローバル状態 (main.cpp / 各モジュールから参照)
struct AppState {
    volatile bool bmaIrq = false;      // BMA423 割り込みフラグ
    volatile bool pmuIrq = false;      // AXP2101 割り込みフラグ
    uint32_t steps = 0;                // 歩数
    int battery = -1;                  // バッテリー% (-1=不明)
    bool charging = false;
    bool bleConnected = false;
    int wifiState = 0;                 // 0=off 1=connecting 2=on
    int32_t tzOffsetMin = 9 * 60;      // タイムゾーンオフセット(分) 既定JST
    uint8_t brightness = 128;
    // 画面管理
    int32_t screenTimeoutS = 15;       // 無操作で消灯する秒数 (0=常時点灯)
    bool tiltWake = true;              // 傾き/ダブルタップで画面ON
    bool screenOn = true;
};

extern AppState app;
