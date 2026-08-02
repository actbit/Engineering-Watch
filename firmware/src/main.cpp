#include <Arduino.h>
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include <LilyGoLib.h>
#include "LV_Helper.h"
#include "watchface.h"
#include "notifications.h"
#include "ble_server.h"
#include "wifi_mgr.h"
#include "screen_mgr.h"

AppState app;

// screen_mgr.cpp 内で定義 (BMA割り込みから画面ON用)
void screen_mgr_bma_wake();

// バッテリー電圧 → %
static int battery_percent() {
    float v = watch.getBattVoltage() / 1000.0f;
    if (v >= 4.20f) return 100;
    if (v <= 3.30f) return 0;
    return (int)((v - 3.30f) / 0.90f * 100.0f + 0.5f);
}

void setup() {
    Serial.begin(115200);
    delay(200);
    Serial.println("\n[main] Engineering-Watch boot");

    // ハードウェア初期化 (TFT / タッチ / PMU / BMA423 / RTC / DRV2605)
    watch.begin();

    // 明るさ復元
    app.brightness = (uint8_t)nvs_load_i32(NVS_BRIGHTNESS, 128);
    watch.setBrightness(app.brightness);

    beginLvglHelper(false);

    storage_init();

    app.tzOffsetMin = nvs_load_i32(NVS_TZ_OFFSET, 9 * 60);
    app.steps = (uint32_t)nvs_load_i32(NVS_STEPS, 0);

    // RTC → システム時刻
    watch.hwClockRead();

    // BMA423 センサー初期化 (歩数計 + 傾き/ダブルタップ復帰)
    // ※ 公式ファクトリーデモと同じシーケンスで行うこと
    //   (enableAccelerometer を呼ばないと加速度センサーが動作せず、
    //    傾き/ダブルタップ/歩数 の全機能が無効になる)
    watch.configAccelerometer();
    watch.enableAccelerometer();
    watch.configInterrupt();
    watch.enableFeature(SensorBMA423::FEATURE_STEP_CNTR |
                        SensorBMA423::FEATURE_TILT |
                        SensorBMA423::FEATURE_WAKEUP, true);
    watch.attachBMA([]() { app.bmaIrq = true; });
    watch.attachPMU([]() { app.pmuIrq = true; });

    // 画面管理 (傾けてON のための BMA 傾き/ダブルタップ設定)
    screen_mgr_init();

    // 初回バッテリー読み
    app.battery = battery_percent();
    app.charging = watch.isCharging();

    notifications_init();
    watchface_init();      // 保存済み文字盤 or デフォルト
    wifi_mgr_init();
    ble_init();

    ble_send_state();
    Serial.println("[main] ready");
}

void loop() {
    if (app.screenOn) {
        lv_timer_handler();
        delay(5);
    } else {
        delay(30);   // 消灯中は描画を止めて省電力
    }
    ble_poll();
    wifi_mgr_poll();
    notifications_poll();
    screen_mgr_poll();

    // BMA423 割り込み (歩数/傾き/ダブルタップ)
    if (app.bmaIrq) {
        app.bmaIrq = false;
        uint16_t st = watch.readBMA();
        if (watch.isPedometer()) {
            app.steps = watch.getPedometerCounter();
            nvs_save_i32(NVS_STEPS, (int32_t)app.steps);
            Serial.printf("[steps] %u\n", app.steps);
        }
        if (watch.isTilt() || watch.isDoubleTap()) {
            screen_mgr_bma_wake();
        }
    }

    // AXP2101 割り込み
    if (app.pmuIrq) {
        app.pmuIrq = false;
        watch.clearPMU();
    }

    // バッテリー定期更新 (5秒)
    static uint32_t lastBat = 0;
    if (millis() - lastBat > 5000) {
        lastBat = millis();
        int pct = battery_percent();
        if (pct != app.battery) {
            app.battery = pct;
            app.charging = watch.isCharging();
            Serial.printf("[battery] %d%%%s\n", app.battery, app.charging ? " (charging)" : "");
        }
    }

    // 歩数・バッテリーを定期送信 (60秒, 接続時)
    static uint32_t lastData = 0;
    if (ble_connected() && millis() - lastData > 60000) {
        lastData = millis();
        ble_send_watchdata();
    }

    delay(5);
}
