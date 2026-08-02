#include "watchface.h"
#include "config.h"
#include "app_state.h"
#include "storage.h"
#include "notifications.h"
#include <ArduinoJson.h>

// LVGL 8.4 (LV_USE_PNG=1) が内蔵する lodepng のデコード関数を使用する
// (重複シンボル防止のため自前の lodepng は持たない)
extern "C" {
unsigned lodepng_decode32(unsigned char** out, unsigned* w, unsigned* h,
                          const unsigned char* in, size_t insize);
}

// ============================================================
// 動的パーツ種別
// ============================================================
enum PartType : uint8_t {
    PT_CLOCK_DIGITAL = 1,
    PT_DATE          = 2,
    PT_ANALOG        = 3,
    PT_BATTERY       = 4,
    PT_STEPS         = 5,
    PT_CONN_WIFI     = 6,
    PT_CONN_BLE      = 7,
    PT_NOTIF         = 8,
};

struct DynPart {
    uint8_t type = 0;
    int16_t x = 0, y = 0, w = 0, h = 0;
    int16_t cx = 0, cy = 0, r = 0;
    lv_color_t color = lv_color_white();
    lv_color_t dimColor = lv_color_make(0x33, 0x33, 0x33);
    uint8_t font = 16;
    bool showSeconds = false;
    bool showPct = false;
    bool alignCenter = false;
    bool alignRight = false;
    char format[16] = "";
    char label[32] = "";
    // LVGL widgets (analog: obj=時針 obj2=分針 obj3=秒針 / battery: obj=枠 obj2=充填 obj3=%)
    lv_obj_t* obj = nullptr;
    lv_obj_t* obj2 = nullptr;
    lv_obj_t* obj3 = nullptr;
};

// ============================================================
// 静的データ
// ============================================================
static lv_obj_t* faceScreen = nullptr;
static lv_obj_t* bgImg = nullptr;
static DynPart parts[MAX_DYNAMIC_PARTS];
static int partCount = 0;

// 背景・針画像 (PSRAMバッファ + LVGL dsc)
static uint8_t* bgPixels = nullptr;
static uint8_t* handPixels[3] = { nullptr, nullptr, nullptr }; // hour,min,sec
static lv_img_dsc_t bgDsc;
static lv_img_dsc_t handDsc[3];

// 状態アイコン (24x24, RGB565+alpha, ファームウェア内蔵)
static const int ICON_SZ = 24;
static uint8_t iconWifi[24 * 24 * 3];
static uint8_t iconBle[24 * 24 * 3];
static uint8_t iconBell[24 * 24 * 3];
static lv_img_dsc_t dscWifi, dscBle, dscBell;

// ============================================================
// ツール: 色・フォント
// ============================================================
static lv_color_t parse_color(const char* hex, lv_color_t def) {
    if (!hex || hex[0] != '#') return def;
    unsigned v = (unsigned)strtoul(hex + 1, nullptr, 16);
    uint8_t r = (v >> 16) & 0xFF, g = (v >> 8) & 0xFF, b = v & 0xFF;
    return lv_color_make(r, g, b);
}

static const lv_font_t* lvgl_font_for(uint8_t size) {
    static const struct { uint8_t sz; const lv_font_t* f; } map[] = {
        {12, &lv_font_montserrat_12}, {14, &lv_font_montserrat_14},
        {16, &lv_font_montserrat_16}, {18, &lv_font_montserrat_18},
        {20, &lv_font_montserrat_20}, {22, &lv_font_montserrat_22},
        {24, &lv_font_montserrat_24}, {28, &lv_font_montserrat_28},
        {32, &lv_font_montserrat_32}, {36, &lv_font_montserrat_36},
        {40, &lv_font_montserrat_40}, {44, &lv_font_montserrat_44},
        {48, &lv_font_montserrat_48},
    };
    const lv_font_t* best = &lv_font_montserrat_16;
    for (auto& e : map) {
        best = e.f;
        if (size <= e.sz) break;
    }
    return best;
}

