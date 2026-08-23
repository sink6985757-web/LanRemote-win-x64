# LanRemote 應用程式圖示來源紀錄

## 資產

- `LanRemote.App.generated.png`：內建 ImageGen 產生的最終原始 RGBA 圖。
- `LanRemote.App.png`：裁切透明邊界後置中於 1024×1024 畫布的應用程式 PNG。
- `LanRemote.App.ico`：由同一份 PNG 建立的 Windows 多尺寸圖示，包含 16、20、24、32、40、48、64、128、256 px。

## 生成資訊

- 日期：2026-08-23
- 工具：Codex 內建 ImageGen（built-in tool mode）
- use case：`logo-brand`
- 輸入圖片：無；最終候選由文字提示從零產生。
- 目的：LanRemote Windows 桌面應用程式圖示。

最終提示：

```text
Use case: logo-brand
Asset type: Windows desktop application icon, square composition for 16 px through 256 px
Primary request: Create one original LanRemote icon with two equal rounded monitor frames arranged diagonally and slightly overlapping, one upper-left and one lower-right, connected by one bold teal luminous gateway/link at the center. The layout must communicate two computers cooperating over a private local network.
Style/medium: very clean flat geometric vector-friendly icon, friendly-professional AI-era personality, rounded proportions, minimal subtle depth only
Composition/framing: compact near-square silhouette that fills about 82% of a square canvas; NOT two monitors stretched side-by-side; both monitor frames and central connection remain obvious at 16x16; centered; strong thick outlines; generous transparent safety margin
Color palette: deep navy #0B1220 outlines, blue #1D4ED8 screen accents, teal #14B8A6 and mint #5EEAD4 central link, restrained white highlight
Details: at most two small neural-node dots, no fragile circuit lines, no tiny monitor stands if they hurt small-size clarity
Text: none
Constraints: actual RGBA transparent background with fully transparent exterior pixels; no checkerboard artwork; no enclosing square tile; no border touching canvas; no letters, words, watermark, arrows, eyes, locks, shields, surveillance imagery, photorealism, glossy 3D mockup, or recognizable third-party branding
Avoid: OpenAI knot, Windows logo, TeamViewer arrows/logo, AnyDesk diamond, Chrome Remote Desktop mark, VNC logos, Apple logo
```

## 衍生處理

- 使用 Pillow 12.3.0 讀取 alpha bounding box。
- 將非透明內容以 Lanczos 縮放至最長邊 880 px，置中於透明 1024×1024 RGBA 畫布。
- 由置中 PNG 建立 PNG-compressed multi-resolution ICO。
- 沒有加入或混用第三方圖片、圖示、商標或字型素材。

## SHA-256

- `LanRemote.App.generated.png`：`34C6EED586D5716C55E30D9EB0107B943D49BA6910D4141E365FDFB4C8948DC3`
- `LanRemote.App.png`：`097D220F918A18AD2D4462AFE26015FF1D06B1BB8EBD02CEC859521DB284E071`
- `LanRemote.App.ico`：`1A05513C90E2A0774C3619DC50020C5DE4A42C722037C2D3A0985CCFF6B20FFC`

## 權利與限制

本圖示以專案內建工具從零生成，提示刻意排除可辨識的第三方品牌元素；專案沒有下載或改作外部圖示。此紀錄用於來源追溯與降低第三方素材風險，不取代正式著作權、商標檢索或法律意見。
