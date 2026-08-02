#pragma once
#include <Arduino.h>

// BLE GATT サーバー (docs/PROTOCOL.md)
//  - FF01 WatchFaceConfig : 文字盤パッケージ転送 (チャンク)
//  - FF02 Control         : コマンド (JSON)
//  - FF03 Status          : 状態通知 (Notify)
//  - FF04 WatchData       : 歩数/バッテリー (Notify)
//  - FF11 Notification    : 通知転送 (JSON)

void ble_init();
void ble_poll();          // 通知キュー送出 + コマンド処理 (loop から毎回)
bool ble_connected();
void ble_notify_status(const char* json);
void ble_send_watchdata();
void ble_send_state();
