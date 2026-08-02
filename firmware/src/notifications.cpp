#include "notifications.h"
#include "config.h"
#include "watchface.h"

static WatchNotif notifList[NOTIF_MAX];
static int notifCount = 0;
static int notifUnread = 0;

static lv_obj_t* banner = nullptr;
static uint32_t bannerUntil = 0;
static lv_obj_t* listScreen = nullptr;

// ---- リスト =================================================

static void list_rebuild();

static void list_back_cb(lv_event_t*) {
    lv_obj_del(listScreen);
    listScreen = nullptr;
    watchface_back_to_face();
}

static void list_clear_cb(lv_event_t*) {
    notifications_clear_all();
    if (listScreen) list_rebuild();
}

static void row_click_cb(lv_event_t* e) {
    int idx = (int)(intptr_t)lv_event_get_user_data(e);
    notifications_remove_at(idx);
    if (listScreen) list_rebuild();
}

static void list_rebuild() {
    if (!lv_obj_is_valid(listScreen)) return;
    lv_obj_clean(listScreen);
    lv_obj_t* cont = lv_obj_create(listScreen);
    lv_obj_set_size(cont, WATCH_W, WATCH_H);
    lv_obj_set_pos(cont, 0, 0);
    lv_obj_set_style_bg_opa(cont, LV_OPA_COVER, 0);
    lv_obj_set_style_bg_color(cont, lv_color_make(0x10, 0x14, 0x18), 0);
    lv_obj_set_style_border_width(cont, 0, 0);
    lv_obj_clear_flag(cont, LV_OBJ_FLAG_SCROLLABLE);
    lv_obj_set_style_pad_all(cont, 0, 0);

    // ヘッダー
    lv_obj_t* hdr = lv_obj_create(cont);
    lv_obj_set_size(hdr, WATCH_W, 40);
    lv_obj_set_pos(hdr, 0, 0);
    lv_obj_set_style_bg_color(hdr, lv_color_make(0x1A, 0x20, 0x26), 0);
    lv_obj_set_style_bg_opa(hdr, LV_OPA_COVER, 0);
    lv_obj_set_style_border_width(hdr, 0, 0);
    lv_obj_set_style_pad_all(hdr, 0, 0);

    lv_obj_t* title = lv_label_create(hdr);
    lv_label_set_text(title, "Notifications");
    lv_obj_set_style_text_font(title, &lv_font_montserrat_18, 0);
    lv_obj_set_style_text_color(title, lv_color_white(), 0);
    lv_obj_align(title, LV_ALIGN_LEFT_MID, 12, 0);

    lv_obj_t* backBtn = lv_btn_create(hdr);
    lv_obj_set_size(backBtn, 56, 30);
    lv_obj_align(backBtn, LV_ALIGN_RIGHT_MID, -8, 0);
    lv_obj_add_event_cb(backBtn, list_back_cb, LV_EVENT_CLICKED, nullptr);
    lv_obj_t* bLabel = lv_label_create(backBtn);
    lv_label_set_text(bLabel, "BACK");
    lv_obj_set_style_text_font(bLabel, &lv_font_montserrat_12, 0);
    lv_obj_center(bLabel);

    // リスト本体
    lv_obj_t* list = lv_obj_create(cont);
    lv_obj_set_size(list, WATCH_W, WATCH_H - 44);
    lv_obj_set_pos(list, 0, 42);
    lv_obj_set_style_bg_opa(list, LV_OPA_TRANSP, 0);
    lv_obj_set_style_border_width(list, 0, 0);
    lv_obj_set_flex_flow(list, LV_FLEX_FLOW_COLUMN);
    lv_obj_set_flex_align(list, LV_FLEX_ALIGN_START, LV_FLEX_ALIGN_CENTER, LV_FLEX_ALIGN_CENTER);
    lv_obj_set_style_pad_row(list, 6, 0);

    if (notifCount == 0) {
        lv_obj_t* empty = lv_label_create(list);
        lv_label_set_text(empty, "No notifications");
        lv_obj_set_style_text_color(empty, lv_color_make(0x55, 0x66, 0x77), 0);
        return;
    }

    for (int i = 0; i < notifCount; i++) {
        const WatchNotif& n = notifList[i];
        lv_obj_t* row = lv_obj_create(list);
        lv_obj_set_width(row, WATCH_W - 16);
        lv_obj_set_height(row, LV_SIZE_CONTENT);
        lv_obj_set_style_bg_color(row, lv_color_make(0x1E, 0x26, 0x2E), 0);
        lv_obj_set_style_bg_opa(row, LV_OPA_COVER, 0);
        lv_obj_set_style_border_width(row, 0, 0);
        lv_obj_set_style_radius(row, 6, 0);
        lv_obj_set_style_pad_all(row, 8, 0);
        lv_obj_add_flag(row, LV_OBJ_FLAG_CLICKABLE);
        lv_obj_add_event_cb(row, row_click_cb, LV_EVENT_CLICKED,
                            (void*)(intptr_t)i);

        lv_obj_t* appLbl = lv_label_create(row);
        lv_label_set_text(appLbl, n.app.c_str());
        lv_obj_set_style_text_font(appLbl, &lv_font_montserrat_12, 0);
        lv_obj_set_style_text_color(appLbl, lv_color_make(0x4C, 0xD9, 0x64), 0);

        if (n.title.length() > 0) {
            lv_obj_t* tLbl = lv_label_create(row);
            lv_label_set_text(tLbl, n.title.c_str());
            lv_obj_set_style_text_font(tLbl, &lv_font_montserrat_14, 0);
            lv_obj_set_style_text_color(tLbl, lv_color_white(), 0);
        }
        if (n.text.length() > 0) {
            lv_obj_t* xLbl = lv_label_create(row);
            lv_label_set_text(xLbl, n.text.c_str());
            lv_obj_set_style_text_font(xLbl, &lv_font_montserrat_12, 0);
            lv_obj_set_style_text_color(xLbl, lv_color_make(0xA0, 0xB0, 0xC0), 0);
        }
    }
}

