---
description: Generate commit message and commit
---

請執行以下步驟：

1. 執行 `git status` 和 `git diff` 查看變更
2. 檢查有沒有敏感資訊的內容被提交
3. 分析變更內容，生成符合 Conventional Commits 規範的 commit message
4. 格式：`type(scope): description`
   - type: feat/fix/refactor/docs/test
   - 描述要清楚說明「做了什麼」和「為什麼」
5. commit message 字數限制在300 個字內, 且不要有coauthor
4. 詢問我是否要執行 commit