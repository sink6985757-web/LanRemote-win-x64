---
work_order: WO-LANREMOTE-V441-LIVE-QA-KEYBOARD-HOOK-20260823
status: WORK_ORDER_CONFIRMED
confirmed_at: 2026-08-23
profile: general
cycles_used: 3
---

# LanRemote v4.4.1 實機 QA、全鍵盤擷取與原創圖示工作單

## 目標

在不改動畫面擷取、畫質、檔案傳輸、剪貼簿、配對與安全模型的前提下，先對目前已連線的 v4.4／protocol v7 工作階段收集安全的功能證據，再以 Windows 低階鍵盤攔截器修復 Right Alt 與全實體鍵直通，並加入原創、可辨識且低版權風險的 Windows 應用程式圖示，產出 v4.4.1／protocol v7 的本機測試候選。

## 來源與版本

- Project：`區網windwos`
- Git baseline：`b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722`
- Live input：package v4.4／protocol v7，控制方向維持目前連線，不重新連線或交換角色。
- Output：package v4.4.1／protocol v7 self-contained win-x64 test candidate；鍵盤 payload 不變。

## 已確認選擇

- 先測目前連線中的 v4.4，收集畫面、輸入、狀態、剪貼簿與小型檔案傳輸證據；修改後由 Yulin 另行人工測試。
- 使用標準英文實體鍵盤的 scan code 直通，由被控端自己的鍵盤配置、Caps Lock 與輸入法解讀；不傳送控制端拼音輸入法的 Unicode 組字結果。
- 鎖定遠端輸入時，所有實體按鍵都送到被控端，包括 F11、Esc、Alt+Tab、左右 Alt／Ctrl／Shift、Windows、Caps Lock 與 Num Lock。
- 唯一保留給本機的組合鍵是實體 `Ctrl+Alt+Delete`；Toolbar 的 SAS 按鈕仍是明確送給被控端的另一條路徑。
- 解除遠端輸入使用滑鼠點擊 Toolbar／本機對話框或視窗失焦；不提供會攔截其他實體鍵的本機退出熱鍵。
- 檔案傳輸的本機來源與對方檔案清單保留選取高亮，但改用深色藍綠底與高對比白字，避免選取後看不見檔名。
- 圖示採兩臺互相連線的螢幕與中央通道概念，深藍＋青綠、透明背景、圓角幾何；風格稍微可愛但仍專業，帶 AI 時代的節點／柔光語彙，不使用文字、第三方素材、商標或浮水印。
- 圖示完整整合到 EXE、視窗標題列、工作列、Alt+Tab 與 v4.4.1 ZIP，並在專案保存原始 PNG、多尺寸 ICO、最終提示與 provenance 紀錄。

## 授權動作

- 對目前 v4.4 工作階段做安全、可觀察且可回復的控制方向測試；工作階段保持連線。
- 測試遠端畫面、游標、輸入鎖定狀態、英數鍵、F5、Home／End／方向鍵、左右修飾鍵、Right Alt、被控端輸入法、畫質／縮放／視窗／狀態顯示、純文字剪貼簿及小於 1 KB 的 QA 檔案上下載。
- 新增 `WH_KEYBOARD_LL` 實體鍵攔截器、前景視窗檢查、注入事件忽略、按鍵狀態與 release-all 清理。
- 修改 WPF 輸入路由與 Windows `SendInput` Windows-key 限制，使鎖定期間除本機 SAS 外的按鍵全部送遠端。
- 調整檔案傳輸清單的 active／inactive selection 顏色；不修改檔案傳輸資料模型或操作流程。
- 使用內建圖像生成工具從零產生一個透明背景圖示，檢查小尺寸辨識度後轉成 Windows 16～256 px 多尺寸 ICO。
- 修改 WPF project／window icon 資源、發包內容、UI source-contract 測試與圖示 provenance 文件。
- 新增／更新 hook、mapper、protocol、TLS loopback、輸入狀態與 UI source-contract 測試。
- 更新 README、CHANGELOG、handoff、package README、evidence script 與 ReadyGate 卡。
- 執行 Debug／Release test、build、format、PowerShell parser、NuGet audit、package、ZIP／manifest／Authenticode 回讀。

