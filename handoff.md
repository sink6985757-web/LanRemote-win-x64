# Handoff

## 目前做到哪

LanRemote 已升級為協定 v3。本輪完成 60／48／30 fps、latest-frame drop、Fit／Stretch／Crop、被控端原生游標、無文字標籤的青色控制端游標、工具列 `Ctrl+Alt+0`、最多 20 組本機自訂按鍵，以及固定用途 `Ctrl+Alt+Delete` Windows 服務。

兩臺電腦仍採「單一 session 單向、斷線交換角色」的雙向控制模型，不是同時互控。SAS 服務只在使用者手動以系統管理員執行安裝腳本後啟用；應用程式不會自動安裝服務、改 Windows policy 或操作安全桌面。

## 目前狀態

- source revision：`9515ca698ad1b262fcd1c765dde7082099f61ade`。
- 測試包：[artifacts/LanRemote-win-x64.zip](artifacts/LanRemote-win-x64.zip)，135,966,577 bytes。
- ZIP SHA-256：`CFEE020B5DF15720832DD17AD760760D81483728838E5A56C378183EC10BCC9B`。
- ZIP 回讀：743 entries、743 unique、0 duplicate、9 個關鍵檔案 manifest hash 全數 PASS。
- 已驗證：Release 38/38 tests、六專案 build 0 warning／0 error、format、PowerShell parser、NuGet audit、固定 pipe protocol、快捷鍵限制／store、三縮放 mapper、latest-frame buffer。
- GUI／source 回讀：launcher 工具列可見 48 fps、縮放、CAD、Ctrl+Alt+0、自訂按鍵；XAML 的控制端游標只有箭頭與光圈，沒有「控制」文字標籤。
- GUI 限制：Computer Use 展開選單時回報 `failed to activate captured window`，依技能規則停止；最終 v3 選單、session、雙游標與縮放互動仍待人工證據。
- 未驗證：Windows SCM 實際安裝、Local Security Policy、安全桌面 CAD、Windows 10／11 兩個控制方向、三模式實際 FPS。
- Authenticode：應用程式與服務均 `NotSigned`，只限自有電腦測試。
- ReadyGate：`NOT_READY` 正式發布；無 release override；GitHub `LOCAL_ONLY/NOT_CONFIGURED`。

## 唯一續跑點

1. 兩臺都換成同一份 v3 ZIP，完整解壓縮並關閉舊版。
2. 若某臺作為被控端時要接受 `Ctrl+Alt+Delete`，在該臺以系統管理員 PowerShell 手動執行 `install-sas-service.ps1`；需要時由本人依 README 設定 Windows policy 允許 Services。
3. 先測 Windows 11 控制 Windows 10：核對六位配對碼，測 CAD、Ctrl+Alt+0、自訂按鍵新增／送出／移除、Fit／Stretch／Crop、雙游標、60／48／30 fps 與立即斷線。
4. 斷線交換角色，再測 Windows 10 控制 Windows 11。
5. 每方向用 `New-TwoPcEvidence.ps1` 產生 JSON，複製回 `readygate/evidence-inbox/`；回到本專案執行 `startup`，唯讀回收證據並重算 ReadyGate。

## 回復方式

- Session：任一端按「立即斷線」或關閉程式。
- SAS 服務：在對應電腦以系統管理員 PowerShell 手動執行 `uninstall-sas-service.ps1`；腳本不刪程式檔、不回改 policy。
- Source：使用 `git revert 9515ca6` 建立可追蹤回復；不要 `reset --hard`。

## 後續獨立工作

- 以 Windows Graphics Capture + Media Foundation H.264／硬體編碼取代 GDI/JPEG，再建立 1080p30 與互動延遲 p95 的效能基線。
- 公開發布前另行處理程式簽章、受支援 OS 聲明、安裝器與發布授權。

## 注意事項

- 協定 v3 不向下相容，兩臺必須同時使用本輪新版。
- 安裝服務後不要移動或刪除解壓縮資料夾；需移動時先卸載、移動後再安裝。
- 一般 `SendInput` 仍受 UIPI 限制；CAD 只走固定用途服務。
- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案 repository。
- `readygate/evidence-inbox/` 包含電腦名稱與私人 IP，已加入 `.gitignore`，不得直接提交。

## 最近更新

- 時間：2026-08-23 08:39 +08:00
- 更新者：Codex
- 電腦：YULIN-SFG16-72
- GitHub：`NOT_CONFIGURED`
