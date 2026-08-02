# Engineering-Watch アーキテクチャ

T-Watch S3 (ESP32-S3) スマートウォッチ + Androidアプリのプロジェクト。

```
Engineering-Watch/
├── docs/                 # 設計ドキュメント
├── firmware/             # 時計側ファームウェア (Arduino + LVGL, PlatformIO)
│   └── src/              # main.cpp 等
└── Engineering-Watch/    # Androidアプリ (.NET 10 Android, C#)
```

## 全体像

```
┌─────────────────────┐        BLE (基本)        ┌──────────────────────┐
│   Android アプリ     │◄───────────────────────►│  T-Watch S3           │
│                     │  ・文字盤パッケージ送信    │  ・LVGL UI            │
│  ・文字盤エディタ     │  ・通知転送               │  ・背景JPEG表示        │
│  ・通知リスナー       │  ・制御コマンド            │  ・動的パーツ描画       │
│  ・BLE接続管理        │  ・状態/歩数/バッテリー    │  ・通知バナー/リスト    │
└─────────────────────┘                          └──────────────────────┘
        │ WiFi (オンデマンド)                           │ WiFi (オンデマンド)
        └──────────────► インターネット ◄───────────────┘
               (将来: 天気・OTA・データ同期など、用途に応じて
                Androidが"WiFi ON"コマンドを送ると時計が自動接続)
```

## 設計方針: レンダリングのAndroid側集中

**文字盤のレンダリングはできる限りAndroid側で行い、時計側の仕事を減らす。**

### 静的パーツ (Androidで描画 → JPEG化 → 時計は表示のみ)

| パーツ | 内容 |
|--------|------|
| rect / circle / line / arc | 図形 |
| gradient | グラデーション背景 |
| 画像 | ギャラリーから選択 (Androidが 240×240 にフィット) |
| テキスト | **任意のAndroidフォント**で描画 (時計側にフォント不要) |
| アナログ文字盤 (目盛り等) | 文字盤部分のみ描画 |

→ Androidが **240×240 のPNG** に合成 → BLEで時計に送信
→ 時計は LittleFS(FFat) に保存し、LVGLの `lv_img` で表示するだけ

### 動的パーツ (時計がLVGLで描画、Androidは位置・色だけ送信)

| パーツ | 時計側の処理 |
|--------|-------------|
| clock_digital | `lv_label` に時刻テキスト (LVGL内蔵フォント) |
| clock_analog | Android生成の針画像PNGを `lv_img` + `lv_img_set_angle` で回転 |
| date / steps / battery | `lv_label` |
| conn_wifi / conn_ble / notif | ファームウェア内蔵アイコン画像 + 色指定 (`recolor`) |

→ 送信されるのは **小さなJSON設定** のみ (数KB以下)

### メリット

- 時計側の描画処理・メモリ使用が最小限 → バッテリー長持ち
- カスタムフォント・複雑な装飾もAndroidのSkia描画で自由に
- 文字盤切替が画像転送(10〜30KB) + 小JSONだけで完了
- Androidのエディタプレビューと実機表示がほぼ完全一致

## 技術スタック

### ファームウェア (firmware/)
- **PlatformIO** + `espressif32` プラットフォーム (ESP32-S3, 16MB Flash, 8MB OPI PSRAM)
- **Arduino framework** (esp32 core 2.x, USB-CDC)
- **LVGL 8.3.x** — 画面描画
- **TTGO_TWatch_Library** (`t-watch-s3` ブランチ) — ハードウェア初期化・周辺機器ドライバ
  - TFT_eSPI (ST7789 240x240), FT6x36 タッチ, AXP2101 PMU, BMA423 (歩数), PCF8563 RTC, DRV2605 (振動)
- **ESP32 BLE Arduino** (コア内蔵) — GATTサーバー
- **ArduinoJson** — 動的パーツ設定のパース
- **LVGL内蔵lodepng** — 背景PNGのデコード (PSRAM利用)
- **FFat** — 背景画像・針画像の永続化 (フラッシュ上のFATパーティション)

### Android (Engineering-Watch/)
- **.NET 10 Android (C#)**, ネイティブ Android API のみ (NuGet追加なし)
- `android.graphics.Canvas` / `Bitmap` — 文字盤レンダリング・JPEGエンコード
- `BluetoothGatt` 等 — BLE (中央装置)
- `NotificationListenerService` — 全アプリの通知を取得して転送
- `System.Text.Json` — JSON処理

## 画面構成 (時計側)

| 画面 | 説明 |
|------|------|
| 文字盤 | 背景JPEG + 動的パーツオーバーレイ。タップで通知リストへ |
| 通知リスト | 受信した通知の履歴 (最新20件)。タップで消去 |
| 設定 | 明るさ、WiFi状態、Bluetooth状態、バージョン |

## 画面構成 (Android側)

| タブ | 説明 |
|------|------|
| 文字盤エディタ | パーツパレット → ドラッグ&ドロップ配置 → レンダリング→送信 |
| 接続 | BLEスキャン/接続、歩数・バッテリー表示、タイム同期 |
| 設定 | WiFi認証情報 (オンデマンド用)、通知転送ON/OFF、明るさ |

## 通信の原則

1. **BLEが主経路**。常時接続、MTU 512 (Android側で最大MTU要求)
2. **WiFiはオンデマンド**: Androidが `wifi_on` コマンド(+認証情報)を送ると時計がWiFiへ接続。
   用途が終わったら `wifi_off`、またはタイムアウトで自動OFF (バッテリー保護)。
   特定機能の実装時に自動ONを組み込む (現時点では機能未定のため基盤のみ)
3. **チャンク転送**: 画像等の大きなデータはMTU単位で分割し、CRC32で検証

## 将来の拡張ポイント

- 天気表示 (WiFi + HTTP, 時計側で自動WiFi ON)
- OTAファームウェア更新 (WiFiオンデマンド)
- 位置情報/通知アクション応答 (Android → 時計 → タップで返答)
- 文字盤データのクラウド共有
