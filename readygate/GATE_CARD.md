---
readygate_version: 1
profile: general
subject: LanRemote v4.1 雙向檔案傳輸、Toolbar 狀態與遠端剪貼簿快捷鍵測試候選
scope: Windows 11 25H2 與 Windows 10 22H2 私人區網換角雙向控制
revision: 5a2ce98e420bedb0df060e86afabb2a101f5afd0
evidence_cutoff: 2026-08-23 10:41 +08:00
status: NOT_READY
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-V4-FILE-TRANSFER-UX-20260823
cycles_used: 3
reviewer: Codex (Agent); Yulin 為工作單確認者與雙機實測 owner
---

# ReadyGate：LanRemote v4.1 測試候選

## 結論

**NOT_READY** — v4.1 原始碼、50 項測試、Toolbar 快捷鍵開關 smoke、唯一-entry ZIP 與 9 個關鍵檔案雜湊皆可回讀；既有檔案傳輸由 Yulin 回報正常，但新版 `Ctrl+C／X／V` 與完整 Windows 10／11 雙向回歸仍缺結構化證據，因此只可供自有電腦測試，停止正式發布。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | 已確認工作單固定 session opt-in 雙向傳檔、三種入口、安全限制、游標調整與 Toolbar「關閉／簡易（預設）／詳細」。 |
| G2 輸入與版本 | VERIFIED | Git `5a2ce98`、.NET SDK 10.0.400、LRM4／package v4.1 與 ZIP `B55C4165…36F3247` 可追溯。 |
| G3 耦合與風險 | VERIFIED | 收檔預設關閉且斷線失效；限制大小、SHA-256、不覆寫、受保護路徑與 reparse／UNC／ADS 拒絕均已進入程式及測試。 |
| G4 驗證與證據 | NOT_READY | Release 50/50、build、format、parser、NuGet audit、loopback 快捷鍵與 UI smoke 通過；兩機快捷鍵／SAS／FPS 回歸仍缺 schema v3 證據。 |
| G5 交付與回復 | CONDITIONAL | self-contained ZIP、內嵌 SHA-256、README、安裝／移除及 evidence 腳本齊全；EXE 未簽章、無 remote，正式發布停止。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 已確認工作單 | VERIFIED | `readygate/WORK_ORDER-FILE-TRANSFER-STATUS-V4.md` | WO-LANREMOTE-V4-FILE-TRANSFER-UX-20260823；3 cycles | 已達 ReadyGate cycle 上限；新範圍需新工作單 |
| 可回讀原始碼 | VERIFIED | Git `5a2ce98e420bedb0df060e86afabb2a101f5afd0` | 本機 `main`；協定 LRM4／package v4.1 | 無 remote，維持 local-only |
| 自動驗證 | VERIFIED | Release 50/50；六專案 build 0 warning／0 error；format 與四個 PowerShell 腳本 parser PASS | protocol、TLS loopback、上傳／下載、續傳、按鍵原子序列、既有輸入與 SAS pipe | Windows GUI 行為不能由單元測試完全證明 |
| 依賴風險 | VERIFIED | `dotnet list package --vulnerable --include-transitive` | 六個專案 | NuGet 來源未回報已知弱點套件 |
| v4.1 發包完整性 | VERIFIED | ZIP 136,070,962 bytes；SHA-256 `B55C41650F77749A820381B6AAA61CC6E6EBC1338B7E2CFAAFBAD793A36F3247` | 743 entries、743 unique、0 duplicate；9/9 manifest hash PASS | EXE／服務均 `NotSigned`，只限自有電腦測試 |
| 檔案協定與資料一致性 | VERIFIED | 47 項測試中的真實 TLS loopback | 跨 256 KiB chunk 上傳、反向下載、遠端瀏覽、精確 bytes、同名改名、不授權拒絕、partial offset／清理 | 仍需兩機 GUI 與網路中斷實測 |
| Toolbar 與快捷鍵開關 | VERIFIED | Computer Use 主視窗 smoke；`MainWindow.xaml` 回讀 | `C/X/V 直通` 固定可見且 `IsChecked=True`；檔案按鈕相鄰；三段狀態與游標規格保留 | 連線後快捷鍵實際效果、FPS、雙游標仍待雙機目視 |
| 既有雙機檔案傳輸 | REPORTED | Yulin 本輪回報「檔案傳輸那邊也 OK」 | 先前 v4 使用體驗 | 尚無 schema v3 JSON／hash／方向明細，不升級為 VERIFIED |
| 配對 opt-in 與連線中檔案 UI | UNKNOWN | 啟動 host 時出現 Windows Firewall 系統提示；依工作單與 Computer Use 規則未操作安全 UI | 配對勾選、拖放、Toolbar 視窗、檔案剪貼簿、取消／續傳 | Yulin 在兩臺自有電腦人工允許私人網路並驗證 |
| v4.1 Windows 11 → Windows 10 | UNKNOWN | 尚無 schema v3 evidence JSON | `Ctrl+C／X／V`、關閉後檔案貼上、畫面、鍵鼠、游標、縮放、FPS | Yulin 執行 |
| v4.1 Windows 10 → Windows 11 | UNKNOWN | 尚無 schema v3 evidence JSON | 斷線換角後重測全部 critical 行為 | Yulin 執行 |
| SAS 服務與安全桌面 | UNKNOWN | 未安裝服務、未修改 policy、未自動操作安全桌面 | Windows 10／11 兩個角色方向 | Yulin 需要時手動安裝並驗證 CAD |