static lv_obj_t* make_label(lv_obj_t* parent, const DynPart& p) {
    lv_obj_t* lb = lv_label_create(parent);
    lv_obj_set_pos(lb, p.x, p.y);
    lv_obj_set_style_text_font(lb, lvgl_font_for(p.font), 0);
    lv_obj_set_style_text_color(lb, p.color, 0);
    if (p.alignCenter || p.alignRight) {
        lv_obj_set_width(lb, p.w > 0 ? p.w : 120);
        lv_label_set_long_mode(lb, LV_LABEL_LONG_DOT);
        lv_obj_set_style_text_align(lb, p.alignCenter ? LV_TEXT_ALIGN_CENTER : LV_TEXT_ALIGN_RIGHT, 0);
    }
    return lb;
}

// ============================================================
// アイコン描画 (ソフトウェアラスタライズ)
// ============================================================
static void icon_clear(uint8_t* buf) { memset(buf, 0, ICON_SZ * ICON_SZ * 3); }

static void icon_px(uint8_t* buf, int x, int y) {
    if (x < 0 || y < 0 || x >= ICON_SZ || y >= ICON_SZ) return;
    int i = (y * ICON_SZ + x) * 3;
    buf[i] = 0xFF; buf[i + 1] = 0xFF; buf[i + 2] = 0xFF; // RGB565 white + alpha 255
}

static void icon_segment(uint8_t* buf, float x0, float y0, float x1, float y1, float t) {
    float dx = x1 - x0, dy = y1 - y0;
    float len2 = dx * dx + dy * dy;
    for (int y = 0; y < ICON_SZ; y++) {
        for (int x = 0; x < ICON_SZ; x++) {
            float px = x + 0.5f - x0, py = y + 0.5f - y0;
            float tt = len2 > 0 ? (px * dx + py * dy) / len2 : 0;
            tt = tt < 0 ? 0 : (tt > 1 ? 1 : tt);
            float d2 = (px - tt * dx) * (px - tt * dx) + (py - tt * dy) * (py - tt * dy);
            if (d2 <= t * t * 0.25f) icon_px(buf, x, y);
        }
    }
}

static void icon_ring(uint8_t* buf, float cx, float cy, float r, float t, float a0, float a1) {
    for (int y = 0; y < ICON_SZ; y++) {
        for (int x = 0; x < ICON_SZ; x++) {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            float d = sqrtf(dx * dx + dy * dy);
            if (fabsf(d - r) > t * 0.5f) continue;
            float a = atan2f(dy, dx) * 180.0f / M_PI;
            if (a < 0) a += 360.0f;
            bool in = (a0 <= a1) ? (a >= a0 && a <= a1) : (a >= a0 || a <= a1);
            if (in) icon_px(buf, x, y);
        }
    }
}

static void icon_dot(uint8_t* buf, float cx, float cy, float r) {
    for (int y = 0; y < ICON_SZ; y++)
        for (int x = 0; x < ICON_SZ; x++) {
            float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
            if (dx * dx + dy * dy <= r * r) icon_px(buf, x, y);
        }
}

static void icons_build() {
    icon_clear(iconWifi);
    icon_ring(iconWifi, 12, 21, 5, 2.2f, 200, 340);
    icon_ring(iconWifi, 12, 21, 10, 2.2f, 205, 335);
    icon_ring(iconWifi, 12, 21, 15, 2.4f, 210, 330);
    icon_dot(iconWifi, 12, 20.5f, 1.8f);

    icon_clear(iconBle);
    icon_segment(iconBle, 6, 3, 14, 9, 3);
    icon_segment(iconBle, 14, 9, 6, 15, 3);
    icon_segment(iconBle, 14, 3, 6, 9, 3);
    icon_segment(iconBle, 6, 9, 14, 15, 3);

    icon_clear(iconBell);
    icon_ring(iconBell, 12, 8, 6, 2.6f, 200, 340);
    icon_segment(iconBell, 5.5f, 15, 18.5f, 15, 2.6f);
    icon_dot(iconBell, 12, 18, 2.0f);
}

static void dsc_from_rgb565_alpha(lv_img_dsc_t& dsc, const uint8_t* buf, int w, int h, uint8_t cf) {
    memset(&dsc, 0, sizeof(dsc));
    dsc.header.always_zero = 0;
    dsc.header.w = w;
    dsc.header.h = h;
    dsc.header.cf = cf;
    dsc.data_size = (uint32_t)w * h * 3;
    dsc.data = buf;
}

