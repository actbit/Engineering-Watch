#include "storage.h"
#include <FFat.h>
#include <Preferences.h>

static Preferences pref;

// zlib互換 CRC32 (テーブル法)
static uint32_t crc_table[256];
static bool crc_table_ready = false;

static void crc_init_table() {
    for (uint32_t i = 0; i < 256; i++) {
        uint32_t c = i;
        for (int k = 0; k < 8; k++) {
            c = (c & 1) ? (0xEDB88320u ^ (c >> 1)) : (c >> 1);
        }
        crc_table[i] = c;
    }
    crc_table_ready = true;
}

uint32_t storage_crc32_update(uint32_t crc, const uint8_t* data, size_t len) {
    if (!crc_table_ready) crc_init_table();
    crc = crc ^ 0xFFFFFFFFu;
    for (size_t i = 0; i < len; i++) {
        crc = crc_table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
    }
    return crc ^ 0xFFFFFFFFu;
}

uint32_t storage_crc32(const uint8_t* data, size_t len) {
    return storage_crc32_update(0, data, len);
}

bool storage_init() {
    if (!FFat.begin()) {
        Serial.println("[storage] FFat mount failed -> formatting");
        if (!FFat.format()) {
            Serial.println("[storage] FFat format failed");
            return false;
        }
        if (!FFat.begin()) return false;
    }
    FFat.mkdir("/face");
    pref.begin("ewatch", false);
    Serial.printf("[storage] FFat total=%u used=%u\n",
                  FFat.totalBytes(), FFat.usedBytes());
    return true;
}

bool storage_write_file(const char* path, const uint8_t* data, size_t len) {
    File f = FFat.open(path, FILE_WRITE);
    if (!f) return false;
    size_t w = f.write(data, len);
    f.close();
    return w == len;
}

bool storage_read_file(const char* path, uint8_t*& data, size_t& len) {
    File f = FFat.open(path, FILE_READ);
    if (!f) return false;
    len = f.size();
    data = (uint8_t*)malloc(len ? len : 1);
    if (!data) { f.close(); return false; }
    size_t r = f.read(data, len);
    f.close();
    if (r != len) { free(data); data = nullptr; return false; }
    return true;
}

bool storage_file_exists(const char* path) {
    return FFat.exists(path);
}

size_t storage_file_size(const char* path) {
    File f = FFat.open(path, FILE_READ);
    if (!f) return 0;
    size_t s = f.size();
    f.close();
    return s;
}

// ---- NVS ----
bool nvs_save_string(const char* key, const String& val) {
    return pref.putString(key, val) > 0;
}

String nvs_load_string(const char* key, const String& def) {
    if (!pref.isKey(key)) return def;
    return pref.getString(key, def);
}

bool nvs_save_i32(const char* key, int32_t val) {
    return pref.putInt(key, val);
}

int32_t nvs_load_i32(const char* key, int32_t def) {
    return pref.getInt(key, def);
}

bool nvs_save_bytes(const char* key, const uint8_t* data, size_t len) {
    return pref.putBytes(key, data, len) == len;
}

bool nvs_load_bytes(const char* key, uint8_t*& data, size_t& len) {
    len = pref.getBytesLength(key);
    if (len == 0) return false;
    data = (uint8_t*)malloc(len);
    if (!data) return false;
    pref.getBytes(key, data, len);
    return true;
}
