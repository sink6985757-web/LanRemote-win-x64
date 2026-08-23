# Handoff

## 目前做到哪

LanRemote 目前為 package v4.1／協定 v4。本輪在既有雙向檔案傳輸上加入 Toolbar `C/X/V 直通` 開關，預設開啟；控制端直接按 `Ctrl+C／X／V` 會操作被控端自己的剪貼簿。關閉開關後，本機檔案 `Ctrl+V` 才恢復跨機上傳。這不是背景文字／圖片剪貼簿同步。

兩臺電腦仍採「單一 session 單向、斷線交換角色」的雙向控制模型，不是同時互控。檔案傳輸由被控端在每次配對時另行勾選，預設關閉且斷線失效；SAS 服務仍只由使用者手動安裝，程式不修改 Windows Firewall 或安全性原則。

## 目前狀態

- 封裝來源 revision：`5a2ce98e420bedb0df060e86afabb2a101f5afd0`。
- v4.1 測試包：[artifacts/LanRemote-v4.1-win-x64.zip](artifacts/LanRemote-v4.1-win-x64.zip)，136,070,962 bytes。
- ZIP SHA-256：`B55C41650F77749A820381B6AAA61CC6E6EBC1338B7E2CFAAFBAD793A36F3247`。
- ZIP 回讀：743 entries、743 unique、0 duplicate、9/9 個關鍵檔案 manifest hash PASS；內嵌 README 可見 v4.1 與快捷鍵開關說明。
- 已驗證：Release 50/50 tests、六專案 build 0 warning／0 error、format、四個 PowerShell 腳本 parser、六專案 NuGet vulnerability audit。
- TLS loopback：實際完成跨 chunk 上傳、反向下載、遠端瀏覽、exact bytes、同名改名、不授權拒絕、partial resume offset／清理、磁碟根目錄目的地與內容指紋識別。
- GUI smoke：Toolbar 固定顯示 `C/X/V 直通` 與 `⇄ 檔案`；開關 XAML 預設開啟。底部狀態列移除及三種狀態模式仍保留。
- 使用者回報：既有檔案傳輸使用正常；目前只有對話回報，尚未回收 schema v3 evidence。
- GUI 限制：啟動 host 時出現 Windows Firewall 系統提示；依工作單未修改安全設定，因此配對檔案 opt-in、連線中拖放／傳輸視窗／剪貼簿仍待人工雙機證據。
- 未驗證：v4.1 `Ctrl+C／X／V` 在 Windows 10／11 兩個控制方向的實際效果、關閉開關後檔案貼上回歸、SAS／CAD、雙游標與三模式實際 FPS。
- Authenticode：應用程式與服務均 `NotSigned`，只限自有電腦測試。
- ReadyGate：`NOT_READY` 正式發布；無 release override；GitHub `LOCAL_ONLY/NOT_CONFIGURED`。

## 唯一續跑點

1. 兩臺都換成同一份 v4.1 ZIP，核對 SHA-256 後完整解壓縮並關閉舊版。
2. 先測 Windows 11 控制 Windows 10：保持 `C/X/V 直通` 開啟，在遠端記事本測 `Ctrl+C／X／V`；再關閉開關，測本機檔案總管複製檔案後對遠端畫面按 `Ctrl+V`。
3. 斷線交換角色，再測 Windows 10 控制 Windows 11；需要 CAD 的被控端由本人手動安裝 SAS 服務並測試。
4. 每方向用 `New-TwoPcEvidence.ps1` 產生 schema v3 JSON，複製回 ignored `readygate/evidence-inbox/`。
5. 回到本專案執行 `startup`，由後續 Agent 唯讀回收證據並重算 ReadyGate。

## 回復方式

- Session：任一端按「立即斷線」或關閉程式。
- 檔案：取消傳輸時選擇刪除 partial；保留則供相同來源、內容與目的地在下次連線續傳。
- SAS 服務：在對應電腦以系統管理員 PowerShell 手動執行 `uninstall-sas-service.ps1`；腳本不刪程式檔、不回改 policy。
- Source：只回復本輪快捷鍵修正時使用 `git revert 5a2ce98`；不要 `reset --hard`。

## 後續獨立工作

- 以 Windows Graphics Capture + Media Foundation H.264／硬體編碼取代 GDI/JPEG，再建立 1080p30 與互動延遲 p95 的效能基線。
- 公開發布前另行處理程式簽章、受支援 OS 聲明、安裝器與發布授權。

## 注意事項

- 協定 v4 不向下相容，兩臺必須同時使用本輪新版。
- 安裝 SAS 服務後不要移動或刪除解壓縮資料夾；需移動時先卸載、移動後再安裝。
- 一般 `SendInput` 仍受 UIPI 限制；CAD 只走固定用途服務。
- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案 repository。
- `readygate/evidence-inbox/` 包含電腦名稱與私人 IP，已加入 `.gitignore`，不得直接提交。

## 最近更新

- 時間：2026-08-23 10:41 +08:00
- 更新者：Codex
- 電腦：YULIN-SFG16-72
- GitHub：`NOT_CONFIGURED`
