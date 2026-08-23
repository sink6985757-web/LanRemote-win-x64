# Changelog

## [Unreleased] - 2026-08-23

### Changed

- 建立專案生命週期四檔與 portable manifest。
- 記錄區網雙向遠端控制的 MVP、模組邊界與安全限制。
- 初始化獨立本機 Git `main` 與 .NET 10 solution。
- 實作同一 WPF 應用程式的被控端／控制端雙角色切換。
- 實作私人 IPv4 限制、TLS 1.2/1.3、session-only 憑證、六位 SAS 配對碼與被控端明確核准。
- 實作版本化訊息 framing、主要螢幕 GDI/JPEG 相容傳輸、滑鼠／鍵盤 `SendInput` 與立即斷線。
- 加入 self-contained win-x64 發包與 Windows 10／11 雙機證據腳本。
- 升級線上協定為 v2，加入初始畫質協商、連線中畫質切換與被控端套用回覆；舊協定會被明確拒絕。
- 把 launcher 與遠端桌面整合到同一個 WPF 視窗，加入傳統選單與工具列。
- 加入視窗化、最大化、F11 無邊框全螢幕、工具列隱藏／頂端叫回與斷線返回 launcher。
- 加入流暢 720p30、平衡 900p24、畫質 1080p15 三種 canonical GDI/JPEG 設定與 session 內熱切換。
- 加入等比例影像座標映射，黑邊區域不注入遠端滑鼠事件。
- 升級協定為 v3，新增固定用途 `Ctrl+Alt+Delete` 要求／結果訊息與 Windows LocalSystem SAS 服務。
- 加入由使用者手動執行的 SAS 服務安裝／移除腳本；服務 pipe 僅接受固定命令並限制本機互動式使用者、SYSTEM 與 Administrators。
- 加入工具列 `Ctrl+Alt+0`、自訂按鍵新增／送出／移除、保留組合驗證及 LocalAppData JSON 保存。
- 三種 GDI/JPEG 目標上限加倍為流暢 720p60、平衡 900p48、畫質 1080p30，接收端以單一 latest-frame buffer 丟棄過期 frame。
- 加入 Fit／Stretch／Crop 三種縮放與對應座標映射，預設 Stretch 滿版。
- 被控端原生游標合成進 frame；控制端加入青色箭頭、白色描邊、半透明光圈，並依使用者確認移除「控制」文字標籤。
- 修正重複發包時 `Compress-Archive -Force` 產生 duplicate entries，改成全新暫存 ZIP 完成後覆寫成品。

### Validation

- 初始化檔案已使用 UTF-8 回讀。
- Portable manifest 已通過目前本機 Full Core validator 的結構檢查。
- Runtime skills 與本機發行來源的 SHA-256 一致；發行來源變更尚未進入 manifest 所指的不可變 commit，因此權威版本驗證為 `PARTIAL`。
- 初始化前確認此資料夾沒有獨立 Git repository；之後依已確認工作單建立本機 `main`。
- `.NET SDK 10.0.400` 已安裝；Debug 與 Release 均為 0 警告、0 錯誤。
- v2 歷史驗證：22/22 tests 與本機 WPF 雙實例 TLS/session/視窗/畫質流程曾通過；不能替代 v3 證據。
- v3 `dotnet test --configuration Release`：38/38 通過，涵蓋協定 v3、TLS loopback、SAS 結果、快捷鍵順序／限制／store、三縮放 mapper、latest-frame buffer 與固定 pipe protocol。
- v3 Release build：六個專案 0 warning、0 error；`dotnet format --verify-no-changes --no-restore` 與三個 PowerShell 腳本 parser PASS。
- v3 GUI launcher 以 Computer Use 截圖回讀，確認 48 fps、縮放、CAD、Ctrl+Alt+0 與自訂按鍵工具列；`MainWindow.xaml` 回讀確認控制端游標只有箭頭與光圈、沒有文字標籤。工具在展開選單時無法重新啟用視窗，最終互動證據維持 `UNKNOWN`。
- NuGet vulnerability audit：六個專案目前來源未回報已知弱點套件。
- self-contained win-x64 ZIP 依 source revision `9515ca698ad1b262fcd1c765dde7082099f61ade` 重建；135,966,577 bytes，SHA-256 `CFEE020B5DF15720832DD17AD760760D81483728838E5A56C378183EC10BCC9B`。
- ZIP 回讀：743 entries、743 unique、0 duplicate；`SHA256.txt` 內 9 個關鍵 EXE／DLL／腳本 hash 全數 PASS。
- `LanRemote.App.exe` 與 `LanRemote.SasService.exe` Authenticode 均為 `NotSigned`，符合未簽章測試候選定位。

### Delivery

- GitHub：`LOCAL_ONLY/NOT_CONFIGURED`
- ReadyGate（`WO-LANREMOTE-INPUT-FPS-CURSOR-V1`）：`NOT_READY` 正式發布；v3 ZIP 只供自有兩機測試，等待 Windows 10／11 雙向 GUI、SAS、安全桌面、游標與實際 FPS 證據。
- 原 MVP 的 WGC + Media Foundation H.264 效能目標仍未完成，保留為後續獨立工作項目。