// ============================================================
// PNG デコード (lodepng → PSRAM RGBA)
// ============================================================
static uint8_t* decode_png_rgba(const char* path, uint32_t& w, uint32_t& h) {
    uint8_t* fileData = nullptr;
    size_t fileLen = 0;
    if (!storage_read_file(path, fileData, fileLen)) return nullptr;
    uint8_t* out = nullptr;
    unsigned err = lodepng_decode32(&out, &w, &h, fileData, fileLen);
    free(fileData);
    if (err) return nullptr;
    uint8_t* ps = (uint8_t*)ps_malloc((size_t)w * h * 4);
    if (!ps) { free(out); return nullptr; }
    memcpy(ps, out, (size_t)w * h * 4);
    free(out);
    return ps;
}

// RGBA → RGB565 (背景)
static uint8_t* rgba_to_rgb565(const uint8_t* rgba, uint32_t w, uint32_t h) {
    uint8_t* out = (uint8_t*)ps_malloc((size_t)w * h * 2);
    if (!out) return nullptr;
    for (uint32_t i = 0; i < (uint32_t)w * h; i++) {
        uint8_t r = rgba[i * 4], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2];
        uint16_t c = (uint16_t)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        out[i * 2] = c & 0xFF;
        out[i * 2 + 1] = c >> 8;
    }
    return out;
}

// RGBA → RGB565+alpha (針・アイコン: LV_IMG_CF_TRUE_COLOR_ALPHA)
static uint8_t* rgba_to_rgb565a(const uint8_t* rgba, uint32_t w, uint32_t h) {
    uint8_t* out = (uint8_t*)ps_malloc((size_t)w * h * 3);
    if (!out) return nullptr;
    for (uint32_t i = 0; i < (uint32_t)w * h; i++) {
        uint8_t r = rgba[i * 4], g = rgba[i * 4 + 1], b = rgba[i * 4 + 2];
        uint16_t c = (uint16_t)(((r >> 3) << 11) | ((g >> 2) << 5) | (b >> 3));
        out[i * 3] = c & 0xFF;
        out[i * 3 + 1] = c >> 8;
        out[i * 3 + 2] = rgba[i * 4 + 3];
    }
    return out;
}

// ============================================================
// 背景・針ロード
// ============================================================
static void bg_free() {
    if (bgPixels) { free(bgPixels); bgPixels = nullptr; }
}

static bool bg_load(const char* path) {
    bg_free();
    uint32_t w = 0, h = 0;
    uint8_t* rgba = decode_png_rgba(path, w, h);
    if (!rgba) return false;
    bgPixels = rgba_to_rgb565(rgba, w, h);
    free(rgba);
    if (!bgPixels) return false;
    memset(&bgDsc, 0, sizeof(bgDsc));
    bgDsc.header.always_zero = 0;
    bgDsc.header.w = w;
    bgDsc.header.h = h;
    bgDsc.header.cf = LV_IMG_CF_TRUE_COLOR;
    bgDsc.data_size = w * h * 2;
    bgDsc.data = bgPixels;
    return true;
}

static void hands_free() {
    for (int i = 0; i < 3; i++) {
        if (handPixels[i]) { free(handPixels[i]); handPixels[i] = nullptr; }
    }
}

static bool hand_load(int idx, const char* path) {
    if (handPixels[idx]) { free(handPixels[idx]); handPixels[idx] = nullptr; }
    uint32_t w = 0, h = 0;
    uint8_t* rgba = decode_png_rgba(path, w, h);
    if (!rgba) return false;
    handPixels[idx] = rgba_to_rgb565a(rgba, w, h);
    free(rgba);
    if (!handPixels[idx]) return false;
    memset(&handDsc[idx], 0, sizeof(handDsc[idx]));
    handDsc[idx].header.always_zero = 0;
    handDsc[idx].header.w = w;
    handDsc[idx].header.h = h;
    handDsc[idx].header.cf = LV_IMG_CF_TRUE_COLOR_ALPHA;
    handDsc[idx].data_size = w * h * 3;
    handDsc[idx].data = handPixels[idx];
    return true;
}

