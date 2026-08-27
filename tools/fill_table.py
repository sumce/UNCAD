# -*- coding: utf-8 -*-
"""
fill_table.py — 按 Excel 表格自动填充 AutoCAD 表格（清单表）

用法:
    python fill_table.py <excel.xlsx> [工作表名] [表格起始行(0基, 默认2)]

流程:
    1. openpyxl 读取 Excel（公式取缓存值），第 1 行为表头
    2. win32com 连接正在运行的 AutoCAD，在命令行点选目标表格
    3. 按表头文字自动匹配列（Excel 列 <-> 表格列），对不上的列不动
    4. 从"表格起始行"开始，Excel 一行一行填下去；标题行/表头行不动

说明:
    - Excel 单元格为空 -> 跳过不覆盖
    - Excel 换行(Alt+Enter) -> 表格内 \\P 换行
    - 数字 14.7 -> "14.7"；整数 1 -> "1"
    - 表格表头带 MTEXT 控制码（{\\fSimSun|...;项目名称}）自动清理后匹配
"""
import re
import sys

import openpyxl
import win32com.client

_MTEXT_RULES = [
    (re.compile(r'\\f[^;]*;'), ''),          # \fSimSun|b0|i0|c134|p2;
    (re.compile(r'[{}]'), ''),                 # 花括号
    (re.compile(r'\\[PpLlOo]'), '\n'),      # \P 等 -> 换行
    (re.compile(r'\\[A-Za-z][^;]*;'), ''),   # 其他带参数控制码 \A0; \H1.5x; 等
    (re.compile(r'\\[A-Za-z]'), ''),         # 残余孤立控制码
]

def clean_mtext(s):
    """清理 MTEXT 控制码，取纯文本（用于匹配表头）。"""
    if s is None:
        return ''
    s = str(s)
    for rx, rep in _MTEXT_RULES:
        s = rx.sub(rep, s)
    return s.replace('\n', ' ').strip()

def cell_text(v):
    """Excel 单元格值 -> 表格文本。"""
    if v is None:
        return None
    if isinstance(v, float) and v.is_integer():
        v = int(v)
    if isinstance(v, bool):
        v = 'TRUE' if v else 'FALSE'
    return str(v).replace('\n', '\\P')

def connect_acad():
    for prog in ('AutoCAD.Application.24.1', 'AutoCAD.Application'):
        try:
            return win32com.client.GetActiveObject(prog)
        except Exception:
            continue
    raise RuntimeError('未找到正在运行的 AutoCAD（COM）')

def main():
    if len(sys.argv) < 2:
        print('用法: python fill_table.py <excel.xlsx> [工作表名] [表格起始行(0基,默认2)]')
        sys.exit(1)
    xlsx = sys.argv[1]
    sheet_name = sys.argv[2] if len(sys.argv) > 2 else None
    start_row = int(sys.argv[3]) if len(sys.argv) > 3 else 2

    # 1) 读 Excel
    wb = openpyxl.load_workbook(xlsx, data_only=True)
    ws = wb[sheet_name] if sheet_name else wb.active
    all_rows = list(ws.iter_rows(values_only=True))
    if not all_rows:
        print('[填充] Excel 为空')
        sys.exit(1)
    excel_header = [clean_mtext(c) for c in all_rows[0]]
    excel_data = all_rows[1:]
    print('[填充] Excel:', ws.title, '| 表头 =', excel_header, '| 数据行 =', len(excel_data))

    # 2) 连 AutoCAD + 点选表格
    acad = connect_acad()
    doc = acad.ActiveDocument
    doc.Utility.Prompt('\n[填充] 请在 AutoCAD 中点击要填充的表格: ')
    try:
        picked = doc.Utility.GetEntity()
    except Exception as e:
        print('[填充] 已取消或选择失败:', e)
        sys.exit(1)
    tbl = picked[0]
    if tbl.ObjectName != 'AcDbTable':
        print('[填充] 选中的不是表格')
        sys.exit(1)
    print('[填充] 表格:', tbl.Rows, '行 x', tbl.Columns, '列')

    # 3) 表头匹配（表格表头行 = 起始行 - 1）
    hdr_row = start_row - 1
    mapping = {}  # 表格列 -> Excel 列
    for c in range(tbl.Columns):
        th = clean_mtext(tbl.GetText(hdr_row, c))
        if th in excel_header:
            mapping[c] = excel_header.index(th)
    if not mapping:
        print('[填充] 表头匹配失败：表格表头与 Excel 表头对不上。')
        sys.exit(1)
    print('[填充] 列匹配:', {c: excel_header[i] for c, i in mapping.items()})

    # 4) 逐行填充
    written = 0
    skipped = 0
    filled_rows = 0
    for i, row in enumerate(excel_data):
        tr = start_row + i
        if tr >= tbl.Rows:
            print(f'[填充] 表格行不够：Excel 还有 {len(excel_data) - i} 行未填')
            break
        row_written = 0
        for tc, ec in mapping.items():
            v = cell_text(row[ec])
            if v is None:
                skipped += 1
                continue
            tbl.SetText(tr, tc, v)
            written += 1
            row_written += 1
        if row_written > 0:
            filled_rows += 1
    print(f'[填充] 完成：写入 {written} 个单元格（跳过空 {skipped} 个），共填充 {filled_rows} 行')

if __name__ == '__main__':
    main()
