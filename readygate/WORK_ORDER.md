# ReadyGate Confirmed Work Order

- Work order: `WO-LANREMOTE-MVP-20260822`
- Status: `WORK_ORDER_CONFIRMED / EXECUTION`
- Confirmed: 2026-08-22

## Objective

建立一個讓 Windows 11 25H2 x64 與 Windows 10 22H2 x64 在同一私人區網內互相控制的 MVP。兩臺執行同一套程式，可各自切換成被控端或控制端；每個 session 只允許單一控制方向，必須斷線後才可交換角色。

## Confirmed implementation boundary

- C#、.NET 10 LTS、WPF、win-x64 self-contained package。
- 手動輸入 `IPv4:port`；不做自動探索、網際網路 relay 或 NAT traversal。
- TLS 1.2/1.3 暫時 session 憑證、六位數 SAS 配對碼、被控端明確核准。
- 主要單一螢幕、滑鼠、鍵盤、立即斷線與角色交換。
- 目標為 1920×1080、30 fps、五分鐘互動延遲 p95 小於 200 ms。
- 預定效能基線為 Windows Graphics Capture + Media Foundation H.264。

## Explicit exclusions

- 無人值守、隱蔽監控、提權、UAC／安全桌面繞過。
- 多螢幕、音訊、剪貼簿、檔案傳輸、安裝程式、簽章與公開發布。
- 不自動修改 Windows Firewall；私人網路防火牆提示由使用者操作。
- 不建立 GitHub repository，不 push、merge、tag 或 release。

## Authorization

- 可安裝 Microsoft 官方 .NET 10 SDK、還原 NuGet、建立程式碼／測試／腳本／文件。
- 可初始化獨立本機 Git `main`、執行本機 build/test/UI 冒煙測試、產生 self-contained 測試包與建立 scoped local commit。
- 第二臺電腦的執行與防火牆核准由使用者操作，產生的兩機證據再複製回本專案。

## Compatibility condition

Microsoft 的 .NET 10 支援清單不包含 Windows 10 22H2。即使 self-contained package 在該機實測成功，最終 ReadyGate 最高只能是 `CONDITIONAL`；若失敗則記錄 `FAIL`，不得暗中降級技術棧。

## Known implementation delta

目前垂直切片以 GDI/JPEG 相容傳輸完成可操作路徑；WGC + Media Foundation H.264 效能基線尚未完成，因此不得宣稱 1080p30／p95 200 ms 已達標。
