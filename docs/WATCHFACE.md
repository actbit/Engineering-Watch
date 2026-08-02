# 文字盤フォーマット v1 (dynamic.json)

Androidアプリの文字盤エディタが生成し、BLE経由で時計に送信する動的パーツ設定。

**背景画像 (bg.png) はAndroid側でレンダリング済み** (静的パーツすべてを含む)。
このJSONには**時計がリアルタイム描画するパーツ**の定義のみを書く。

## 全体構造

```json
{
  "v": 1,
  "name": "マイ文字盤",
  "parts": [
    { "t": "clock_digital", "x": 40, "y": 110, "w": 160, "h": 56,
      "font": 40, "color": "#FFFFFF", "format": "HH:MM", "align": "center" },
    { "t": "date", "x": 40, "y": 175, "font": 20, "color": "#8FA3B8",
      "format": "M月d日", "align": "center" },
    { "t": "analog", "cx": 120, "cy": 120, "r": 100,
      "show_seconds": true, "axis_color": "#FFFFFF" },
    { "t": "battery", "x": 160, "y": 16, "w": 64, "h": 18,
      "color": "#4CD964", "show_pct": true, "font": 12 },
    { "t": "steps", "x": 12, "y": 212, "font": 16, "color": "#FFCC00",
      "label": "STEPS", "align": "left" },
    { "t": "conn_wifi", "x": 196, "y": 14, "size": 16, "color": "#8FA3B8" },
    { "t": "conn_ble", "x": 216, "y": 14, "size": 16, "color": "#4CD964" },
    { "t": "notif", "x": 12, "y": 14, "size": 16, "color": "#FF9500" }
  ]
}
```

座標系は **240×240** (T-Watch S3の実解像度)。単位はピクセル。

## パーツ定義

共通: すべてに `t` (種別) が必須。省略可能なプロパティはデフォルト値あり。

### clock_digital — デジタル時計

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| x, y | 必須 | - | 配置位置 (左上) |
| w | 任意 | 自動 | 幅 (align=center で有効) |
| h | 任意 | 自動 | 高さ (align=center で有効) |
| font | 任意 | 24 | フォントサイズ。**LVGL内蔵フォントから最寄り** (12,14,16,18,20,22,24,28,32,36,40,44,48) |
| color | 任意 | #FFFFFF | 文字色 (#RRGGBB) |
| format | 任意 | HH:MM | `HH`=時(24h) `hh`=時(12h) `MM`=分 `SS`=秒 `A`=AM/PM |
| align | 任意 | left | `left` / `center` / `right` |
| show_seconds | 任意 | false | true で秒表示 (format 優先) |

### clock_analog — アナログ時計

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| cx, cy | 必須 | - | 中心座標 |
| r | 任意 | 100 | 半径 (針の長さの目安。実際はAndroidが生成した針画像を使用) |
| show_seconds | 任意 | true | 秒針を表示 |
| axis_color | 任意 | #FFFFFF | 中心軸の色 |
| axis_r | 任意 | 4 | 中心軸の半径 |

針画像 (`hour.png`, `min.png`, `sec.png`) はAndroid側が生成。
- 各画像は縦長の透過PNG。**画像の中央**が針の回転軸 (Android側で中央に軸が来るよう描画)
- 時計側は `lv_img_set_angle` で回転 (LVGL: 0.1°単位)

### date — 日付

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| x, y | 必須 | - | 配置位置 |
| font | 任意 | 18 | フォントサイズ |
| color | 任意 | #FFFFFF | 文字色 |
| format | 任意 | M/D | `Y`=年(4桁) `M`=月 `D`=日 `W`=曜日(日〜土) |
| align | 任意 | left | 配置 |

### battery — バッテリー (テキスト+バー)

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| x, y | 必須 | - | 配置位置 |
| w, h | 任意 | 40x14 | バーサイズ |
| color | 任意 | #4CD964 | バー+テキストの色 |
| show_pct | 任意 | false | バー右にパーセント表示 |
| font | 任意 | 12 | パーセント文字サイズ |
| align | 任意 | left | テキスト配置 |

### steps — 歩数

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| x, y | 必須 | - | 配置位置 |
| font | 任意 | 16 | フォントサイズ |
| color | 任意 | #FFFFFF | 文字色 |
| label | 任意 | "" | 数値の前につけるラベル (例 "STEPS") |
| align | 任意 | left | 配置 |

### conn_wifi / conn_ble / notif — 状態アイコン

| キー | 必須 | 既定 | 説明 |
|------|------|------|------|
| x, y | 必須 | - | 配置位置 |
| size | 任意 | 16 | アイコンサイズ (ピクセル) |
| color | 任意 | #FFFFFF | アイコン色 (LVGLのrecolorで着色) |
| dim_color | 任意 | #333333 | 非アクティブ時の色 (wifi未接続/BLE未接続/未読0) |

- `conn_wifi`: WiFi接続中=color、それ以外=dim_color
- `conn_ble`: BLE接続中=color、それ以外=dim_color
- `notif`: 未読通知があるとき color + 件数バッジ、ないとき dim_color

## 制約

- パーツ数: 最大 20
- JSONサイズ: 2KB以内
- 座標は 0..240 の範囲を推奨 (はみ出しは時計側でクランプ)
- フォントサイズはLVGL内蔵フォントの実サイズに丸められるため、
  **エディタでは選択可能サイズを制限して見た目の一致を保証**する
