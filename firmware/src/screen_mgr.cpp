#include "screen_mgr.h"
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include <LilyGoLib.h>
#include <lvgl.h>

static uint32_t lastActivity = 0;
static uint32_t lastTouchPoll = 0;
static bool bmaWakeIrq = false;   // BMA 割り込みハンドラから立てるフラグ

// BMA423 割り込み (傾き/ダブルタップ) で画面ON
void screen_mgr_bma_wake() { bmaWakeIrq = true; }

void screen_mgr_init() {
    app.screenTimeoutS = nvs_load_i32(NVS_SCREEN_TIMEOUT, 15);
    app.tiltWake = nvs_load_i32(NVS_TILT_WAKE, 1) != 0;
    app.screenOn = true;
    lastActivity = millis();

    // 傾き/ダブルタップの割り込みを INT1 にマップする
    // (configreFeatureInterrupt = interruptMap。センサー本体の初期化は
    //  main.cpp で enableAccelerometer 等を実施済み)
    watch.configreFeatureInterrupt(
        SensorBMA423::INT_STEP_CNTR |
        (app.tiltWake ? (SensorBMA423::INT_TILT | SensorBMA423::INT_WAKEUP) : 0), true);
    Serial.printf("[screen] tilt wake %s, timeout %ds\n",
                  app.tiltWake ? "on" : "off", app.screenTimeoutS);
}

void screen_on() {
    if (!app.screenOn) {
        app.screenOn = true;
        watch.writecommand(0x11);   // ST7789 sleep out
        watch.setBrightness(app.brightness);
        lv_disp_trig_activity(nullptr);
        Serial.println("[screen] on");
    }
    lastActivity = millis();
}

void screen_off() {
    if (app.screenOn) {
        app.screenOn = false;
        watch.writecommand(0x10);   // ST7789 sleep in
        watch.setBrightness(0);
        Serial.println("[screen] off");
    }
}

void screen_activity() {
    lastActivity = millis();
    if (!app.screenOn) screen_on();
}

bool screen_is_on() { return app.screenOn; }

void screen_mgr_poll() {
    uint32_t now = millis();

    // BMA の傾き/ダブルタップ割り込み → 画面ON
    if (bmaWakeIrq) {
        bmaWakeIrq = false;
        if (!app.screenOn) screen_on();
    }

    if (app.screenOn) {
        // タイムアウトで消灯
        if (app.screenTimeoutS > 0 &&
            now - lastActivity > (uint32_t)app.screenTimeoutS * 1000) {
            screen_off();
        }
    } else {
        // 消灯中: タッチでON (30ms間隔でポーリング)
        if (now - lastTouchPoll > 30) {
            lastTouchPoll = now;
            if (watch.getTouched()) screen_on();
        }
    }
}
