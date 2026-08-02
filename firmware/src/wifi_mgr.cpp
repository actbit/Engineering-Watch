#include "wifi_mgr.h"
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include "ble_server.h"
#include <WiFi.h>

static String savedSsid = "";
static String savedPass = "";
static uint32_t connStart = 0;
static uint32_t lastActivity = 0;

static void set_state(int st, const char* extra = nullptr) {
    if (app.wifiState == st) return;
    app.wifiState = st;
    String j = String("{\"type\":\"wifi\",\"state\":\"");
    switch (st) {
    case 0: j += "off"; break;
    case 1: j += "connecting"; break;
    case 2: j += "connected"; break;
    }
    j += "\"";
    if (extra) j += String(",") + extra;
    j += "}";
    ble_notify_status(j.c_str());
    Serial.printf("[wifi] state=%d\n", st);
}

void wifi_mgr_init() {
    savedSsid = nvs_load_string(NVS_WIFI_SSID, "");
    savedPass = nvs_load_string(NVS_WIFI_PASS, "");
    if (savedSsid.length() > 0) {
        Serial.printf("[wifi] saved AP: %s\n", savedSsid.c_str());
    }
    WiFi.mode(WIFI_OFF);
    app.wifiState = 0;
}

void wifi_mgr_connect(const String& ssid, const String& pass) {
    if (app.wifiState != 0) return;   // 既に接続/接続中
    String s = ssid;
    String p = pass;
    if (s.length() == 0) { s = savedSsid; p = savedPass; }
    if (p.length() == 0) p = savedPass;   // パスワード省略時は保存済みを使用
    if (s.length() == 0) {
        ble_notify_status("{\"type\":\"wifi\",\"state\":\"error\",\"msg\":\"no_ssid\"}");
        return;
    }
    nvs_save_string(NVS_WIFI_SSID, s);
    nvs_save_string(NVS_WIFI_PASS, p);
    savedSsid = s;
    savedPass = p;
    WiFi.mode(WIFI_STA);
    WiFi.disconnect();
    WiFi.begin(s.c_str(), p.c_str());
    connStart = millis();
    set_state(1);
    Serial.printf("[wifi] connecting to %s\n", s.c_str());
}

void wifi_mgr_disconnect() {
    if (app.wifiState == 0) return;
    WiFi.disconnect(true);
    WiFi.mode(WIFI_OFF);
    set_state(0);
}

void wifi_mgr_poll() {
    if (app.wifiState == 1) {
        if (WiFi.status() == WL_CONNECTED) {
            lastActivity = millis();
            char ip[24];
            snprintf(ip, sizeof(ip), "\"ip\":\"%s\"", WiFi.localIP().toString().c_str());
            set_state(2, ip);
        } else if (millis() - connStart > WIFI_CONNECT_TIMEOUT) {
            WiFi.disconnect(true);
            WiFi.mode(WIFI_OFF);
            app.wifiState = 0;
            ble_notify_status("{\"type\":\"wifi\",\"state\":\"error\",\"msg\":\"timeout\"}");
            Serial.println("[wifi] connect timeout");
        }
    } else if (app.wifiState == 2) {
        if (WiFi.status() != WL_CONNECTED) {
            WiFi.mode(WIFI_OFF);
            set_state(0);
        } else if (millis() - lastActivity > WIFI_AUTO_OFF_MS) {
            // 自動OFF
            wifi_mgr_disconnect();
            ble_notify_status("{\"type\":\"wifi\",\"state\":\"auto_off\"}");
        }
    }
}

void wifi_mgr_activity() {
    lastActivity = millis();
}
