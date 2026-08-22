LanRemote 區網雙向控制 MVP
===========================

這是一份未簽章的本機測試包，不是公開發布版本。

重要：本包使用協定 v2，兩臺電腦都必須換成這一份新版。舊版不能和新版混用。

適用情境
--------
- 同一個私人區網內的兩臺 x64 Windows 電腦。
- 本次測試目標：Windows 11 25H2 與 Windows 10 22H2。
- Windows 10 22H2 不在 .NET 10 官方支援清單，必須以這臺實機測試結果為準。

第一次使用
----------
1. 把整個資料夾解壓縮到兩臺電腦；不要只複製 LanRemote.App.exe。
2. 兩臺電腦分別執行 LanRemote.App.exe。
3. 若 Windows Firewall 詢問，僅由你本人決定是否允許「私人網路」；不要允許公用網路。
4. 若檔案來源或 SHA256 與專案內 SHA256.txt 不一致，請勿執行。

連線視窗與畫面模式
------------------
- 程式先顯示連線介面；核准後，同一個視窗會切換成遠端桌面。
- 預設為可調整大小的視窗化；畫面會依視窗比例自動縮放並保留長寬比。
- 頂部工具列可選「視窗化」、「最大化」與「全螢幕」；F11 可切換全螢幕，Esc 可退出全螢幕。
- 按「隱藏工具列」後，把滑鼠移到視窗最頂端即可暫時叫回選單和工具列。
- 斷線後會回到連線介面，並保留剛才的位址與畫面模式。

三種畫面設定
------------
- 流暢：最高 1280x720 / 30 fps，適合網路或電腦較慢時。
- 平衡：最高 1600x900 / 24 fps，預設模式。
- 畫質：最高 1920x1080 / 15 fps，優先提高靜態畫面品質。
- 可在已連線時直接從 Quality 選單或工具列下拉選單切換，不必重新連線。

Windows 11 控制 Windows 10
--------------------------
1. Windows 10：按「開始等候」，記下顯示的 IPv4:45873。
2. Windows 11：在「控制另一臺電腦」輸入該位址，按「連線並核對」。
3. 比對兩端六位數配對碼完全相同後，在 Windows 10 按「是」。
4. 在遠端畫面內按一下，再測試滑鼠、滾輪與鍵盤。
5. 任一端需要停止時，按「立即斷線」或「停止被控端」。

Windows 10 反向控制 Windows 11
------------------------------
1. 先結束上一個 session。
2. Windows 11 改按「開始等候」。
3. Windows 10 輸入 Windows 11 顯示的 IPv4:45873 並連線。
4. 重新核對六位數配對碼並由 Windows 11 明確核准。

留下兩機測試證據
------------------
完成每個方向的五分鐘測試後，在該臺電腦的 PowerShell 執行同資料夾內的
New-TwoPcEvidence.ps1。範例：

powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\New-TwoPcEvidence.ps1 `
  -Direction Windows11-controls-Windows10 -Result PASS -PeerComputer 另一臺名稱 `
  -DurationSeconds 300 -ObservedFps 30 -P95InteractionLatencyMs 180 `
  -PairingCodesMatched $true -MousePassed $true -KeyboardPassed $true `
  -ImmediateDisconnectPassed $true -Notes "主要螢幕測試"

腳本會在 evidence 子資料夾產生 JSON。請把兩個方向、兩臺電腦的 JSON 複製回
專案 readygate/evidence-inbox；這些檔案包含電腦名稱與私人 IP，預設不進 Git。

已知限制
--------
- 一個 session 只允許單方向控制；必須斷線後交換角色。
- 目前以 GDI/JPEG 相容模式傳畫面；WGC + H.264 效能基線尚未完成。
- 三種模式的 fps 是上限，不是對所有硬體與網路保證的實測值。
- 只擷取主要螢幕；沒有多螢幕、音訊、剪貼簿或檔案傳輸。
- 不支援網際網路 relay、自動探索或無人值守連線。
- 不會也不能繞過 UAC、安全桌面或較高權限視窗（Windows UIPI 限制）。
