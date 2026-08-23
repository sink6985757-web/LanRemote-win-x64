---
work_order: WO-LANREMOTE-V44-KEYBOARD-CAPTURE-CURSOR-20260823
status: WORK_ORDER_CONFIRMED
confirmed_at: 2026-08-23
profile: general
cycles_used: 1
---

# LanRemote v4.4 鍵盤擷取與幽靈游標工作單

## 目標

在不改動畫面擷取、畫質、檔案傳輸、剪貼簿、配對與安全模型的前提下，修復不同鍵盤配置／IME、修飾鍵、功能鍵與 extended key 的遠端輸入，並降低控制端定位游標對被控端原生游標的遮擋。

## 來源與版本

- Project：`區網windwos`
- Git baseline：`b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722`
- Input：package v4.3.1／protocol v6
- Output：package v4.4／protocol v7 self-contained win-x64 test candidate
- Pre-change live evidence：v6 TLS／配對／畫面正常；單一字母曾到達，Ctrl、Home 與後續字母不穩定；測試後已安全斷線且未保存端點／裝置識別。

## 已確認選擇

- `1A`：點遠端畫面進入遠端輸入；點 Toolbar／對話框或視窗失焦回本機，滑鼠離開不自動解除。
- `2A`：實體鍵／快捷鍵使用 scan code，IME 組字使用 Unicode fallback。
- `3A`：F1–F10、Home、End、方向鍵、Tab、Alt、Ctrl、Shift 送遠端；F11／Esc、Alt+Tab 留本機；Ctrl+Alt+Delete 維持 SAS。
- `4A`：2 px 青色細框與 Toolbar「遠端輸入／本機操作」。
- `5A`：空心幽靈箭頭，15% 填色、70% 邊框、目前大小的 80%，停止 350 ms 後淡出。

## 授權動作

- 升級協定 magic／version 與鍵盤 payload，加入 scan code、extended、Unicode scalar 與 release-all。
- 修改控制端 WPF 焦點／輸入路由、失焦釋放、狀態提示與游標動畫。
- 修改被控端 Windows `SendInput` 注入與按鍵狀態清理。
- 新增／更新 protocol、TLS loopback、輸入狀態與 UI source-contract 測試。
- 更新 README、CHANGELOG、handoff、package README、evidence script 與 ReadyGate 卡。
- 執行 Debug／Release test、build、format、PowerShell parser、NuGet audit、package、ZIP／manifest／Authenticode 回讀。

## 驗收條件

1. 英文字母、大小寫、數字與常用符號可穩定輸入，IME 組字可走 Unicode fallback。
2. F5、Home、End、方向鍵、Tab、Ctrl、Alt、Shift 使用正確 scan code／extended／down-up 順序。
3. 點本機 UI、視窗失焦、斷線時釋放遠端按鍵且停止轉送；再點遠端畫面才重新啟用。
4. F11／Esc 維持本機視窗控制，Alt+Tab 維持本機切換，SAS 路徑不變。
5. 遠端輸入細框／Toolbar 狀態可見，幽靈箭頭不明顯遮住 frame 內原生游標並會淡出。
6. 既有功能回歸、自動測試與 v4.4 package 完整性通過。

## 限制與不納入

- protocol v7 不與 v6 混用；兩臺必須同時換成 v4.4。
- 不更新另一臺電腦、不修改 Windows Firewall／輸入法／系統設定、不操作安全桌面。
- 不修改畫面擷取、解析度、FPS、縮放、檔案傳輸或剪貼簿資料模型。
- 不 commit、push、tag、release 或變更 GitHub。

## 回復

- Package：兩臺同時換回 v4.3.1。
- Source：以 Git `b3ca0f26cbc964b04d5f6a77f9f2c93fc8a87722` 為基準檢視差異；未授權發布。
- Session：立即斷線與輸入失焦都必須送出 release-all 並清空本機狀態。
