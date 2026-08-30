# UNCAD v1.9.7.13 BOQ 模板定位修复

- 修复 U1F/U1U 首次创建 BOQ 时使用 AutoCAD AppDomain 基目录查找模板，导致已安装模板仍被误报缺失的问题。
- 模板定位改为 UNCAD.dll 实际程序集目录，支持 BOQ_Template.xlsx 和 BOQ模板.xlsx。
- 模板缺失错误会显示实际搜索的插件目录。
- 新增显式插件目录、两个模板文件名及缺失诊断回归测试。