## 最短補強路徑

1. 兩臺都換成同一份 v4.1 ZIP 並核對 SHA-256；由 Yulin 自行決定 Windows Firewall 私人網路提示。
2. 先測 Windows 11 控制 Windows 10，再斷線交換角色；每方向驗證開關開啟時 `Ctrl+C／X／V` 直通、關閉時本機檔案 `Ctrl+V` 上傳，以及既有鍵鼠／快捷鍵／縮放／FPS。
3. 需要 CAD 時在被控端手動安裝 SAS 服務並測試；兩方向各執行 `New-TwoPcEvidence.ps1` 產生 schema v3 JSON。
4. 把 JSON 回收至 ignored `readygate/evidence-inbox/`，由後續 Agent 唯讀回讀並重算 Delivery Gate；公開發布另需簽章與新授權。

## 例外與責任

- Windows 10 22H2、Windows Firewall、雙向 GUI 與網路中斷續傳為人工實機驗證 — owner：Yulin；期限：下一次雙機測試；核准：僅核准自有電腦測試候選。
- GDI/JPEG 的 60／48／30 fps 是目標上限，不是已證明效能 — owner：Yulin 實測、後續 Agent 回讀；期限：下一次雙機測試；核准：未核准效能宣稱。
- EXE 與服務 `NotSigned` — owner：未指派；期限：公開發布前；核准：未核准公開發布。

## 放行決定

- 決定：停止正式 delivery／release；依已確認工作單保留本機 v4.1 ZIP，供 Yulin 在自有兩臺電腦執行測試。
- override：無。

## 回復／交棒

- rollback：任一端立即斷線或關閉程式；可改回 v4 ZIP；SAS 服務用 `uninstall-sas-service.ps1` 移除；原始碼以 `git revert 5a2ce98` 回復本輪快捷鍵修正。
- 下一位：Yulin 執行 v4.1 Windows 10／11 雙向人工測試；後續 Agent 唯讀回讀 schema v3 evidence。
- 重新檢查條件：原始碼、ZIP／manifest hash、SDK/runtime、Windows build、服務／policy、網路拓撲、安全模型或工作單任一項變更。

> 本卡是證據式準備度摘要，不取代 Microsoft 平臺支援政策、安全審查或合格專業人員正式 sign-off。
