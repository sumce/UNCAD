# UNCAD v1.9.7.14 双授权发布

- Release 配置继续生成正式版（Perpetual，无期限）。
- Temporary 配置生成临时授权版（Trial），到期时间为北京时间 2026-09-08 23:59:59 UTC+8。
- 新增 release-temp.ps1，使用隔离 staging Bundle，避免临时 DLL 和授权清单覆盖正式版。
- 临时版产物名包含 temp-20260908 后缀。
- 授权截止时刻按带时区时间解析；到期后 U1F、U1U、U1DWG 被阻止，关于与授权及使用条款仍可查看。
