---
work_order: WO-LANREMOTE-V431-REMOTE-DISPLAY-FIX-20260823
status: WORK_ORDER_CONFIRMED
confirmed_at: 2026-08-23
profile: general
cycles_used: 0
---

# LanRemote v4.3.1 遠端畫面與介面修復工作單

## 目標

修復 v4.3 連線後不顯示遠端畫面的 UI 路由回歸，移除可見 session Tab、改善 launcher 對比，並為錯誤位址、交握與配對等待加入逾時和「未連線成功」對話框。

## 來源與版本

- Project：`區網windwos`
- Git baseline：`ca147365bf4a2e7dea951781ae23645399843539`
- Input package：v4.3／protocol v6 本機 dirty working tree
- Output：v4.3.1／protocol v6 self-contained win-x64 test candidate

## 授權動作

- 修改 WPF session／launcher／檔案面板介面。
- 修改控制端連線逾時及錯誤呈現。
- 新增或更新測試、證據腳本、README、CHANGELOG、handoff 與 ReadyGate 卡。
- 執行本機 build、test、format、NuGet audit、GUI smoke、package 與 ZIP readback。

## 驗收條件

1. 控制端配對後直接顯示遠端桌面，不因分頁狀態丟棄 frame。
2. 不出現工作階段概覽、遠端控制或檔案傳輸 Tab。
3. 檔案面板只從 Toolbar「檔案」進入，返回桌面不中止背景傳輸。
4. launcher 深色區文字／核取方塊具明確高對比。
5. TCP／TLS／初始協定交握 15 秒、配對人工核准 120 秒；失敗清理連線並顯示「未連線成功」。
6. 既有測試與新增回歸測試全部通過，產物可回讀。

## 限制與不納入

- 不修改 protocol v6、配對信任模型、檔案授權或安全邊界。
- 不新增固定密碼、永久 key、無人值守或公網 relay。
- 不操作 Windows Firewall、SAS 安裝或 Windows 安全設定。
- 不 commit、push、建立 remote、tag 或 release。

## 人工驗收缺口

Windows 11 25H2／Windows 10 22H2 必須以同一份 v4.3.1 ZIP 驗證第一張與持續遠端畫面、雙向角色、Toolbar 檔案面板、實際 15 秒失敗路徑及既有功能回歸。
