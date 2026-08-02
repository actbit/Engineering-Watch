#pragma once
#include <Arduino.h>

// 画面管理 (省電力)
//  - 無操作タイムアウトでディスプレイ消灯 (ST7789 sleep + バックライトOFF)
//  - 傾き/ダブルタップ (BMA423) またはタッチで画面ON
//  - BLE経由で設定変更可能 (screen_timeout / tilt_wake / wake)

void screen_mgr_init();
void screen_mgr_poll();     // loop から毎回呼ぶ
void screen_on();           // 画面を点灯 (最後の操作時刻も更新)
void screen_off();
void screen_activity();     // 操作があったことを通知 (タイマー延長)
bool screen_is_on();
