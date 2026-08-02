# BLE プロトコル仕様 v1

Android (中央装置/GATT Client) と T-Watch S3 (周辺装置/GATT Server) の通信仕様。

## 接続情報

| 項目 | 値 |
|------|-----|
| アドバタイズ名 | `EWatch-XXXX` (XXXX はMAC下位4桁) |
| 接続間隔 | 15ms (優先: 低消費電力) |
| MTU | 要求可能な最大値 (Android側で `requestMtu(512)`) |

## GATT 構成

```
<service>   Watch Service       0000FF00-0000-1000-8000-00805F9B34FB
├── FF01    WatchFaceConfig      Write / Read       文字盤パッケージ転送 (チャンク)
├── FF02    Control              Write / Read       コマンド (JSON)
├── FF03    Status               Notify             状態通知 (JSON)
└── FF04    WatchData            Notify             歩数・バッテリー (JSON)

<service>   Notification Service 0000FF10-0000-1000-8000-00805F9B34FB
└── FF11    Notification         Write              通知転送 (JSON)
```

## 共通フォーマット

### FF01: WatchFaceConfig (チャンク転送)

画像や設定は1つの "FacePackage" として、ファイル単位で転送する。

**フレーム形式 (1回の Write が1フレーム):**

| バイト | 内容 |
|--------|------|
| 0 | 種別 (下記) |
| 1 | fileId |
| 2..5 | ヘッダ/チャンクごとの情報 (下記) |
| 6..N | データ |

| 種別 | 値 | 内容 |
|------|----|------|
| WF_BEGIN | 0x01 | ファイル転送開始。バイト2..5=総サイズ(u32 LE)。バイト6.. = ファイル名 (UTF-8, 最大32B) |
| WF_DATA  | 0x02 | データチャンク。バイト2..5=先頭からのオフセット(u32 LE)。バイト6.. = データ (最大 MTU-6) |
| WF_END   | 0x03 | 転送完了。バイト2..5=CRC32 (u32 LE) |
| WF_APPLY | 0x04 | 全ファイル転送後の適用。バイト2..5=JSON長(u32 LE)。バイト6.. = dynamic.json |

**fileId 定義:**

| fileId | ファイル | 用途 |
|--------|---------|------|
| 1 | `bg.png` | 背景画像 (240x240 PNG, Android側で静的パーツをレンダリング済み) |
| 2 | `hour.png` | アナログ時計の時針画像 |
| 3 | `min.png` | 分針画像 |
| 4 | `sec.png` | 秒針画像 |

- 転送は fileId 昇順で行い、最後に `WF_APPLY` を送る
- 時計側は各ファイルを FFat に保存。CRC32 不一致時は FF04/FF03 でエラー応答
- 応答: 転送開始ごとに FF03 から `{"type":"wf","file":"bg.jpg","state":"ok"}` 等

### FF02: Control (JSON, 1Write=1コマンド)

| コマンド | 例 | 説明 |
|----------|-----|------|
| time_sync | `{"cmd":"time_sync","utc":1782691200,"tz":32400,"dst":false}` | 時刻同期 (utc=Unix秒, tz=分単位オフセット) |
| wifi_on | `{"cmd":"wifi_on","ssid":"Home","pass":"secret"}` | WiFi接続 (認証情報は時計側NVSに保存) |
| wifi_off | `{"cmd":"wifi_off"}` | WiFi切断 |
| wifi_status | `{"cmd":"wifi_status"}` | 状態問い合わせ (FF03応答) |
| brightness | `{"cmd":"brightness","v":128}` | バックライト明度 0-255 |
| vibrate | `{"cmd":"vibrate","ms":300}` | 振動テスト |
| reboot | `{"cmd":"reboot"}` | 再起動 |
| get_status | `{"cmd":"get_status"}` | 状態問い合わせ |

### FF03: Status (Notify, JSON)

| 例 | 説明 |
|----|------|
| `{"type":"state","ver":"0.1.0","wifi":"off","ble":true}` | 状態 |
| `{"type":"wifi","state":"connecting"}` / `"connected"` / `"off"` | WiFi状態変化 |
| `{"type":"wf","file":"bg.jpg","state":"ok","bytes":24512}` | 文字盤転送進捗/結果 |
| `{"type":"wf","state":"error","msg":"crc_mismatch"}` | 文字盤エラー |
| `{"type":"notif","state":"shown","id":123}` | 通知表示完了 |

### FF04: WatchData (Notify, JSON)

| 例 | 説明 |
|----|------|
| `{"steps":1234,"battery":87,"charging":false,"ts":1782691200}` | 定期送信 (60秒毎+接続時) |

### FF11: Notification (Write, JSON, 1Write=1通知)

```json
{
  "app": "LINE",
  "title": "山田 太郎",
  "text": "明日の会議お願いします",
  "id": 12345,
  "when": 1782691200,
  "icon": "line"
}
```

| キー | 必須 | 説明 |
|------|------|------|
| app | 必須 | アプリ名 (64B以内) |
| title | 任意 | タイトル (128B以内) |
| text | 任意 | 本文 (512B以内) |
| id | 任意 | 通知ID (int64, 重複をマージ) |
| when | 任意 | 受信時刻 (Unix秒) |
| icon | 任意 | アイコンキー (`line`/`mail`/`msg`/`phone`/`other` など、未指定=other) |

通知は **512B以内** に収めるため、Android側で適宜テキストを切り詰める。

## 時計側の動作仕様

### 文字盤適用 (WF_APPLY 受信時)

1. `dynamic.json` (JSON) をパース
2. `bg.png` を FFat から読んで lodepng でデコード → LVGL画像へ
3. `hour.png` / `min.png` / `sec.png` を LVGL画像としてロード
4. dynamic パーツ (時計・日付・歩数等) を LVGL ウィジェットとして生成
5. 完了を FF03 で通知 → Androidは次の文字盤があれば続けて送信可

### 通知受信時

1. モータ振動 (DRV2605, 300ms) + バックライト点灯
2. バナー表示 (3秒) → タップで通知リストへ
3. 通知リストに追加 (最大20件, 超過時は古い順に削除)
4. 未読数に変化があれば `notif` パーツを更新

### WiFi オンデマンド

- `wifi_on` で接続開始 (非同期)。認証情報は NVS に保存 (次回から省略可)
- `wifi_off` または **10分間通信なし** で自動切断
- 接続中は FF03 で状態を通知

### 起動時

1. FFat マウント → `dynamic.json` と `bg.jpg` があれば復元
2. NVS から WiFi 認証情報・明度・前回の歩数等を復元
3. BLE アドバタイズ開始
4. バッテリー・歩数・時刻を FF04 で定期送信 (接続時のみ)
