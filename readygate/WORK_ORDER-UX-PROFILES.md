# ReadyGate Confirmed Work Order — VNC-style UX and Quality Profiles

- Work order: `WO-LANREMOTE-UX-PROFILES-V1`
- Status: `WORK_ORDER_CONFIRMED / EXECUTION`
- Confirmed: 2026-08-22
- Clarification cycles used: 2 of 3

## Objective

把已能在兩臺電腦間建立連線的 LanRemote MVP，改成接近 Linux VNC／TeamViewer 與 IC Layout 工具的單一視窗操作方式。程式先顯示連線介面；核准連線後，同一個 WPF 視窗切換成遠端桌面工作區。

## Confirmed implementation boundary

- 保留同一個 EXE 的被控端／控制端雙角色；單一 session 仍只有一個控制方向，斷線後交換角色即可反向控制。
- 遠端 session 初始為視窗化，並可切換「視窗化」、「最大化」與「無邊框全螢幕」。
- 視窗化時遠端影像依視窗大小等比例自適應；黑邊區域不得錯誤映射滑鼠座標。
- 頂部提供 `File`、`Connection`、`View`、`Quality` 傳統選單與工具列。
- 工具列可隱藏；隱藏後可由視窗頂端感應區暫時叫回。
- 連線中可直接切換三種 GDI/JPEG 畫面設定：
  - 流暢：最高 1280×720、30 fps、JPEG quality 40。
  - 平衡：最高 1600×900、24 fps、JPEG quality 60。
  - 畫質：最高 1920×1080、15 fps、JPEG quality 82。
- 斷線後回到原連線介面，保留被控端位址與最後選用的畫面模式。
- 協定升級為 v2；兩臺都必須換成相同新版，舊協定不得靜默相容。

## Acceptance

1. 本機真實 TLS 雙實例可完成 SAS 核准並進入同一視窗遠端工作區。
2. 視窗化、最大化、F11 無邊框全螢幕均可切換，退出全螢幕可回到先前模式。
3. 工具列可隱藏，並可由頂端感應區重新顯示。
4. 流暢、平衡、畫質三種設定可在同一連線中熱切換，控制端收到被控端已套用回覆。
5. 斷線後返回 launcher，位址與最後模式仍保留。
6. 協定、畫質參數、TLS session 與自適應座標具有自動測試。

## Explicit exclusions

- 本輪不改成 Windows Graphics Capture、Media Foundation H.264 或硬體編碼；這些是後續效能工作項目。
- 不新增網際網路 relay、NAT traversal、自動探索、無人值守、隱蔽監控、提權或 UAC／安全桌面繞過。
- 不新增多螢幕、音訊、剪貼簿、檔案傳輸、安裝程式、簽章或公開發布。
- 不自動修改 Windows Firewall，也不代替使用者操作系統安全提示。
- 不建立 remote／GitHub repository，不 push、merge、tag 或 release。

## Authorization

- 可修改程式碼、協定、測試、文件與本機發包腳本。
- 可執行 build、test、format、NuGet audit 與本機雙實例 GUI 操作驗證。
- 可建立 win-x64 self-contained ZIP、SHA-256 與 scoped local commits。
- Windows 10／11 兩臺實機安裝、私人防火牆選擇與新版雙向驗證仍由使用者操作。

## Delivery evidence boundary

本機 TLS 雙實例可以證明 v2 交握、畫面傳送、選單／視窗狀態與熱切換路徑成立，但不能取代 Windows 10 22H2 與 Windows 11 25H2 的跨裝置證據。新版測試包在兩個方向都實測前，只能作為有條件的本機測試候選，不能宣稱正式發布或跨版本完整相容。