// ============================================================
// ローカル時刻
// ============================================================
static struct tm local_tm() {
    time_t now = time(nullptr);
    struct tm t;
    gmtime_r(&now, &t);
    return t;
}

// ============================================================
// 動的パーツ生成
// ============================================================
static lv_obj_t* create_part_image(lv_img_dsc_t* dsc, const DynPart& p) {
    lv_obj_t* img = lv_img_create(faceScreen);
    lv_img_set_src(img, dsc);
    lv_obj_set_pos(img, p.x, p.y);
    lv_obj_set_style_img_recolor(img, p.color, 0);
    lv_obj_set_style_img_recolor_opa(img, LV_OPA_COVER, 0);
    // zoom (256=100%)
    if (p.w > 0 && dsc->header.w > 0) {
        int zoom = (int)p.w * 256 / (int)dsc->header.w;
        if (zoom < 64) zoom = 64;
        if (zoom > 1024) zoom = 1024;
        lv_img_set_zoom(img, (uint16_t)zoom);
    }
    return img;
}

static void create_part(DynPart& p) {
    switch (p.type) {
    case PT_CLOCK_DIGITAL:
    case PT_DATE:
    case PT_STEPS:
        p.obj = make_label(faceScreen, p);
        break;

    case PT_ANALOG: {
        // 針 (hour=0, min=1, sec=2)
        for (int i = 0; i < 3; i++) {
            if (!handPixels[i]) continue;
            if (i == 2 && !p.showSeconds) continue;
            lv_obj_t* img = lv_img_create(faceScreen);
            lv_img_set_src(img, &handDsc[i]);
            int iw = (int)handDsc[i].header.w, ih = (int)handDsc[i].header.h;
            lv_obj_set_pos(img, p.cx - iw / 2, p.cy - ih / 2);
            lv_img_set_angle(img, 0);
            if (i == 0) p.obj = img;
            else if (i == 1) p.obj2 = img;
            else p.obj3 = img;
        }
        // 中心軸 (固定8px)
        lv_obj_t* axis = lv_obj_create(faceScreen);
        lv_obj_set_size(axis, 8, 8);
        lv_obj_set_pos(axis, p.cx - 4, p.cy - 4);
        lv_obj_set_style_bg_color(axis, p.color, 0);
        lv_obj_set_style_bg_opa(axis, LV_OPA_COVER, 0);
        lv_obj_set_style_radius(axis, LV_RADIUS_CIRCLE, 0);
        lv_obj_set_style_border_width(axis, 0, 0);
        break;
    }

    case PT_BATTERY: {
        int bw = p.w > 0 ? p.w : 48;
        int bh = p.h > 0 ? p.h : 16;
        // 外枠
        lv_obj_t* frame = lv_obj_create(faceScreen);
        lv_obj_set_size(frame, bw, bh);
        lv_obj_set_pos(frame, p.x, p.y);
        lv_obj_set_style_border_color(frame, p.color, 0);
        lv_obj_set_style_border_width(frame, 1, 0);
        lv_obj_set_style_border_opa(frame, LV_OPA_COVER, 0);
        lv_obj_set_style_bg_opa(frame, LV_OPA_TRANSP, 0);
        lv_obj_set_style_radius(frame, 2, 0);
        lv_obj_set_style_pad_all(frame, 0, 0);
        p.obj = frame;
        // 充填
        lv_obj_t* fill = lv_obj_create(frame);
        lv_obj_set_style_bg_color(fill, p.color, 0);
        lv_obj_set_style_bg_opa(fill, LV_OPA_COVER, 0);
        lv_obj_set_style_radius(fill, 1, 0);
        lv_obj_set_style_border_width(fill, 0, 0);
        lv_obj_set_pos(fill, 1, 1);
        p.obj2 = fill;
        // %
        if (p.showPct) {
            DynPart q = p;
            q.x = p.x + bw + 4;
            q.y = p.y + (bh - q.font) / 2 - 2;
            p.obj3 = make_label(faceScreen, q);
        }
        break;
    }

    case PT_CONN_WIFI:
        p.obj = create_part_image(&dscWifi, p);
        break;
    case PT_CONN_BLE:
        p.obj = create_part_image(&dscBle, p);
        break;

    case PT_NOTIF: {
        p.obj = create_part_image(&dscBell, p);
        // 未読バッジ
        lv_obj_t* badge = lv_obj_create(faceScreen);
        lv_obj_set_size(badge, 16, 16);
        lv_obj_set_pos(badge, p.x + 12, p.y - 6);
        lv_obj_set_style_bg_color(badge, lv_color_make(0xFF, 0x3B, 0x30), 0);
        lv_obj_set_style_bg_opa(badge, LV_OPA_TRANSP, 0);
        lv_obj_set_style_radius(badge, LV_RADIUS_CIRCLE, 0);
        lv_obj_set_style_border_width(badge, 0, 0);
        lv_obj_t* cnt = lv_label_create(badge);
        lv_obj_set_style_text_font(cnt, &lv_font_montserrat_12, 0);
        lv_obj_set_style_text_color(cnt, lv_color_white(), 0);
        lv_label_set_text(cnt, "0");
        lv_obj_center(cnt);
        p.obj2 = badge;
        break;
    }
    default:
        break;
    }
}

