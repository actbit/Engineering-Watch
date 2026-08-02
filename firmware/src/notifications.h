#pragma once
#include <Arduino.h>
#include <lvgl.h>

// 通知 (Android から BLE 経由で受信)

struct WatchNotif {
    uint32_t id = 0;
    uint32_t when = 0;
    String app;
    String title;
    String text;
};

#define NOTIF_MAX 20

void notifications_init();
void notifications_add(const String& app, const String& title,
                       const String& text, uint32_t id, uint32_t when);
void notifications_remove_at(int index);
void notifications_clear_all();
int  notifications_count();
int  notifications_unread();

void notifications_attach_to_face();  // 文字盤再構築後: バナー状態をリセット
void notifications_open_list();       // 通知リスト画面へ
void notifications_poll();            // バナー自動消去 (loop から毎回呼ぶ)
