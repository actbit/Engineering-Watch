#include "settings.h"
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include "screen_mgr.h"
#include "watchface.h"
#include "ble_server.h"
#include "wifi_mgr.h"
#include <LilyGoLib.h>
#include <lvgl.h>

static lv_obj_t* settingsScreen = nullptr;

// ---- UI 要素 ----
static lv_obj_t* brightnessSlider = nullptr;
static lv_obj_t* timeoutLabel = nullptr;
static lv_obj_t* tiltSwitch = nullptr;
static lv_obj_t* wifiLabel = nullptr;
static lv_obj_t* bleLabel = nullptr;
static lv_obj_t* batteryLabel = nullptr;
static lv_obj_t* versionLabel = nullptr;

// ---- 戻るボタン ----
static void back_cb(lv_event_t*) {
    settings_close();
    watchface_back_to_face();
}

// ---- 明るさスライダー ----
static void brightness_cb(lv_event_t* e) {
    lv_obj_t* slider = lv_event_get_target(e);
    int val = (int)lv_slider_get_value(slider);
    app.brightness = (uint8_t)val;
    watch.setBrightness(app.brightness);
    nvs_save_i32(NVS_BRIGHTNESS, app.brightness);
}

// ---- 画面タイムアウト (サイクル: 0→5→10→15→30→60→常時) ----
static void timeout_cb(lv_event_t*) {
    static const int vals[] = {0, 5, 10, 15, 30, 60};
    static const char* labels[] = {"常時ON", "5秒", "10秒", "15秒", "30秒", "60秒"};
    int idx = 0;
    for (int i = 0; i < 6; i++) {
        if (app.screenTimeoutS == vals[i]) { idx = i; break; }
    }
    idx = (idx + 1) % 6;
    app.screenTimeoutS = vals[idx];
    nvs_save_i32(NVS_SCREEN_TIMEOUT, app.screenTimeoutS);
    if (timeoutLabel) lv_label_set_text(timeoutLabel, labels[idx]);
}

// ---- 傾き復帰トグル ----
static void tilt_cb(lv_event_t* e) {
    app.tiltWake = lv_obj_has_state(lv_event_get_target(e), LV_STATE_CHECKED);
    nvs_save_i32(NVS_TILT_WAKE, app.tiltWake ? 1 : 0);
    watch.enableFeature(SensorBMA423::FEATURE_TILT | SensorBMA423::FEATURE_WAKEUP, app.tiltWake);
    watch.configreFeatureInterrupt(
        SensorBMA423::INT_STEP_CNTR |
        (app.tiltWake ? (SensorBMA423::INT_TILT | SensorBMA423::INT_WAKEUP) : 0), true);
}