void notifications_open_list() {
    notifUnread = 0;
    if (listScreen) { list_rebuild(); return; }
    if (banner) { lv_obj_del(banner); banner = nullptr; }
    listScreen = lv_obj_create(NULL);
    lv_obj_set_size(listScreen, WATCH_W, WATCH_H);
    lv_obj_clear_flag(listScreen, LV_OBJ_FLAG_SCROLLABLE);
    list_rebuild();
    lv_scr_load(listScreen);
}

// ---- バナー =================================================

static void banner_click_cb(lv_event_t*) {
    if (banner) { lv_obj_del(banner); banner = nullptr; }
    notifications_open_list();
}

static void banner_show(const WatchNotif& n) {
    if (!lv_obj_is_valid(watchface_get_screen())) return;
    if (banner) { lv_obj_del(banner); banner = nullptr; }
    lv_obj_t* b = lv_obj_create(watchface_get_screen());
    lv_obj_set_size(b, WATCH_W - 12, 74);
    lv_obj_set_pos(b, 6, -80);
    lv_obj_set_style_bg_color(b, lv_color_make(0x10, 0x14, 0x18), 0);
    lv_obj_set_style_bg_opa(b, LV_OPA_COVER, 0);
    lv_obj_set_style_radius(b, 8, 0);
    lv_obj_set_style_border_color(b, lv_color_make(0x2A, 0x36, 0x42), 0);
    lv_obj_set_style_border_width(b, 1, 0);
    lv_obj_set_style_shadow_opa(b, LV_OPA_50, 0);
    lv_obj_set_style_pad_all(b, 8, 0);
    lv_obj_add_flag(b, LV_OBJ_FLAG_CLICKABLE);
    lv_obj_add_event_cb(b, banner_click_cb, LV_EVENT_CLICKED, nullptr);

    lv_obj_t* appLbl = lv_label_create(b);
    lv_label_set_text(appLbl, n.app.c_str());
    lv_obj_set_style_text_font(appLbl, &lv_font_montserrat_12, 0);
    lv_obj_set_style_text_color(appLbl, lv_color_make(0x4C, 0xD9, 0x64), 0);
    lv_obj_set_pos(appLbl, 4, 2);

    if (n.title.length() > 0) {
        lv_obj_t* tLbl = lv_label_create(b);
        lv_label_set_text(tLbl, n.title.c_str());
        lv_obj_set_style_text_font(tLbl, &lv_font_montserrat_14, 0);
        lv_obj_set_style_text_color(tLbl, lv_color_white(), 0);
        lv_obj_set_pos(tLbl, 4, 20);
    }
    if (n.text.length() > 0) {
        lv_obj_t* xLbl = lv_label_create(b);
        lv_label_set_text(xLbl, n.text.c_str());
        lv_obj_set_style_text_font(xLbl, &lv_font_montserrat_12, 0);
        lv_obj_set_style_text_color(xLbl, lv_color_make(0xA0, 0xB0, 0xC0), 0);
        lv_obj_set_pos(xLbl, 4, 44);
    }
    // スライドイン
    lv_anim_t a;
    lv_anim_init(&a);
    lv_anim_set_var(&a, b);
    lv_anim_set_values(&a, -80, 6);
    lv_anim_set_time(&a, 250);
    lv_anim_set_exec_cb(&a, (lv_anim_exec_xcb_t)lv_obj_set_y);
    lv_anim_set_path_cb(&a, lv_anim_path_ease_out);
    lv_anim_start(&a);
    banner = b;
    bannerUntil = millis() + 3000;
}

