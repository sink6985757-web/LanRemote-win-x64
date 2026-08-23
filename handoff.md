# Handoff

## 目前做到哪

LanRemote 目前為本機 package v4.4.1／協定 v7 測試候選。既有遠端畫面、三種畫質、縮放、游標、檔案傳輸、文字剪貼簿、配對與安全模型未改；本輪以 Windows 低階鍵盤 hook 修復 Right Alt 與全實體鍵直通，把檔案清單選取列改成深藍綠高亮與白字，並加入原創的深藍／青綠雙螢幕連線應用程式圖示。

控制端必須點遠端畫面才進入「遠端輸入」，此時顯示 2 px 青色細框與 Toolbar 狀態。`WH_KEYBOARD_LL` 只在 LanRemote 為前景且已鎖定時攔截非 injected 實體按鍵；F11、Esc、Alt+Tab、Windows、Right Alt、Caps Lock、Num Lock 與其他按鍵都以 scan code／extended flag 送被控端，唯一保留本機的是實體 `Ctrl+Alt+Delete`。點 Toolbar／本機對話框、視窗失焦、斷線或關閉會解除 hook、送出 release-all 並回到「本機操作」。

控制端不再傳送本機 IME 完成組字；標準英文實體鍵盤只傳 physical scan code，由被控端自己的鍵盤配置、大小寫與輸入法解讀。控制端幽靈游標仍是 15% 填色、70% 白邊、縮小 20% 的空心箭頭，停止 350 ms 後淡出。

## 目前狀態

- Git：v4.4.1 source commit `5f356da46c51146fda6c47c736a02b65a99756af` 已非強制推送到 `origin/main` 並回讀一致；目前只剩本檔與相關收工文件的 follow-up checkpoint。
- v4.4.1 測試包：[artifacts/LanRemote-v4.4.1-win-x64.zip](artifacts/LanRemote-v4.4.1-win-x64.zip)，136,931,178 bytes。
- ZIP SHA-256：`85FD41ADDE5D106AFC53122D3C596AFF0597D0BA0C4430889F0223F0FDC59579`；`LanRemote.App.exe` SHA-256：`71AB957E94FE2694218F14354A012A14B1A5C72254764A068FB79527CED9DA08`。
- ZIP 回讀：746 entries、746 unique、0 duplicate、12/12 manifest PASS；包內附 `LanRemote.App.png`、`LanRemote.App.ico` 與 `ICON-PROVENANCE.md`。
- Debug／Release tests：91/91 PASS；涵蓋低階 hook routing、injected-event 忽略、Right Alt／Windows／Caps Lock、僅本機 SAS、release-all、TLS session、選取高對比色與透明多尺寸圖示 wiring。
- Debug／Release build：六專案 0 warning／0 error；`dotnet format --verify-no-changes` PASS。
- 四個 PowerShell 腳本 parser：0 error；雙機證據腳本為 schema v8。
- 六專案 NuGet vulnerability audit：目前來源未回報已知弱點套件。
- 圖示來源：[src/LanRemote.App/Assets/PROVENANCE.md](src/LanRemote.App/Assets/PROVENANCE.md)；原始與置中 PNG 為 RGBA，ICO 包含 16、20、24、32、40、48、64、128、256 px，16／32／48／256 px 的淺色與深色背景回讀可辨識。
- v4.4.1 封裝版 GUI smoke：標題列可見新圖示，高對比 launcher、無 session Tab、三種畫質、Toolbar「檔案」／剪貼簿／自訂按鍵與既有連線入口正常；額外候選視窗已關閉。
- Authenticode：應用程式與 SAS 服務均 `NotSigned`，只限自有電腦測試。
- 本輪沒有切換或中斷目前裝置上既有的 v4.4 工作階段；v4.4.1 的 Right Alt、被控端輸入法與檔案選取視覺仍待 Yulin 另行實機驗收。
- ReadyGate：v4.4.1 本機測試包為 `CONDITIONAL`，可供自有兩機驗收；本次只放行公開 source checkpoint，正式簽章 binary／GitHub Release 仍為 `NOT_READY`。
- Google Drive 仍保留三個 recoverable previous 目錄：v4.2 為 0 file、v4.4 為 33 files、v4.4.1 只有鎖定中的 `System.Printing.dll`；本輪未強制刪除。

