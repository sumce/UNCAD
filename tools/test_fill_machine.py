# -*- coding: utf-8 -*-
import importlib.util
from pathlib import Path
ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('fill_machine', ROOT / 'fill_machine.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)
import openpyxl
ws = openpyxl.load_workbook(ROOT.parent / 'data.xlsx', data_only=True).active

def check(name, got, want):
    assert got == want, f'{name}: got {got!r} want {want!r}'
    print('PASS', name)

rows = m.find_rows(ws, 'MPAMT09')
check('find by machine MPAMT09', len(rows), 5)
check('first circuit name', rows[0][m.COL_NAME], 'AC Power Box Main 1')
thc = [r for r in rows if r[m.COL_NAME] == 'THC'][0]
check('THC cable', thc[m.COL_CABLE], 'ZB-YJV-3*70+1*35')
check('THC dia', thc[m.COL_DIA], 51)
check('THC seq', thc[m.COL_SEQ], 1086)

r2 = m.find_rows(ws, 'THC')
check('find by name THC', len(r2) > 5, True)

check('breaker_name', m.breaker_name(' 3P4W 3P200A', 'x'), '3P200A配电')
check('breaker_name fallback', m.breaker_name('-', 'THC'), 'THC')
check('breaker_name 1P', m.breaker_name('U220 1P3W 1P20A', 'x'), '1P20A配电')

check('cable template', m.CABLE_DESC_TEMPLATE.format(cable='ZB-YJV-3*70+1*35'),
      '1.名称:ZB-YJV-3*70+1*35mm²单芯电缆\\P2.配线形式:穿管或桥架敷设\\P3.说明:包含电缆接头；支吊架；测试等一切主辅材料')
check('conduit template', m.CONDUIT_DESC_TEMPLATE.format(dia='51'),
      '1.名称:51mm(1-1/2")包塑金属软管(波纹管)附镀锌接头\\P2.材质:镀锌金属软管和PVC包覆')
import tkinter
print('tkinter OK')
print('ALL TESTS PASSED')