// ---- 公開 API ===============================================

void notifications_init() {
    notifCount = 0;
    notifUnread = 0;
}

void notifications_add(const String& app, const String& title,
                       const String& text, uint32_t id, uint32_t when) {
    // 同IDは置換
    for (int i = 0; i < notifCount; i++) {
        if (notifList[i].id == id && id != 0) {
            notifList[i].app = app;
            notifList[i].title = title;
            notifList[i].text = text;
            notifList[i].when = when;
            return;
        }
    }
    // 先頭に挿入
    if (notifCount < NOTIF_MAX) {
        for (int i = notifCount; i > 0; i--) notifList[i] = notifList[i - 1];
        notifCount++;
    } else {
        for (int i = NOTIF_MAX - 1; i > 0; i--) notifList[i] = notifList[i - 1];
    }
    notifList[0].id = id;
    notifList[0].when = when;
    notifList[0].app = app;
    notifList[0].title = title;
    notifList[0].text = text;
    notifUnread++;
    if (notifUnread > NOTIF_MAX) notifUnread = NOTIF_MAX;
    // リスト画面が開いていれば再構築、そうでなければバナー
    if (listScreen && lv_obj_is_valid(listScreen)) {
        list_rebuild();
    } else {
        banner_show(notifList[0]);
    }
}

void notifications_remove_at(int index) {
    if (index < 0 || index >= notifCount) return;
    if (index == 0 && notifUnread > 0) notifUnread--;
    for (int i = index; i < notifCount - 1; i++) notifList[i] = notifList[i + 1];
    notifCount--;
    if (notifCount == 0) notifUnread = 0;
}

void notifications_clear_all() {
    notifCount = 0;
    notifUnread = 0;
}

int notifications_count() { return notifCount; }
int notifications_unread() { return notifUnread; }

void notifications_attach_to_face() {
    // 文字盤が再構築されるとバナーは削除される
    banner = nullptr;
    bannerUntil = 0;
}

void notifications_poll() {
    if (banner && lv_obj_is_valid(banner) && millis() > bannerUntil) {
        lv_obj_del(banner);
        banner = nullptr;
    }
}
