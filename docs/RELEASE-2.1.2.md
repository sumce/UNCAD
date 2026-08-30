# UNCAD v2.1.2 通用能力与命令帮助

## 新增功能

- 新增 U1HELP 命令帮助页面，覆盖全部 20 个公开命令。
- 每条帮助包含命令名、显示名称、使用方法、功能说明和注意事项。
- 帮助页面为只读操作，不扫描图纸、不读取 Excel、不修改 CAD。

## 通用能力

- U1S、U1DWG、XLAYOUT 统一通过 FrameIdentityReader 读取图框身份和清单状态。
- CommandHelpCatalog 作为命令注册、Ribbon、Bundle 和帮助页面的文档契约。
- 开发规范明确 Core、Cad、Infra、Features、UI 的依赖方向和 Feature 编排职责。
- 新增代码评审门槛，要求事务所有权、失败回滚、输入验证、复用边界和测试覆盖明确。

## 版本

版本：2.1.2；程序集和 Bundle 版本：2.1.2.0。
