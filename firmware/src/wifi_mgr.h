#pragma once
#include <Arduino.h>

// オンデマンドWiFiマネージャー
// Android からの wifi_on コマンドでのみ接続し、
// 無通信タイムアウト (WIFI_AUTO_OFF_MS) で自動切断する (バッテリー保護)。

void wifi_mgr_init();
void wifi_mgr_connect(const String& ssid, const String& pass); // pass空=保存済み
void wifi_mgr_disconnect();
void wifi_mgr_poll();
void wifi_mgr_activity();   // 通信アクティビティ (自動OFFタイマー延長)
