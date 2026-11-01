using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using UNCAD.Infra;

namespace UNCAD.Cad
{
    /// <summary>
    /// 当前文档上下文：封装 Document/Editor/Database 及常用快捷属性。
    /// 命令一律通过 ctx 访问 AutoCAD 环境，避免散落全局调用。
    /// </summary>
    public sealed class CadContext
    {
        public CadContext(Document doc)
        {
            Doc = doc;
        }

        public Document Doc { get; }

        /// <summary>
        /// 当前命令的操作记录作用域（2.10.0）；命令实现可登记机台/回路与输出文件。
        /// 直接 Run/单元测试里可能为 null，调用方必须判空。
        /// </summary>
        public OperationLogScope Operation { get; internal set; }

        public Editor Ed => Doc.Editor;
        public Database Db => Doc.Database;
        public ObjectId CurrentSpaceId => Db.CurrentSpaceId;
        public ObjectId CurrentLayerId => Db.Clayer;

        /// <summary>向当前空间写消息（等价命令行输出）。</summary>
        public void Write(string msg) => Ed.WriteMessage(msg);

        /// <summary>把实体加入当前空间并登记到事务（返回其 ObjectId）。</summary>
        public ObjectId AddToCurrentSpace(Transaction tr, Entity ent)
        {
            var btr = (BlockTableRecord)tr.GetObject(CurrentSpaceId, OpenMode.ForWrite);
            btr.AppendEntity(ent);
            tr.AddNewlyCreatedDBObject(ent, true);
            return ent.Id;
        }
    }
}
