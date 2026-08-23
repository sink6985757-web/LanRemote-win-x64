# Handoff

## 目前做到哪

LanRemote 已升級為協定 v4。本輪完成 session-only 雙向檔案／資料夾傳輸、VM 式拖放、檔案剪貼簿、Toolbar 雙欄路徑視窗、SHA-256／partial 續傳／不覆寫安全政策，以及 Toolbar「關閉／簡易（預設）／詳細」狀態模式。控制端青色／白邊箭頭已移除光圈並縮小約 20%，沒有文字標籤。

兩臺電腦仍採「單一 session 單向、斷線交換角色」的雙向控制模型，不是同時互控。檔案傳輸由被控端在每次配對時另行勾選，預設關閉且斷線失效；SAS 服務仍只由使用者手動安裝，程式不修改 Windows Firewall 或安全性原則。

## 目前狀態

- 封裝來源 revision：`52904ae5e381e225b046707d58e24390282f1c0d`。
- v4 測試包：[artifacts/LanRemote-v4-win-x64.zip](artifacts/LanRemote-v4-win-x64.zip)，136,069,681 bytes。
- ZIP SHA-256：`BAF18E575BB6AE8D88E6B1945D2CD4F9139BFE1A8F0A8DDE9844BE4FFE395B37`。
- ZIP 回讀：743 entries、743 unique、0 duplicate、9/9 個關鍵檔案 manifest hash PASS；內嵌 README 可見協定 v4 與三種狀態模式。
- 已驗證：Release 47/47 tests、六專案 build 0 warning／0 error、format、四個 PowerShell 腳本 parser、六專案 NuGet vulnerability audit。
- TLS loopback：實際完成跨 chunk 上傳、反向下載、遠端瀏覽、exact bytes、同名改名、不授權拒絕、partial resume offset／清理、磁碟根目錄目的地與內容指紋識別。
- GUI smoke：底部狀態列已移除；Toolbar 簡易摘要預設且固定可見；詳細薄列與關閉模式均實際切換成功。
- GUI 限制：啟動 host 時出現 Windows Firewall 系統提示；依工作單未修改安全設定，因此配對檔案 opt-in、連線中拖放／傳輸視窗／剪貼簿仍待人工雙機證據。
- 未驗證：Windows 10／11 兩個控制方向、實體網路中斷續傳、SAS 安裝／安全桌面 CAD、連線後雙游標與三模式實際 FPS。
- Authenticode：應用程式與服務均 `NotSigned`，只限自有電腦測試。
- ReadyGate：`NOT_READY` 正式發布；無 release override；GitHub `LOCAL_ONLY/NOT_CONFIGURED`。

## 唯一續跑點

1. 兩臺都換成同一份 v4 ZIP，核對 SHA-256 後完整解壓縮並關閉舊版。
2. 先測 Windows 11 控制 Windows 10：核對六位配對碼，在被控端勾選允許檔案傳輸，再驗證拖放、Toolbar 上傳／下載、檔案剪貼簿、取消保留 partial 後重連續傳、三種狀態、雙游標與既有鍵鼠／快捷鍵／縮放／FPS。
3. 斷線交換角色，再測 Windows 10 控制 Windows 11；需要 CAD 的被控端由本人手動安裝 SAS 服務並測試。
4. 每方向用 `New-TwoPcEvidence.ps1` 產生 schema v2 JSON，複製回 ignored `readygate/evidence-inbox/`。
5. 回到本專案執行 `startup`，由後續 Agent 唯讀回收證據並重算 ReadyGate。

## 回復方式

- Session：任一端按「立即斷線」或關閉程式。
- 檔案：取消傳輸時選擇刪除 partial；保留則供相同來源、內容與目的地在下次連線續傳。
- SAS 服務：在對應電腦以系統管理員 PowerShell 手動執行 `uninstall-sas-service.ps1`；腳本不刪程式檔、不回改 policy。
- Source：使用 `git revert 52904ae d21cb91 0b2ad34` 建立可追蹤回復；不要 `reset --hard`。

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

- 時間：2026-08-23 10:00 +08:00
- 更新者：Codex
- 電腦：YULIN-SFG16-72
- GitHub：`NOT_CONFIGURED`