// ---- ラベル行を作成するヘルパー ----
static lv_obj_t* add_row(lv_obj_t* parent, const char* title, const char* value, lv_coord_t y) {
    lv_obj_t* cont = lv_obj_create(parent);
    lv_obj_set_size(cont, WATCH_W - 16, 36);
    lv_obj_set_pos(cont, 8, y);
    lv_obj_set_style_bg_opa(cont, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(cont, 0, 0);
    lv_obj_set_style_pad_all(cont, 0, 0);

    lv_obj_t* lbl = lv_label_create(cont);
    lv_label_set_text(lbl, title);
    lv_obj_set_style_text_font(lbl, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(lbl, lv_color_make(0xA0, 0xB0, 0xC0), 0);
    lv_obj_align(lbl, LV_ALIGN_LEFT_MID, 0, 0);

    lv_obj_t* val = lv_label_create(cont);
    lv_label_set_text(val, value);
    lv_obj_set_style_text_font(val, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(val, lv_color_white(), 0);
    lv_obj_align(val, LV_ALIGN_RIGHT_MID, 0, 0);

    return val;
}

// ---- 設定画面構築 ----
static void build_ui() {
    settingsScreen = lv_obj_create(NULL);
    lv_obj_set_size(settingsScreen, WATCH_W, WATCH_H);
    lv_obj_clear_flag(settingsScreen, LV_OBJ_FLAG_SCROLLABLE);

    // 背景
    lv_obj_t* bg = lv_obj_create(settingsScreen);
    lv_obj_set_size(bg, WATCH_W, WATCH_H);
    lv_obj_set_pos(bg, 0, 0);
    lv_obj_set_style_bg_color(bg, lv_color_make(0x10, 0x14, 0x18), 0);
    lv_obj_set_style_bg_opa(bg, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(bg, 0, 0);
    lv_obj_clear_flag(bg, LV_OBJ_FLAG_SCROLLABLE);

    // ヘッダー
    lv_obj_t* hdr = lv_obj_create(bg);
    lv_obj_set_size(hdr, WATCH_W, 40);
    lv_obj_set_pos(hdr, 0, 0);
    lv_obj_set_style_bg_color(hdr, lv_color_make(0x1A, 0x20, 0x26), 0);
    lv_obj_set_style_bg_opa(hdr, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(hdr, 0, 0);
    lv_obj_set_style_pad_all(hdr, 0, 0);

    lv_obj_t* title = lv_label_create(hdr);
    lv_label_set_text(title, "Settings");
    lv_obj_set_style_text_font(title, &lv_font_montserrat_18, 0);
    lv_obj_set_style_text_color(title, lv_color_white(), 0);
    lv_obj_align(title, LV_ALIGN_LEFT_MID, 12, 0);

    lv_obj_t* backBtn = lv_btn_create(hdr);
    lv_obj_set_size(backBtn, 56, 30);
    lv_obj_align(backBtn, LV_ALIGN_RIGHT_MID, -8, 0);
    lv_obj_add_event_cb(backBtn, back_cb, LV_EVENT_CLICKED, nullptr);
    lv_obj_t* bLabel = lv_label_create(backBtn);
    lv_label_set_text(bLabel, "BACK");
    lv_obj_set_style_text_font(bLabel, &lv_font_montserrat_12, 0);
    lv_obj_center(bLabel);

    // コンテンツ (スクロール可能)
    lv_obj_t* content = lv_obj_create(bg);
    lv_obj_set_size(content, WATCH_W, WATCH_H - 40);
    lv_obj_set_pos(content, 0, 42);
    lv_obj_set_style_bg_opa(content, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(content, 0, 0);
    lv_obj_set_flex_flow(content, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(content, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_row(content, 4, 0);
    lv_obj_set_style_pad_all(content, 8, 0);

    // ---- 明るさ ----
    lv_obj_t* brTitle = lv_label_create(content);
    lv_label_set_text(brTitle, "Brightness");
    lv_obj_set_style_text_font(brTitle, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(brTitle, lv_color_make(0x4C, 0xD9, 0x64), 0);
    lv_obj_set_width(brTitle, WATCH_W - 16);

    brightnessSlider = lv_slider_create(content);
    lv_obj_set_width(brightnessSlider, WATCH_W - 32);
    lv_slider_set_range(brightnessSlider, 5, 255);
    lv_slider_set_value(brightnessSlider, app.brightness, LV_ANIM_OFF);
    lv_obj_add_event_cb(brightnessSlider, brightness_cb, LV_EVENT_VALUE_CHANGED, nullptr);

    // ---- 画面タイムアウト ----
    lv_obj_t* toRow = lv_obj_create(content);
    lv_obj_set_size(toRow, WATCH_W - 16, 40);
    lv_obj_set_style_bg_opa(toRow, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(toRow, 0, 0);
    lv_obj_set_style_pad_all(toRow, 0, 0);
    lv_obj_add_flag(toRow, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_add_event_cb(toRow, timeout_cb, LV_EVENT_CLICKED, nullptr);

    lv_obj_t* toTitle = lv_label_create(toRow);
    lv_label_set_text(toTitle, "Screen Timeout");
    lv_obj_set_style_text_font(toTitle, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(toTitle, lv_color_make(0xA0, 0xB0, 0xC0), 0);
    lv_obj_align(toTitle, LV_ALIGN_LEFT_MID, 0, 0);

    static const char* timeoutStrs[] = {"常時ON", "5秒", "10秒", "15秒", "30秒", "60秒"};
    static const int timeoutVals[] = {0, 5, 10, 15, 30, 60};
    const char* toVal = "15秒";
    for (int i = 0; i < 6; i++) {
        if (app.screenTimeoutS == timeoutVals[i]) { toVal = timeoutStrs[i]; break; }
    }
    timeoutLabel = lv_label_create(toRow);
    lv_label_set_text(timeoutLabel, toVal);
    lv_obj_set_style_text_font(timeoutLabel, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(timeoutLabel, lv_color_white(), 0);
    lv_obj_align(timeoutLabel, LV_ALIGN_RIGHT_MID, 0, 0);

    // ---- 傾き復帰トグル ----
    lv_obj_t* tiltRow = lv_obj_create(content);
    lv_obj_set_size(tiltRow, WATCH_W - 16, 40);
    lv_obj_set_style_bg_opa(tiltRow, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(tiltRow, 0, 0);
    lv_obj_set_style_pad_all(tiltRow, 0, 0);

    lv_obj_t* tiltTitle = lv_label_create(tiltRow);
    lv_label_set_text(tiltTitle, "Tilt to Wake");
    lv_obj_set_style_text_font(tiltTitle, &lv_font_montserrat_14, 0);
    lv_obj_set_style_text_color(tiltTitle, lv_color_make(0xA0, 0xB0, 0xC0), 0);
    lv_obj_align(tiltTitle, LV_ALIGN_LEFT_MID, 0, 0);

    tiltSwitch = lv_switch_create(tiltRow);
    lv_obj_align(tiltSwitch, LV_ALIGN_RIGHT_MID, 0, 0);
    if (app.tiltWake) lv_obj_add_state(tiltSwitch, LV_STATE_CHECKED);
    lv_obj_add_event_cb(tiltSwitch, tilt_cb, LV_EVENT_VALUE_CHANGED, nullptr);

    // ---- 区切り線 ----
    lv_obj_t* sep = lv_obj_create(content);
    lv_obj_set_size(sep, WATCH_W - 32, 1);
    lv_obj_set_style_bg_color(sep, lv_color_make(0x2A, 0x36, 0x42), 0);
    lv_obj_set_style_bg_opa(sep, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(sep, 0, 0);

    // ---- ステータス情報 ----
    const char* ws = app.wifiState == 0 ? "OFF" : (app.wifiState == 1 ? "Connecting" : "ON");
    wifiLabel = add_row(content, "WiFi", ws, LV_SIZE_CONTENT);

    bleLabel = add_row(content, "BLE", app.bleConnected ? "Connected" : "Advertising", LV_SIZE_CONTENT);

    char batStr[16];
    snprintf(batStr, sizeof(batStr), "%d%%%s", app.battery < 0 ? 0 : app.battery,
             app.charging ? " +" : "");
    batteryLabel = add_row(content, "Battery", batStr, LV_SIZE_CONTENT);

    versionLabel = add_row(content, "Version", FW_VERSION, LV_SIZE_CONTENT);
}

void settings_open() {
    if (settingsScreen) {
        // 既存画面を再構築 (状態が変わっているため)
        lv_obj_del(settingsScreen);
        settingsScreen = nullptr;
    }
    build_ui();
    lv_scr_load(settingsScreen);
}

void settings_close() {
    if (settingsScreen) {
        lv_obj_del(settingsScreen);
        settingsScreen = nullptr;
    }
}

bool settings_is_open() {
    return settingsScreen != nullptr && lv_obj_is_valid(settingsScreen);
}

// ---- 定期更新 (main loop から呼ぶ) ----
void settings_poll() {
    if (!settings_is_open()) return;

    // WiFi 状態更新
    if (wifiLabel) {
        const char* ws = app.wifiState == 0 ? "OFF" : (app.wifiState == 1 ? "Connecting" : "ON");
        lv_label_set_text(wifiLabel, ws);
    }
    // BLE 状態更新
    if (bleLabel) {
        lv_label_set_text(bleLabel, app.bleConnected ? "Connected" : "Advertising");
    }
    // バッテリー更新
    if (batteryLabel) {
        char batStr[16];
        snprintf(batStr, sizeof(batStr), "%d%%%s", app.battery < 0 ? 0 : app.battery,
                 app.charging ? " +" : "");
        lv_label_set_text(batteryLabel, batStr);
    }
}
