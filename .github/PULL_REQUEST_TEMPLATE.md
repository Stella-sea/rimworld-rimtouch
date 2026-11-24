## PR 类型 / PR Type

- [ ] `feat/*` · `fix/*` · `refactor/*` → `dev`（日常开发/修复合入集成测试分支，建议 **Squash and merge**）
- [ ] `dev` → `main`（实机验收通过后同步至稳定主干，**必须使用 Create a merge commit**，严禁 Squash 以免分支历史分叉）
- [ ] `hotfix/*` → `main`（紧急修复，合入 `main` 后须立即同步回 `dev`）

## 变更摘要 / Summary

<!-- 用 1–3 句话说明改动背景、核心修改与行为影响 -->

## 验证清单 / Verification

- [ ] GitHub Actions `ci / verify` 检查通过（仓库卫生、中英文键对齐、Roslyn C# 语法、3 套隔离回归脚本）
- [ ] 本机 `Build17.ps1` 编译通过（0 警告 / 0 错误），记录 `RimTouch.dll` SHA256：`<!-- sha256 -->`
- [ ] 本机 `scripts/test-harmony-patches.ps1` 全量 `[HarmonyPatch]` 反射核验通过
- [ ] 实机验收状态（Dell Latitude 7210 / RimWorld 1.7）：`<!-- 已实机验证 / 待在 dev 打包测试 -->`
- [ ] 如涉及发版：`About/About.xml` `<modVersion>` 与 `CHANGELOG.md` 已同步更新

## 安全与兼容边界 / Safety & Compatibility

- [ ] 保留 `GPL-3.0-or-later` 许可、原作者署名、`packageId`（`cinder.rimtouch17.private`）及 `About/PublishedFileId.txt`
- [ ] 新增/变更界面文案已同步更新 `Languages/English/` 与 `Languages/ChineseSimplified/`，且兼容已有存档设置键
- [ ] 保留鼠标回退路径及焦点丢失/视图切换清理逻辑；已注明编译通过不代表全机型/全模组兼容证明
