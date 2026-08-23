---
readygate_version: 1
profile: general
subject: LanRemote GitHub source checkpoint 與 Apache-2.0 授權
scope: 既有公開 repository 的首次 main、收工文件與遠端回讀
revision: 498c88cbb2fc5f17555121649906b5d33aca40fc
evidence_cutoff: 2026-08-23 14:01 +08:00
status: READY
workflow_state: DELIVERY_REVIEW
work_order: WO-LANREMOTE-GITHUB-APACHE2-SHUTDOWN-20260823
cycles_used: 0
reviewer: Codex；Yulin 為工作單確認者
---

# ReadyGate：LanRemote GitHub source checkpoint

## 結論

**READY** — 工作單授權的原始碼／文件 checkpoint 已通過自動驗證與公開掃描，commit `498c88cbb2fc5f17555121649906b5d33aca40fc` 已以非 force push 建立 GitHub `main`；遠端 LICENSE 與 Apache 官方原文一致，GitHub 已偵測為 `apache-2.0`。

此 `READY` 只適用 GitHub 原始碼 checkpoint。LanRemote v4.3.1 正式軟體發布仍為 **NOT_READY**，因 Windows 11 25H2／Windows 10 22H2 schema v6 雙機驗收尚未完成。

## 五道閘門

| 閘門 | 狀態 | 一句話證據／缺口 |
|---|---|---|
| G1 目的與範圍 | VERIFIED | 已確認工作單限定既有空白 public repository、Apache-2.0、source-only push 與 shutdown readback。 |
| G2 輸入與版本 | VERIFIED | 本機 Git root、`main`、baseline、目標 repository identity、v4.3.1／protocol v6 均可追溯。 |
| G3 耦合與風險 | VERIFIED | manifest 已登記 remote identity 與 `LICENSE` allowlist；未建立 Release、tag、PR 或權限變更。 |
| G4 驗證與證據 | VERIFIED | Debug／Release 61/61、Release build 0 warning／0 error、format、四個 scripts parser、NuGet audit 與公開掃描通過。 |
| G5 交付與回復 | VERIFIED | GitHub `main` SHA、預設分支、README、LICENSE 及 license metadata 已回讀；發布錯誤使用 `git revert`。 |

## Critical 證據

| 項目 | 狀態 | 證據／來源 | 範圍與版本 | 缺口 |
|---|---|---|---|---|
| 已確認工作單 | VERIFIED | `readygate/WORK_ORDER-GITHUB-APACHE2-SHUTDOWN.md` | `WO-LANREMOTE-GITHUB-APACHE2-SHUTDOWN-20260823` | 無 |
| GitHub repository | VERIFIED | `gh repo view`、`git ls-remote` | `sink6985757-web/LanRemote-win-x64`；public；default `main` | 無 |
| 原始碼 checkpoint | VERIFIED | local／remote SHA 相等 | `498c88cbb2fc5f17555121649906b5d33aca40fc` | 後續收工文件 commit 另回讀最新 SHA |
| Apache License 2.0 | VERIFIED | 本機及 GitHub contents API 與 Apache 官方文本逐字比較 | 11,357 characters；GitHub metadata `apache-2.0` | 無 |
| 自動測試與建置 | VERIFIED | `dotnet test`／`build`／`format` | Debug 61/61、Release 61/61、0 warning／0 error | 雙機 GUI 不由自動測試證明 |
| Script／dependency | VERIFIED | PowerShell parser、NuGet vulnerability audit | 四個 scripts；六個 projects | audit 只代表當次來源回報 |
| 公開內容掃描 | VERIFIED | staged／publishable scans | secret、本機名稱、裝置絕對路徑、binary diff、超過 100 MB 均為 0 | ignored evidence 仍只留本機 |
| 正式軟體發布 | NOT_READY | `handoff.md` 的雙機測試續跑點 | v4.3.1／protocol v6 | Windows 11 25H2／Windows 10 22H2 兩方向實測 |

## 例外與責任

- v4.3.1 ZIP 為 136 MB、未簽章且位於 ignored `artifacts/`；依工作單不納入 GitHub source checkpoint，也不建立 Release。
- Apache-2.0 對已取得副本的授權不可溯及撤回；未來新增第三方內容時必須重新審核 attribution／NOTICE 需求。
- Windows 10 22H2 不在 .NET 10 官方支援清單；實機可運行不等同官方支援聲明。

## 放行決定

- 決定：放行並完成 GitHub source checkpoint 與收工文件 checkpoint。
- 不放行：tag、GitHub Release、簽章發包、正式相容性或效能宣稱。
- override：無。

## 回復／交棒

- rollback：對已發布錯誤建立 `git revert <commit>`，再非強制推送；不使用 `reset --hard` 或 force push。
- 下一位：Yulin 依 `handoff.md` 在 Windows 11 25H2／Windows 10 22H2 執行 v4.3.1 schema v6 雙機驗收。
- 重新檢查條件：原始碼、授權、dependency、ZIP、協定、OS、安全模型、remote identity 或雙機證據任一項變更。

> 本卡只證明工作單範圍內的 GitHub source delivery，不取代正式軟體發布、安全審查或 Microsoft 平臺支援政策。
