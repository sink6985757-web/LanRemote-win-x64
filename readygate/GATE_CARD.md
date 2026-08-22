---
readygate_version: 1
profile: general
subject: LanRemote v2 VNC-style UX 與畫質模式本機測試候選
scope: Windows 11 25H2 與 Windows 10 22H2 私人區網換角雙向控制
revision: 11ea76e28ce7d28064260ff3f49d9c1dfcd9f98a
evidence_cutoff: 2026-08-23 00:15 +08:00
status: CONDITIONAL
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-UX-PROFILES-V1
cycles_used: 2
reviewer: Codex (Agent); Yulin 為工作單確認者、舊版連線回報者與新版實機操作者
---

# ReadyGate：LanRemote v2 UX 測試候選

## 結論

**CONDITIONAL** — 協定 v2、單視窗工作區、三種視窗模式、工具列與畫質熱切換已有可回讀的本機證據；新版尚缺 Windows 10／11 兩個方向的跨裝置驗證，因此停止正式發布。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | `WORK_ORDER-UX-PROFILES.md` 固定單視窗 UX、三種視窗／畫質模式、安全邊界與 GDI/JPEG 本輪範圍。 |
| G2 輸入與版本 | PARTIAL | source revision、協定 v2 與 ZIP／EXE 雜湊可回讀；Windows 10 22H2 仍是未驗證平臺例外。 |
| G3 耦合與風險 | VERIFIED | TLS/SAS、私人 IPv4、每次核准、UIPI、canonical profile 與舊協定拒絕均保留。 |
| G4 驗證與證據 | CONDITIONAL | Release 22/22 與 Windows 11 本機雙實例 GUI 全流程通過；新版跨裝置兩個方向為 UNKNOWN。 |
| G5 交付與回復 | CONDITIONAL | self-contained ZIP、SHA-256、操作與證據腳本齊全；未簽章、無 remote，正式發布停止。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 已確認工作單 | VERIFIED | `readygate/WORK_ORDER-UX-PROFILES.md` | WO-LANREMOTE-UX-PROFILES-V1 | 無 |
| 可回讀原始碼 | VERIFIED | Git `11ea76e28ce7d28064260ff3f49d9c1dfcd9f98a`；implementation `b976710` | 本機 `main` | 無 remote，維持 local-only |
| 自動驗證 | VERIFIED | Release 22/22、Debug build、format、NuGet audit、self-contained EXE 啟動 | .NET SDK 10.0.400 | 尚未在第二臺重跑 |
| 同視窗與模式切換 | VERIFIED | Windows 11 本機兩個真實 EXE、TLS/SAS、持續 frame readback | v2；視窗化／最大化／F11／工具列 | 跨裝置 UI 仍待實測 |
| 畫質熱切換 | VERIFIED | session 內平衡→流暢→畫質→平衡；狀態回讀 720p30／1080p15／900p24 | GDI/JPEG canonical profiles | 跨裝置流量與效能待觀察 |
| v2 self-contained 包 | VERIFIED | ZIP `5C971B6A...5A216`；EXE `32CE7CD6...020E` | 79,784,941 bytes；win-x64 | 未簽章，只限自有電腦測試 |
| 舊版雙機連線 | REPORTED | 使用者回報兩臺測試正常 | 舊版，非 v2 candidate | 不可替代新版證據 |
| Windows 11 → Windows 10 v2 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 跨裝置畫面、輸入、UX、三模式 | 使用者執行 |
| Windows 10 → Windows 11 v2 | UNKNOWN | 尚無 `readygate/evidence-inbox` JSON | 斷線換角後重測 | 使用者執行 |

## 最短補強路徑

1. 兩臺都關閉舊版並改用同一份 v2 ZIP，先測 Windows 11 控制 Windows 10。
2. 斷線交換角色，測 Windows 10 控制 Windows 11；兩個方向都跑視窗、工具列與三種畫質切換。
3. 回收兩個方向的 evidence JSON 與雜湊後，重新產生 Gate Card；效能 H.264 另開工作單。

## 例外與責任

- Windows 10 22H2 非 .NET 10 官方支援 OS，且 v2 尚未跨機實測 — owner：Yulin；期限：下一次雙機測試；核准：未核准正式發布例外。
- 本輪使用 GDI/JPEG，WGC／H.264 為明確排除的後續工作 — owner：後續 implementation agent；期限：效能工作單確認後；核准：只允許本機測試候選。

## 放行決定

- 決定：停止正式 delivery／release；本機 ZIP 只保留為已確認工作單內的自有兩機測試候選。
- override：無。

## 回復／交棒

- rollback：任一端按「立即斷線」或關閉程式；移除解壓縮資料夾即可撤回測試包；程式碼以 `git revert` 留下可追蹤回復。
- 下一位：Yulin 在 Windows 10／11 執行 v2 雙向測試，後續 Agent 唯讀審查 evidence。
- 重新檢查條件：原始碼、SDK/runtime、ZIP／EXE 雜湊、Windows build、網路拓撲、安全模型或工作單任一項變更。

> 本卡是證據式準備度摘要，不取代 Microsoft 平臺支援政策、安全審查或合格專業人員正式 sign-off。
