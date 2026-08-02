#pragma once
#include <lvgl.h>
#include <Arduino.h>

// 文字盤 (背景画像 + 動的パーツ) モジュール
//
// 背景・針画像は Android 側でレンダリングされた PNG を FFat から読み込み、
// lodepng でデコードして LVGL 画像として表示する。時計側で描画するのは
// 時刻・日付・バッテリー・歩数などの動的テキスト/アイコンのみ。

void watchface_init();           // 初期化 (保存済み文字盤 or デフォルト)
void watchface_apply_face();     // /face/* から再読込して適用 (BLE受信後)
void watchface_show();           // 文字盤画面へ切替
void watchface_back_to_face();   // 他の画面から文字盤へ戻る

lv_obj_t* watchface_get_screen();

// 動的パーツの値更新 (他モジュールから呼ばれる)
void watchface_set_steps(uint32_t steps);
void watchface_set_battery(int pct, bool charging);
void watchface_set_conn(bool ble, int wifiState);
void watchface_set_unread(int n);
void watchface_tick();           // 1秒毎の更新 (lv_timer から)
