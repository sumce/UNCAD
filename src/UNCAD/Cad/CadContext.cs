using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;

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
