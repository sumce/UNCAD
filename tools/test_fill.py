# -*- coding: utf-8 -*-
import importlib.util, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('fill_table', ROOT / 'fill_table.py')
m = importlib.util.module_from_spec(spec)
spec.loader.exec_module(m)

def check(name, got, want):
    assert got == want, f'{name}: got {got!r} want {want!r}'
    print('PASS', name)

check('clean_mtext font code', m.clean_mtext('{\\fSimSun|b0|i0|c134|p2;项目名称}'), '项目名称')
check('clean_mtext plain', m.clean_mtext('NO.'), 'NO.')
check('clean_mtext empty', m.clean_mtext(None), '')
check('cell_text decimal', m.cell_text(14.7), '14.7')
check('cell_text integer', m.cell_text(1.0), '1')
check('cell_text int', m.cell_text(2), '2')
check('cell_text str', m.cell_text('桥架'), '桥架')
check('cell_text newline', m.cell_text('a\nb'), 'a\\Pb')
check('cell_text bool', m.cell_text(True), 'TRUE')
check('cell_text None', m.cell_text(None), None)
print('ALL TESTS PASSED')