// ============================================================
// 動的パーツ更新
// ============================================================
static void update_clock_digital(DynPart& p, const struct tm& t) {
    char buf[32];
    String f = String(p.format);
    if (f.isEmpty()) f = "HH:MM";
    f.replace("HH", "%H"); f.replace("hh", "%I"); f.replace("MM", "%M");
    f.replace("SS", "%S"); f.replace("A", "%p");
    strftime(buf, sizeof(buf), f.c_str(), &t);
    lv_label_set_text(p.obj, buf);
}

static void update_date(DynPart& p, const struct tm& t) {
    char buf[32];
    String f = String(p.format);
    if (f.isEmpty()) f = "M/D";
    f.replace("YYYY", "%Y"); f.replace("YY", "%y");
    f.replace("MM", "%m"); f.replace("M", "%m");
    f.replace("DD", "%d"); f.replace("D", "%d");
    f.replace("W", "%u");
    strftime(buf, sizeof(buf), f.c_str(), &t);
    lv_label_set_text(p.obj, buf);
}

static void update_battery(DynPart& p) {
    int pct = app.battery < 0 ? 0 : app.battery;
    if (p.obj && p.obj2) {
        int iw = (int)lv_obj_get_width(p.obj) - 2;
        if (iw < 0) iw = 0;
        lv_obj_set_width(p.obj2, iw * pct / 100);
        lv_obj_set_height(p.obj2, (int)lv_obj_get_height(p.obj) - 2);
    }
    if (p.obj3) {
        char buf[8];
        snprintf(buf, sizeof(buf), "%d%%", pct);
        lv_label_set_text(p.obj3, buf);
    }
}

static void update_steps(DynPart& p) {
    char buf[64];
    snprintf(buf, sizeof(buf), "%s%u", p.label, app.steps);
    lv_label_set_text(p.obj, buf);
}

static void update_analog(DynPart& p, const struct tm& t) {
    float h = (float)(t.tm_hour % 12) + (float)t.tm_min / 60.0f;
    float m = (float)t.tm_min + (float)t.tm_sec / 60.0f;
    float s = (float)t.tm_sec;
    // LVGL 8: 角度は 0.1° 単位、正=時計回り、0°=画像の向き (上向き)
    if (p.obj)  lv_img_set_angle(p.obj,  (int16_t)(h * 300.0f));
    if (p.obj2) lv_img_set_angle(p.obj2, (int16_t)(m * 60.0f));
    if (p.obj3 && p.showSeconds) lv_img_set_angle(p.obj3, (int16_t)(s * 60.0f));
}

static void update_icon(DynPart& p, bool active) {
    if (p.obj) lv_obj_set_style_img_recolor(p.obj, active ? p.color : p.dimColor, 0);
}