## 實機測試安全界線

- 不操作本機或遠端的 Ctrl+Alt+Delete、安全桌面、UAC、Firewall、防毒／安全設定、麥克風權限或實際錄音。
- Right Alt 僅觀察被控端語音功能選單是否出現；不啟動錄音或傳送語音。
- 文字輸入使用被控端未儲存的 Notepad／等價空白測試區；不保存到既有使用者文件。
- 剪貼簿只寫入固定 QA 純文字標記，不讀取或回報原有剪貼簿內容。
- 檔案測試只建立固定內容的 QA 檔，保留測試檔、不刪除既有資料。
- 不測角色反轉、不重新連線、不把目前工作階段切換成修正版。

## 驗收條件

1. 目前 v4.4 的安全功能測試結果有逐項 PASS／FAIL／NOT_TESTED 證據，且沒有中斷工作階段或觸及安全桌面。
2. v4.4.1 鎖定遠端輸入且程式為前景時，非注入的實體按鍵只送遠端，不再被控制端的 WPF／本機快捷鍵消費。
3. Right Alt、左右修飾鍵、功能鍵、extended keys 與 Windows keys 保留正確 scan code／extended／down-up 順序。
4. 實體 Ctrl+Alt+Delete 不送遠端並維持本機 SAS；不以自動化實際觸發安全桌面。
5. 點 Toolbar／本機對話框、視窗失焦、斷線或關閉時解除攔截、送出 release-all 並清空本機按鍵狀態。
6. 忽略低階 hook 的 injected events，避免合成輸入回圈；WPF fallback 不造成實體按鍵重複。
7. 既有功能回歸、自動測試與 v4.4.1 package 完整性通過。
8. 檔案清單選取列仍明顯可辨，且 active／inactive 狀態下檔名皆保持可讀。
9. 圖示在 16、32、48、256 px 皆能辨識兩臺裝置與雙向連線概念，深／淺色 Windows 背景可讀，且 EXE、視窗、工作列與 Alt+Tab 使用同一圖示。
10. 圖示不包含可辨識的第三方品牌元素；原始 PNG、ICO、生成提示與處理紀錄可追溯。

## 限制與不納入

- protocol 仍為 v7；兩臺必須使用 protocol v7，但修正版的低階擷取只影響控制端。
- 不修改畫面擷取、解析度、FPS、縮放、檔案傳輸或剪貼簿資料模型。
- `Ctrl+Alt+Delete` 是否進入 Windows 本機安全桌面不以自動化驗證，只以程式路由與人工驗收確認。
- Windows 低階 hook 不能繞過較高完整性程式、UIPI 或 Windows 安全桌面。
- 原執行範圍不包含 commit、push、tag、release 或 GitHub 變更；後續 source checkpoint 另依下方收工交付授權執行。
- 不申請商標、不進行正式法律意見或全球商標相似性檢索；原創生成與 provenance 可降低第三方素材風險，但不冒充正式法律保證。

## 收工交付授權（2026-08-23 後續確認）

- Yulin 已明確要求「這版先做收工，上傳 GitHub」，並補充 README 要以 Open Source 方式公開給大家使用。
- 授權範圍：目前 v4.4.1 原始碼、測試、Apache-2.0 公開 README、原創圖示資產、工作單、Gate Card、CHANGELOG 與 handoff，非強制推送到 identity-matching 的既有 `origin/main`。
- 明確排除：ignored `artifacts/` 測試 ZIP／build outputs、`readygate/evidence-inbox/` 私人雙機 evidence、Google Drive previous 目錄、tag、GitHub Release、簽章、權限變更與正式相容性／效能宣稱。
- 必須先通過 allowlist、secret／私人資料、大檔、測試、remote alignment 與 staged diff 回讀，push 後以遠端 SHA 回讀才算完成。

## 回復

- Package：關閉 v4.4.1 控制端並換回目前可連線的 v4.4；protocol 仍為 v7。
- Source：以 Git `b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722` 為基準檢視差異；未授權發布。
- Session：任何失焦、斷線、關閉或攔截器錯誤都必須解除 hook、release-all 並回到本機輸入。
