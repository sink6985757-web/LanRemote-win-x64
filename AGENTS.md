# LAN Windows Remote Control

## 目標

建立一個只在受信任區域網路內使用的 Windows 應用程式，讓兩臺已明確配對的電腦可以在使用者同意下互相觀看畫面並控制滑鼠與鍵盤。

## 專案結構

- `src/LanRemote.Protocol/`：版本化 framing、payload 與輸入／畫面訊息。
- `src/LanRemote.Core/`：私人區網政策、TLS/SAS pairing 與 session orchestration。
- `src/LanRemote.Windows/`：Windows 畫面擷取與受限輸入注入。
- `src/LanRemote.SasService/`：固定用途的 Windows Ctrl+Alt+Delete 服務，不接受任意命令。
- `src/LanRemote.App/`：同一套 WPF 程式的被控端／控制端 UI。
- `tests/LanRemote.Tests/`：單元測試與 TLS loopback 整合測試。
- `scripts/`：self-contained 發包與兩機證據收集。
- `readygate/`：已確認工作單與交付證據。
- `README.md`：人類與 Agent／Tool 安裝、使用及公開版本文案。
- `CHANGELOG.md`：每次收工的近期修改與 delivery 狀態。
- `handoff.md`：目前狀態、下一步與唯一續跑點。
- `.agents/project-lifecycle.json`：可攜式生命週期、Git 與 checkpoint 契約。

## 安全與產品邊界

1. 只接受已驗證且由本機使用者明確允許的配對；不得提供隱蔽監控或未授權控制。
2. 控制期間必須持續顯示可見狀態，並提供本機立即中止連線的方式。
3. 網路通道必須加密、驗證對端身分並防止重放；不得把固定密碼、私鑰或憑證提交進 Git。
4. MVP 不包含網際網路 relay、無人值守存取、UAC／安全桌面繞過、提權、檔案傳輸、剪貼簿或音訊。
5. 畫面擷取、編碼、傳輸、輸入注入及 UI 必須分層，協定訊息需有版本與長度限制。
6. 先以單機 loopback 與模擬輸入測試，再進行兩臺電腦的人工授權測試。
7. 技術棧、權限模型、配對流程或上述邊界若要改變，先以 ReadyGate 確認工作單。
8. 目前 GDI/JPEG 只視為相容垂直切片；在 WGC + Media Foundation H.264 與雙機效能證據完成前，不得宣稱達成 1080p30／p95 200 ms。
9. 線上協定目前為 v3；同一 session 兩端必須使用相同主要版本，不得對舊版靜默降級。
10. 遠端桌面維持單一 WPF 視窗工作區；視窗化、最大化、無邊框全螢幕、可隱藏工具列與 Fit／Stretch／Crop 座標映射是穩定 UX 邊界。
11. GDI/JPEG 的流暢、平衡、畫質參數由 `QualityProfiles` 集中定義；協定端只接受 canonical profile，不得由對端任意提高擷取負載。
12. `Ctrl+Alt+Delete` 只經由使用者手動安裝的固定用途 LocalSystem 服務與本機 named pipe；程式不得自動安裝服務、修改安全性原則或加入任意提權命令。

## 共用規則

1. 每次開工先讀本檔、`handoff.md` 與 Git 狀態。
2. 保留既有修改；不提交 secret、credential、憑證、裝置個資或未知檔案。
3. canonical 路徑使用專案相對路徑。
4. 每次收工更新 `CHANGELOG.md` 與 `handoff.md`。
5. GitHub delivery 前更新 `README.md`，並依工作單／ReadyGate 放行。

## 生命週期路由

- `initial`：只負責第一次治理結構、缺件修復或技能部署；完成後停止。
- `startup`：每次開工唯讀回報，完成後等待工作選擇。
- `shutdown`：每次收工更新版本紀錄與交接；是否 checkpoint 由專案 manifest 的 `manual`／`standing_scoped` 決定。
- `ReadyGate`：功能開發、權限變更、兩機測試、發布或其他高風險／外部動作前使用。

## 整合

- GitHub：`NOT_CONFIGURED`
- Git：本目錄是獨立本機 repository，預設 branch `main`；不得把上層 workspace Git 當成本專案。
- 生命週期 manifest：`.agents/project-lifecycle.json`
- 外部知識庫：`ON_DEMAND_ONLY`，不屬於專案生命週期。