// ============================================================
// 1秒タイマー
// ============================================================
void watchface_tick() {
    if (!faceScreen) return;
    struct tm t = local_tm();
    for (int i = 0; i < partCount; i++) {
        DynPart& p = parts[i];
        switch (p.type) {
        case PT_CLOCK_DIGITAL: if (p.obj) update_clock_digital(p, t); break;
        case PT_DATE: if (p.obj) update_date(p, t); break;
        case PT_BATTERY: update_battery(p); break;
        case PT_STEPS: if (p.obj) update_steps(p); break;
        case PT_ANALOG: update_analog(p, t); break;
        case PT_CONN_WIFI: update_icon(p, app.wifiState == 2); break;
        case PT_CONN_BLE: update_icon(p, app.bleConnected); break;
        case PT_NOTIF: {
            bool unread = notifications_unread() > 0;
            update_icon(p, unread);
            if (p.obj2) {
                lv_obj_set_style_bg_opa(p.obj2, unread ? LV_OPA_COVER : LV_OPA_TRANSP, 0);
                lv_obj_t* cnt = lv_obj_get_child(p.obj2, 0);
                int n = notifications_unread();
                if (n > 99) n = 99;
                lv_label_set_text_fmt(cnt, "%d", n);
            }
            break;
        }
        default: break;
        }
    }
}

// ============================================================
// dynamic.json パース + ウィジェット構築
// ============================================================
static void parts_clear() { partCount = 0; }

static void parse_and_build(const char* json, size_t jsonLen) {
    parts_clear();
    if (!json || jsonLen == 0) return;
    JsonDocument doc;
    DeserializationError err = deserializeJson(doc, json, jsonLen);
    if (err) {
        Serial.printf("[face] json error: %s\n", err.c_str());
        return;
    }
    JsonArray arr = doc["parts"].as<JsonArray>();
    if (arr.isNull()) return;
    for (JsonVariant v : arr) {
        if (partCount >= MAX_DYNAMIC_PARTS) break;
        DynPart& p = parts[partCount];
        const char* t = v["t"] | "";
        if (!strcmp(t, "clock_digital")) p.type = PT_CLOCK_DIGITAL;
        else if (!strcmp(t, "date")) p.type = PT_DATE;
        else if (!strcmp(t, "analog")) p.type = PT_ANALOG;
        else if (!strcmp(t, "battery")) p.type = PT_BATTERY;
        else if (!strcmp(t, "steps")) p.type = PT_STEPS;
        else if (!strcmp(t, "conn_wifi")) p.type = PT_CONN_WIFI;
        else if (!strcmp(t, "conn_ble")) p.type = PT_CONN_BLE;
        else if (!strcmp(t, "notif")) p.type = PT_NOTIF;
        else continue;
        p.x = v["x"] | 0;
        p.y = v["y"] | 0;
        p.w = v["w"] | 0;
        p.h = v["h"] | 0;
        p.cx = v["cx"] | 0;
        p.cy = v["cy"] | 0;
        p.r = v["r"] | 0;
        p.font = (uint8_t)(v["font"] | 16);
        p.showSeconds = v["show_seconds"] | false;
        p.showPct = v["show_pct"] | false;
        const char* al = v["align"] | "left";
        p.alignCenter = !strcmp(al, "center");
        p.alignRight = !strcmp(al, "right");
        strncpy(p.format, v["format"] | "", sizeof(p.format) - 1);
        strncpy(p.label, v["label"] | "", sizeof(p.label) - 1);
        p.color = parse_color(v["color"] | "", lv_color_white());
        p.dimColor = parse_color(v["dim_color"] | "", lv_color_make(0x33, 0x33, 0x33));
        partCount++;
    }
}

static void rebuild_all() {
    if (lv_obj_is_valid(faceScreen)) lv_obj_clean(faceScreen);
    // 背景 (なければ黒背景)
    if (bgPixels) {
        bgImg = lv_img_create(faceScreen);
        lv_img_set_src(bgImg, &bgDsc);
        lv_obj_set_pos(bgImg, 0, 0);
    } else {
        bgImg = nullptr;
        lv_obj_set_style_bg_color(faceScreen, lv_color_make(0x00, 0x00, 0x00), 0);
        lv_obj_set_style_bg_opa(faceScreen, LV_OPA_COVER, 0);
    }
    // 動的パーツ
    for (int i = 0; i < partCount; i++) create_part(parts[i]);
}