## 唯一續跑點

在 Windows 11 25H2 與 Windows 10 22H2 使用同一份 v4.4.1 ZIP，核對 SHA-256 後完成 schema v8 實機測試：

1. 兩臺都關閉舊版並完整解壓 v4.4.1；protocol v7 不能與 v6 混用。v4.4 host 雖可與 v4.4.1 controller 交握，但完整驗收建議兩端使用同一包。
2. 配對後確認控制端直接顯示遠端畫面且持續更新；三種畫質、三種縮放、視窗化／最大化／全螢幕與幽靈游標沒有回歸。
3. 點遠端畫面，確認青色細框與「遠端輸入」；測試英文大小寫、數字、常用符號、F1～F12、Home、End、方向鍵、Tab、左右 Ctrl／Alt／Shift、Right Alt、Caps Lock、Num Lock、Windows 鍵與被控端輸入法。
4. 確認鎖定期間 F11、Esc、Alt+Tab 與 Windows 鍵都送被控端，只有實體 Ctrl+Alt+Delete 留本機；不要用自動化觸發安全桌面。點 Toolbar／對話框或切換本機視窗後，鍵盤應立即恢復本機且沒有 Ctrl／Alt 卡住。
5. 從 Toolbar「檔案」打開檔案面板，分別確認作用中及失焦選取列都是深藍綠底、白字且檔名可讀；回歸雙端主動／同時雙向傳輸、衝突模式與續傳。
6. 確認新圖示在 EXE、視窗標題列、工作列與 Alt+Tab 一致；回歸文字剪貼簿三模式、拖放、連線逾時與立即斷線。
7. 每個控制方向執行 `New-TwoPcEvidence.ps1`，把 schema v8 JSON 放回 ignored `readygate/evidence-inbox/`。

## 回復方式

- Session：任一端按「立即斷線」或關閉程式；檔案與文字剪貼簿授權隨 session 失效。
- Package：若 v4.4.1 鍵盤擷取有 critical 問題，關閉 v4.4.1 控制端並換回 v4.4；兩者都是 protocol v7。若回到 v4.3.1，兩端都必須一起切回 protocol v6。
- Source：以 Git `b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722` 檢視差異；已發布錯誤使用 `git revert <commit>`，不要 `reset --hard` 或 force push。
- SAS 服務：在對應電腦以系統管理員 PowerShell 手動執行 `uninstall-sas-service.ps1`。

## 注意事項

- 鎖定遠端輸入後沒有鍵盤退出熱鍵；請用滑鼠點 Toolbar／本機對話框或切換到其他視窗解除。
- 實體 Ctrl+Alt+Delete 永遠保留本機；要送遠端 SAS，必須明確使用 Toolbar 按鈕與固定用途服務。
- 一般 `SendInput` 仍受 UIPI 限制；不能控制較高權限視窗或 Windows 安全桌面。
- 原創圖示未使用第三方圖片或商標，provenance 可追溯，但不取代正式著作權／商標法律檢索。
- Windows 10 22H2 不在 .NET 10 官方支援清單；self-contained 可運行測試不等於 Microsoft 官方支援。
- 上層 `gogoYulin` 是多個獨立 repository 的工作區索引，不得把上層 Git 工作樹當成本專案。
- `readygate/evidence-inbox/` 含電腦名稱與私人 IP，已忽略，不得直接提交。

## 最近更新

- 時間：2026-08-23 16:26 +08:00
- 更新者：Codex
- 執行環境：runtime device（未寫入裝置識別）
- 成果 commit：`5f356da46c51146fda6c47c736a02b65a99756af`
- GitHub：`VERIFIED source checkpoint 5f356da46c51146fda6c47c736a02b65a99756af`；本檔為 follow-up shutdown docs checkpoint，不建立 tag／Release
