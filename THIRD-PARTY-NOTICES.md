# 第三方与参考说明

## 随工具分发的依赖

- **Harmony 2.4.3**：用于进程内方法适配，MIT License。上游：[pardeike/Harmony](https://github.com/pardeike/Harmony/tree/v2.4.3)，许可全文：[licenses/Harmony.txt](licenses/Harmony.txt)。使用上游 Fat 构建，保留其许可与依赖声明。
- Fat 构建中的 **MonoMod** 与 **Mono.Cecil** 使用 MIT 许可，分别保留 [MonoMod 许可](licenses/MonoMod.txt) 与 [Mono.Cecil 许可](licenses/Mono.Cecil.txt)。上游：[MonoMod/MonoMod](https://github.com/MonoMod/MonoMod)、[jbevain/cecil](https://github.com/jbevain/cecil)。

Windows、.NET Framework 和原 PCL 使用自己的安装与许可。修复包不重新分发原 PCL、赞助文件、备份源码或完整 .NET 运行时。

## 功能思路参考

- [ShiraiFyuya/pcl2-patch](https://github.com/ShiraiFyuya/pcl2-patch)：参考结构探测、完整入口覆盖及检查思路。
- [xiaoqingwagua/PCL2-Download-Fix](https://github.com/xiaoqingwagua/PCL2-Download-Fix)：参考手动导入与整合包分发属于独立恢复点的分析。

本项目的补丁代码和安装界面独立实现，未复制上述项目的实现或界面。

实际下载与安装由用户已有 PCL 的原生模块执行。PCL 与 PCL CE 的名称用于说明兼容对象；本项目不代表其官方维护者。