void watchface_apply_face() {
    if (!faceScreen) return;
    hands_free();
    bg_load(FACE_PATH_BG);
    hand_load(0, FACE_PATH_HOUR);
    hand_load(1, FACE_PATH_MIN);
    hand_load(2, FACE_PATH_SEC);
    // dynamic.json
    uint8_t* data = nullptr;
    size_t len = 0;
    if (storage_read_file(FACE_PATH_DYNAMIC, data, len)) {
        parse_and_build((const char*)data, len);
        free(data);
    } else {
        parts_clear();
    }
    if (partCount == 0) {
        // デフォルト文字盤 (ASCIIのみ)
        static const char def[] =
            "{\"v\":1,\"name\":\"default\",\"parts\":["
            "{\"t\":\"clock_digital\",\"x\":20,\"y\":86,\"w\":200,\"h\":60,\"font\":44,\"color\":\"#FFFFFF\",\"format\":\"HH:MM\",\"align\":\"center\"},"
            "{\"t\":\"date\",\"x\":20,\"y\":158,\"w\":200,\"h\":22,\"font\":20,\"color\":\"#8FA3B8\",\"format\":\"MM/DD\",\"align\":\"center\"},"
            "{\"t\":\"steps\",\"x\":20,\"y\":196,\"w\":120,\"h\":22,\"font\":16,\"color\":\"#FFCC00\",\"label\":\"STEPS \"},"
            "{\"t\":\"conn_wifi\",\"x\":196,\"y\":14,\"w\":24,\"h\":24,\"color\":\"#8FA3B8\"},"
            "{\"t\":\"conn_ble\",\"x\":164,\"y\":14,\"w\":24,\"h\":24,\"color\":\"#4CD964\"},"
            "{\"t\":\"notif\",\"x\":12,\"y\":14,\"w\":24,\"h\":24,\"color\":\"#FF9500\"}]}";
        parse_and_build(def, sizeof(def) - 1);
    }
    rebuild_all();
    notifications_attach_to_face();
    lv_obj_invalidate(faceScreen);
    Serial.printf("[face] applied: %d parts\n", partCount);
}

void watchface_init() {
    if (!faceScreen) {
        faceScreen = lv_obj_create(NULL);
        lv_obj_set_size(faceScreen, WATCH_W, WATCH_H);
        lv_obj_clear_flag(faceScreen, LV_OBJ_FLAG_SCROLLABLE);
        lv_obj_add_flag(faceScreen, LV_OBJ_FLAG_CLICKABLE);
        // タップで通知リスト
        lv_obj_add_event_cb(faceScreen, [](lv_event_t*) {
            if (notifications_count() > 0) notifications_open_list();
        }, LV_EVENT_CLICKED, nullptr);
    }
    icons_build();
    dsc_from_rgb565_alpha(dscWifi, iconWifi, ICON_SZ, ICON_SZ, LV_IMG_CF_TRUE_COLOR_ALPHA);
    dsc_from_rgb565_alpha(dscBle, iconBle, ICON_SZ, ICON_SZ, LV_IMG_CF_TRUE_COLOR_ALPHA);
    dsc_from_rgb565_alpha(dscBell, iconBell, ICON_SZ, ICON_SZ, LV_IMG_CF_TRUE_COLOR_ALPHA);
    watchface_apply_face();
    watchface_show();
    lv_timer_create([](lv_timer_t*) { watchface_tick(); }, 1000, nullptr);
}

void watchface_show() {
    if (faceScreen) lv_scr_load(faceScreen);
}

void watchface_back_to_face() {
    watchface_show();
    notifications_attach_to_face();
}

lv_obj_t* watchface_get_screen() { return faceScreen; }

// ---- 外部更新 (tick が app 状態を参照するため実体は不要) ----
void watchface_set_steps(uint32_t) {}
void watchface_set_battery(int, bool) {}
void watchface_set_conn(bool, int) {}
void watchface_set_unread(int) {}
