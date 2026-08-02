#pragma once
#include <Arduino.h>
#include <cstdint>

// ---- FFat (文字盤ファイル等) ----
bool storage_init();                                    // FFat マウント + 初期化
bool storage_write_file(const char* path, const uint8_t* data, size_t len);
bool storage_read_file(const char* path, uint8_t*& data, size_t& len);  // 呼び出し側で free()
bool storage_file_exists(const char* path);
size_t storage_file_size(const char* path);

// ---- CRC32 (zlib互換) ----
uint32_t storage_crc32_update(uint32_t crc, const uint8_t* data, size_t len);
uint32_t storage_crc32(const uint8_t* data, size_t len);

// ---- NVS (Preferences) ----
bool nvs_save_string(const char* key, const String& val);
String nvs_load_string(const char* key, const String& def = "");
bool nvs_save_i32(const char* key, int32_t val);
int32_t nvs_load_i32(const char* key, int32_t def = 0);
bool nvs_save_bytes(const char* key, const uint8_t* data, size_t len);
bool nvs_load_bytes(const char* key, uint8_t*& data, size_t& len);
